package proto

import (
	"encoding/binary"
	"errors"
)

// UDP datagram inside a veil frame:
//   addrlen(1) | addr | port(2, BE) | payload
// Один кадр — один датаграм.

var ErrUDP = errors.New("proto: bad udp frame")

// PackUDP собирает датаграм для отправки в туннель.
func PackUDP(host string, port int, payload []byte) []byte {
	if len(host) > 255 {
		host = host[:255]
	}
	buf := make([]byte, 0, 3+len(host)+2+len(payload))
	buf = append(buf, byte(len(host)))
	buf = append(buf, host...)
	var pb [2]byte
	binary.BigEndian.PutUint16(pb[:], uint16(port))
	buf = append(buf, pb[:]...)
	buf = append(buf, payload...)
	return buf
}

// UnpackUDP разбирает датаграм.
func UnpackUDP(b []byte) (host string, port int, payload []byte, err error) {
	if len(b) < 4 {
		return "", 0, nil, ErrUDP
	}
	n := int(b[0])
	if len(b) < 3+n+2 {
		return "", 0, nil, ErrUDP
	}
	host = string(b[1 : 1+n])
	port = int(binary.BigEndian.Uint16(b[1+n : 3+n]))
	payload = b[3+n:]
	return host, port, payload, nil
}
