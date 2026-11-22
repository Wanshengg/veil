#!/usr/bin/env python3
"""Capture veil-client's ClientHello and compare its fingerprint with a real
browser measured on this host (tls.peet.ws, HeadlessChrome 145).

Usage: python tools/ja3check.py [chrome|firefox|safari|edge]
"""
import hashlib
import os
import socket
import subprocess
import sys
import threading

PORT = 9443

# Measured on this host: https://tls.peet.ws/api/all
CHROME145 = {
    "ja3": "771,4865-4866-4867-49195-49199-49196-49200-52393-52392-49171-49172-156-157-47-53,"
           "5-17613-35-0-43-45-51764-18-27-23-13-11-65281-10-51-16-65037,4588-29-23-24,0",
    "ja3_hash": "6a6492578a508acad01c01cdd86ecad2",
    "ja4": "t13d1517h2_8daaf6152771_dcad5a053991",
}


def is_grease(v: int) -> bool:
    return (v & 0x0F0F) == 0x0A0A and (v >> 8) == (v & 0xFF)


def capture(result: dict) -> None:
    s = socket.socket()
    s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
    s.bind(("127.0.0.1", PORT))
    s.listen(1)
    s.settimeout(15)
    c, _ = s.accept()
    c.settimeout(0.4)
    buf = b""
    while True:
        try:
            d = c.recv(4096)
            if not d:
                break
            buf += d
        except socket.timeout:
            break
    c.close()
    s.close()
    result["hello"] = buf


def parse(buf: bytes) -> dict:
    assert buf[0] == 0x16, "not a TLS handshake record"
    rec_len = int.from_bytes(buf[3:5], "big")
    hs = buf[5 : 5 + rec_len]
    assert hs[0] == 1, "not a ClientHello"
    p = 4
    ver = int.from_bytes(hs[p : p + 2], "big")
    p += 2 + 32
    sid_len = hs[p]
    p += 1 + sid_len
    cs_len = int.from_bytes(hs[p : p + 2], "big")
    p += 2
    ciphers = [int.from_bytes(hs[p + i : p + i + 2], "big") for i in range(0, cs_len, 2)]
    p += cs_len
    comp_len = hs[p]
    p += 1 + comp_len
    ext_len = int.from_bytes(hs[p : p + 2], "big")
    p += 2
    order, exts = [], {}
    end = p + ext_len
    while p < end:
        et = int.from_bytes(hs[p : p + 2], "big")
        el = int.from_bytes(hs[p + 2 : p + 4], "big")
        p += 4
        order.append(et)
        exts[et] = hs[p : p + el]
        p += el

    curves, pf, alpn = [], [], []
    if 10 in exts:
        d = exts[10]
        n = int.from_bytes(d[0:2], "big")
        curves = [int.from_bytes(d[2 + i : 4 + i], "big") for i in range(0, n, 2)]
    if 11 in exts:
        d = exts[11]
        pf = list(d[1 : 1 + d[0]])
    if 16 in exts:
        d = exts[16]
        q = 2
        while q < len(d):
            l = d[q]
            alpn.append(d[q + 1 : q + 1 + l].decode("ascii", "replace"))
            q += 1 + l

    ciphers = [c for c in ciphers if not is_grease(c)]
    order = [e for e in order if not is_grease(e)]
    curves = [c for c in curves if not is_grease(c)]

    ja3 = "%d,%s,%s,%s,%s" % (
        ver,
        "-".join(map(str, ciphers)),
        "-".join(map(str, order)),
        "-".join(map(str, curves)),
        "-".join(map(str, pf)),
    )
    # JA4-a: version, sni flag, cipher count, extension count, alpn tag
    ext_no_sni_alpn = [e for e in order if e not in (0, 16)]
    alpn_tag = (alpn[0][0] + alpn[0][-1]) if alpn else "00"
    ja4a = "t1%d%s%02d%02d%s" % (
        ver & 0xFF,
        "d" if 0 in order else "i",
        len(ciphers),
        len(order),  # peet.ws counts all extensions, incl. SNI/ALPN
        alpn_tag,
    )
    return {
        "ver": ver,
        "ciphers": ciphers,
        "exts": order,
        "exts_no_sni_alpn": ext_no_sni_alpn,
        "curves": curves,
        "pf": pf,
        "alpn": alpn,
        "ja3": ja3,
        "ja3_hash": hashlib.md5(ja3.encode()).hexdigest(),
        "ja4a": ja4a,
    }


def main() -> None:
    fp = sys.argv[1] if len(sys.argv) > 1 else "chrome"
    result: dict = {}
    exe = os.path.abspath(os.path.join("bin", "veil-client"))
    if not os.path.exists(exe):
        exe = os.path.abspath(os.path.join("bin", "veil-client.exe"))
    t = threading.Thread(target=capture, args=(result,), daemon=True)
    t.start()
    subprocess.run(
        [exe, "-probe-hello", f"127.0.0.1:{PORT}", "-sni", "localhost", "-psk", "selftest", "-fp", fp],
        timeout=30,
    )
    t.join(timeout=20)
    hello = result.get("hello", b"")
    print(f"captured {len(hello)} bytes")
    got = parse(hello)

    ref = parse_simple(CHROME145["ja3"])
    print("\n            ours            chrome-145")
    print("ja3 md5   : %s  %s" % (got["ja3_hash"], CHROME145["ja3_hash"]))
    print("ja4-a     : %s  %s" % (got["ja4a"], CHROME145["ja4"].split("_")[0]))
    print("ciphers   : %d              %d" % (len(got["ciphers"]), len(ref["ciphers"])))
    print("exts      : %d              %d" % (len(got["exts"]), len(ref["exts"])))
    print("alpn      : %-15s %s" % (",".join(got["alpn"]), ",".join(ref["alpn"])))

    cm = set(ref["ciphers"]) - set(got["ciphers"])
    cx = set(got["ciphers"]) - set(ref["ciphers"])
    em = set(ref["exts"]) - set(got["exts"])
    ex = set(got["exts"]) - set(ref["exts"])
    print("\nmissing ciphers/exts vs chrome:", sorted(cm), sorted(em))
    print("extra   ciphers/exts vs chrome:", sorted(cx), sorted(ex))
    print("curves match:", got["curves"] == ref["curves"], got["curves"])
    print("flags:", "sni" if 0 in got["exts"] else "no-sni",
          "| ech" if 65037 in got["exts"] else "")


def parse_simple(ja3: str) -> dict:
    ver, ciphers, exts, curves, pf = ja3.split(",")
    return {
        "ver": int(ver),
        "ciphers": [int(x) for x in ciphers.split("-") if x],
        "exts": [int(x) for x in exts.split("-") if x],
        "curves": [int(x) for x in curves.split("-") if x],
        "pf": [int(x) for x in pf.split("-") if x],
        "alpn": ["h2", "http/1.1"],
    }


if __name__ == "__main__":
    main()
