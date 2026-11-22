// gencert writes a self-signed cert/key pair for local tests.
// For production use a real Let's Encrypt certificate instead.
package main

import (
	"crypto/ecdsa"
	"crypto/elliptic"
	"crypto/rand"
	"crypto/x509"
	"crypto/x509/pkix"
	"encoding/pem"
	"flag"
	"log"
	"math/big"
	"net"
	"os"
	"path/filepath"
	"strings"
	"time"
)

func main() {
	out := flag.String("out", "certs", "output directory")
	dns := flag.String("dns", "localhost", "comma separated DNS names")
	ips := flag.String("ips", "127.0.0.1,::1", "comma separated IP addresses")
	days := flag.Int("days", 397, "validity in days")
	flag.Parse()

	priv, err := ecdsa.GenerateKey(elliptic.P256(), rand.Reader)
	if err != nil {
		log.Fatal(err)
	}
	serial, _ := rand.Int(rand.Reader, new(big.Int).Lsh(big.NewInt(1), 128))
	tpl := x509.Certificate{
		SerialNumber:          serial,
		Subject:               pkix.Name{CommonName: strings.Split(*dns, ",")[0]},
		NotBefore:             time.Now().Add(-time.Hour),
		NotAfter:              time.Now().AddDate(0, 0, *days),
		KeyUsage:              x509.KeyUsageDigitalSignature | x509.KeyUsageCertSign,
		ExtKeyUsage:           []x509.ExtKeyUsage{x509.ExtKeyUsageServerAuth},
		BasicConstraintsValid: true,
		IsCA:                  true,
	}
	for _, d := range strings.Split(*dns, ",") {
		if d = strings.TrimSpace(d); d != "" {
			tpl.DNSNames = append(tpl.DNSNames, d)
		}
	}
	for _, s := range strings.Split(*ips, ",") {
		if s = strings.TrimSpace(s); s != "" {
			if ip := net.ParseIP(s); ip != nil {
				tpl.IPAddresses = append(tpl.IPAddresses, ip)
			}
		}
	}
	der, err := x509.CreateCertificate(rand.Reader, &tpl, &tpl, &priv.PublicKey, priv)
	if err != nil {
		log.Fatal(err)
	}
	if err := os.MkdirAll(*out, 0o755); err != nil {
		log.Fatal(err)
	}
	cf, err := os.Create(filepath.Join(*out, "cert.pem"))
	if err != nil {
		log.Fatal(err)
	}
	pem.Encode(cf, &pem.Block{Type: "CERTIFICATE", Bytes: der})
	cf.Close()

	kb, err := x509.MarshalECPrivateKey(priv)
	if err != nil {
		log.Fatal(err)
	}
	kf, err := os.OpenFile(filepath.Join(*out, "key.pem"), os.O_CREATE|os.O_WRONLY|os.O_TRUNC, 0o600)
	if err != nil {
		log.Fatal(err)
	}
	pem.Encode(kf, &pem.Block{Type: "EC PRIVATE KEY", Bytes: kb})
	kf.Close()
	log.Printf("wrote %s/{cert,key}.pem for %s %s", *out, *dns, *ips)
}
