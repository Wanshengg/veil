using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Threading;

namespace Veil
{
    // Запрет (DPI-bypass): обёртка над Flowseal/zapret-discord-youtube.
    // Логика перенесена из zapret-client (engine.cjs + updater.cjs):
    // загрузка релиза с GitHub, разбор general*.bat, запуск winws.exe,
    // прогон стратегий с проверкой YouTube/Discord.
    public static class Zapret
    {
        const string ApiUrl = "https://api.github.com/repos/Flowseal/zapret-discord-youtube/releases/latest";
        static readonly string[] UserFiles = {
            "lists/list-general-user.txt", "lists/list-exclude-user.txt",
            "lists/ipset-exclude-user.txt", "utils/game_filter.enabled"
        };

        public static string Root
        {
            get
            {
                string d = Path.Combine(Engine.Dir, "zapret");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        static string StatePath { get { return Path.Combine(Root, "state.ini"); } }

        public static string StateGet(string key)
        {
            try
            {
                if (!File.Exists(StatePath)) return "";
                foreach (string line in File.ReadAllLines(StatePath))
                {
                    int eq = line.IndexOf('=');
                    if (eq > 0 && line.Substring(0, eq).Trim() == key) return line.Substring(eq + 1).Trim();
                }
            }
            catch { }
            return "";
        }

        public static void StateSet(string key, string value)
        {
            try
            {
                var map = new Dictionary<string, string>();
                if (File.Exists(StatePath))
                    foreach (string line in File.ReadAllLines(StatePath))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0) map[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
                    }
                map[key] = value ?? "";
                var sb = new StringBuilder();
                foreach (var kv in map) sb.Append(kv.Key).Append('=').Append(kv.Value).Append('\n');
                File.WriteAllText(StatePath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Program.Log("zapret state: " + ex.Message); }
        }

        public static bool Installed
        {
            get { return File.Exists(Path.Combine(Root, "bin", "winws.exe")) && File.Exists(Path.Combine(Root, "general.bat")); }
        }

        public static string Version { get { return StateGet("version"); } }
        public static string SelectedStrategy { get { return StateGet("strategy"); } }
        public static bool Autoconnect { get { return StateGet("autoconnect") == "1"; } }

        // ── обновление с GitHub ─────────────────────────────────────────────

        public class Release
        {
            public string Version = "", Url = "", Digest = "", Notes = "";
        }

        static HttpClient Http()
        {
            var h = new HttpClient();
            h.Timeout = TimeSpan.FromSeconds(60);
            h.DefaultRequestHeaders.UserAgent.ParseAdd("veil-zapret/1.0");
            h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return h;
        }

        public static Release Latest()
        {
            using (var h = Http())
            {
                string json = h.GetStringAsync(ApiUrl).GetAwaiter().GetResult();
                var rel = new Release();
                Dictionary<string, object> root = Json.AsObj(Json.Parse(json));
                if (root == null) throw new InvalidOperationException("GitHub вернул неожидаемый ответ");

                object tv, bv;
                if (root.TryGetValue("tag_name", out tv)) rel.Version = Convert.ToString(tv);
                if (root.TryGetValue("body", out bv)) rel.Notes = Convert.ToString(bv);

                object av;
                if (!root.TryGetValue("assets", out av)) throw new InvalidOperationException("в релизе нет файлов");
                foreach (object item in (List<object>)av)
                {
                    Dictionary<string, object> asset = Json.AsObj(item);
                    if (asset == null) continue;
                    object name;
                    if (!asset.TryGetValue("name", out name)) continue;
                    string sname = Convert.ToString(name) ?? "";
                    if (!sname.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) continue;
                    object url;
                    if (asset.TryGetValue("browser_download_url", out url)) rel.Url = Convert.ToString(url);
                    object dig;
                    if (asset.TryGetValue("digest", out dig))
                    {
                        Match m = Regex.Match(Convert.ToString(dig) ?? "", "sha256:([a-fA-F0-9]{64})");
                        if (m.Success) rel.Digest = m.Groups[1].Value.ToLowerInvariant();
                    }
                    if (rel.Url.Length > 0) break;
                }
                if (rel.Url.Length == 0) throw new InvalidOperationException("в релизе нет ZIP-архива");
                if (rel.Digest.Length == 0) throw new InvalidOperationException("GitHub не опубликовал SHA-256 архива — обновление остановлено");
                return rel;
            }
        }

        public static string Install(Action<string> progress)
        {
            Release rel = Latest();
            progress("Загружаю " + rel.Version + "…");
            string tmp = Path.Combine(Engine.Dir, "zapret-tmp");
            try
            {
                if (Directory.Exists(tmp)) Directory.Delete(tmp, true);
                Directory.CreateDirectory(tmp);
                string zip = Path.Combine(tmp, "payload.zip");

                using (var h = Http())
                using (var s = h.GetStreamAsync(rel.Url).GetAwaiter().GetResult())
                using (var f = File.Create(zip)) s.CopyTo(f);

                string digest;
                using (var sha = System.Security.Cryptography.SHA256.Create())
                using (var fs = File.OpenRead(zip))
                    digest = BitConverter.ToString(sha.ComputeHash(fs)).Replace("-", "").ToLowerInvariant();
                if (digest != rel.Digest) throw new InvalidOperationException("SHA-256 не совпал, архив отброшен");

                progress("Распаковываю…");
                string stage = Path.Combine(tmp, "stage");
                ZipFile.ExtractToDirectory(zip, stage);

                string payload = stage;
                if (!File.Exists(Path.Combine(payload, "bin", "winws.exe")))
                {
                    string[] dirs = Directory.GetDirectories(stage);
                    if (dirs.Length != 1) throw new InvalidOperationException("неизвестная структура архива");
                    payload = dirs[0];
                }
                if (!File.Exists(Path.Combine(payload, "bin", "winws.exe"))) throw new InvalidOperationException("в архиве нет bin/winws.exe");

                // свои списки переносим поверх новых
                foreach (string rel2 in UserFiles)
                {
                    string src = Path.Combine(Root, rel2.Replace('/', Path.DirectorySeparatorChar));
                    string dst = Path.Combine(payload, rel2.Replace('/', Path.DirectorySeparatorChar));
                    try { if (File.Exists(src)) { Directory.CreateDirectory(Path.GetDirectoryName(dst)); File.Copy(src, dst, true); } }
                    catch { }
                }

                Stop();   // старые процессы winws держат файлы
                Thread.Sleep(400);
                string backup = Root + ".bak";
                try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }
                try { Directory.Move(Root, backup); } catch { }
                try { Directory.Move(payload, Root); }
                catch
                {
                    CopyDir(payload, Root);
                }
                try { if (Directory.Exists(backup)) Directory.Delete(backup, true); } catch { }

                StateSet("version", rel.Version);
                EnsureUserLists();
                progress("Установлено " + rel.Version);
                return rel.Version;
            }
            finally
            {
                try { if (Directory.Exists(tmp)) Directory.Delete(tmp, true); } catch { }
            }
        }

        static void CopyDir(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(d.Replace(from, to));
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
                File.Copy(f, f.Replace(from, to), true);
        }

        static void EnsureUserLists()
        {
            var defaults = new Dictionary<string, string>
            {
                { "lists/ipset-exclude-user.txt", "203.0.113.113/32\r\n" },
                { "lists/list-general-user.txt", "# Never leave this file empty\r\ndomain.example.abc\r\n" },
                { "lists/list-exclude-user.txt", "domain.example.abc\r\n" }
            };
            foreach (var kv in defaults)
            {
                string p = Path.Combine(Root, kv.Key.Replace('/', Path.DirectorySeparatorChar));
                try
                {
                    if (File.Exists(p)) continue;
                    Directory.CreateDirectory(Path.GetDirectoryName(p));
                    File.WriteAllText(p, kv.Value, new UTF8Encoding(false));
                }
                catch { }
            }
        }

        // ── стратегии ───────────────────────────────────────────────────────

        public class Strategy { public string Id = "", Name = ""; }

        static string NaturalName(string file)
        {
            string n = Regex.Replace(file, "^general\\s*", "", RegexOptions.IgnoreCase);
            n = Regex.Replace(n, "\\.bat$", "", RegexOptions.IgnoreCase);
            n = n.Replace("(", "").Replace(")", "").Trim();
            return n.Length == 0 ? "Стандартная" : n;
        }

        public static List<Strategy> Strategies()
        {
            var list = new List<Strategy>();
            try
            {
                foreach (string f in Directory.GetFiles(Root, "general*.bat"))
                    list.Add(new Strategy { Id = Path.GetFileName(f), Name = NaturalName(Path.GetFileName(f)) });
                list.Sort(delegate(Strategy a, Strategy b)
                {
                    if (a.Id == "general.bat") return -1;
                    if (b.Id == "general.bat") return 1;
                    return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
                });
            }
            catch (Exception ex) { Program.Log("zapret strategies: " + ex.Message); }
            return list;
        }

        static string[] ReadGameFilter()
        {
            string tcp = "12", udp = "12";
            try
            {
                string p = Path.Combine(Root, "utils", "game_filter.enabled");
                if (File.Exists(p))
                {
                    string text = File.ReadAllText(p);
                    Match mode = Regex.Match(text, "^mode=(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    Match t = Regex.Match(text, "^tcp=(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    Match u = Regex.Match(text, "^udp=(.*)$", RegexOptions.Multiline | RegexOptions.IgnoreCase);
                    string m = mode.Success ? mode.Groups[1].Value.Trim().ToLowerInvariant() : "";
                    string tv = t.Success ? t.Groups[1].Value.Trim() : "1024-65535";
                    string uv = u.Success ? u.Groups[1].Value.Trim() : "1024-65535";
                    if (m == "all") { tcp = tv; udp = uv; }
                    else if (m == "tcp") { tcp = tv; udp = "12"; }
                    else if (m == "udp") { tcp = "12"; udp = uv; }
                }
            }
            catch { }
            return new[] { tcp, udp };
        }

        static string[] Tokenize(string command)
        {
            var tokens = new List<string>();
            var cur = new StringBuilder();
            bool quoted = false, started = false;
            foreach (char c in command.Trim())
            {
                if (c == '"') { quoted = !quoted; started = true; continue; }
                if (char.IsWhiteSpace(c) && !quoted)
                {
                    if (started) { tokens.Add(cur.ToString()); cur.Clear(); started = false; }
                    continue;
                }
                cur.Append(c); started = true;
            }
            if (quoted) throw new InvalidOperationException("незакрытая кавычка в аргументах стратегии");
            if (started) tokens.Add(cur.ToString());
            return tokens.ToArray();
        }

        public static string[] ParseStrategy(string strategyId, out string exePath)
        {
            string bat = Path.Combine(Root, strategyId);
            string text = File.ReadAllText(bat).TrimStart('\uFEFF').Replace("\r", "");
            string line = null;
            foreach (string l in text.Split('\n'))
            {
                if (Regex.IsMatch(l, "^\\s*start\\s+\"zapret:.*winws\\.exe\"", RegexOptions.IgnoreCase)) { line = l; break; }
            }
            if (line == null) throw new InvalidOperationException("в .bat не найдена команда запуска winws.exe");
            Match m = Regex.Match(line, "winws\\.exe\"\\s+(.+)$", RegexOptions.IgnoreCase);
            if (!m.Success) throw new InvalidOperationException("не удалось прочитать аргументы стратегии");

            string bin = Path.Combine(Root, "bin") + Path.DirectorySeparatorChar;
            string lists = Path.Combine(Root, "lists") + Path.DirectorySeparatorChar;
            string[] gf = ReadGameFilter();
            string expanded = m.Groups[1].Value
                .Replace("%BIN%", bin).Replace("%LISTS%", lists)
                .Replace("%GameFilterTCP%", gf[0]).Replace("%GameFilterUDP%", gf[1]);
            if (Regex.IsMatch(expanded, "%[A-Za-z][A-Za-z0-9_]*%"))
                throw new InvalidOperationException("стратегия содержит неподдерживаемую переменную окружения");
            exePath = Path.Combine(Root, "bin", "winws.exe");
            return Tokenize(expanded);
        }

        // ── запуск / остановка ──────────────────────────────────────────────

        static Process proc;
        static string runningStrategy = "";

        public static bool IsRunning { get { return proc != null && !proc.HasExited; } }
        public static string RunningStrategy { get { return IsRunning ? runningStrategy : ""; } }

        public static bool ForeignRunning
        {
            get
            {
                try
                {
                    foreach (Process p in Process.GetProcessesByName("winws"))
                    {
                        if (proc != null && p.Id == proc.Id) continue;
                        return true;
                    }
                }
                catch { }
                return false;
            }
        }

        public static void Start(string strategyId)
        {
            if (ForeignRunning)
                throw new InvalidOperationException("запрет уже запущен другой программой (закрой Zapret Client) — две копии winws не уживаются");
            Stop();
            EnsureUserLists();
            try
            {
                var psi = new ProcessStartInfo("netsh", "interface tcp set global timestamps=enabled");
                psi.CreateNoWindow = true; psi.UseShellExecute = false;
                using (Process p = Process.Start(psi)) { if (!p.WaitForExit(5000)) p.Kill(); }
            }
            catch { }

            string exe;
            string[] args = ParseStrategy(strategyId, out exe);
            var start = new ProcessStartInfo(exe, string.Join(" ", args.Select(Quote)))
            {
                WorkingDirectory = Path.Combine(Root, "bin"),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            Process p2 = new Process { StartInfo = start, EnableRaisingEvents = true };
            p2.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Program.Log("winws: " + e.Data); };
            p2.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Program.Log("winws: " + e.Data); };
            p2.Start();
            try { p2.BeginErrorReadLine(); p2.BeginOutputReadLine(); } catch { }
            proc = p2;
            runningStrategy = strategyId;
            Thread.Sleep(1100);
            if (p2.HasExited)
            {
                proc = null; runningStrategy = "";
                throw new InvalidOperationException("winws.exe завершился с кодом " + p2.ExitCode +
                    ". Запусти приложение от администратора и закрой другие копии zapret.");
            }
        }

        static string Quote(string s)
        {
            return s.IndexOf(' ') >= 0 || s.Length == 0 ? "\"" + s + "\"" : s;
        }

        public static void Stop()
        {
            Process p = proc;
            proc = null; runningStrategy = "";
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2500); } } catch { }
            try { p.Dispose(); } catch { }
        }

        // ── проверка стратегий ──────────────────────────────────────────────

        public class Result
        {
            public string Id = "", Name = "";
            public bool Ok;
            public string Error = "";
            public int PingMs;
            public string Desc { get { return Ok ? PingMs + " мс" : (Error.Length > 0 ? Error : "не прошла"); } }
        }

        // Проверка через curl.exe: он делает IPv4 и не висит на IPv6, в отличие от .NET HttpClient.
        static bool Probe(string url, int expected, int timeoutMs, out int ms, out string why)
        {
            ms = 0; why = "";
            try
            {
                string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
                if (!File.Exists(curl)) curl = "curl.exe";
                var psi = new ProcessStartInfo(curl,
                    "--ipv4 -s -o NUL --max-time " + Math.Max(1, timeoutMs / 1000) +
                    " -w \"%{http_code} %{time_total}\" \"" + url + "\"")
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(timeoutMs + 2000);
                    string[] parts = outp.Split(' ');
                    int code = 0;
                    int.TryParse(parts.Length > 0 ? parts[0] : "0", out code);
                    double secs = 0;
                    if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out secs);
                    ms = (int)Math.Round(secs * 1000);
                    if (code == 0) { why = "нет ответа"; return false; }
                    if (expected > 0 && code != expected) { why = "HTTP " + code; return false; }
                    if (expected == 0 && (code < 200 || code >= 400)) { why = "HTTP " + code; return false; }
                    return true;
                }
            }
            catch (Exception ex) { why = ex.Message; return false; }
        }

        public static Result CheckOne(string id, string name)
        {
            var r = new Result { Id = id, Name = name };
            try
            {
                Start(id);
                int ms1, ms2; string why1, why2;
                bool yt = Probe("https://www.youtube.com/generate_204", 204, 7000, out ms1, out why1);
                bool dc = Probe("https://discord.com/api/v10/gateway", 200, 7000, out ms2, out why2);
                r.Ok = yt && dc;
                r.PingMs = r.Ok ? Math.Max(ms1, ms2) : 0;
                if (!r.Ok) r.Error = !yt ? "youtube: " + why1 : "discord: " + why2;
            }
            catch (Exception ex) { r.Ok = false; r.Error = ex.Message; }
            finally { Stop(); }
            return r;
        }

        public static List<Result> CheckAll(Action<Strategy, int, int> progress)
        {
            List<Strategy> list = Strategies();
            var results = new List<Result>();
            for (int i = 0; i < list.Count; i++)
            {
                if (progress != null) progress(list[i], i, list.Count);
                results.Add(CheckOne(list[i].Id, list[i].Name));
            }
            return results;
        }

        public static Result Best(List<Result> results)
        {
            Result best = null;
            foreach (Result r in results)
                if (r.Ok && (best == null || r.PingMs < best.PingMs)) best = r;
            return best;
        }
    }
}
