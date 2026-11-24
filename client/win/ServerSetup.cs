using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Renci.SshNet;

namespace Veil
{
    // Всё, что приложение делает на сервере по SSH: установка, выдача и отзыв ключей.
    public class SshTarget
    {
        public string Host = "";
        public int Port = 22;
        public string User = "root";
        public string Password = "";
        public string KeyPath = "";   // приватный ключ, если вместо пароля
        public string ProxyType = ""; // "" | socks5 — заходить через локальный туннель
        public string ProxyHost = "";
        public int ProxyPort;

        public ConnectionInfo Build()
        {
            var methods = new List<AuthenticationMethod>();
            if (!string.IsNullOrEmpty(KeyPath))
            {
                PrivateKeyFile kf = string.IsNullOrEmpty(Password)
                    ? new PrivateKeyFile(KeyPath)
                    : new PrivateKeyFile(KeyPath, Password);
                methods.Add(new PrivateKeyAuthenticationMethod(User, kf));
            }
            if (!string.IsNullOrEmpty(Password) || methods.Count == 0)
                methods.Add(new PasswordAuthenticationMethod(User, Password));
            ConnectionInfo ci = new ConnectionInfo(Host, Port, User, methods.ToArray());
            ci.Timeout = TimeSpan.FromSeconds(20);
            return ci;
        }
    }

    public static class ServerSetup
    {
        public static SshClient Connect(SshTarget t, int connectTimeoutSec = 20)
        {
            var c = new SshClient(t.Build());
            c.Connect();
            return c;
        }

        // Выполнить команду, вернуть stdout+stderr (stderr не роняет вызов).
        public static string Run(SshClient c, string cmd, int timeoutSec = 180)
        {
            using (SshCommand sc = c.CreateCommand("bash -lc " + Quote(cmd)))
            {
                sc.CommandTimeout = TimeSpan.FromSeconds(timeoutSec);
                string outp = sc.Execute();
                string err = sc.Error;
                return string.IsNullOrEmpty(err) ? outp : outp + "\n" + err;
            }
        }

        public static void Upload(SshClient c, byte[] data, string remotePath)
        {
            using (var sftp = new SftpClient(c.ConnectionInfo))
            {
                sftp.Connect();
                using (var ms = new MemoryStream(data))
                    sftp.UploadFile(ms, remotePath, true);
                sftp.ChangePermissions(remotePath, 0755);
                sftp.Disconnect();
            }
        }

        public static void UploadText(SshClient c, string text, string remotePath)
        {
            Upload(c, Encoding.UTF8.GetBytes(text.Replace("\r\n", "\n")), remotePath);
        }

        // Установка сервера «с нуля». Возвращает строку ключа veil:// и статус.
        public static string Install(SshClient c, byte[] linuxBin, string bootstrap,
                                     string domain, string log, out string keyLine)
        {
            keyLine = "";
            var sb = new StringBuilder();
            Upload(c, linuxBin, "/tmp/veil-server");
            UploadText(c, bootstrap, "/tmp/veil-bootstrap.sh");
            sb.AppendLine(Run(c, "chmod +x /tmp/veil-bootstrap.sh"));
            string args = domain == null || domain.Trim().Length == 0 ? "--no-domain" : "--domain " + domain.Trim();
            string outp = Run(c, "bash /tmp/veil-bootstrap.sh " + args, 600);
            sb.AppendLine(outp);
            foreach (string line in outp.Split('\n'))
            {
                string l = line.Trim();
                if (l.StartsWith("VEIL_KEY=")) keyLine = l.Substring(9).Trim();
                if (l.StartsWith("VEIL_PIN=") && keyLine.Length > 0) keyLine += "|" + l.Substring(9).Trim();
            }
            return sb.ToString();
        }

        // Список ключей: имена из /opt/veil/keys.txt
        // Установлен ли сервер: есть бинарник. Если юнит остановлен — поднимаем.
        // Возвращает true, если сервер готов принимать клиентов.
        public static string DefaultPsk(SshClient c)
        {
            string o = Run(c, "grep '^default:' /opt/veil/keys.txt 2>/dev/null | head -1 | cut -d: -f2");
            return o.Trim();
        }

        public static bool Installed(SshClient c)
        {
            string o = Run(c, "if [ -x /opt/veil/veil-server ]; then echo bin; else echo none; fi", 30);
            if (o.IndexOf("bin", StringComparison.OrdinalIgnoreCase) < 0) return false;
            Run(c, "systemctl is-active veil >/dev/null 2>&1 || systemctl start veil", 60);
            return true;
        }

        public static List<string> Keys(SshClient c)
        {
            var list = new List<string>();
            string outp = Run(c, "cut -d: -f1 /opt/veil/keys.txt 2>/dev/null");
            foreach (string line in outp.Split('\n'))
            {
                string l = line.Trim();
                if (l.Length > 0 && l.IndexOf("cut:") < 0) list.Add(l);
            }
            return list;
        }

        // Отпечаток сертификата сервера — нужен для ключей к самоподписанным серверам.
        // Имя из сертификата сервера — ключ всегда должен быть по домену, не по IP.
        public static string CertName(SshClient c)
        {
            string outp = Run(c, "openssl x509 -in /opt/veil/certs/fullchain.pem -noout -subject 2>/dev/null | sed 's/.*CN *= *//' | tr -d ' '");
            string cn = outp.Trim();
            return cn.Length > 0 && cn.Length < 200 && cn.IndexOf('\n') < 0 ? cn : "";
        }

        public static string Pin(SshClient c)
        {
            string outp = Run(c, "openssl x509 -in /opt/veil/certs/fullchain.pem -noout -fingerprint -sha256 2>/dev/null | sed 's/.*=//; s/://g' | tr 'A-Z' 'a-z'");
            string pin = outp.Trim();
            return pin.Length == 64 ? pin : "";
        }

        public static string RandomPsk()
        {
            const string alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";
            var buf = new byte[32];
            using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create()) rng.GetBytes(buf);
            var sb = new StringBuilder(32);
            foreach (byte b in buf) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }

        // Выдать ключ: дописать строку и перечитать файл без обрыва соединений.
        public static string Issue(SshClient c, string name, string host, int port, string sni, string pin)
        {
            string psk = RandomPsk();
            string n = Safe(name);
            Run(c, "printf '%s:%s\\n' " + Quote(n) + " " + Quote(psk) + " >> /opt/veil/keys.txt.tmp"
                + " && cat /opt/veil/keys.txt.tmp >> /opt/veil/keys.txt && rm -f /opt/veil/keys.txt.tmp"
                + " && chown root:veil /opt/veil/keys.txt && chmod 640 /opt/veil/keys.txt && systemctl reload veil");
            return Encode(host + ":" + port + "|" + (string.IsNullOrEmpty(sni) ? host : sni) + "|chrome|" + psk, pin);
        }

        // Добавить готовый PSK (выдача персонального ключа подписки).
        public static void AddKey(SshClient c, string name, string psk)
        {
            string n = Safe(name);
            Run(c, "grep -q '^" + n + ":' /opt/veil/keys.txt 2>/dev/null && sed -i " + Quote("/^" + n + ":/d") + " /opt/veil/keys.txt || true; " +
                    "printf '%s:%s\\n' " + Quote(n) + " " + Quote(psk) + " >> /opt/veil/keys.txt"
                    + " && chown root:veil /opt/veil/keys.txt && chmod 640 /opt/veil/keys.txt"
                    + " && systemctl reload veil");
        }

        // Положить блоб пула на раздатчик.
        public static void PublishBlob(SshClient c, byte[] blob, string name) { PublishBlob(c, blob, name, "/var/www/api"); }

        public static void PublishBlob(SshClient c, byte[] blob, string name, string root)
        {
            string dir = root.TrimEnd('/') + "/static/data";
            Run(c, "mkdir -p " + dir);
            Upload(c, blob, dir + "/" + name);
            Run(c, "chown www-data:www-data " + dir + "/" + name + " && chmod 644 " + dir + "/" + name
                 + " && ls -1t " + dir + " | tail -n +11 | xargs -r -I{} rm -f " + dir + "/{}");
        }

        // ── каскад и раздатчик ────────────────────────────────────────────
        // nginx со stream: ставим только если его нет, конфиг не трогаем.
        public static string EnsureNginxStream(SshClient c)
        {
            string has = Run(c, "command -v nginx >/dev/null && echo yes || echo no");
            if (has.IndexOf("yes") < 0)
            {
                Run(c, "export DEBIAN_FRONTEND=noninteractive; (apt-get update -qq && apt-get install -y -qq nginx libnginx-mod-stream) " +
                       "|| (dnf install -y nginx nginx-mod-stream) || (yum install -y nginx)", 600);
            }
            else
            {
                // модуль stream мог не приехать вместе с nginx
                Run(c, "ls /usr/lib/nginx/modules/ngx_stream_module.so >/dev/null 2>&1 || " +
                       "(export DEBIAN_FRONTEND=noninteractive; apt-get install -y -qq libnginx-mod-stream || dnf install -y nginx-mod-stream || true)", 600);
            }
            Run(c, "mkdir -p /etc/nginx/stream.d");
            string inc = Run(c, "grep -q 'stream.d' /etc/nginx/nginx.conf && echo yes || echo no");
            if (inc.IndexOf("yes") < 0)
            {
                // stream {} один на конфиг: вставляем блок перед http {, если его нет
                Run(c, "python3 - <<'PY'\n" +
                       "p='/etc/nginx/nginx.conf'\n" +
                       "s=open(p).read()\n" +
                       "if 'stream {' not in s:\n" +
                       "    s=s.replace('http {','stream {\\n    include /etc/nginx/stream.d/*.conf;\\n}\\n\\nhttp {',1)\n" +
                       "    open(p,'w').write(s)\n" +
                       "PY");
            }
            return Run(c, "nginx -v 2>&1");
        }

        // Раздатчик: кладём списки в корень сайта, который nginx уже отдаёт.
        // Свой server-блок не пишем — иначе он воюет с дефолтным сайтом хоста.
        // Возвращает каталог, доступный по http://<хост>/.
        public static string EnsurePublisher(SshClient c, string domain)
        {
            string o = Run(c, "grep -rh -o -E 'root[[:space:]]+[^;]+' /etc/nginx/sites-enabled /etc/nginx/conf.d 2>/dev/null | head -3");
            string root = "";
            foreach (string line in o.Split('\n'))
            {
                string l = line.Trim();
                if (!l.StartsWith("root")) continue;
                string p = l.Substring(4).Trim();
                if (p.Length > 1 && p.StartsWith("/") && !p.Contains("$")) { root = p; break; }
            }
            if (root.Length == 0) root = "/var/www/html";
            Run(c, "mkdir -p " + root + "/static/data && (chown -R www-data:www-data " + root + " || true) && chmod 755 " + root);
            if (domain.Length > 0)
                Run(c, "nginx -t >/dev/null 2>&1 && systemctl reload nginx || true");
            return root.TrimEnd('/');
        }

        // Свободный порт для релея (по умолчанию с 10000).
        public static int FreePort(SshClient c, int from)
        {
            string o = Run(c, "for p in $(seq " + from + " " + (from + 40) + "); do " +
                              "ss -ltn | grep -q ':'$p' ' || { echo $p; break; }; done");
            int p2;
            int.TryParse(o.Trim(), out p2);
            return p2;
        }

        // Существующий релей на этого получателя: возвращает порт или 0.
        public static int FindRelay(SshClient c, string targetHost, int targetPort)
        {
            string o = Run(c, "grep -l 'proxy_pass " + targetHost + ":" + targetPort + ";' /etc/nginx/stream.d/relay-*.conf 2>/dev/null | head -1");
            string f = o.Trim();
            if (f.Length == 0) return 0;
            string m = System.Text.RegularExpressions.Regex.Match(f, @"relay-(\d+)\.conf").Groups[1].Value;
            int p;
            return int.TryParse(m, out p) ? p : 0;
        }

        // Релей: приём на порту и передача в зарубежный сервер без расшифровки.
        public static void WriteRelay(SshClient c, int port, string targetHost, int targetPort, string label)
        {
            string conf = "# " + label + "\nserver {\n    listen " + port + ";\n    proxy_pass " + targetHost + ":" + targetPort + ";\n" +
                          "    proxy_connect_timeout 10s;\n    proxy_timeout 1h;\n}\n";
            Upload(c, Encoding.UTF8.GetBytes(conf), "/etc/nginx/stream.d/relay-" + port + ".conf");
            Run(c, "nginx -t && systemctl reload nginx");
        }

        // Проверка каскада: сертификат за портом должен быть от зарубежного сервера.
        public static string RelaySubject(SshClient c, int port, string sni)
        {
            string o = Run(c, "echo | timeout 10 openssl s_client -connect 127.0.0.1:" + port +
                              " -servername " + sni + " 2>/dev/null | openssl x509 -noout -subject 2>/dev/null", 40);
            return o.Trim();
        }

        public static void DeleteBlob(SshClient c, string pattern)
        {
            Run(c, "rm -f /var/www/api/static/data/" + pattern);
        }

        public static void Revoke(SshClient c, string name)
        {
            Run(c, "sed -i " + Quote("/^" + Safe(name).Replace("/", "\\/") + ":/d") + " /opt/veil/keys.txt && systemctl reload veil");
        }

        public static string Encode(string payload, string pin)
        {
            if (!string.IsNullOrEmpty(pin)) payload += "|" + pin;
            return "veil://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(payload))
                .Replace('+', '-').Replace('/', '_').TrimEnd('=');
        }

        static string Safe(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in (s ?? ""))
                if (char.IsLetterOrDigit(ch) || ch == '_' || ch == '-' || ch == '.') sb.Append(ch);
            return sb.Length == 0 ? "user" : sb.ToString();
        }

        static string Quote(string s) { return "'" + (s ?? "").Replace("'", "'\\''") + "'"; }
    }
}

namespace Veil
{
    public static class SshText
    {
        // Разэкранировать строку, пришедшую из JSON веб-страницы:
        // \" → ", \\ → \, \n, \r, \t и \uXXXX. Без этого Windows-путь
        // C:\\Users\\... приходит с удвоенными слэшами и File.Exists его не видит.
        public static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s) || s.IndexOf('\\') < 0) return s;
            var sb = new StringBuilder(s.Length);
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '\\' || i + 1 >= s.Length) { sb.Append(s[i]); continue; }
                char n = s[++i];
                if (n == 'n') sb.Append('\n');
                else if (n == 'r') sb.Append('\r');
                else if (n == 't') sb.Append('\t');
                else if (n == 'u' && i + 4 < s.Length)
                {
                    string hex = s.Substring(i + 1, 4);
                    int code;
                    if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                    { sb.Append((char)code); i += 4; }
                    else sb.Append(n);
                }
                else sb.Append(n);
            }
            return sb.ToString();
        }
    }

    // Данные SSH, сохранённые между запусками. Хост и логин лежат открыто,
    // секрет (пароль или путь к ключу) — через DPAPI, то есть расшифровать
    // их может только этот же пользователь Windows на этой машине.
    public static class SshCreds
    {
        public static string Host = "", User = "", Secret = "";
        public static int Port = 22;

        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                return Path.Combine(dir, "ssh.dat");
            }
        }

        public static bool Has { get { return Host.Length > 0; } }

        public static void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                string text = "host=" + Host + "\nport=" + Port + "\nuser=" + User + "\nsecret=" + Secret + "\n";
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(text), null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(FilePath, enc);
            }
            catch (Exception ex) { Program.Log("ssh save: " + ex.Message); }
        }

        public static bool Load()
        {
            try
            {
                if (!File.Exists(FilePath)) return false;
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                foreach (string line in Encoding.UTF8.GetString(raw).Split('\n'))
                {
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim(), v = line.Substring(i + 1).Trim();
                    if (k == "host") Host = v;
                    else if (k == "user") User = v;
                    else if (k == "secret") Secret = v;
                    else if (k == "port") { int p; if (int.TryParse(v, out p)) Port = p; }
                }
                // путь, сохранённый старой версией с удвоенными слэшами
                if (Secret.IndexOf("\\\\") >= 0)
                {
                    string fixedPath = Secret.Replace("\\\\", "\\");
                    if (File.Exists(fixedPath)) { Secret = fixedPath; Save(); }
                }
                return Host.Length > 0;
            }
            catch (Exception ex) { Program.Log("ssh load: " + ex.Message); return false; }
        }

        public static void Clear()
        {
            Host = ""; User = ""; Secret = ""; Port = 22;
            try { if (File.Exists(FilePath)) File.Delete(FilePath); } catch (Exception ex) { Program.Log("ssh clear: " + ex.Message); }
        }
    }
}
