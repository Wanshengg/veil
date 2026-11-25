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
            // в .bat команда может быть разбита переносами через «^» — склеиваем, как в оригинале
            text = text.Replace("^\n", " ");
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

        // pid нашего winws переживает перезапуск приложения: если он жив,
        // считаем его своим и гасим при старте, а не ругаемся «чужая копия».
        public static int OwnPid { get { int p; return int.TryParse(StateGet("pid"), out p) ? p : 0; } }

        public static void StopStale()
        {
            int pid = OwnPid;
            if (pid <= 0) return;
            try
            {
                Process p = Process.GetProcessById(pid);
                if (p != null && !p.HasExited)
                {
                    p.Kill();
                    p.WaitForExit(2500);
                    Program.Log("zapret: остановлен прошлый winws (pid " + pid + ")");
                }
            }
            catch { }
            StateSet("pid", "0");
        }

        public static bool ForeignRunning
        {
            get
            {
                try
                {
                    int own = OwnPid;
                    foreach (Process p in Process.GetProcessesByName("winws"))
                    {
                        if (proc != null && p.Id == proc.Id) continue;
                        if (own > 0 && p.Id == own) continue;
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
            Program.Log("winws cmd: " + exe + " " + start.Arguments);
            Process p2 = new Process { StartInfo = start, EnableRaisingEvents = true };
            p2.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Program.Log("winws: " + e.Data); };
            p2.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { if (e.Data != null) Program.Log("winws: " + e.Data); };
            p2.Start();
            try { p2.BeginErrorReadLine(); p2.BeginOutputReadLine(); } catch { }
            proc = p2;
            runningStrategy = strategyId;
            try { StateSet("pid", p2.Id.ToString()); } catch { }
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
            StateSet("pid", "0");
            Process p = proc;
            proc = null; runningStrategy = "";
            if (p == null) return;
            try { if (!p.HasExited) { p.Kill(); p.WaitForExit(2500); } } catch { }
            try { p.Dispose(); } catch { }
        }

        // ── проверка стратегий ──────────────────────────────────────────────

        // Что проверяем: два адреса тремя протоколами + ICMP, как в zapret-client.
        static readonly string[][] Protocols = {
            new[] { "HTTP/1.1", "--http1.1" },
            new[] { "TLS 1.2", "--tlsv1.2", "--tls-max", "1.2" },
            new[] { "TLS 1.3", "--tlsv1.3", "--tls-max", "1.3" }
        };

        public class ProbeInfo
        {
            public bool Ok; public int Code; public int Ms; public int Passed, Total;
            public string Raw = "";
            public string Desc {
                get
                {
                    if (Raw.Length > 0) return Raw;
                    if (Total == 0) return "—";
                    if (Passed == 0) return "нет ответа";
                    return (Code > 0 ? Code + " " : "") + Ms + " мс · " + Passed + "/" + Total;
                }
            }
        }

        public class Result
        {
            public string Id = "", Name = "";
            public bool Ok;
            public string Error = "";
            public int PingMs, PingPassed, PingTotal;
            public ProbeInfo Youtube = new ProbeInfo(), Discord = new ProbeInfo();
            public int ChecksOk, ChecksTotal;
            public string IcmpText = "";
            public string Status { get { return Ok ? "Работает" : (Error.Length > 0 ? Error : "не прошла"); } }
            public string Desc { get { return Ok ? PingMs + " мс" : Status; } }
        }

        // Один запрос curl с параметрами протокола: код ответа и время.
        static bool CurlOnce(string url, string[] protoArgs, int expected, int timeoutMs, out int code, out int ms)
        {
            code = 0; ms = 0;
            try
            {
                string curl = Path.Combine(Environment.SystemDirectory, "curl.exe");
                if (!File.Exists(curl)) curl = "curl.exe";
                string args = "--ipv4 -s -o NUL --max-time " + Math.Max(1, timeoutMs / 1000) +
                              " " + string.Join(" ", protoArgs) +
                              " -w \"%{http_code} %{time_total}\" \"" + url + "\"";
                var psi = new ProcessStartInfo(curl, args)
                {
                    UseShellExecute = false, CreateNoWindow = true,
                    RedirectStandardOutput = true, RedirectStandardError = true
                };
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(timeoutMs + 2000);
                    string[] parts = outp.Split(' ');
                    int.TryParse(parts.Length > 0 ? parts[0] : "0", out code);
                    double secs = 0;
                    if (parts.Length > 1) double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out secs);
                    ms = (int)Math.Round(secs * 1000);
                }
                if (code == 0) return false;
                if (expected > 0) return code == expected;
                return code >= 200 && code < 400;
            }
            catch { return false; }
        }

        // Метрики по адресу: три протокола, считаем сколько прошло и среднюю задержку.
        static ProbeInfo ProbeAll(string url, int expected, int timeoutMs)
        {
            var info = new ProbeInfo { Total = Protocols.Length };
            // три протокола параллельно — иначе проверка всех стратегий тянется часами
            var tasks = new List<Task<int[]>>();
            foreach (string[] proto in Protocols)
            {
                string[] args = proto.Skip(1).ToArray();
                tasks.Add(Task.Run(delegate()
                {
                    int code, ms;
                    bool ok = CurlOnce(url, args, expected, timeoutMs, out code, out ms);
                    return new[] { ok ? 1 : 0, code, ms };
                }));
            }
            Task.WaitAll(tasks.ToArray());
            int sumMs = 0;
            foreach (Task<int[]> t in tasks)
            {
                int[] v = t.Result;
                if (v[0] == 1) { info.Passed++; sumMs += v[2]; if (info.Code == 0) info.Code = v[1]; }
                else if (info.Code == 0 && v[1] > 0) info.Code = v[1];
            }
            info.Ok = info.Passed > 0;
            info.Ms = info.Passed > 0 ? sumMs / info.Passed : 0;
            return info;
        }

        // ICMP: два пакета, считаем сколько вернулось.
        static void Ping(string host, out int ms, out int passed, out int total)
        {
            ms = 0; passed = 0; total = 2;
            try
            {
                var psi = new ProcessStartInfo("ping", "-n 2 -w 1500 " + host)
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                using (Process p = Process.Start(psi))
                {
                    string outp = p.StandardOutput.ReadToEnd();
                    p.WaitForExit(6000);
                    // "Ответ от 142.250.74.206: число байт=32 время=27мс TTL=115"
                    foreach (Match m in Regex.Matches(outp, "время[=<]\\s*(\\d+)\\s*мс|time[=<]\\s*(\\d+)\\s*ms", RegexOptions.IgnoreCase))
                    {
                        int v;
                        string s = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
                        if (int.TryParse(s, out v)) { ms += v; passed++; }
                    }
                    if (passed > 0) ms = ms / passed;
                }
            }
            catch { }
        }

        public static Result CheckOne(string id, string name)
        {
            var r = new Result { Id = id, Name = name };
            try
            {
                Start(id);
                Thread.Sleep(300);   // дать winws встать на поток
                Task<ProbeInfo> tYt = Task.Run(delegate { return ProbeAll("https://www.youtube.com/generate_204", 204, 6000); });
                Task<ProbeInfo> tDc = Task.Run(delegate { return ProbeAll("https://discord.com/api/v10/gateway", 200, 6000); });
                Task.WaitAll(tYt, tDc);
                r.Youtube = tYt.Result;
                r.Discord = tDc.Result;
                Ping("1.1.1.1", out r.PingMs, out r.PingPassed, out r.PingTotal);
                r.IcmpText = r.PingPassed > 0 ? r.PingMs + " мс · " + r.PingPassed + "/" + r.PingTotal : "—";

                r.ChecksTotal = r.Youtube.Total + r.Discord.Total;
                r.ChecksOk = r.Youtube.Passed + r.Discord.Passed;
                r.Ok = r.Youtube.Passed > 0 && r.Discord.Passed > 0;
                if (!r.Ok)
                    r.Error = r.Youtube.Passed == 0 ? "youtube: нет ответа" : "discord: нет ответа";
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

        // Результаты проверки переживают перезапуск приложения.
        static string ResultsPath { get { return Path.Combine(Root, "results.ini"); } }

        public static Dictionary<string, Result> LoadResults()
        {
            var map = new Dictionary<string, Result>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (!File.Exists(ResultsPath)) return map;
                foreach (string line in File.ReadAllLines(ResultsPath))
                {
                    string[] f = line.Split('|');
                    if (f.Length < 13) continue;
                    var r = new Result { Id = f[0], Name = f[0], Ok = f[1] == "1" };
                    int v;
                    r.Youtube = new ProbeInfo { Ok = true };
                    if (int.TryParse(f[2], out v)) r.Youtube.Code = v;
                    if (int.TryParse(f[3], out v)) r.Youtube.Ms = v;
                    if (int.TryParse(f[4], out v)) r.Youtube.Passed = v;
                    if (int.TryParse(f[5], out v)) r.Youtube.Total = v;
                    r.Discord = new ProbeInfo { Ok = true };
                    if (int.TryParse(f[6], out v)) r.Discord.Code = v;
                    if (int.TryParse(f[7], out v)) r.Discord.Ms = v;
                    if (int.TryParse(f[8], out v)) r.Discord.Passed = v;
                    if (int.TryParse(f[9], out v)) r.Discord.Total = v;
                    r.IcmpText = f[10];
                    if (int.TryParse(f[11], out v)) r.ChecksOk = v;
                    if (int.TryParse(f[12], out v)) r.ChecksTotal = v;
                    r.Error = f.Length > 13 ? f[13] : "";
                    map[r.Id] = r;
                }
            }
            catch (Exception ex) { Program.Log("zapret results load: " + ex.Message); }
            return map;
        }

        public static void SaveResults(Dictionary<string, Result> map)
        {
            try
            {
                var sb = new StringBuilder();
                foreach (var kv in map)
                {
                    Result r = kv.Value;
                    sb.Append(kv.Key.Replace("|", "/")).Append('|')
                      .Append(r.Ok ? "1" : "0").Append('|')
                      .Append(r.Youtube.Code).Append('|').Append(r.Youtube.Ms).Append('|')
                      .Append(r.Youtube.Passed).Append('|').Append(r.Youtube.Total).Append('|')
                      .Append(r.Discord.Code).Append('|').Append(r.Discord.Ms).Append('|')
                      .Append(r.Discord.Passed).Append('|').Append(r.Discord.Total).Append('|')
                      .Append(r.IcmpText.Replace("|", "/")).Append('|')
                      .Append(r.ChecksOk).Append('|').Append(r.ChecksTotal).Append('|')
                      .Append((r.Error ?? "").Replace("|", "/")).Append('\n');
                }
                File.WriteAllText(ResultsPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch (Exception ex) { Program.Log("zapret results save: " + ex.Message); }
        }

        // Быстрая проверка текущей стратегии: один запрос, без полного прогона.
        public static bool QuickOk()
        {
            int code, ms;
            return CurlOnce("https://www.youtube.com/generate_204", new[] { "--http1.1" }, 204, 4000, out code, out ms);
        }

        // Порядок для авто-подбора: лучшие по результатам, затем остальные рабочие.
        public static List<string> Ranked()
        {
            var list = new List<string>();
            var res = LoadResults();
            var ok = new List<KeyValuePair<string, int>>();
            foreach (var kv in res)
                if (kv.Value.Ok) ok.Add(new KeyValuePair<string, int>(kv.Key, kv.Value.Youtube.Ms + kv.Value.Discord.Ms));
            ok.Sort(delegate(KeyValuePair<string, int> a, KeyValuePair<string, int> b) { return a.Value.CompareTo(b.Value); });
            foreach (var kv in ok) list.Add(kv.Key);
            foreach (Strategy s in Strategies())
                if (!list.Contains(s.Id)) list.Add(s.Id);
            return list;
        }

        // Включить лучшую из известных: сначала по результатам, иначе первую, которая проходит.
        public static string StartBest()
        {
            foreach (string id in Ranked())
            {
                if (!File.Exists(Path.Combine(Root, id))) continue;
                try { Start(id); } catch { continue; }
                if (QuickOk()) { StateSet("strategy", id); return id; }
                Stop();
            }
            throw new InvalidOperationException("ни одна стратегия не проходит проверку");
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
