using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            var ci = new ConnectionInfo(Host, Port, User, methods.ToArray());
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
            Run(c, "printf '%s:%s\\n' " + Quote(n) + " " + Quote(psk) + " >> /opt/veil/keys.txt && chmod 600 /opt/veil/keys.txt && systemctl reload veil");
            return Encode(host + ":" + port + "|" + (string.IsNullOrEmpty(sni) ? host : sni) + "|chrome|" + psk, pin);
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
