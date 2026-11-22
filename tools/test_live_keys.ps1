$ErrorActionPreference = "Continue"
$root = "Y:\hermes\veil"
$psk = (Get-Content "$env:TEMP\psk.txt" -Raw).Trim()

function Try-Key($name, $key, $port) {
  $argline = "-socks 127.0.0.1:$port -http= -server se.example.com:443 -sni se.example.com -psk $key"
  $p = Start-Process -FilePath "$root\bin\veil-client.exe" -ArgumentList $argline -PassThru -WindowStyle Hidden
  Start-Sleep -Seconds 5
  $ip = & curl.exe -s --max-time 25 --socks5-hostname "127.0.0.1:$port" https://api.ipify.org
  if ($p) { Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue }
  "{0,-10} ip={1}" -f $name, ($ip -replace "`r|`n","")
  Start-Sleep -Seconds 1
}

Try-Key "существующий" $psk 18091
