#!/usr/bin/env python3
"""Проверка UDP через SOCKS5 UDP ASSOCIATE: DNS-запрос в 8.8.8.8:53.

Usage: python tools/test_udp.py [socks_host] [socks_port] [dns_host] [dns_port]
"""
import socket
import struct
import sys
import time


def socks_udp_associate(host: str, port: int) -> tuple[str, int, socket.socket, socket.socket]:
    tcp = socket.create_connection((host, port), 10)
    tcp.settimeout(10)
    tcp.sendall(b"\x05\x01\x00")
    if tcp.recv(2) != b"\x05\x00":
        raise RuntimeError("socks: auth rejected")

    # CMD=3 (UDP ASSOCIATE), ATYP=1, 0.0.0.0:0 — «дай мне релей-порт»
    tcp.sendall(b"\x05\x03\x00\x01" + b"\x00\x00\x00\x00" + b"\x00\x00")
    rep = tcp.recv(4)
    if len(rep) < 4 or rep[1] != 0:
        raise RuntimeError(f"socks: associate failed {rep!r}")

    atyp = rep[3]
    if atyp == 1:
        addr = socket.inet_ntoa(tcp.recv(4))
    elif atyp == 4:
        addr = socket.inet_ntop(socket.AF_INET6, tcp.recv(16))
    else:
        n = tcp.recv(1)[0]
        addr = tcp.recv(n).decode()
    rport = struct.unpack("!H", tcp.recv(2))[0]

    udp = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    udp.settimeout(10)
    return addr, rport, tcp, udp


def dns_query(name: str) -> tuple[bytes, int]:
    tid = 0x1234
    header = struct.pack("!HHHHHH", tid, 0x0100, 1, 0, 0, 0)
    q = b"".join(bytes([len(p)]) + p.encode() for p in name.split(".")) + b"\x00"
    return header + q + struct.pack("!HH", 1, 1), tid


def main() -> int:
    host = sys.argv[1] if len(sys.argv) > 1 else "127.0.0.1"
    port = int(sys.argv[2]) if len(sys.argv) > 2 else 1080
    dns_host = sys.argv[3] if len(sys.argv) > 3 else "8.8.8.8"
    dns_port = int(sys.argv[4]) if len(sys.argv) > 4 else 53

    relay_ip, relay_port, tcp, udp = socks_udp_associate(host, port)
    print(f"socks: UDP relay {relay_ip}:{relay_port}")

    query, tid = dns_query("example.com")
    # SOCKS5 UDP request header: RSV(2) FRAG(1) ATYP(1) ADDR PORT DATA
    pkt = b"\x00\x00\x00\x01" + socket.inet_aton(dns_host) + struct.pack("!H", dns_port) + query

    t0 = time.time()
    udp.sendto(pkt, (relay_ip, relay_port))
    data, _ = udp.recvfrom(4096)
    ms = int((time.time() - t0) * 1000)

    # ответ тоже в SOCKS5-обёртке
    if data[:3] != b"\x00\x00\x00":
        print("FAIL: bad socks udp header", data[:8])
        return 1
    atyp = data[3]
    off = 4 + (4 if atyp == 1 else 16 if atyp == 4 else 1 + data[4])
    off += 2
    dns = data[off:]

    rid, flags = struct.unpack("!HH", dns[:4])
    answers = struct.unpack("!H", dns[6:8])[0]
    ok = rid == tid and (flags & 0x8000) != 0
    print(f"dns: id={rid:#06x} answers={answers} bytes={len(dns)} time={ms}ms -> {'OK' if ok else 'FAIL'}")
    udp.close()
    tcp.close()
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
