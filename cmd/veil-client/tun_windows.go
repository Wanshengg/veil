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
	engine.Insert(&engine.Key{
		Device:   "tun://" + tunName,
		Proxy:    "socks5://127.0.0.1:1080",
		MTU:      1500,
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
		cleanup()
		return nil, err
	}
	log.Printf("tun: default route through %q", tunName)
	return cleanup, nil
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

	// DNS внутри туннеля
	_ = run("netsh", "interface", "ipv4", "set", "dnsservers",
		"name="+tunName, "source=static", "address=1.1.1.1", "register=primary")

	// исключение для самого сервера: иначе туннель уйдёт в петлю
	host, _, err := net.SplitHostPort(c.server)
	if err != nil {
		return err
	}
	ips, err := net.LookupIP(host)
	if err != nil || len(ips) == 0 {
		return fmt.Errorf("tun: не могу разрешить сервер %s: %v", host, err)
	}
	gw, ifIdx, gwName := defaultGateway()
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
