"""Мост SSH через SOCKS5-прокси veil (127.0.0.1:1080). Только stdlib.
ssh -o ProxyCommand="python socksbridge.py %h %p" root@host
"""
import os
import socket
import sys
import threading

PROXY = ("127.0.0.1", 1080)


def main():
    if len(sys.argv) < 3:
        os.write(2, b"usage: socksbridge.py host port\n")
        os._exit(2)
    host, port = sys.argv[1], int(sys.argv[2])

    s = socket.create_connection(PROXY, timeout=20)
    s.sendall(b"\x05\x01\x00")
    if s.recv(2) != b"\x05\x00":
        os._exit(3)

    hb = host.encode()
    s.sendall(b"\x05\x01\x00\x03" + bytes([len(hb)]) + hb + port.to_bytes(2, "big"))
    rep = s.recv(4)
    if len(rep) < 4 or rep[1] != 0:
        os._exit(4)
    atyp = rep[3]
    if atyp == 1:
        s.recv(6)
    elif atyp == 4:
        s.recv(18)
    else:
        n = s.recv(1)[0]
        s.recv(n + 2)

    def up():
        try:
            while True:
                d = os.read(0, 32768)
                if not d:
                    break
                s.sendall(d)
        except Exception:
            pass
        try:
            s.shutdown(socket.SHUT_WR)
        except Exception:
            pass

    threading.Thread(target=up, daemon=True).start()

    try:
        while True:
            d = s.recv(32768)
            if not d:
                break
            os.write(1, d)
    except Exception:
        pass
    os._exit(0)


if __name__ == "__main__":
    main()
