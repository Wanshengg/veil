$ErrorActionPreference = "Continue"
$root = "Y:\hermes\veil"
$cases = @(
  @{ name = "alice";  psk = "alicepsk0123456789abcdef0123456789"; port = 18081 },
  @{ name = "bob";    psk = "bobpsk9876543210fedcba9876543210abcd"; port = 18082 },
  @{ name = "чужой";  psk = "wrongpsk000000000000000000000000000"; port = 18083 }
)

foreach ($c in $cases) {
  $argline = "-socks 127.0.0.1:$($c.port) -http= -server 127.0.0.1:18443 -sni localhost -insecure -psk $($c.psk)"
  $p = Start-Process -FilePath "$root\bin\veil-client.exe" -ArgumentList $argline -PassThru -WindowStyle Hidden
  Start-Sleep -Seconds 4
  $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 20 --socks5-hostname "127.0.0.1:$($c.port)" https://example.com/
  Stop-Process -Id $p.Id -Force -ErrorAction SilentlyContinue
  "{0,-8} http={1}" -f $c.name, $code
  Start-Sleep -Seconds 1
}
