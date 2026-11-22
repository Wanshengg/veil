#!/usr/bin/env python3
"""Собирает ui/ в один файл dist/veil-ui.html для встраивания в WebView2."""
import os
import re

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
UI = os.path.join(ROOT, "ui")
DIST = os.path.join(ROOT, "dist")
os.makedirs(DIST, exist_ok=True)


def read(name):
    with open(os.path.join(UI, name), encoding="utf-8") as f:
        return f.read()



BRIDGE = """
<script>
/* мост WebView2: тот же интерфейс адаптера, но команды уходят в exe */
(() => {
  const wv = window.chrome && window.chrome.webview;
  if (!wv) return;
  let cb = null, servers = [];
  const post = (m) => wv.postMessage(m);
  wv.addEventListener("message", (e) => {
    const m = e.data;
    if (!m || m.type !== "snapshot") return;
    if (m.servers) servers = m.servers;
    if (cb) cb(m.snapshot);
  });
  const host = {
    subscribe(fn) { cb = fn; post({ cmd: "ready" }); },
    serverList: () => servers,
    connect: () => post({ cmd: "connect" }),
    disconnect: () => post({ cmd: "disconnect" }),
    selectServer: (id) => post({ cmd: "select", id }),
    toggleFav: (id) => post({ cmd: "fav", id }),
    setSetting: (k, v) => post({ cmd: "setting", key: k, value: String(v) }),
    refresh: () => post({ cmd: "refresh" }),
  };
  if (window.VEIL_UI) window.VEIL_UI.setAdapter(host);
})();
</script>
"""

html = read("index.html")
tokens = read("tokens.css")
css = read("app.css")
js = read("app.js")

html = re.sub(r'<link rel="stylesheet" href="tokens\.css[^"]*">', lambda m: "<style>\n" + tokens + "\n</style>", html)
html = re.sub(r'<link rel="stylesheet" href="app\.css[^"]*">', lambda m: "<style>\n" + css + "\n</style>", html)
html = re.sub(r'<script src="app\.js[^"]*"></script>', lambda m: "<script>\n" + js + "\n</script>", html)

html = html.replace("</body>", BRIDGE + "</body>")

# отдельная версия страницы для WebView2 (внешние css/js + мост)
host = html.replace('href="tokens.css?v=3"', 'href="tokens.css"') \
           .replace('href="app.css?v=3"', 'href="app.css"') \
           .replace('src="app.js?v=3"', 'src="app.js"')
with open(os.path.join(UI, "host.html"), "w", encoding="utf-8", newline="\n") as f:
    f.write(host)

out = os.path.join(DIST, "veil-ui.html")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write(html)
print("built", out, round(os.path.getsize(out) / 1024), "KB")
