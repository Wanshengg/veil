//go:build windows

package main

import (
	"fmt"
	"log"
	"net"
	"os/exec"
	"strconv"
	"strings"
	"time"

	"github.com/xjasonlyu/tun2socks/v2/engine"
)

const tunName = "veil"

// startTUN поднимает виртуальный адаптер, заворачивает весь трафик в SOCKS-клиент
// и настраивает маршруты. Возвращает функцию отката. Требует прав администратора.
func startTUN(c *cfg) (func(), error) {
	// Резолвим сервер ДО того, как весь DNS уйдёт в туннель: иначе клиент
	// будет резолвить имя через собственный туннель и получит дедлок.
	host, port, err := net.SplitHostPort(c.server)
	if err != nil {
		return nil, err
	}
	var v4 []string
	if c.serverIPPin != "" {
		// чужой VPN (Karing) отдаёт fake-IP по DNS — берём адрес из конфига
		if p := net.ParseIP(c.serverIPPin); p != nil && p.To4() != nil {
			v4 = append(v4, p.String())
		}
	}
	if len(v4) == 0 {
		ips, rerr := net.LookupIP(host)
		if rerr != nil || len(ips) == 0 {
			return nil, fmt.Errorf("tun: не могу разрешить сервер %s: %v", host, rerr)
		}
		for _, ip := range ips {
			if i4 := ip.To4(); i4 != nil {
				v4 = append(v4, i4.String())
			}
		}
	}
	if len(v4) == 0 {
		return nil, fmt.Errorf("tun: у сервера нет IPv4")
	}
	c.server = net.JoinHostPort(v4[0], port)
	log.Printf("tun: server pinned to %s (было %s)", c.server, host)

	engine.Insert(&engine.Key{
		Device:   "tun://" + tunName,
		Proxy:    "socks5://127.0.0.1:1080",
		MTU:      mtuOf(c),
		LogLevel: "warn",
	})
	engine.Start()
	log.Printf("tun: adapter %q created", tunName)

	cleanup := func() {
		removeRoutes()
		engine.Stop()
		log.Printf("tun: stopped")
	}

	if err := waitAdapter(10 * time.Second); err != nil {
		cleanup()
		return nil, err
	}
	if err := configureAdapter(c); err != nil {
		engine.Stop()
		return nil, err
	}
	// Сторож: если трафик через адаптер не пошёл, машина осталась бы без сети.
	// Через 12 секунд проверяем реальный проход по TUN и сами откатываем маршруты.
	go tunGuard(c)
	log.Printf("tun: default route through %q", tunName)
	return cleanup, nil
}

func mtuOf(c *cfg) int {
	if c.tunMTU >= 576 && c.tunMTU <= 1500 {
		return c.tunMTU
	}
	return 1400
}

func waitAdapter(timeout time.Duration) error {
	end := time.Now().Add(timeout)
	for time.Now().Before(end) {
		if run("netsh", "interface", "ipv4", "show", "interfaces") == nil {
			out := output("netsh", "interface", "ipv4", "show", "interfaces")
			if strings.Contains(out, tunName) {
				return nil
			}
		}
		time.Sleep(300 * time.Millisecond)
	}
	return fmt.Errorf("tun: адаптер %q не появился", tunName)
}

func configureAdapter(c *cfg) error {
	// адрес на нашем интерфейсе
	if err := run("netsh", "interface", "ipv4", "set", "address",
		"name="+tunName, "source=static", "address=198.18.0.1", "mask=255.255.255.0"); err != nil {
		return fmt.Errorf("tun: set address: %w", err)
	}
	// интерфейс должен побеждать по метрике, иначе другой VPN/шлюз останется главным
	_ = run("netsh", "interface", "ipv4", "set", "interface",
		"name="+tunName, "metric=1")
	_ = run("netsh", "interface", "ipv4", "set", "subinterface",
		tunName, "mtu="+strconv.Itoa(mtuOf(c)), "store=persistent")

	// DNS внутри туннеля
	// DNS внутри туннеля: российский резолвер, чтобы RU-сайты получали локальные адреса,
	// а сами запросы шли напрямую (77.88.8.8 есть в geoip-списке)
	_ = run("netsh", "interface", "ipv4", "set", "dnsservers",
		"name="+tunName, "source=static", "address=77.88.8.8", "register=primary")

	// исключение для самого сервера: иначе туннель уйдёт в петлю
	host, _, err := net.SplitHostPort(c.server)
	if err != nil {
		return err
	}
	ips := []net.IP{net.ParseIP(host)}
	gw, ifIdx, gwName := defaultGateway()
	// адрес физического интерфейса: им привязываем «прямые» соединения,
	// иначе они снова уйдут в TUN и завернутся в петлю
	if ip := ifaceIPv4(ifIdx); ip != nil {
		c.directIP = ip
		log.Printf("tun: direct dials bound to %s (%s)", ip, gwName)
	}
	if gw == "" {
		return fmt.Errorf("tun: не найден физический шлюз")
	}
	for _, ip := range ips {
		if ip4 := ip.To4(); ip4 != nil {
			_ = run("route", "add", ip4.String(), "mask", "255.255.255.255", gw, "if", strconv.Itoa(ifIdx), "metric", "1")
		}
	}
	log.Printf("tun: server %s via %s (%s, if %d)", host, gw, gwName, ifIdx)

	// основной маршрут — в туннель
	if err := run("route", "add", "0.0.0.0", "mask", "0.0.0.0", "198.18.0.1", "metric", "1", "if", strconv.Itoa(tunIndex())); err != nil {
		return fmt.Errorf("tun: default route: %w", err)
	}
	return nil
}

// tunIndex — ifIndex нашего адаптера.
func tunIndex() int {
	out := output("powershell", "-NoProfile", "-Command",
		"(Get-NetAdapter -Name '"+tunName+"' -ErrorAction SilentlyContinue).ifIndex")
	for _, line := range strings.Split(out, "\n") {
		if v, err := strconv.Atoi(strings.TrimSpace(line)); err == nil {
			return v
		}
	}
	return 0
}

func removeRoutes() {
	_ = run("route", "delete", "0.0.0.0", "mask", "0.0.0.0", "198.18.0.1")
}

// tunGuard проверяет, что трафик реально идёт через адаптер, и откатывает
// маршруты, если туннель не поднялся: иначе пользователь остаётся без интернета.
func tunGuard(c *cfg) {
	for i := 0; i < 3; i++ {
		time.Sleep(4 * time.Second)
		if !routeOwned() {
			log.Printf("tun: default route принадлежит другому адаптеру (Karing/Amnezia?) — TUN не включён")
			removeRoutes()
			engine.Stop()
			return
		}
		if tunReachable() {
			log.Printf("tun: трафик через адаптер идёт (проверка %d/3)", i+1)
			return
		}
	}
	log.Printf("tun: трафик через адаптер НЕ идёт — откатываю маршруты, работаю как прокси")
	removeRoutes()
	engine.Stop()
}

// routeOwned — реально ли наш маршрут выигрывает по метрике.
func routeOwned() bool {
	out := output("powershell", "-NoProfile", "-Command",
		"(Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Sort-Object RouteMetric | "+
			"Select-Object -First 1).NextHop")
	return strings.TrimSpace(out) == "198.18.0.1"
}

// tunReachable — сквозная проверка: соединение уходит в адаптер, tun2socks
// заворачивает его в SOCKS, дальше в туннель.
func tunReachable() bool {
	for _, target := range []string{"1.1.1.1:443", "8.8.8.8:443"} {
		conn, err := net.DialTimeout("tcp", target, 4*time.Second)
		if err == nil {
			conn.Close()
			return true
		}
	}
	return false
}

// ifaceIPv4 — первый IPv4-адрес интерфейса по ifIndex.
func ifaceIPv4(idx int) net.IP {
	out := output("powershell", "-NoProfile", "-Command",
		"(Get-NetIPAddress -InterfaceIndex "+strconv.Itoa(idx)+
			" -AddressFamily IPv4 -ErrorAction SilentlyContinue | Select-Object -First 1).IPAddress")
	return net.ParseIP(strings.TrimSpace(out))
}

// defaultGateway возвращает шлюз и ifIndex физического маршрута по умолчанию.
// route.exe печатает в колонке интерфейса IP, а не индекс, поэтому берём данные
// через PowerShell (Get-NetRoute), с откатом на разбор route print.
func defaultGateway() (gw string, ifIdx int, name string) {
	out := output("powershell", "-NoProfile", "-Command",
		"(Get-NetRoute -DestinationPrefix '0.0.0.0/0' | Where-Object { $_.NextHop -ne '0.0.0.0' } | "+
			"Sort-Object RouteMetric | Select-Object -First 1) | ForEach-Object { $_.NextHop + '|' + $_.ifIndex + '|' + $_.InterfaceAlias }")
	for _, line := range strings.Split(out, "\n") {
		f := strings.Split(strings.TrimSpace(line), "|")
		if len(f) != 3 {
			continue
		}
		idx, err := strconv.Atoi(strings.TrimSpace(f[1]))
		if err != nil {
			continue
		}
		return strings.TrimSpace(f[0]), idx, strings.TrimSpace(f[2])
	}
	return "", 0, ""
}

func adapterName(idx int) string {
	for _, line := range strings.Split(output("netsh", "interface", "ipv4", "show", "interfaces"), "\n") {
		if strings.Contains(line, strconv.Itoa(idx)) {
			f := strings.Fields(line)
			if len(f) > 4 {
				return f[4]
			}
		}
	}
	return "?"
}

func run(name string, args ...string) error {
	cmd := exec.Command(name, args...)
	cmd.SysProcAttr = hiddenWindow()
	out, err := cmd.CombinedOutput()
	if err != nil {
		return fmt.Errorf("%s %v: %v (%s)", name, args, err, strings.TrimSpace(string(out)))
	}
	return nil
}

func output(name string, args ...string) string {
	cmd := exec.Command(name, args...)
	cmd.SysProcAttr = hiddenWindow()
	out, _ := cmd.CombinedOutput()
	return decodeConsole(out)
}
