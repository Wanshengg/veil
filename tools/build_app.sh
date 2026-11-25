#!/usr/bin/env bash
# Сборка Windows-приложения veil: UI → ресурсы → csc → один exe.
set -euo pipefail
cd "$(dirname "$0")/.."

export PATH="/c/Users/RS34/tools/go/bin:$PATH"
GOOS=windows GOARCH=amd64 go build -trimpath -ldflags "-s -w" -o dist/veil-client/veil-client.exe ./cmd/veil-client
GOOS=linux  GOARCH=amd64 go build -trimpath -ldflags "-s -w" -o dist/veil-client/veil-server-linux ./cmd/veil-server
[ -f dist/veil-client/wintun.dll ] || cp /c/Users/RS34/tools/wintun/wintun/bin/amd64/wintun.dll dist/veil-client/ 2>/dev/null || true
python tools/build_ui.py

CSC=/c/Windows/Microsoft.NET/Framework64/v4.0.30319/csc.exe
SEC=C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Security.dll
HTTP=C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Net.Http.dll
ZIP=C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.IO.Compression.dll
ZIPF=C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.IO.Compression.FileSystem.dll
SSH=client\\win\\sshnet
WEB=client\\win\\webview2

"$CSC" /nologo /target:winexe /platform:x64 /codepage:65001 /win32icon:client\\win\\veil.ico /out:dist\\veil.exe \
  /r:$SEC /r:$HTTP /r:$ZIP /r:$ZIPF /r:$WEB\\Microsoft.Web.WebView2.Core.dll /r:$WEB\\Microsoft.Web.WebView2.WinForms.dll \
  /r:$SSH\\Renci.SshNet.dll /r:$SSH\\BouncyCastle.Cryptography.dll \
  /r:$SSH\\Microsoft.Bcl.AsyncInterfaces.dll /r:$SSH\\System.Memory.dll \
  /r:$SSH\\System.Buffers.dll /r:$SSH\\System.Runtime.CompilerServices.Unsafe.dll \
  /r:$SSH\\System.Numerics.Vectors.dll \
  /resource:dist\\veil-client\\veil-client.exe,veil-client.exe \
  /resource:dist\\veil-client\\wintun.dll,wintun.dll \
  /resource:third_party\\zstd.exe,zstd.exe \
  /resource:dist\\veil-client\\veil-server-linux,veil-server-linux \
  /resource:deploy\\bootstrap.sh,bootstrap.sh \
  /resource:$SSH\\Renci.SshNet.dll,Renci.SshNet.dll \
  /resource:$SSH\\BouncyCastle.Cryptography.dll,BouncyCastle.Cryptography.dll \
  /resource:$SSH\\Microsoft.Bcl.AsyncInterfaces.dll,Microsoft.Bcl.AsyncInterfaces.dll \
  /resource:$SSH\\System.Memory.dll,System.Memory.dll \
  /resource:$SSH\\System.Buffers.dll,System.Buffers.dll \
  /resource:$SSH\\System.Runtime.CompilerServices.Unsafe.dll,System.Runtime.CompilerServices.Unsafe.dll \
  /resource:$SSH\\System.Numerics.Vectors.dll,System.Numerics.Vectors.dll \
  /resource:client\\win\\veil.ico,veil.ico \
  /resource:ui\\index.html,index.html /resource:ui\\app.css,app.css \
  /resource:ui\\app.js,app.js /resource:ui\\tokens.css,tokens.css \
  /resource:$WEB\\Microsoft.Web.WebView2.Core.dll,Microsoft.Web.WebView2.Core.dll \
  /resource:$WEB\\Microsoft.Web.WebView2.WinForms.dll,Microsoft.Web.WebView2.WinForms.dll \
  /resource:$WEB\\WebView2Loader.dll,WebView2Loader.dll \
  client\\\\win\\\\VeilApp.cs client\\\\win\\\\ServerSetup.cs client\\\\win\\\\Pool.cs client\\\\win\\\\PoolCodec.cs client\\\\win\\\\PoolIssue.cs client\\\\win\\\\Zapret.cs

echo "built: $(du -h dist/veil.exe | cut -f1)"
