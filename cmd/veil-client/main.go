// veil-client exposes local SOCKS5/SOCKS4/HTTP proxies and drives every
// connection through a real TLS session with a browser-exact ClientHello.
package main

import (
	"bufio"
	"crypto/sha256"
	"encoding/binary"
	"flag"
	"fmt"
	"io"
	"log"
	"net"
	"os"
	"strconv"
	"strings"
	"sync"
	"time"

	utls "github.com/refraction-networking/utls"
	"veil/internal/proto"
	"veil/internal/rules"
)

type bufConn struct {
	net.Conn
	r *bufio.Reader
}

func (b bufConn) Read(p []byte) (int, error) { return b.r.Read(p) }

type cfg struct {
	server, sni string
	key         []byte
	insecure    bool
	fp          string
	rules       *rules.Rules

	mu   sync.Mutex
	good string
	bad  map[string]time.Time
}

// order returns the fingerprints to try: the requested one first, then fallbacks.
func (c *cfg) order() []string {
	c.mu.Lock()
	defer c.mu.Unlock()
	if c.bad == nil {
		c.bad = map[string]time.Time{}
	}
	seen := map[string]bool{}
	var out []string
	add := func(f string) {
		f = strings.TrimSpace(f)
		if f == "" || seen[f] || f == "random" {
			return
		}
		if t, ok := c.bad[f]; ok && time.Since(t) < 5*time.Minute {
			return
		}
		seen[f] = true
		out = append(out, f)
	}
	add(c.good)
	for _, f := range strings.Split(c.fp, ",") {
		add(f)
	}
	for _, f := range []string{"firefox", "safari", "edge", "chrome"} {
		add(f)
	}
	return out
}

func (c *cfg) promote(fp string) {
	c.mu.Lock()
	c.good = fp
	delete(c.bad, fp)
	c.mu.Unlock()
}

func (c *cfg) penalize(fp string) {
	c.mu.Lock()
	if c.bad == nil {
		c.bad = map[string]time.Time{}
	}
	c.bad[fp] = time.Now()
	if c.good == fp {
		c.good = ""
	}
	c.mu.Unlock()
}

func main() {
	socksAddr := flag.String("socks", "127.0.0.1:1080", "local SOCKS5/SOCKS4 listen address")
	httpAddr := flag.String("http", "127.0.0.1:8081", "local HTTP proxy listen address (empty to disable)")
	server := flag.String("server", "127.0.0.1:443", "veil server host:port")
	sni := flag.String("sni", "", "TLS SNI (default: host from -server)")
	psk := flag.String("psk", "", "pre-shared key")
	insecure := flag.Bool("insecure", false, "skip certificate verification (self-signed tests only)")
	fp := flag.String("fp", "chrome", "TLS fingerprint: chrome|firefox|safari|edge|random")
	probe := flag.String("probe-hello", "", "dial this address with the utls fingerprint, then exit (self-check)")
	direct := flag.Bool("direct", true, "send Russian/local destinations straight to the internet (no tunnel)")
	rulesFile := flag.String("rules", "", "extra direct-rule file: domains or CIDRs, one per line")
	cfgPath := flag.String("config", "", "optional key=value config file (flags win)")
	flag.Parse()
	applyConfig(*cfgPath)

	if *psk == "" {
		log.Fatal("veil-client: -psk is required")
	}
	sum := sha256.Sum256([]byte(*psk))

	sniName := *sni
	if sniName == "" {
		h, _, err := net.SplitHostPort(*server)
		if err != nil {
			log.Fatalf("veil-client: bad -server: %v", err)
		}
		sniName = h
	}

	c := &cfg{server: *server, sni: sniName, key: sum[:], insecure: *insecure, fp: *fp,
		rules: rules.NewRules(*direct, *rulesFile)}

	if *probe != "" {
		uc, err := dialTunnel(c, *probe, c.fp)
		if err == nil {
			uc.Close()
		}
		log.Printf("probe-hello %s: %v", *probe, err)
		time.Sleep(500 * time.Millisecond)
		return
	}

	ln, err := net.Listen("tcp", *socksAddr)
	if err != nil {
		log.Fatalf("veil-client: listen %s: %v", *socksAddr, err)
	}
	log.Printf("veil-client: SOCKS5/4 on %s -> %s (sni=%s fp=%s direct=%v)", *socksAddr, *server, sniName, *fp, *direct)

	if *httpAddr != "" {
		hl, herr := net.Listen("tcp", *httpAddr)
		if herr != nil {
			log.Printf("veil-client: http %s: %v", *httpAddr, herr)
		} else {
			log.Printf("veil-client: HTTP proxy on %s", *httpAddr)
			go func() {
				for {
					hc, err := hl.Accept()
					if err != nil {
						log.Printf("http accept: %v", err)
						continue
					}
					go handleHTTP(hc, c)
				}
			}()
		}
	}

	for {
		conn, err := ln.Accept()
		if err != nil {
			log.Printf("accept: %v", err)
			continue
		}
		go handleAny(conn, c)
	}
}

// applyConfig fills flags from a simple key=value file; explicit CLI flags win.
func applyConfig(path string) {
	if path == "" {
		return
	}
	data, err := os.ReadFile(path)
	if err != nil {
		log.Fatalf("veil-client: config: %v", err)
	}
	seen := map[string]bool{}
	flag.Visit(func(f *flag.Flag) { seen[f.Name] = true })
	for _, line := range strings.Split(string(data), "\n") {
		line = strings.TrimSpace(line)
		if line == "" || strings.HasPrefix(line, "#") || strings.HasPrefix(line, ";") {
			continue
		}
		k, v, ok := strings.Cut(line, "=")
		if !ok {
			continue
		}
		k, v = strings.TrimSpace(k), strings.TrimSpace(v)
		if seen[k] {
			continue
		}
		if err := flag.Set(k, v); err != nil {
			log.Fatalf("veil-client: config %q: %v", k, err)
		}
	}
}

func helloID(name string) (utls.ClientHelloID, error) {
	switch name {
	case "chrome", "":
		return utls.HelloChrome_Auto, nil
	case "firefox":
		return utls.HelloFirefox_Auto, nil
	case "safari":
		return utls.HelloSafari_Auto, nil
	case "edge":
		return utls.HelloEdge_Auto, nil
	case "random":
		return utls.HelloRandomizedALPN, nil
	}
	return utls.ClientHelloID{}, fmt.Errorf("unknown fingerprint %q", name)
}

func dialTunnel(c *cfg, addr, fp string) (*utls.UConn, error) {
	id, err := helloID(fp)
	if err != nil {
		return nil, err
	}
	raw, err := net.DialTimeout("tcp", addr, 10*time.Second)
	if err != nil {
		return nil, err
	}
	raw.SetDeadline(time.Now().Add(15 * time.Second))
	cfgTLS := &utls.Config{
		ServerName:         c.sni,
		InsecureSkipVerify: c.insecure,
		NextProtos:         []string{"h2", "http/1.1"},
	}
	uc := utls.UClient(raw, cfgTLS, id)
	if err := uc.Handshake(); err != nil {
		raw.Close()
		return nil, err
	}
	raw.SetDeadline(time.Time{})
	return uc, nil
}

// openTunnel tries the configured fingerprints in order and returns the first
// one that survives both the TLS handshake and the veil handshake.
func openTunnel(c *cfg, target string) (*utls.UConn, []byte, []byte, string, error) {
	var last error = fmt.Errorf("no fingerprints available")
	for _, fp := range c.order() {
		uc, err := dialTunnel(c, c.server, fp)
		if err != nil {
			c.penalize(fp)
			last = err
			log.Printf("fp %s: tls %v", fp, err)
			continue
		}
		out, in, err := proto.ClientHandshake(uc, c.key, target)
		if err != nil {
			uc.Close()
			c.penalize(fp)
			last = err
			log.Printf("fp %s: veil %v", fp, err)
			continue
		}
		c.promote(fp)
		return uc, out, in, fp, nil
	}
	return nil, nil, nil, "", last
}

// serve dials the destination (direct or through the tunnel) and splices bytes.
func serve(client net.Conn, br *bufio.Reader, target string, c *cfg) {
	if c.rules != nil && c.rules.Direct(target) {
		up, err := net.DialTimeout("tcp", target, 10*time.Second)
		if err != nil {
			log.Printf("direct %s: %v", target, err)
			return
		}
		defer up.Close()
		log.Printf("direct -> %s", target)
		relayPlain(bufConn{Conn: client, r: br}, up)
		return
	}

	uc, outKey, inKey, fp, err := openTunnel(c, target)
	if err != nil {
		log.Printf("%s: %v", target, err)
		return
	}
	defer uc.Close()
	log.Printf("tunnel -> %s (fp=%s)", target, fp)
	proto.Relay(bufConn{Conn: client, r: br}, uc, uc, outKey, inKey)
}

// handleAny dispatches by the first byte: 5 = SOCKS5, 4 = SOCKS4/4a.
func handleAny(conn net.Conn, c *cfg) {
	br := bufio.NewReader(conn)
	defer conn.Close()
	ver, err := br.Peek(1)
	if err != nil {
		return
	}
	switch ver[0] {
	case 5:
		handleSocks5(conn, br, c)
	case 4:
		handleSocks4(conn, br, c)
	}
}

func handleSocks5(conn net.Conn, br *bufio.Reader, c *cfg) {
	var g [2]byte
	if _, err := io.ReadFull(br, g[:]); err != nil || g[0] != 5 {
		return
	}
	methods := make([]byte, int(g[1]))
	if _, err := io.ReadFull(br, methods); err != nil {
		return
	}
	if _, err := conn.Write([]byte{5, 0}); err != nil {
		return
	}

	var r [4]byte
	if _, err := io.ReadFull(br, r[:]); err != nil {
		return
	}
	if r[1] != 1 {
		conn.Write([]byte{5, 7, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	var host string
	switch r[3] {
	case 1:
		var ip [4]byte
		if _, err := io.ReadFull(br, ip[:]); err != nil {
			return
		}
		host = net.IP(ip[:]).String()
	case 3:
		var l [1]byte
		if _, err := io.ReadFull(br, l[:]); err != nil {
			return
		}
		b := make([]byte, int(l[0]))
		if _, err := io.ReadFull(br, b); err != nil {
			return
		}
		host = string(b)
	case 4:
		var ip [16]byte
		if _, err := io.ReadFull(br, ip[:]); err != nil {
			return
		}
		host = "[" + net.IP(ip[:]).String() + "]"
	default:
		conn.Write([]byte{5, 8, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	var pb [2]byte
	if _, err := io.ReadFull(br, pb[:]); err != nil {
		return
	}
	target := net.JoinHostPort(host, strconv.Itoa(int(binary.BigEndian.Uint16(pb[:]))))
	if _, err := conn.Write([]byte{5, 0, 0, 1, 0, 0, 0, 0, 0, 0}); err != nil {
		return
	}
	serve(conn, br, target, c)
}

// handleSocks4 supports SOCKS4 and SOCKS4a (Chrome maps system "socks=" to v4).
func handleSocks4(conn net.Conn, br *bufio.Reader, c *cfg) {
	var h [8]byte
	if _, err := io.ReadFull(br, h[:]); err != nil {
		return
	}
	if h[0] != 4 || h[1] != 1 {
		conn.Write([]byte{0, 0x5B, 0, 0, 0, 0, 0, 0})
		return
	}
	port := int(binary.BigEndian.Uint16(h[2:4]))
	ip := net.IP(h[4:8])
	for {
		b, err := br.ReadByte()
		if err != nil || b == 0 {
			break
		}
	}
	host := ip.String()
	if ip[0] == 0 && ip[1] == 0 && ip[2] == 0 && ip[3] != 0 {
		var sb []byte
		for {
			b, err := br.ReadByte()
			if err != nil || b == 0 {
				break
			}
			sb = append(sb, b)
		}
		if len(sb) == 0 {
			conn.Write([]byte{0, 0x5B, 0, 0, 0, 0, 0, 0})
			return
		}
		host = string(sb)
	}
	target := net.JoinHostPort(host, strconv.Itoa(port))
	if _, err := conn.Write([]byte{0, 0x5A, 0, 0, 0, 0, 0, 0}); err != nil {
		return
	}
	serve(conn, br, target, c)
}

// handleHTTP is a minimal forward proxy: CONNECT for TLS, absolute-URI for plain HTTP.
func handleHTTP(conn net.Conn, c *cfg) {
	br := bufio.NewReader(conn)
	defer conn.Close()
	conn.SetReadDeadline(time.Now().Add(30 * time.Second))
	req, err := readRequest(br)
	if err != nil {
		return
	}
	conn.SetReadDeadline(time.Time{})
	log.Printf("http %s %s", req.method, req.url)
	client := bufConn{Conn: conn, r: br}

	if strings.EqualFold(req.method, "CONNECT") {
		if c.rules != nil && c.rules.Direct(req.target) {
			up, derr := net.DialTimeout("tcp", req.target, 10*time.Second)
			if derr != nil {
				log.Printf("http connect %s: %v", req.target, derr)
				conn.Write([]byte("HTTP/1.1 502 Bad Gateway\r\n\r\n"))
				return
			}
			defer up.Close()
			log.Printf("direct -> %s", req.target)
			if _, werr := conn.Write([]byte("HTTP/1.1 200 Connection established\r\n\r\n")); werr != nil {
				return
			}
			relayPlain(client, up)
			return
		}
		uc, outKey, inKey, fp, derr := openTunnel(c, req.target)
		if derr != nil {
			log.Printf("http connect %s: %v", req.target, derr)
			conn.Write([]byte("HTTP/1.1 502 Bad Gateway\r\n\r\n"))
			return
		}
		defer uc.Close()
		log.Printf("tunnel -> %s (fp=%s)", req.target, fp)
		if _, werr := conn.Write([]byte("HTTP/1.1 200 Connection established\r\n\r\n")); werr != nil {
			return
		}
		proto.Relay(client, uc, uc, outKey, inKey)
		return
	}

	u := req.url
	i := strings.Index(u, "://")
	if i < 0 {
		conn.Write([]byte("HTTP/1.1 400 Bad Request\r\n\r\n"))
		return
	}
	rest := u[i+3:]
	hostpart, path := rest, "/"
	if j := strings.IndexByte(rest, '/'); j >= 0 {
		hostpart, path = rest[:j], rest[j:]
	}
	if !strings.Contains(hostpart, ":") {
		hostpart += ":80"
	}
	target := hostpart

	var b strings.Builder
	fmt.Fprintf(&b, "%s %s HTTP/1.1\r\n", req.method, path)
	for _, h := range req.headers {
		if strings.EqualFold(h.name, "Proxy-Connection") {
			continue
		}
		fmt.Fprintf(&b, "%s: %s\r\n", h.name, h.value)
	}
	b.WriteString("Connection: close\r\n\r\n")
	raw := []byte(b.String())

	if c.rules != nil && c.rules.Direct(target) {
		up, derr := net.DialTimeout("tcp", target, 10*time.Second)
		if derr != nil {
			conn.Write([]byte("HTTP/1.1 502 Bad Gateway\r\n\r\n"))
			return
		}
		defer up.Close()
		log.Printf("direct -> %s", target)
		if _, werr := up.Write(raw); werr != nil {
			return
		}
		relayPlain(client, up)
		return
	}

	uc, outKey, inKey, fp, derr := openTunnel(c, target)
	if derr != nil {
		log.Printf("http %s: %v", target, derr)
		conn.Write([]byte("HTTP/1.1 502 Bad Gateway\r\n\r\n"))
		return
	}
	defer uc.Close()
	log.Printf("tunnel -> %s (fp=%s)", target, fp)
	if werr := proto.WriteFrame(uc, outKey, raw); werr != nil {
		return
	}
	proto.Relay(client, uc, uc, outKey, inKey)
}

type header struct{ name, value string }

type request struct {
	method, url, target string
	headers             []header
}

func readRequest(br *bufio.Reader) (*request, error) {
	line, err := br.ReadString('\n')
	if err != nil {
		return nil, err
	}
	f := strings.Fields(strings.TrimSpace(line))
	if len(f) < 2 {
		return nil, fmt.Errorf("bad request line")
	}
	r := &request{method: f[0], url: f[1]}
	if strings.EqualFold(r.method, "CONNECT") {
		r.target = r.url
		if !strings.Contains(r.target, ":") {
			r.target += ":443"
		}
	}
	for {
		h, err := br.ReadString('\n')
		if err != nil {
			return nil, err
		}
		h = strings.TrimRight(h, "\r\n")
		if h == "" {
			break
		}
		if k, v, ok := strings.Cut(h, ":"); ok {
			r.headers = append(r.headers, header{strings.TrimSpace(k), strings.TrimSpace(v)})
		}
	}
	return r, nil
}

// relayPlain splices two plain TCP connections (direct mode).
func relayPlain(a, b net.Conn) {
	done := make(chan struct{}, 2)
	cp := func(dst, src net.Conn) {
		buf := make([]byte, 32*1024)
		for {
			n, err := src.Read(buf)
			if n > 0 {
				if _, werr := dst.Write(buf[:n]); werr != nil {
					break
				}
			}
			if err != nil {
				break
			}
		}
		done <- struct{}{}
	}
	go cp(b, a)
	go cp(a, b)
	<-done
	a.Close()
	b.Close()
	<-done
}
