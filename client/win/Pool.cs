using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Cache;
using System.Security.Cryptography;
using System.Text;

namespace Veil
{
    // Ключ veil:// бывает двух видов:
    //   v1: "host:port|sni|fp|psk"
    //   v2: "v2|sub=url1,url2|seed=host:port|sni=...|fp=chrome|psk=...|pin=..."
    public class KeyInfo
    {
        public string Server = "", Sni = "", Fp = "chrome", Psk = "", Pin = "";
        public string Sub = "", Seed = "";
        public string Pk = "";           // публичный ключ пула (своя подписка); пусто → встроенный
        public bool IsV2;

        public string[] Subs { get { return Sub.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries); } }

        public static KeyInfo Parse(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            int i = text.IndexOf("veil://", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;

            string b = text.Substring(i + 7).Trim().Replace('-', '+').Replace('_', '/');
            while (b.Length % 4 != 0) b += "=";
            string s;
            try { s = Encoding.UTF8.GetString(Convert.FromBase64String(b)); }
            catch { return null; }

            var ki = new KeyInfo();
            if (s.StartsWith("v2|", StringComparison.Ordinal))
            {
                ki.IsV2 = true;
                foreach (string part in s.Split('|'))
                {
                    int eq = part.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = part.Substring(0, eq).ToLowerInvariant(), v = part.Substring(eq + 1);
                    if (k == "sub") ki.Sub = v;
                    else if (k == "seed") ki.Seed = v;
                    else if (k == "sni") ki.Sni = v;
                    else if (k == "fp") ki.Fp = v;
                    else if (k == "psk") ki.Psk = v;
                    else if (k == "pin") ki.Pin = v;
                    else if (k == "pk") ki.Pk = v;
                }
                if (ki.Fp.Length == 0) ki.Fp = "chrome";
                if (ki.Psk.Length < 8) return null;
                ki.Server = ki.Seed;
                if (ki.Sni.Length == 0 && ki.Seed.Length > 0) ki.Sni = ki.Seed.Split(':')[0];
                if (ki.Sub.Length == 0 && ki.Seed.Length == 0) return null;
                return ki;
            }

            string[] p = s.Split('|');
            if (p.Length < 4 || p[0].Length == 0 || p[3].Length < 8) return null;
            ki.Server = p[0];
            ki.Sni = p[1].Length > 0 ? p[1] : p[0].Split(':')[0];
            ki.Fp = p[2].Length > 0 ? p[2] : "chrome";
            ki.Psk = p[3];
            if (p.Length > 4) ki.Pin = p[4].Trim();
            return ki;
        }

        public static string MakeV2(string sub, string seed, string sni, string fp, string psk, string pin, string pk)
        {
            var sb = new StringBuilder("v2");
            if (!string.IsNullOrEmpty(pk)) sb.Append("|pk=").Append(pk);
            if (!string.IsNullOrEmpty(sub)) sb.Append("|sub=").Append(sub);
            if (!string.IsNullOrEmpty(seed)) sb.Append("|seed=").Append(seed);
            if (!string.IsNullOrEmpty(sni)) sb.Append("|sni=").Append(sni);
            sb.Append("|fp=").Append(string.IsNullOrEmpty(fp) ? "chrome" : fp);
            sb.Append("|psk=").Append(psk);
            if (!string.IsNullOrEmpty(pin)) sb.Append("|pin=").Append(pin);
            byte[] raw = Encoding.UTF8.GetBytes(sb.ToString());
            return "veil://" + Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }
    }

    // ── минимальный JSON ───────────────────────────────────────────────────
    // Схема пула фиксированная, поэтому полноценный парсер не нужен: объекты,
    // массивы, строки, числа, true/false. Всё, что не влезает в схему, — ошибка.
    public static class Json
    {
        public static object Parse(string s)
        {
            int i = 0;
            object v = Value(s, ref i);
            Skip(s, ref i);
            if (i < s.Length) throw new FormatException("json: лишние данные в позиции " + i);
            return v;
        }

        static void Skip(string s, ref int i)
        {
            while (i < s.Length && (s[i] == ' ' || s[i] == '\n' || s[i] == '\r' || s[i] == '\t')) i++;
        }

        static object Value(string s, ref int i)
        {
            Skip(s, ref i);
            if (i >= s.Length) throw new FormatException("json: конец строки");
            char c = s[i];
            if (c == '{') return Obj(s, ref i);
            if (c == '[') return Arr(s, ref i);
            if (c == '"') return Str(s, ref i);
            if (c == 't' || c == 'f') return Bool(s, ref i);
            if (c == 'n') { Num(s, ref i); return null; }
            return Num(s, ref i);
        }

        static Dictionary<string, object> Obj(string s, ref int i)
        {
            var d = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == '}') { i++; return d; }
            while (true)
            {
                Skip(s, ref i);
                string k = Str(s, ref i);
                Skip(s, ref i);
                if (s[i] != ':') throw new FormatException("json: ждали ':' в позиции " + i);
                i++;
                d[k] = Value(s, ref i);
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("json: не закрыт объект");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == '}') { i++; return d; }
                throw new FormatException("json: ждали ',' или '}' в позиции " + i);
            }
        }

        static List<object> Arr(string s, ref int i)
        {
            var l = new List<object>();
            i++; Skip(s, ref i);
            if (i < s.Length && s[i] == ']') { i++; return l; }
            while (true)
            {
                l.Add(Value(s, ref i));
                Skip(s, ref i);
                if (i >= s.Length) throw new FormatException("json: не закрыт массив");
                if (s[i] == ',') { i++; continue; }
                if (s[i] == ']') { i++; return l; }
                throw new FormatException("json: ждали ',' или ']' в позиции " + i);
            }
        }

        static string Str(string s, ref int i)
        {
            if (s[i] != '"') throw new FormatException("json: ждали строку в позиции " + i);
            i++;
            var sb = new StringBuilder();
            while (i < s.Length)
            {
                char c = s[i++];
                if (c == '"') return sb.ToString();
                if (c != '\\') { sb.Append(c); continue; }
                char e = s[i++];
                switch (e)
                {
                    case 'n': sb.Append('\n'); break;
                    case 'r': sb.Append('\r'); break;
                    case 't': sb.Append('\t'); break;
                    case 'b': sb.Append('\b'); break;
                    case 'f': sb.Append('\f'); break;
                    case 'u':
                        sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber));
                        i += 4; break;
                    default: sb.Append(e); break;
                }
            }
            throw new FormatException("json: не закрыта строка");
        }

        static object Bool(string s, ref int i)
        {
            if (string.CompareOrdinal(s, i, "true", 0, 4) == 0) { i += 4; return true; }
            if (string.CompareOrdinal(s, i, "false", 0, 5) == 0) { i += 5; return false; }
            throw new FormatException("json: ждали true/false в позиции " + i);
        }

        static object Num(string s, ref int i)
        {
            int st = i;
            while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
            string t = s.Substring(st, i - st);
            double d;
            if (t.Length == 0 || !double.TryParse(t, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                throw new FormatException("json: число в позиции " + st);
            return d;
        }

        public static Dictionary<string, object> AsObj(object o) { return o as Dictionary<string, object>; }
        public static List<object> AsArr(object o) { return o as List<object>; }
        public static string S(Dictionary<string, object> o, string k, string def = "")
        {
            object v; return o != null && o.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : def;
        }
        public static int I(Dictionary<string, object> o, string k, int def = 0)
        {
            object v; if (o == null || !o.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToInt32(v); } catch { return def; }
        }
        public static bool B(Dictionary<string, object> o, string k, bool def = false)
        {
            object v; if (o == null || !o.TryGetValue(k, out v) || v == null) return def;
            try { return Convert.ToBoolean(v); } catch { return def; }
        }
    }

    // ── документ пула ──────────────────────────────────────────────────────

    public class PoolServer
    {
        public string Id = "", Host = "", Sni = "", Cc = "", Country = "";
        public List<int> Ports = new List<int>();
        public bool Up = true;

        public string Endpoint { get { return Host + ":" + (Ports.Count > 0 ? Ports[0] : 443); } }
    }

    public class PoolDoc
    {
        public int V = 2;
        public string Name = "", Issued = "";
        public int Ttl = 21600;
        public List<PoolServer> Servers = new List<PoolServer>();

        public static PoolDoc Parse(string json)
        {
            Dictionary<string, object> root = Json.AsObj(Json.Parse(json));
            if (root == null) throw new FormatException("пул: не объект");
            var doc = new PoolDoc
            {
                V = Json.I(root, "v", 2),
                Name = Json.S(root, "pool"),
                Issued = Json.S(root, "issued"),
                Ttl = Json.I(root, "ttl", 21600)
            };
            List<object> arr = Json.AsArr(root.ContainsKey("servers") ? root["servers"] : null);
            if (arr == null) throw new FormatException("пул: нет списка серверов");
            foreach (object o in arr)
            {
                Dictionary<string, object> so = Json.AsObj(o);
                if (so == null) continue;
                var s = new PoolServer
                {
                    Id = Json.S(so, "id"),
                    Host = Json.S(so, "host"),
                    Sni = Json.S(so, "sni"),
                    Cc = Json.S(so, "cc"),
                    Country = Json.S(so, "country"),
                    Up = Json.B(so, "up", true)
                };
                if (s.Sni.Length == 0) s.Sni = s.Host;
                int one = Json.I(so, "port", 0);
                List<object> ports = Json.AsArr(so.ContainsKey("ports") ? so["ports"] : null);
                if (ports != null)
                    foreach (object p in ports) { try { s.Ports.Add(Convert.ToInt32(p)); } catch { } }
                if (s.Ports.Count == 0 && one > 0) s.Ports.Add(one);
                if (s.Ports.Count == 0) s.Ports.Add(443);
                if (s.Host.Length > 0) doc.Servers.Add(s);
            }
            return doc;
        }
    }

    // ── подписка: один ключ → несколько профилей ───────────────────────────

    static class Subs
    {
        public static string Id(string sub)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] h = sha.ComputeHash(Encoding.UTF8.GetBytes(sub ?? ""));
                var sb = new StringBuilder();
                for (int i = 0; i < 4; i++) sb.Append(h[i].ToString("x2"));
                return sb.ToString();
            }
        }

        // Профили из документа пула: по одному на сервер, все с общим PSK/SNI-настройками.
        public static List<Profile> Build(string v2Key, KeyInfo ki, PoolDoc doc)
        {
            var list = new List<Profile>();
            string subId = Id(ki.Sub);
            foreach (PoolServer s in doc.Servers)
            {
                if (!s.Up) continue;
                var p = new Profile();
                p.Key = v2Key;
                p.Psk = ki.Psk;
                p.Fp = ki.Fp;
                p.Pin = ki.Pin;
                p.Sni = s.Sni;
                p.Server = s.Endpoint;
                p.Ports = s.Ports.ToArray();
                p.Sub = subId;
                p.Cc = s.Cc;
                p.Country = s.Country;
                list.Add(p);
            }
            return list;
        }

        // Обновить все подписки в списке: удалённые серверы уходят, новые приходят,
        // порядок — как в пуле. Ошибка загрузки не ломает текущий список.
        public static string Refresh(List<Profile> profiles, out int added, out int dropped)
        {
            added = dropped = 0;
            string err = "";
            List<string> subs = new List<string>();
            foreach (Profile p in profiles)
                if (!subs.Contains(p.Sub) && p.Sub.Length > 0) subs.Add(p.Sub);

            foreach (string sid in subs)
            {
                Profile sample = profiles.Find(x => x.Sub == sid);
                if (sample == null) continue;
                KeyInfo ki = KeyInfo.Parse(sample.Key);
                if (ki == null || !ki.IsV2 || ki.Sub.Length == 0) continue;

                // id группы всегда считаем из адреса пула, а не берём из файла:
                // иначе старые записи и новые могут разойтись по разным группам
                string gid = Id(ki.Sub);
                foreach (Profile x in profiles) if (x.Sub == sid) x.Sub = gid;
                string subId = gid;

                PoolDoc doc;
                string used = "";
                try
                {
                    doc = PoolClient.Fetch(ki.Subs, ki.Psk, ki.Pin, ki.Pk, out used);
                    Program.Log("subs: " + subId + " ← " + used);
                }
                catch (Exception ex) { err = "пул " + ki.Sub + ": " + ex.Message; continue; }

                List<Profile> fresh = Build(sample.Key, ki, doc);
                Program.Log("subs: " + subId + " пул " + doc.Servers.Count + " → профилей " + fresh.Count +
                            " · в списке сейчас " + profiles.FindAll(x => x.Sub == subId).Count);
                foreach (Profile f in fresh)
                {
                    Profile old = profiles.Find(x => x.Sub == subId && x.Server == f.Server);
                    if (old != null) { f.Cc = old.Cc.Length > 0 ? old.Cc : f.Cc; f.Country = old.Country; f.ExitIp = old.ExitIp; }
                    else added++;
                }
                int before = profiles.RemoveAll(x => x.Sub == subId);
                int after = fresh.Count;
                if (before > after) dropped += before - after;
                profiles.AddRange(fresh);
            }
            return err;
        }
    }

    // ── загрузка пула ──────────────────────────────────────────────────────

    public static class PoolClient
    {
        // Публичный ключ подписи пула. Приватный лежит только дома.
        public const string PublicKeyB64 = "P7hnrMYP++C9p9Itf9IG/9rR/3qNGyAppYnE/dfdGWc=";

        public static PoolDoc Fetch(string[] urls, string psk, string pin, string pk, out string usedUrl)
        {
            usedUrl = "";
            string lastErr = "";
            foreach (string raw in urls)
            {
                string url = (raw ?? "").Trim();
                if (url.Length == 0) continue;
                try
                {
                    byte[] blob = Download(url, pin);
                    string pubText = string.IsNullOrEmpty(pk) ? PublicKeyB64 : pk;
                    string json = PoolCodec.Unpack(blob, PoolCodec.KeyFromBase64(pubText), psk);
                    PoolDoc doc = PoolDoc.Parse(json);
                    usedUrl = url;
                    return doc;
                }
                catch (Exception ex) { lastErr = ex.Message; }
            }
            throw new InvalidDataException(lastErr.Length > 0 ? lastErr : "пул: нет адресов");
        }

        static byte[] Download(string url, string pin)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            ServicePointManager.Expect100Continue = false;

            // файл пула меняется на месте, поэтому кэш заголовков не годится
            if (url.IndexOf('?') < 0) url += "?t=" + DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.CachePolicy = new RequestCachePolicy(RequestCacheLevel.NoCacheNoStore);
            req.Proxy = null;                      // пул нужен ДО туннеля: идём напрямую
            req.Timeout = 7000;
            req.ReadWriteTimeout = 7000;
            req.UserAgent = "Mozilla/5.0";
            req.KeepAlive = false;

            if (!string.IsNullOrEmpty(pin))
            {
                string want = pin.Replace("sha256:", "").Replace(":", "").Trim().ToLowerInvariant();
                req.ServerCertificateValidationCallback = delegate(object s, System.Security.Cryptography.X509Certificates.X509Certificate cert,
                                                                   System.Security.Cryptography.X509Certificates.X509Chain chain, System.Net.Security.SslPolicyErrors err)
                {
                    if (cert == null || want.Length == 0) return err == System.Net.Security.SslPolicyErrors.None;
                    using (SHA256 sha = SHA256.Create())
                    {
                        byte[] h = sha.ComputeHash(cert.GetRawCertData());
                        var sb = new StringBuilder();
                        foreach (byte b in h) sb.Append(b.ToString("x2"));
                        if (sb.ToString() == want) return true;
                    }
                    return false;
                };
            }

            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream st = resp.GetResponseStream())
            using (var ms = new MemoryStream())
            {
                st.CopyTo(ms);
                byte[] data = ms.ToArray();
                if (data.Length < 32) throw new InvalidDataException("пул: пустой ответ");
                if (data.Length > 256 * 1024) throw new InvalidDataException("пул: ответ слишком большой");
                return data;
            }
        }
    }
}
