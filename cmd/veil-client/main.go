// veil-client exposes a local SOCKS5 proxy and drives every connection through
// a real TLS session whose ClientHello is a byte-exact Chrome/Firefox/Safari
// fingerprint (utls), then speaks the veil framing on top.
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
	"time"

	utls "github.com/refraction-networking/utls"
	"veil/internal/proto"
)

type bufConn struct {
	net.Conn
	r *bufio.Reader
}

func (b bufConn) Read(p []byte) (int, error) { return b.r.Read(p) }

func main() {
	socksAddr := flag.String("socks", "127.0.0.1:1080", "local SOCKS5 listen address")
	server := flag.String("server", "127.0.0.1:443", "veil server host:port")
	sni := flag.String("sni", "", "TLS SNI (default: host from -server)")
	psk := flag.String("psk", "", "pre-shared key")
	insecure := flag.Bool("insecure", false, "skip certificate verification (self-signed tests only)")
	fp := flag.String("fp", "chrome", "TLS fingerprint: chrome|firefox|safari|edge|random")
	probe := flag.String("probe-hello", "", "dial this address with the utls fingerprint, then exit (self-check)")
	cfgPath := flag.String("config", "", "optional key=value config file (flags win)")
	flag.Parse()
	applyConfig(*cfgPath)

	if *psk == "" {
		log.Fatal("veil-client: -psk is required")
	}
	sum := sha256.Sum256([]byte(*psk))
	key := sum[:]

	sniName := *sni
	if sniName == "" {
		h, _, err := net.SplitHostPort(*server)
		if err != nil {
			log.Fatalf("veil-client: bad -server: %v", err)
		}
		sniName = h
	}

	if *probe != "" {
		uc, err := dialTunnel(*probe, sniName, true, *fp)
		if err == nil {
			uc.Close()
		}
		log.Printf("probe-hello %s: %v", *probe, err)
		time.Sleep(500 * time.Millisecond)
		return
	}

	ln, err := net.Listen("tcp", *socksAddr)
	if err != nil {
		log.Fatalf("veil-client: listen: %v", err)
	}
	log.Printf("veil-client: SOCKS5 on %s -> %s (sni=%s fp=%s)", *socksAddr, *server, sniName, *fp)

	for {
		c, err := ln.Accept()
		if err != nil {
			log.Printf("accept: %v", err)
			continue
		}
		go handleSocks(c, *server, sniName, key, *insecure, *fp)
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

func dialTunnel(server, sni string, insecure bool, fp string) (*utls.UConn, error) {
	id, err := helloID(fp)
	if err != nil {
		return nil, err
	}
	raw, err := net.DialTimeout("tcp", server, 10*time.Second)
	if err != nil {
		return nil, err
	}
	raw.SetDeadline(time.Now().Add(15 * time.Second))
	cfg := &utls.Config{
		ServerName:         sni,
		InsecureSkipVerify: insecure,
		NextProtos:         []string{"h2", "http/1.1"},
	}
	uc := utls.UClient(raw, cfg, id)
	if err := uc.Handshake(); err != nil {
		raw.Close()
		return nil, err
	}
	raw.SetDeadline(time.Time{})
	return uc, nil
}

func handleSocks(c net.Conn, server, sni string, key []byte, insecure bool, fp string) {
	br := bufio.NewReader(c)
	defer c.Close()

	// greeting: ver, nmethods, methods
	var g [2]byte
	if _, err := io.ReadFull(br, g[:]); err != nil || g[0] != 5 {
		return
	}
	methods := make([]byte, int(g[1]))
	if _, err := io.ReadFull(br, methods); err != nil {
		return
	}
	if _, err := c.Write([]byte{5, 0}); err != nil { // no auth
		return
	}

	// request: ver, cmd, rsv, atyp, addr, port
	var r [4]byte
	if _, err := io.ReadFull(br, r[:]); err != nil {
		return
	}
	if r[1] != 1 { // CONNECT only
		c.Write([]byte{5, 7, 0, 1, 0, 0, 0, 0, 0, 0})
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
		c.Write([]byte{5, 8, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	var pb [2]byte
	if _, err := io.ReadFull(br, pb[:]); err != nil {
		return
	}
	target := net.JoinHostPort(host, strconv.Itoa(int(binary.BigEndian.Uint16(pb[:]))))

	uc, err := dialTunnel(server, sni, insecure, fp)
	if err != nil {
		log.Printf("%s: tls: %v", target, err)
		c.Write([]byte{5, 1, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	defer uc.Close()

	outKey, inKey, err := proto.ClientHandshake(uc, key, target)
	if err != nil {
		log.Printf("%s: veil: %v", target, err)
		c.Write([]byte{5, 4, 0, 1, 0, 0, 0, 0, 0, 0})
		return
	}
	if _, err := c.Write([]byte{5, 0, 0, 1, 0, 0, 0, 0, 0, 0}); err != nil {
		return
	}
	log.Printf("tunnel -> %s", target)
	proto.Relay(bufConn{Conn: c, r: br}, uc, uc, outKey, inKey)
}
