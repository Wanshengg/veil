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


html = read("index.html")
tokens = read("tokens.css")
css = read("app.css")
js = read("app.js")

html = re.sub(r'<link rel="stylesheet" href="tokens\.css[^"]*">', "<style>\n" + tokens + "\n</style>", html)
html = re.sub(r'<link rel="stylesheet" href="app\.css[^"]*">', "<style>\n" + css + "\n</style>", html)
html = re.sub(r'<script src="app\.js[^"]*"></script>', "<script>\n" + js + "\n</script>", html)

out = os.path.join(DIST, "veil-ui.html")
with open(out, "w", encoding="utf-8", newline="\n") as f:
    f.write(html)
print("built", out, round(os.path.getsize(out) / 1024), "KB")
