using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Renci.SshNet;

namespace Veil
{
    // Выдача персонального ключа подписки.
    //
    //  1. новый PSK для человека;
    //  2. PSK добавляется в keys.txt на КАЖДОМ сервере пула (иначе он не примет
    //     ключ на чужом сервере) и служба перечитывает файл;
    //  3. личный блоб пула шифруется его PSK — чужой его не расшифрует;
    //  4. блоб кладётся на раздатчик, наружу отдаётся строка veil://…
    public class PoolHost
    {
        public string Label = "";      // «Швеция», «РУ-выход», «раздатчик»
        public string Host = "";
        public int Port = 22;
        public string User = "root";
        public string Secret = "";     // пароль или путь к ключу
        public bool ViaTunnel;         // подключаться через SOCKS veil (127.0.0.1:1080)
        public bool IsPublisher;       // на этом хосте лежит каталог с блобами
        public string Role = "";       // "ru" — РУ-сервер, через него каскад; "" — зарубежный

        public SshTarget ToTarget(string socks)
        {
            var t = new SshTarget { Host = Host, Port = Port, User = User };
            bool path = Secret.Contains("\\") || Secret.Contains("/");
            if (path) t.KeyPath = Secret; else t.Password = Secret;
            if (ViaTunnel && !string.IsNullOrEmpty(socks))
            {
                t.ProxyType = "socks5";
                t.ProxyHost = socks.Split(':')[0];
                t.ProxyPort = int.Parse(socks.Split(':')[1]);
            }
            return t;
        }

        public string Line()
        {
            return Label + "|" + Host + "|" + Port + "|" + User + "|" + Secret + "|" +
                   (ViaTunnel ? "1" : "0") + "|" + (IsPublisher ? "1" : "0") + "|" + Role;
        }

        public static PoolHost Parse(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 5) return null;
            var h = new PoolHost
            {
                Label = f[0], Host = f[1], Port = int.Parse(f[2]), User = f[3], Secret = f[4],
                ViaTunnel = f.Length > 5 && f[5] == "1",
                IsPublisher = f.Length > 6 && f[6] == "1",
                Role = f.Length > 7 ? f[7] : ""
            };
            return h;
        }
    }

    // Список серверов подписки: лежит на машине оператора, шифруется DPAPI.
    public static class PoolRegistry
    {
        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                return Path.Combine(dir, "pool_servers.dat");
            }
        }

        public static List<PoolHost> Load()
        {
            var list = new List<PoolHost>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                foreach (string line in Encoding.UTF8.GetString(raw).Split('\n'))
                {
                    string l = line.Trim();
                    if (l.Length == 0) continue;
                    PoolHost h = PoolHost.Parse(l);
                    if (h != null) list.Add(h);
                }
            }
            catch (Exception ex) { Program.Log("pool registry load: " + ex.Message); }
            return list;
        }

        public static void Save(List<PoolHost> list)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (PoolHost h in list) sb.Append(h.Line()).Append('\n');
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllBytes(FilePath, enc);
            }
            catch (Exception ex) { Program.Log("pool registry save: " + ex.Message); }
        }

        public static void Add(PoolHost h)
        {
            List<PoolHost> list = Load();
            list.RemoveAll(x => string.Equals(x.Host, h.Host, StringComparison.OrdinalIgnoreCase));
            list.Add(h);
            Save(list);
        }
    }

    public class IssuedKey
    {
        public string Name = "", Blob = "", Date = "";
        public string Line() { return Name + "|" + Blob + "|" + Date; }
        public static IssuedKey Parse(string l)
        {
            string[] f = l.Split('|');
            if (f.Length < 2) return null;
            return new IssuedKey { Name = f[0], Blob = f[1], Date = f.Length > 2 ? f[2] : "" };
        }
    }

    // Что мы выдали: имя, имя блоба, дата. Нужно, чтобы отзыв снимал и блоб.
    public static class IssuedKeys
    {
        static string FilePath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                return Path.Combine(dir, "issued_keys.dat");
            }
        }

        public static List<IssuedKey> Load()
        {
            var list = new List<IssuedKey>();
            try
            {
                if (!File.Exists(FilePath)) return list;
                byte[] raw = ProtectedData.Unprotect(File.ReadAllBytes(FilePath), null, DataProtectionScope.CurrentUser);
                foreach (string l in Encoding.UTF8.GetString(raw).Split('\n'))
                {
                    if (l.Trim().Length == 0) continue;
                    IssuedKey k = IssuedKey.Parse(l.Trim());
                    if (k != null) list.Add(k);
                }
            }
            catch (Exception ex) { Program.Log("issued keys load: " + ex.Message); }
            return list;
        }

        public static void Save(List<IssuedKey> list)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (IssuedKey k in list) sb.Append(k.Line()).Append('\n');
                byte[] enc = ProtectedData.Protect(Encoding.UTF8.GetBytes(sb.ToString()), null, DataProtectionScope.CurrentUser);
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllBytes(FilePath, enc);
            }
            catch (Exception ex) { Program.Log("issued keys save: " + ex.Message); }
        }

        public static void Add(string name, string blob)
        {
            List<IssuedKey> list = Load();
            list.RemoveAll(x => x.Name == name);
            list.Add(new IssuedKey { Name = name, Blob = blob, Date = DateTime.Now.ToString("yyyy-MM-dd") });
            Save(list);
        }

        public static void Remove(string name)
        {
            List<IssuedKey> list = Load();
            list.RemoveAll(x => x.Name == name);
            Save(list);
        }
    }

    // Сборка каскада: РУ-сервер (релей + раздатчик + выход для РУ) и зарубежный.
    public static class Cascade
    {
        public class Out
        {
            public bool Ok;
            public string Log = "";
            public string Key = "";
            public string Blob = "";
            public string PoolUrl = "";
            public string ForeignEntry = "";
            public string RuEntry = "";
        }

        public static Out Build(PoolHost ru, string ruDomain, PoolHost foreign, string foreignDomain,
                                string foreignCc, string foreignCountry,
                                Func<string, string, string, string> ensureInstalled,   // (target, domain, what) → psk или ""
                                string socks)
        {
            var o = new Out();
            var sb = new StringBuilder();
            try
            {
                // 1. зарубежный сервер
                sb.AppendLine("зарубежный: " + foreign.Host);
                string foreignPsk = ensureInstalled(foreign.ToTarget(socks).Host + "|" + foreign.Port + "|" + foreign.User + "|" + foreign.Secret,
                                                    foreignDomain, "зарубежный");
                if (foreignPsk.Length == 0) throw new InvalidOperationException("зарубежный сервер не установлен");

                // 2. РУ-сервер: он же выход для РУ-адресов
                sb.AppendLine("РУ-сервер: " + ru.Host);
                string ruPsk = ensureInstalled(ru.ToTarget(socks).Host + "|" + ru.Port + "|" + ru.User + "|" + ru.Secret,
                                               ruDomain, "РУ");
                if (ruPsk.Length == 0) throw new InvalidOperationException("РУ-сервер не установлен");

                // один PSK на весь пул: берём с РУ и ставим зарубежному
                if (ruPsk != foreignPsk)
                {
                    using (SshClient cF = ServerSetup.Connect(foreign.ToTarget(socks), 25))
                    {
                        ServerSetup.AddKey(cF, "default", ruPsk);
                        sb.AppendLine("PSK зарубежного приведён к общему");
                    }
                }
                string pubRoot = "/var/www/api";
                string ruHostForRelay = string.IsNullOrEmpty(ruDomain) ? ru.Host : ruDomain;
                string fHost = string.IsNullOrEmpty(foreignDomain) ? foreign.Host : foreignDomain;
                int port = 0;
                bool https = false;

                using (SshClient cRu = ServerSetup.Connect(ru.ToTarget(socks), 25))
                {
                    ServerSetup.EnsureNginxStream(cRu);
                    pubRoot = ServerSetup.EnsurePublisher(cRu, ruDomain);
                    sb.AppendLine("РУ: nginx + раздатчик готовы");

                    // HTTPS на РУ не поднимаем: там же живёт veil, а порт 80 занят
                    // дефолтным сайтом — раздача списков идёт по http, блоб зашифрован.

                    port = ServerSetup.FindRelay(cRu, fHost, 443);
                    if (port > 0) sb.AppendLine("релей уже есть: " + port + " → " + fHost + ":443");
                    else
                    {
                        port = ServerSetup.FreePort(cRu, 10000);
                        if (port == 0) throw new InvalidOperationException("нет свободного порта для релея");
                        ServerSetup.WriteRelay(cRu, port, fHost, 443, "каскад → " + fHost);
                    }
                    sb.AppendLine("релей: " + port + " → " + fHost + ":443");

                    string subj = ServerSetup.RelaySubject(cRu, port, fHost);
                    sb.AppendLine("проверка сертификата через релей: " + (subj.Length > 0 ? subj : "не ответил"));
                    if (subj.Length == 0) throw new InvalidOperationException("каскад не отвечает");
                }

                var ruSrv = new PoolServer { Id = "ru", Host = ruHostForRelay, Sni = ruHostForRelay, Cc = "RU", Country = "Россия", City = "Россия", Up = true };
                ruSrv.Ports.AddRange(new[] { 8443, 2083, 9443 });
                var viaSrv = new PoolServer
                {
                    Id = "via-ru", Host = ruHostForRelay, Sni = fHost,
                    Cc = foreignCc.Length > 0 ? foreignCc : "XX",
                    Country = foreignCountry.Length > 0 ? foreignCountry : "Зарубежный",
                    City = "через РУ", Up = true
                };
                viaSrv.Ports.Add(port);

                o.PoolUrl = "http://" + (ruDomain.Length > 0 ? ruDomain : ru.Host) + "/static/data";
                sb.AppendLine("адрес раздатчика: " + o.PoolUrl);

                // 3. пул: РУ как выход + каскад на зарубежный, с сохранением прежних серверов
                var doc = new PoolDoc { Name = "veil", Issued = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), Ttl = 21600 };
                string prev = "";
                try
                {
                    string prevUrl = Path.Combine(Engine.Dir, "pool_url.txt");
                    if (File.Exists(prevUrl))
                    {
                        prev = File.ReadAllText(prevUrl).Trim();
                        if (prev.EndsWith("/static/data", StringComparison.Ordinal)) prev = "";
                    }
                }
                catch { }
                if (prev.Length > 0)
                {
                    try
                    {
                        string used2;
                        PoolDoc old = PoolClient.Fetch(new[] { prev }, ruPsk, "", PoolIssue.PublicKey, out used2);
                        foreach (PoolServer s in old.Servers)
                        {
                            bool sameRu = s.Id == "ru";
                            bool sameVia = s.Id == "via-ru" && string.Equals(s.Sni, fHost, StringComparison.OrdinalIgnoreCase);
                            if (sameRu || sameVia) continue;
                            doc.Servers.Add(s);
                        }
                        sb.AppendLine("прежний пул: серверов " + old.Servers.Count + " → остаётся " + doc.Servers.Count);
                    }
                    catch (Exception ex) { sb.AppendLine("прежний пул не прочитан: " + ex.Message); }
                }
                doc.Servers.Insert(0, ruSrv);
                doc.Servers.Add(viaSrv);
                o.RuEntry = ruSrv.Endpoint;
                o.ForeignEntry = viaSrv.Endpoint;
                string json = PoolJson.Write(doc);
                byte[] blob = PoolCodec.Pack(json, LoadSignKey(), ruPsk);
                string blobName = "pool-" + DateTime.UtcNow.ToString("yyMMddHHmm");
                using (SshClient cRu2 = ServerSetup.Connect(ru.ToTarget(socks), 25))
                {
                    ServerSetup.PublishBlob(cRu2, blob, blobName, pubRoot);
                    string vhost = ruDomain.Length > 0 ? ruDomain : "_";
                    string code = ServerSetup.Run(cRu2, "curl -s -o /dev/null -w '%{http_code}' -H 'Host: " + vhost +
                                                       "' http://127.0.0.1/static/data/" + blobName, 40).Trim();
                    sb.AppendLine("отдача списка: " + code + " (" + o.PoolUrl + ")");
                    if (code != "200") throw new InvalidOperationException("блоб не отдаётся (код " + code + ")");
                }
                o.Blob = blobName;
                sb.AppendLine("пул выложен: " + blobName + " (" + blob.Length + " байт)");

                // 4. ключ владельцу
                string sub = o.PoolUrl + "/" + blobName;
                o.Key = KeyInfo.MakeV2(sub, "", "", "chrome", ruPsk, "", PoolIssue.PublicKey);
                o.Ok = true;
                sb.AppendLine("ключ подписки готов");
            }
            catch (Exception ex)
            {
                o.Log = sb.ToString() + "ОШИБКА: " + ex.Message;
                Program.Log("cascade: " + ex);
                return o;
            }
            o.Log = sb.ToString();
            return o;
        }

        static byte[] LoadSignKey() { return PoolIssue.SignKeyBytes(); }
    }

    public static class PoolIssue
    {
        // Ключ подписи пула (Ed25519). Лежит у оператора, DPAPI.
        static string SignKeyPath
        {
            get
            {
                string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                return Path.Combine(dir, "pool_signing.dat");
            }
        }

        public static bool HasSignKey { get { return File.Exists(SignKeyPath); } }

        public static void ImportSignKey(string pathFrom)
        {
            byte[] priv = PoolCodec.KeyFromBase64(File.ReadAllText(pathFrom));
            byte[] enc = ProtectedData.Protect(priv, null, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(SignKeyPath, enc);
        }

        public static byte[] SignKeyBytes() { return LoadSignKey(); }

        static byte[] LoadSignKey()
        {
            if (!File.Exists(SignKeyPath)) throw new InvalidOperationException("нет ключа подписи пула");
            return ProtectedData.Unprotect(File.ReadAllBytes(SignKeyPath), null, DataProtectionScope.CurrentUser);
        }

        public static string PublicKey
        {
            get { return PoolCodec.KeyToBase64(PoolCodec.PublicFromPrivate(LoadSignKey())); }
        }

        public class Result
        {
            public bool Ok;
            public string Key = "";
            public string Log = "";
        }

        // servers     — на кого добавить PSK (все серверы пула)
        // publisher   — хост, на котором лежит каталог блобов
        // poolUrlBase — https://хост/static/data
        // poolJson    — список серверов, который увидit человек
        public static Result Issue(string name, string psk, List<PoolHost> servers, PoolHost publisher,
                                   string poolUrlBase, string poolJson, string seed, string seedSni, string socks)
        {
            var res = new Result();
            var sb = new StringBuilder();
            try
            {
                if (string.IsNullOrWhiteSpace(name)) throw new InvalidOperationException("нужно имя");
                if (servers.Count == 0) throw new InvalidOperationException("нет серверов подписки");
                if (publisher == null) throw new InvalidOperationException("не выбран раздатчик списка");

                string safeName = MakeSafe(name);
                sb.AppendLine("PSK для «" + name + "» создан, длина " + psk.Length);

                // 1. ключ на всех серверах
                foreach (PoolHost h in servers)
                {
                    if (publisher != null && string.Equals(h.Host, publisher.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("пропущен раздатчик: " + (h.Label.Length > 0 ? h.Label : h.Host));
                        continue;
                    }
                    using (SshClient c = ServerSetup.Connect(h.ToTarget(socks), 20))
                    {
                        ServerSetup.AddKey(c, safeName, psk);
                        sb.AppendLine("ключ добавлен: " + (h.Label.Length > 0 ? h.Label : h.Host));
                    }
                }

                // 2. личный блоб, зашифрованный его PSK
                byte[] blob = PoolCodec.Pack(poolJson, LoadSignKey(), psk);
                string blobName = MakeSafe(name).Replace(" ", "-") + "-" + RandomName();
                sb.AppendLine("блоб собран: " + blob.Length + " байт");

                // 3. публикация
                using (SshClient c = ServerSetup.Connect(publisher.ToTarget(socks), 20))
                {
                    ServerSetup.PublishBlob(c, blob, blobName);
                    sb.AppendLine("блоб выложен: " + blobName);
                }

                // 4. строка ключа
                string key = KeyInfo.MakeV2(poolUrlBase.TrimEnd('/') + "/" + blobName,
                                            seed, seedSni, "chrome", psk, "", PublicKey);
                res.Key = key;
                IssuedKeys.Add(safeName, blobName);
                res.Ok = true;
                sb.AppendLine("готово");
            }
            catch (Exception ex)
            {
                res.Log = sb.ToString() + "ОШИБКА: " + ex.Message;
                Program.Log("pool issue: " + ex);
                return res;
            }
            res.Log = sb.ToString();
            return res;
        }

        // Простой ключ на один сервер (РУ): без списка, без личного блоба.
        public static Result IssueOne(string name, string psk, PoolHost server, string endpoint, string sni, string socks)
        {
            var res = new Result();
            var sb = new StringBuilder();
            try
            {
                string safeName = MakeSafe(name);
                if (server == null) throw new InvalidOperationException("нет РУ-сервера в списке");
                if (endpoint.Length == 0) throw new InvalidOperationException("не нашёл адрес РУ-сервера в пуле");

                string pin = "";
                using (SshClient c = ServerSetup.Connect(server.ToTarget(socks), 20))
                {
                    ServerSetup.AddKey(c, safeName, psk);
                    pin = ServerSetup.Pin(c);
                    sb.AppendLine("ключ добавлен: " + (server.Label.Length > 0 ? server.Label : server.Host));
                }

                string key = KeyInfo.MakeV2("", endpoint, sni, "chrome", psk, pin, "");
                res.Key = key;
                res.Ok = true;
                IssuedKeys.Add(safeName, "");
                sb.AppendLine("pin: " + (pin.Length > 0 ? pin.Substring(0, 16) + "…" : "нет"));
                sb.AppendLine("готово");
            }
            catch (Exception ex)
            {
                res.Log = sb.ToString() + "ОШИБКА: " + ex.Message;
                Program.Log("pool issue-one: " + ex);
                return res;
            }
            res.Log = sb.ToString();
            return res;
        }

        // Список имён, которые реально лежат в keys.txt на серверах
        // (переживает переустановку приложения: читаем с серверов, а не из локального файла).
        public static List<string> NamesOnServers(List<PoolHost> servers, PoolHost publisher, string socks, out string log)
        {
            var sb = new StringBuilder();
            var names = new List<string>();
            foreach (PoolHost h in servers)
            {
                if (publisher != null && string.Equals(h.Host, publisher.Host, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    using (SshClient c = ServerSetup.Connect(h.ToTarget(socks), 20))
                    {
                        foreach (string n in ServerSetup.Keys(c))
                        {
                            if (n == "default" || names.Contains(n)) continue;
                            names.Add(n);
                        }
                        sb.AppendLine("прочитан: " + (h.Label.Length > 0 ? h.Label : h.Host));
                    }
                }
                catch (Exception ex) { sb.AppendLine("не ответил: " + h.Host + " — " + ex.Message); }
            }
            log = sb.ToString();
            return names;
        }

        // Отзыв: убрать ключ со всех серверов и снять личный блоб.
        public static Result Revoke(string name, List<PoolHost> servers, PoolHost publisher, string socks)
        {
            var res = new Result();
            var sb = new StringBuilder();
            try
            {
                string safeName = MakeSafe(name);
                if (safeName.Length == 0) throw new InvalidOperationException("нужно имя");
                if (servers.Count == 0) throw new InvalidOperationException("нет серверов подписки");

                int done = 0;
                foreach (PoolHost h in servers)
                {
                    if (publisher != null && string.Equals(h.Host, publisher.Host, StringComparison.OrdinalIgnoreCase))
                    {
                        sb.AppendLine("пропущен раздатчик: " + (h.Label.Length > 0 ? h.Label : h.Host));
                        continue;
                    }
                    using (SshClient c = ServerSetup.Connect(h.ToTarget(socks), 20))
                    {
                        ServerSetup.Revoke(c, safeName);
                        done++;
                        sb.AppendLine("ключ снят: " + (h.Label.Length > 0 ? h.Label : h.Host));
                    }
                }

                // блоб: точное имя из реестра, иначе всё family <name>-*
                IssuedKey rec = IssuedKeys.Load().Find(x => x.Name == safeName);
                if (publisher != null)
                {
                    using (SshClient c = ServerSetup.Connect(publisher.ToTarget(socks), 20))
                    {
                        string pattern = rec != null && rec.Blob.Length > 0 ? rec.Blob : safeName + "-*";
                        ServerSetup.DeleteBlob(c, pattern);
                        sb.AppendLine("блоб снят: " + pattern);
                    }
                }
                IssuedKeys.Remove(safeName);
                res.Ok = done > 0;
                sb.AppendLine(done > 0 ? "готово" : "ОШИБКА: ни на одном сервере ключ не снят");
                if (!res.Ok) throw new InvalidOperationException("ключ не снят ни с одного сервера");
            }
            catch (Exception ex)
            {
                res.Log = sb.ToString() + "ОШИБКА: " + ex.Message;
                Program.Log("pool revoke: " + ex);
                return res;
            }
            res.Log = sb.ToString();
            return res;
        }

        public static string Normalize(string s) { return MakeSafe(s); }

        static string MakeSafe(string s)
        {
            var sb = new StringBuilder();
            foreach (char c in (s ?? "").Trim().ToLowerInvariant())
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ') sb.Append('-');
            return sb.Length == 0 ? "user" : sb.ToString();
        }

        static string RandomName()
        {
            var r = new RNGCryptoServiceProvider();
            byte[] buf = new byte[8];
            r.GetBytes(buf);
            var sb = new StringBuilder();
            foreach (byte b in buf) sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        public static string NewPsk()
        {
            const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz23456789";
            var r = new RNGCryptoServiceProvider();
            byte[] buf = new byte[40];
            r.GetBytes(buf);
            var sb = new StringBuilder(40);
            foreach (byte b in buf) sb.Append(alphabet[b % alphabet.Length]);
            return sb.ToString();
        }
    }
}
