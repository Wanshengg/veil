#!/usr/bin/env python3
"""Step-by-step SOCKS debug against a running veil-client."""
import socket
import sys
import time

port = int(sys.argv[1])
hostport = sys.argv[2] if len(sys.argv) > 2 else "127.0.0.1:8080"
host, _, ps = hostport.partition(":")
tport = int(ps) if ps else 80

s = socket.create_connection(("127.0.0.1", port), 5)
s.settimeout(6)
print("greeting ->")
s.sendall(b"\x05\x01\x00")
print("  reply:", s.recv(2))

hb = host.encode()
print("connect ->", hostport)
s.sendall(b"\x05\x01\x00\x03" + bytes([len(hb)]) + hb + tport.to_bytes(2, "big"))
r = s.recv(4)
print("  reply4:", r, "atyp:", r[3])
if r[3] == 1:
    print("  addr:", s.recv(4))
else:
    l = s.recv(1)[0]
    print("  addr:", l, s.recv(l))
print("  port:", s.recv(2))

req = f"GET / HTTP/1.1\r\nHost: {host}\r\nConnection: close\r\n\r\n".encode()
print("GET ->", req[:40])
t0 = time.time()
s.sendall(req)
total = 0
try:
    while True:
        d = s.recv(65536)
        if not d:
            print("EOF after", total, "bytes")
            break
        total += len(d)
        print(f"  +{len(d)}B @{time.time()-t0:.2f}s: {d[:40]!r}")
        if total > 4096:
            break
except socket.timeout:
    print("TIMEOUT after", total, "bytes")
