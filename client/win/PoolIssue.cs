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
                   (ViaTunnel ? "1" : "0") + "|" + (IsPublisher ? "1" : "0");
        }

        public static PoolHost Parse(string line)
        {
            string[] f = line.Split('|');
            if (f.Length < 5) return null;
            var h = new PoolHost
            {
                Label = f[0], Host = f[1], Port = int.Parse(f[2]), User = f[3], Secret = f[4],
                ViaTunnel = f.Length > 5 && f[5] == "1",
                IsPublisher = f.Length > 6 && f[6] == "1"
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
