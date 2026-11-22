// Package proto implements the veil wire format:
//
//	TLS(real) -> [VEIL][ver][ts][salt] -> AEAD frames; each frame carries a
//	2-byte payload length plus random padding, so sizes are not constant.
package proto

import (
	"bufio"
	"crypto/rand"
	"crypto/sha256"
	"encoding/binary"
	"errors"
	"fmt"
	"io"
	"sync"
	"time"

	"golang.org/x/crypto/chacha20poly1305"
	"golang.org/x/crypto/hkdf"
)

const (
	Magic     = "VEIL"
	Version   = 1
	SaltLen   = 16
	NonceLen  = chacha20poly1305.NonceSizeX // 24
	TagLen    = 16                          // poly1305 tag
	HeaderLen = len(Magic) + 1 + 8 + SaltLen
	MaxFrame  = 64*1024 - 128
	PadMax    = 16
)

var (
	Tolerance  = 120 * time.Second
	ErrNoMagic = errors.New("proto: not a veil stream")
	ErrReplay  = errors.New("proto: stale or replayed handshake")
	ErrBadAck  = errors.New("proto: bad ack")
)

// DeriveKey expands a pre-shared key into a per-connection AEAD key.
func DeriveKey(psk, salt []byte, label string) []byte {
	r := hkdf.New(sha256.New, psk, salt, []byte(label))
	k := make([]byte, chacha20poly1305.KeySize)
	if _, err := io.ReadFull(r, k); err != nil {
		panic(err) // hkdf.Read with sha256 never fails
	}
	return k
}

func randBytes(n int) []byte {
	b := make([]byte, n)
	if _, err := rand.Read(b); err != nil {
		panic(err)
	}
	return b
}

// WriteFrame writes: 4-byte BE frame length || 24-byte nonce || ciphertext.
// Plaintext is: 2-byte BE payload length || payload || random padding.
func WriteFrame(w io.Writer, key, payload []byte) error {
	if len(payload) > MaxFrame {
		return fmt.Errorf("proto: payload %d > %d", len(payload), MaxFrame)
	}
	pad := int(randBytes(1)[0] % (PadMax + 1))
	pt := make([]byte, 2+len(payload)+pad)
	binary.BigEndian.PutUint16(pt, uint16(len(payload)))
	copy(pt[2:], payload)
	copy(pt[2+len(payload):], randBytes(pad))

	aead, err := chacha20poly1305.NewX(key)
	if err != nil {
		return err
	}
	nonce := randBytes(NonceLen)
	frame := make([]byte, 4+NonceLen+len(pt)+TagLen)
	binary.BigEndian.PutUint32(frame, uint32(len(frame)-4))
	copy(frame[4:], nonce)
	aead.Seal(frame[4+NonceLen:4+NonceLen], nonce, pt, nil)
	_, err = w.Write(frame)
	return err
}

// ReadFrame reads one frame written by WriteFrame.
func ReadFrame(r io.Reader, key []byte) ([]byte, error) {
	var lb [4]byte
	if _, err := io.ReadFull(r, lb[:]); err != nil {
		return nil, err
	}
	n := binary.BigEndian.Uint32(lb[:])
	if n < uint32(NonceLen+TagLen+2) || n > 4*MaxFrame {
		return nil, fmt.Errorf("proto: frame length %d out of range", n)
	}
	buf := make([]byte, n)
	if _, err := io.ReadFull(r, buf); err != nil {
		return nil, err
	}
	aead, err := chacha20poly1305.NewX(key)
	if err != nil {
		return nil, err
	}
	pt, err := aead.Open(nil, buf[:NonceLen], buf[NonceLen:], nil)
	if err != nil {
		return nil, err
	}
	if len(pt) < 2 {
		return nil, errors.New("proto: short plaintext")
	}
	l := int(binary.BigEndian.Uint16(pt))
	if 2+l > len(pt) {
		return nil, errors.New("proto: bad payload length")
	}
	return pt[2 : 2+l], nil
}

// ClientHandshake sends the header and target address, then waits for ack.
// Returns the outbound (client->server) and inbound (server->client) keys.
func ClientHandshake(conn io.ReadWriter, psk []byte, target string) (out, in []byte, err error) {
	salt := randBytes(SaltLen)
	var ts [8]byte
	binary.BigEndian.PutUint64(ts[:], uint64(time.Now().Unix()))
	hdr := make([]byte, 0, HeaderLen)
	hdr = append(hdr, Magic...)
	hdr = append(hdr, Version)
	hdr = append(hdr, ts[:]...)
	hdr = append(hdr, salt...)
	if _, err = conn.Write(hdr); err != nil {
		return nil, nil, err
	}
	out = DeriveKey(psk, salt, "veil v1 c2s")
	in = DeriveKey(psk, salt, "veil v1 s2c")
	if err = WriteFrame(conn, out, []byte(target)); err != nil {
		return nil, nil, err
	}
	ack, err := ReadFrame(conn, in)
	if err != nil {
		return nil, nil, fmt.Errorf("proto: no ack: %w", err)
	}
	if string(ack) != "OK" {
		return nil, nil, ErrBadAck
	}
	return out, in, nil
}

// ServerHandshake validates the header and returns the requested target.
// On ErrNoMagic / ErrReplay the caller should fall back to the real site.
func ServerHandshake(br *bufio.Reader, w io.Writer, psk []byte, rp *ReplayCache) (out, in []byte, target string, err error) {
	hdr, err := br.Peek(HeaderLen)
	if err != nil {
		return nil, nil, "", ErrNoMagic
	}
	if string(hdr[:len(Magic)]) != Magic || hdr[len(Magic)] != Version {
		return nil, nil, "", ErrNoMagic
	}
	ts := int64(binary.BigEndian.Uint64(hdr[len(Magic)+1:]))
	if d := time.Since(time.Unix(ts, 0)); d > Tolerance || d < -Tolerance {
		return nil, nil, "", ErrReplay
	}
	salt := append([]byte(nil), hdr[len(Magic)+1+8:]...)
	if !rp.Seen(salt) {
		return nil, nil, "", ErrReplay
	}
	if _, err = br.Discard(HeaderLen); err != nil {
		return nil, nil, "", err
	}
	out = DeriveKey(psk, salt, "veil v1 s2c")
	in = DeriveKey(psk, salt, "veil v1 c2s")
	payload, err := ReadFrame(br, in)
	if err != nil {
		return nil, nil, "", err
	}
	if err = WriteFrame(w, out, []byte("OK")); err != nil {
		return nil, nil, "", err
	}
	return out, in, string(payload), nil
}


// ServerHandshakeMulti — то же, что ServerHandshake, но сервер знает несколько
// ключей (по одному на человека). Заголовок не содержит id ключа, поэтому
// первый AEAD-кадр пробуем расшифровать каждым ключом: он аутентифицирован,
// и чужой ключ даст ошибку. Имя совпавшего ключа возвращается для лога.
func ServerHandshakeMulti(br *bufio.Reader, w io.Writer, keys []NamedKey, rp *ReplayCache) (out, in []byte, target, keyName string, err error) {
	hdr, perr := br.Peek(HeaderLen)
	if perr != nil {
		return nil, nil, "", "", ErrNoMagic
	}
	if string(hdr[:len(Magic)]) != Magic || hdr[len(Magic)] != Version {
		return nil, nil, "", "", ErrNoMagic
	}
	ts := int64(binary.BigEndian.Uint64(hdr[len(Magic)+1:]))
	if d := time.Since(time.Unix(ts, 0)); d > Tolerance || d < -Tolerance {
		return nil, nil, "", "", ErrReplay
	}
	salt := append([]byte(nil), hdr[len(Magic)+1+8:]...)
	if !rp.Seen(salt) {
		return nil, nil, "", "", ErrReplay
	}

	// первый кадр целиком, без вычитывания из буфера — чтобы можно было
	// перебрать ключи, не теряя байты
	lb, perr := br.Peek(HeaderLen + 4)
	if perr != nil {
		return nil, nil, "", "", perr
	}
	n := binary.BigEndian.Uint32(lb[HeaderLen:])
	if n < uint32(NonceLen+TagLen+2) || n > 4*MaxFrame {
		return nil, nil, "", "", fmt.Errorf("proto: frame length %d out of range", n)
	}
	raw, perr := br.Peek(HeaderLen + 4 + int(n))
	if perr != nil {
		return nil, nil, "", "", perr
	}
	buf := raw[HeaderLen+4:]

	for _, k := range keys {
		candOut := DeriveKey(k.PSK, salt, "veil v1 s2c")
		candIn := DeriveKey(k.PSK, salt, "veil v1 c2s")
		aead, aerr := chacha20poly1305.NewX(candIn)
		if aerr != nil {
			continue
		}
		pt, oerr := aead.Open(nil, buf[:NonceLen], buf[NonceLen:], nil)
		if oerr != nil || len(pt) < 2 {
			continue // не наш ключ
		}
		if _, derr := br.Discard(HeaderLen + 4 + int(n)); derr != nil {
			return nil, nil, "", "", derr
		}
		if werr := WriteFrame(w, candOut, []byte("OK")); werr != nil {
			return nil, nil, "", "", werr
		}
		plen := int(binary.BigEndian.Uint16(pt[:2]))
		if 2+plen > len(pt) {
			continue
		}
		return candOut, candIn, string(pt[2 : 2+plen]), k.Name, nil
	}
	return nil, nil, "", "", ErrNoMagic
}

// NamedKey — ключ с именем владельца (для логов и отзыва).
type NamedKey struct {
	Name string
	PSK  []byte
}

// Relay pumps bytes both ways: fr/fw is the framed tunnel side,
// target is a plain TCP connection.
func Relay(target io.ReadWriteCloser, fr io.Reader, fw io.Writer, keyOut, keyIn []byte) {
	done := make(chan struct{}, 2)
	go func() { // target -> client, framed with keyOut
		buf := make([]byte, 32*1024)
		for {
			n, err := target.Read(buf)
			if n > 0 {
				if werr := WriteFrame(fw, keyOut, buf[:n]); werr != nil {
					break
				}
			}
			if err != nil {
				break
			}
		}
		target.Close()
		done <- struct{}{}
	}()
	go func() { // client -> target, unframed
		for {
			p, err := ReadFrame(fr, keyIn)
			if len(p) > 0 {
				if _, werr := target.Write(p); werr != nil {
					break
				}
			}
			if err != nil {
				break
			}
		}
		target.Close()
		done <- struct{}{}
	}()
	<-done
	// First side finished: tear everything down so the peer sees EOF immediately
	// instead of hanging until an application timeout.
	target.Close()
	if c, ok := fw.(io.Closer); ok {
		c.Close()
	}
	<-done
}

// ReplayCache remembers handshake salts for a short window.
type ReplayCache struct {
	mu  sync.Mutex
	ttl time.Duration
	m   map[string]time.Time
}

func NewReplayCache(ttl time.Duration) *ReplayCache {
	return &ReplayCache{ttl: ttl, m: make(map[string]time.Time)}
}

// Seen records salt and reports whether it is fresh.
func (c *ReplayCache) Seen(salt []byte) bool {
	c.mu.Lock()
	defer c.mu.Unlock()
	now := time.Now()
	for k, t := range c.m {
		if now.Sub(t) > c.ttl {
			delete(c.m, k)
		}
	}
	k := string(salt)
	if _, ok := c.m[k]; ok {
		return false
	}
	c.m[k] = now
	return true
}
