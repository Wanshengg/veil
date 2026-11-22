// veil-server terminates real TLS on :443 and serves two faces:
//   - a real website (fallback), for anything that is not a veil client;
//   - the veil tunnel, for clients that present the magic + valid AEAD header.
package main

import (
	"bufio"
	"crypto/sha256"
	"strconv"
	"sync"
	"crypto/tls"
	"flag"
	"io"
	"log"
	"net"
	"os"
	"time"

	"veil/internal/proto"
)

func main() {
	addr := flag.String("addr", ":443", "listen address")
	certFile := flag.String("cert", "cert.pem", "TLS certificate (fullchain)")
	keyFile := flag.String("key", "key.pem", "TLS private key")
	psk := flag.String("psk", "", "pre-shared key")
	fallback := flag.String("fallback", "127.0.0.1:8080", "where to proxy non-veil traffic (real site)")
	flag.Parse()

	if *psk == "" {
		log.Fatal("veil-server: -psk is required")
	}
	sum := sha256.Sum256([]byte(*psk))
	key := sum[:]

	cert, err := tls.LoadX509KeyPair(*certFile, *keyFile)
	if err != nil {
		log.Fatalf("veil-server: load cert: %v", err)
	}
	cfg := &tls.Config{
		Certificates: []tls.Certificate{cert},
		NextProtos:   []string{"http/1.1"},
		MinVersion:   tls.VersionTLS12,
	}
	ln, err := tls.Listen("tcp", *addr, cfg)
	if err != nil {
		log.Fatalf("veil-server: listen: %v", err)
	}
	log.Printf("veil-server: listening on %s, fallback %s", *addr, *fallback)

	rp := proto.NewReplayCache(10 * time.Minute)
	for {
		c, err := ln.Accept()
		if err != nil {
			log.Printf("accept: %v", err)
			continue
		}
		go handle(c, key, rp, *fallback)
	}
}

func handle(c net.Conn, key []byte, rp *proto.ReplayCache, fallback string) {
	defer c.Close()
	if tc, ok := c.(*tls.Conn); ok {
		tc.SetDeadline(time.Now().Add(15 * time.Second))
		if err := tc.Handshake(); err != nil {
			log.Printf("%s: tls handshake: %v", c.RemoteAddr(), err)
			return
		}
		tc.SetDeadline(time.Time{})
	}

	br := bufio.NewReader(c)
	out, in, target, err := proto.ServerHandshake(br, c, key, rp)
	switch {
	case err == proto.ErrNoMagic:
		log.Printf("%s: probe/http -> fallback", c.RemoteAddr())
		proxyTo(c, br, fallback)
		return
	case err != nil:
		log.Printf("%s: handshake rejected: %v", c.RemoteAddr(), err)
		return
	}

	if target == "udp" {
		log.Printf("%s: udp relay", c.RemoteAddr())
		relayUDP(br, c, out, in)
		return
	}

	up, derr := net.DialTimeout("tcp", target, 10*time.Second)
	if derr != nil {
		log.Printf("%s: dial %s: %v", c.RemoteAddr(), target, derr)
		return
	}
	defer up.Close()
	log.Printf("%s: tunnel -> %s", c.RemoteAddr(), target)
	proto.Relay(up, br, c, out, in)
}

// proxyTo replays everything already buffered and then splices raw bytes.
func proxyTo(c net.Conn, br *bufio.Reader, dst string) {
	up, err := net.DialTimeout("tcp", dst, 10*time.Second)
	if err != nil {
		return
	}
	defer up.Close()
	if n := br.Buffered(); n > 0 {
		buf, _ := br.Peek(n)
		if _, err := up.Write(buf); err != nil {
			return
		}
		br.Discard(n)
	}
	done := make(chan struct{}, 2)
	go func() { io.Copy(up, br); halfClose(up); done <- struct{}{} }()
	go func() { io.Copy(c, up); halfClose(c); done <- struct{}{} }()
	<-done
	<-done
}

// relayUDP пересылает датаграмы из туннеля в интернет и обратно.
func relayUDP(br *bufio.Reader, w io.Writer, keyOut, keyIn []byte) {
	conns := make(map[string]*net.UDPConn)
	var wmu sync.Mutex

	for {
		p, err := proto.ReadFrame(br, keyIn)
		if err != nil {
			return
		}
		host, port, data, err := proto.UnpackUDP(p)
		if err != nil {
			continue
		}
		key := net.JoinHostPort(host, strconv.Itoa(port))
		uc := conns[key]
		if uc == nil {
			addr, rerr := net.ResolveUDPAddr("udp", key)
			if rerr != nil {
				continue
			}
			uc, rerr = net.DialUDP("udp", nil, addr)
			if rerr != nil {
				continue
			}
			conns[key] = uc
			go func(uc *net.UDPConn, host string, port int) {
				buf := make([]byte, 65535)
				for {
					n, _, rerr := uc.ReadFrom(buf)
					if rerr != nil {
						return
					}
					wmu.Lock()
					proto.WriteFrame(w, keyOut, proto.PackUDP(host, port, buf[:n]))
					wmu.Unlock()
				}
			}(uc, host, port)
		}
		if _, werr := uc.Write(data); werr != nil {
			continue
		}
	}
}

func halfClose(c net.Conn) {
	if tc, ok := c.(*tls.Conn); ok {
		tc.CloseWrite()
		return
	}
	if cw, ok := c.(interface{ CloseWrite() error }); ok {
		cw.CloseWrite()
	}
}

var _ = os.Getpid
