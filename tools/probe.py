#!/usr/bin/env python3
"""Survival probe: every N seconds measure (1) plain TLS reachability of the
server port and (2) a real HTTP request through the veil tunnel.
Writes one JSON line per cycle; prints time-to-first-failure.

Usage:
  python tools/probe.py -server veil.example.com:443 -sni veil.example.com \
      -psk secret -interval 300 -log probe.jsonl
"""
import argparse
import json
import os
import socket
import ssl
import subprocess
import sys
import time

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(HERE)


def free_port() -> int:
    s = socket.socket()
    s.bind(("127.0.0.1", 0))
    p = s.getsockname()[1]
    s.close()
    return p


def tls_check(host: str, port: int, sni: str, timeout: float) -> tuple[bool, int, str]:
    ctx = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    ctx.check_hostname = False
    ctx.verify_mode = ssl.CERT_NONE
    ctx.set_alpn_protocols(["h2", "http/1.1"])
    t0 = time.time()
    try:
        with socket.create_connection((host, port), timeout) as raw:
            with ctx.wrap_socket(raw, server_hostname=sni) as s:
                s.sendall(b"GET / HTTP/1.1\r\nHost: %s\r\nConnection: close\r\n\r\n" % sni.encode())
                s.recv(64)
        return True, int((time.time() - t0) * 1000), ""
    except Exception as e:  # noqa: BLE001
        return False, int((time.time() - t0) * 1000), repr(e)


def socks_http(proxy_port: int, hostport: str, path: str = "/", timeout: float = 15) -> tuple[bool, int, int, str]:
    t0 = time.time()
    host, _, port_s = hostport.partition(":")
    port = int(port_s) if port_s else 80
    try:
        s = socket.create_connection(("127.0.0.1", proxy_port), timeout)
        s.settimeout(timeout)
        s.sendall(b"\x05\x01\x00")
        if s.recv(2) != b"\x05\x00":
            raise RuntimeError("socks auth rejected")
        hb = host.encode()
        s.sendall(b"\x05\x01\x00\x03" + bytes([len(hb)]) + hb + port.to_bytes(2, "big"))
        r = s.recv(4)
        if len(r) < 2 or r[1] != 0:
            raise RuntimeError(f"socks connect failed: {r!r}")
        # drain bound address
        atyp = r[3]
        s.recv(4 if atyp == 1 else 16 if atyp == 4 else s.recv(1)[0] + 2)
        s.recv(2)
        s.sendall(
            f"GET {path} HTTP/1.1\r\nHost: {host}\r\nUser-Agent: probe/1\r\nConnection: close\r\n\r\n".encode()
        )
        total = 0
        head = b""
        while True:
            chunk = s.recv(65536)
            if not chunk:
                break
            total += len(chunk)
            if len(head) < 12:
                head += chunk
            if total > 2_000_000:
                break
        s.close()
        ok = head.startswith(b"HTTP/1.") or b"HTTP/1." in head[:64]
        return ok, int((time.time() - t0) * 1000), total, "" if ok else head[:40].decode("latin1")
    except Exception as e:  # noqa: BLE001
        return False, int((time.time() - t0) * 1000), 0, repr(e)


def main() -> None:
    ap = argparse.ArgumentParser()
    ap.add_argument("-server", required=True, help="host:port")
    ap.add_argument("-sni", required=True)
    ap.add_argument("-psk", required=True)
    ap.add_argument("-interval", type=int, default=300)
    ap.add_argument("-log", default="probe.jsonl")
    ap.add_argument("-fp", default="chrome")
    ap.add_argument("-url", default="example.com", help="plain-HTTP host[:port] to fetch through the tunnel")
    ap.add_argument("-rounds", type=int, default=0, help="0 = forever")
    ap.add_argument("-insecure", action="store_true", help="self-signed test server")
    a = ap.parse_args()

    host, port = a.server.rsplit(":", 1)
    port = int(port)
    exe = os.path.join(ROOT, "bin", "veil-client")
    if os.name == "nt" and not os.path.exists(exe):
        exe += ".exe"

    started = time.time()
    first_fail = None
    n = 0
    while True:
        n += 1
        pport = free_port()
        cli = subprocess.Popen(
            [exe, "-socks", f"127.0.0.1:{pport}", "-server", a.server, "-sni", a.sni, "-psk", a.psk, "-fp", a.fp] + (["-insecure"] if a.insecure else []),
            stdout=subprocess.DEVNULL,
            stderr=subprocess.DEVNULL,
        )
        time.sleep(1.5)
        tls_ok, tls_ms, tls_err = tls_check(host, port, a.sni, 10)
        tun_ok, tun_ms, nbytes, tun_err = socks_http(pport, a.url)
        cli.terminate()
        try:
            cli.wait(5)
        except subprocess.TimeoutExpired:
            cli.kill()

        rec = {
            "ts": time.strftime("%Y-%m-%dT%H:%M:%S%z"),
            "n": n,
            "tls_ok": tls_ok,
            "tls_ms": tls_ms,
            "tls_err": tls_err,
            "tunnel_ok": tun_ok,
            "tunnel_ms": tun_ms,
            "bytes": nbytes,
            "tunnel_err": tun_err,
            "alive_s": int(time.time() - started),
        }
        with open(a.log, "a", encoding="utf-8") as f:
            f.write(json.dumps(rec, ensure_ascii=False) + "\n")
        flag = "OK " if (tls_ok and tun_ok) else "FAIL"
        print(f"[{rec['ts']}] {flag} tls={tls_ms}ms tunnel={tun_ms}ms {nbytes}B "
              f"{tun_err or tls_err}", flush=True)
        if not (tls_ok and tun_ok) and first_fail is None:
            first_fail = rec["alive_s"]
            print(f"*** first failure after {first_fail}s ({first_fail/3600:.2f}h) ***", flush=True)
        if a.rounds and n >= a.rounds:
            break
        time.sleep(a.interval)

    print(f"done: {n} cycles, first failure: {first_fail}s")


if __name__ == "__main__":
    sys.exit(main())
