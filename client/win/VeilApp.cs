// veil — WebView2 shell: HTML-интерфейс + наш Go-клиент под ним.
// Собирается csc.exe (C# 5), без SDK и без NuGet на машине пользователя:
// DLL WebView2 и сама страница лежат внутри exe ресурсами.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using Renci.SshNet;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Win32;

namespace Veil
{
    static class Program
    {
        public static string RuntimeDir
        {
            get
            {
                string d = Path.Combine(Engine.Dir, "runtime");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += delegate(object s, UnhandledExceptionEventArgs e)
            {
                Log("fatal: " + e.ExceptionObject);
                SysProxy.Disable();
            };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try
            {
                Settings.Load();
                SshCreds.Load();             // данные SSH прошлого раза
                SysProxy.RestoreIfStale();   // убрать висящий прокси прошлого запуска
                ExtractAll();      // разложить DLL до первого обращения к WebView2
                InitLoader();      // теперь тип CoreWebView2Environment доступен
                // диагностика: несколько кликов подряд, через ;
                string clicks = ArgOf(Environment.GetCommandLineArgs(), "--clicks=");
                string subTest = ArgOf(Environment.GetCommandLineArgs(), "--sub-test=");
                if (subTest.Length > 0) { RunSubTest(subTest, false); return; }
                string subAdd = ArgOf(Environment.GetCommandLineArgs(), "--sub-add=");
                if (subAdd.Length > 0) { RunSubTest(subAdd, true); return; }
                string jsonTest = ArgOf(Environment.GetCommandLineArgs(), "--json-test=");
                if (jsonTest.Length > 0)
                {
                    string v = SshText.Unescape(jsonTest);
                    Log("json-test: in=" + jsonTest);
                    Log("json-test: out=" + v + " len=" + v.Length + " exists=" + File.Exists(v));
                    return;
                }
                string sshSave = ArgOf(Environment.GetCommandLineArgs(), "--ssh-save=");
                if (sshSave.Length > 0) { SshCreds.Host = "h"; RunSshCredsTest(sshSave, true); return; }
                string sshShow = ArgOf(Environment.GetCommandLineArgs(), "--ssh-show=");
                if (sshShow.Length > 0) { RunSshCredsTest(sshShow, false); return; }
                string srvTest = ArgOf(Environment.GetCommandLineArgs(), "--srv-test=");
                if (srvTest.Length > 0) { RunServerTest(srvTest); return; }
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += delegate(object s, ThreadExceptionEventArgs e) { Log("ui exception: " + e.Exception); };
                Application.Run(new Shell(Environment.GetCommandLineArgs()));
            }
            catch (Exception ex)
            {
                Log("main: " + ex);
                MessageBox.Show(ex.ToString(), "veil — ошибка запуска");
            }
        }

        public static string QuoteJs(string s)
        {
            return "'" + (s ?? "").Replace("\\", "\\\\").Replace("'", "\\'") + "'";
        }

        public static string ArgOf(string[] args, string prefix)
        {
            foreach (string a in args) if (a.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return a.Substring(prefix.Length);
            return "";
        }

        // Проверка подписки без интерфейса: veil.exe --sub-test=<ключ или файл с ключом>
        // --sub-add добавляет серверы в profiles.txt.
        static void RunSubTest(string arg, bool apply)
        {
            try
            {
                string key = arg.Trim();
                if (File.Exists(key)) key = File.ReadAllText(key).Trim();
                KeyInfo ki = KeyInfo.Parse(key);
                if (ki == null) { Log("sub-test: ключ не распознан"); return; }
                Log("sub-test: v2=" + ki.IsV2 + " sub=" + ki.Sub + " seed=" + ki.Seed + " pskLen=" + ki.Psk.Length +
                    " pk=" + (ki.Pk.Length > 0 ? ki.Pk : "(встроенный)"));

                string used;
                PoolDoc doc = PoolClient.Fetch(ki.Subs, ki.Psk, ki.Pin, ki.Pk, out used);
                Log("sub-test: пул " + (doc.Name.Length > 0 ? doc.Name : "?") + " от " + doc.Issued + " · серверов " + doc.Servers.Count + " · url " + used);
                foreach (PoolServer s in doc.Servers)
                    Log("sub-test:   " + s.Id + " " + s.Endpoint + " [" + string.Join(",", s.Ports.ConvertAll(x => x.ToString()).ToArray()) + "] " + s.Cc + " " + s.Country + (s.Up ? "" : " (down)"));

                List<Profile> list = Subs.Build(key, ki, doc);
                Log("sub-test: профилей " + list.Count);

                if (apply)
                {
                    List<Profile> existing = Store.Load();
                    int added = 0;
                    foreach (Profile p in list)
                        if (!existing.Exists(x => string.Equals(x.Server, p.Server, StringComparison.OrdinalIgnoreCase)))
                        { existing.Add(p); added++; }
                    Store.Save(existing);
                    Log("sub-add: добавлено " + added + ", всего " + existing.Count);
                    foreach (Profile p in existing) Log("sub-add:   " + p.Sub + " " + p.Server + " " + p.Cc);
                }
            }
            catch (Exception ex) { Log("sub-test: ОШИБКА " + ex.Message); }
        }

        // Проверка сохранения данных SSH: --ssh-save="host|user|secret" пишет,
        // --ssh-show="-" читает обратно и пишет в лог, что получилось.
        static void RunSshCredsTest(string arg, bool save)
        {
            if (save)
            {
                string[] p = arg.Split('|');
                SshCreds.Host = p.Length > 0 ? p[0] : "";
                SshCreds.User = p.Length > 1 ? p[1] : "root";
                SshCreds.Secret = p.Length > 2 ? p[2] : "";
                SshCreds.Save();
                Log("ssh-save: written host=" + SshCreds.Host + " user=" + SshCreds.User
                    + " secretLen=" + SshCreds.Secret.Length);
            }
            else
            {
                SshCreds.Host = ""; SshCreds.User = ""; SshCreds.Secret = "";
                bool ok = SshCreds.Load();
                Log("ssh-show: ok=" + ok + " host=" + SshCreds.Host + " user=" + SshCreds.User
                    + " secretLen=" + SshCreds.Secret.Length + " secret=" + SshCreds.Secret);
            }
        }

        // Диагностика SSH/ключей без интерфейса: veil.exe --srv-test=C:\path\srv.txt
        // файл: host=..., port=22, user=root, key=... или pass=...
        static void RunServerTest(string cfgPath)
        {
            try
            {
                SshTarget t = new SshTarget();
                foreach (string raw in File.ReadAllLines(cfgPath))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#")) continue;
                    int i = line.IndexOf('=');
                    if (i < 0) continue;
                    string k = line.Substring(0, i).Trim().ToLowerInvariant();
                    string v = line.Substring(i + 1).Trim();
                    if (k == "host") t.Host = v;
                    else if (k == "port") t.Port = Convert.ToInt32(v);
                    else if (k == "user") t.User = v;
                    else if (k == "key") t.KeyPath = v;
                    else if (k == "pass") t.Password = v;
                }
                Log("srv-test: connecting " + t.Host + " as " + t.User + " key=" + t.KeyPath);
                using (SshClient sc = ServerSetup.Connect(t))
                {
                    Log("srv-test: ok");
                    Log("srv-test: cn=" + ServerSetup.CertName(sc));
                    Log("srv-test: pin=" + ServerSetup.Pin(sc));
                    Log("srv-test: keys=" + string.Join(",", ServerSetup.Keys(sc)));
                }
                MessageBox.Show("SSH ok", "veil — тест сервера");
            }
            catch (Exception ex)
            {
                Log("srv-test: FAILED " + ex);
                MessageBox.Show(ex.ToString(), "veil — тест сервера");
            }
        }

        static Assembly Resolve(object sender, ResolveEventArgs e)
        {
            string name = new AssemblyName(e.Name).Name + ".dll";
            string path = Path.Combine(RuntimeDir, name);
            return File.Exists(path) ? Assembly.LoadFrom(path) : null;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ExtractAll()
        {
            Extract("Microsoft.Web.WebView2.Core.dll");
            Extract("Microsoft.Web.WebView2.WinForms.dll");
            Extract("WebView2Loader.dll");
            Extract("Renci.SshNet.dll");
            Extract("BouncyCastle.Cryptography.dll");
            Extract("System.Numerics.Vectors.dll");
            Extract("System.Runtime.CompilerServices.Unsafe.dll");
            Extract("System.Buffers.dll");
            Extract("System.Memory.dll");
            Extract("Microsoft.Bcl.AsyncInterfaces.dll");
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void InitLoader()
        {
            CoreWebView2Environment.SetLoaderDllFolderPath(RuntimeDir);
            Log("loader ready: " + RuntimeDir);
        }

        static string Extract(string name)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string res = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith(name, StringComparison.OrdinalIgnoreCase)) { res = n; break; }
            string dst = Path.Combine(RuntimeDir, name);
            if (res == null) return dst;
            using (Stream s = asm.GetManifestResourceStream(res))
            {
                bool same = File.Exists(dst) && new FileInfo(dst).Length == s.Length;
                if (!same)
                    using (FileStream f = File.Create(dst)) s.CopyTo(f);
            }
            return dst;
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Светлая рамка окна: тёмный системный заголовок спорит с бумажным интерфейсом
        // Заголовок окна под тему интерфейса: dark=1 — тёмный, 0 — светлый.
        // 20 — DWMWA_USE_IMMERSIVE_DARK_MODE, 35 — DWMWA_CAPTION_COLOR (BGR),
        // 36 — DWMWA_TEXT_COLOR.
        public static void ForceLightTitleBar(IntPtr hwnd, int dark)
        {
            try
            {
                int useDark = dark == 1 ? 1 : 0;
                DwmSetWindowAttribute(hwnd, 20, ref useDark, sizeof(int));

                int caption = dark == 1 ? 0x00121212 : 0x00EFF4F6;   // BGR: #121212 / #F6F4EF
                int textCol = dark == 1 ? 0x00EDEDED : 0x001D1816;   // BGR: #EDEDED / #16181D
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
                DwmSetWindowAttribute(hwnd, 36, ref textCol, sizeof(int));
            }
            catch { }
        }

        public static void Log(string msg)
        {
            try
            {
                File.AppendAllText(Path.Combine(Engine.Dir, "app.log"),
                    DateTime.Now.ToString("HH:mm:ss") + " " + msg + Environment.NewLine);
            }
            catch { }
        }
    }

    /* ── хранилище профилей ─────────────────────────────────────────────── */

    class Profile
    {
        public string Key = "", Server = "", Sni = "", Fp = "chrome", Psk = "", Pin = "";
        public string Cc = "", Country = "", ExitIp = "";
        public string City = "";         // подпись из пула: город или маршрут («через РУ»)
        public string Sub = "";          // id подписки, если сервер пришёл из пула
        public int[] Ports = null;       // порты из пула, по порядку
        public int Ping;
        public string Title
        {
            get { int i = Server.IndexOf(':'); return i > 0 ? Server.Substring(0, i) : Server; }
        }
    }

    static class Store
    {
        static string File2 { get { return Path.Combine(Engine.Dir, "profiles.txt"); } }

        public static List<Profile> Load()
        {
            List<Profile> list = new List<Profile>();
            if (!File.Exists(File2)) return list;
            foreach (string line in File.ReadAllLines(File2))
            {
                if (line.Trim().Length == 0) continue;
                string[] f = line.Split('\t');
                Profile p = Codec.ToProfile(f[0]);
                if (p == null) continue;
                string host0 = p.Server;
                int c0 = host0.LastIndexOf(':');
                if (c0 > 0) host0 = host0.Substring(0, c0);
                System.Net.IPAddress ip0;
                if (System.Net.IPAddress.TryParse(host0, out ip0)) continue; // ключи по IP больше не нужны
                if (f.Length > 1) p.Cc = f[1];
                if (f.Length > 2) p.Country = f[2];
                if (f.Length > 3) p.ExitIp = f[3];
                if (f.Length > 4) p.Pin = f[4].Trim();
                if (f.Length > 5) p.Sub = f[5].Trim();
                if (f.Length > 6 && f[6].Trim().Length > 0) p.Server = f[6].Trim();
                if (f.Length > 7 && f[7].Trim().Length > 0)
                    p.Ports = Array.ConvertAll(f[7].Trim().Split(','), x => int.Parse(x.Trim()));
                if (f.Length > 8) p.City = f[8].Trim();
                list.Add(p);
            }
            return list;
        }

        public static void Save(List<Profile> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Profile p in list)
                sb.AppendLine(p.Key + "\t" + p.Cc + "\t" + p.Country + "\t" + p.ExitIp + "\t" + p.Pin +
                              "\t" + p.Sub + "\t" + p.Server +
                              "\t" + (p.Ports == null ? "" : string.Join(",", Array.ConvertAll(p.Ports, x => x.ToString()))) +
                              "\t" + p.City);
            File.WriteAllText(File2, sb.ToString(), new UTF8Encoding(false));
        }
    }

    static class Settings
    {
        public static bool Split = true, KillSwitch = true, Autostart = false, Tun = false, Onboarded = false;
        public static string Dns = "Через туннель";
        public static string Theme = "system";   // light | dark | system

        static string File2 { get { return Path.Combine(Engine.Dir, "settings.ini"); } }

        public static void Load()
        {
            if (!File.Exists(File2)) return;
            foreach (string l in File.ReadAllLines(File2))
            {
                string[] kv = l.Split(new[] { '=' }, 2);
                if (kv.Length != 2) continue;
                string k = kv[0].Trim(), v = kv[1].Trim();
                if (k == "split") Split = v != "0";
                if (k == "kill") KillSwitch = v != "0";
                if (k == "autostart") Autostart = v != "0";
                if (k == "tun") Tun = v != "0";
                if (k == "onboarded") Onboarded = v != "0";
                if (k == "dns") Dns = v;
                if (k == "theme") Theme = v;
            }
        }

        public static void Save()
        {
            File.WriteAllText(File2, "split=" + (Split ? "1" : "0") + "\nkill=" + (KillSwitch ? "1" : "0") +
                "\nautostart=" + (Autostart ? "1" : "0") + "\ntun=" + (Tun ? "1" : "0") +
                "\nonboarded=" + (Onboarded ? "1" : "0") +
                "\ndns=" + Dns + "\ntheme=" + Theme + "\n");
        }

        // Системная тема: Windows хранит её в AppsUseLightTheme (0 — тёмная).
        public static bool OsPrefersDark()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    if (k == null) return false;
                    object v = k.GetValue("AppsUseLightTheme");
                    return v != null && Convert.ToInt32(v) == 0;
                }
            }
            catch { return false; }
        }

        public static bool ResolveDark()
        {
            if (Theme == "dark") return true;
            if (Theme == "light") return false;
            return OsPrefersDark();
        }

        public static void ApplyAutostart()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (k == null) return;
                    if (Autostart) k.SetValue("veil", "\"" + Application.ExecutablePath + "\"");
                    else k.DeleteValue("veil", false);
                }
            }
            catch { }
        }
    }

    static class Codec
    {
        public static Profile ToProfile(string key)
        {
            KeyInfo ki = KeyInfo.Parse(key);
            if (ki == null || ki.Server.Length == 0) return null;
            Profile x = new Profile();
            x.Key = key.Trim();
            x.Server = ki.Server;
            x.Sni = ki.Sni;
            x.Fp = ki.Fp;
            x.Psk = ki.Psk;
            x.Pin = ki.Pin;
            return x;
        }
    }

    /* ── системный прокси ───────────────────────────────────────────────── */

    static class SysProxy
    {
        const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr h, int opt, IntPtr buf, int len);

        static string Bak { get { return Path.Combine(Engine.Dir, "proxy.bak"); } }

        public static void Enable()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (k == null) return;
                    if (!File.Exists(Bak))
                        File.WriteAllText(Bak, k.GetValue("ProxyEnable", 0) + "\n" + k.GetValue("ProxyServer", ""));
                    k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    k.SetValue("ProxyServer", "http=127.0.0.1:8081;https=127.0.0.1:8081;socks=127.0.0.1:1080", RegistryValueKind.String);
                    k.SetValue("ProxyOverride", "<local>", RegistryValueKind.String);
                }
            }
            catch { }
            Refresh();
        }

        // Если приложение упало, в реестре остаётся наш прокси без живого клиента —
        // браузеры показывают «нет подключения». Возвращаем как было при старте.
        public static void RestoreIfStale()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath, false))
                {
                    if (k == null) return;
                    int en = Convert.ToInt32(k.GetValue("ProxyEnable", 0));
                    string srv = Convert.ToString(k.GetValue("ProxyServer", ""));
                    bool ours = en == 1 && srv.IndexOf("127.0.0.1:8081", StringComparison.Ordinal) >= 0;
                    bool clientUp = Process.GetProcessesByName("veil-client").Length > 0;
                    if (ours && !clientUp)
                    {
                        Program.Log("proxy: найден наш висящий прокси, восстанавливаю");
                        Disable();
                    }
                }
            }
            catch (Exception ex) { Program.Log("proxy restore: " + ex.Message); }
        }

        public static void Disable()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (k == null) return;
                    int en = 0; string srv = "";
                    if (File.Exists(Bak))
                    {
                        string[] ln = File.ReadAllLines(Bak);
                        if (ln.Length > 0) int.TryParse(ln[0], out en);
                        if (ln.Length > 1) srv = ln[1];
                        File.Delete(Bak);
                    }
                    k.SetValue("ProxyEnable", en, RegistryValueKind.DWord);
                    if (srv.Length > 0) k.SetValue("ProxyServer", srv, RegistryValueKind.String);
                }
            }
            catch { }
            Refresh();
        }

        static void Refresh()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, 39, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, 37, IntPtr.Zero, 0);
            }
            catch { }
        }
    }

    /* ── движок: запуск veil-client.exe ─────────────────────────────────── */

    static class Engine
    {
        public static readonly List<string> Log = new List<string>();

        public static string Dir
        {
            get
            {
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string ClientPath { get { return Path.Combine(Dir, "veil-client.exe"); } }
        public static string IniPath { get { return Path.Combine(Dir, "veil.ini"); } }

        public static void ExtractClient()
        {
            ExtractRes("veil-client.exe", ClientPath, true);
            // wintun нужен рядом с клиентом для TUN-режима
            ExtractRes("wintun.dll", Path.Combine(Dir, "wintun.dll"), false);
        }

        static void ExtractRes(string suffix, string dst, bool required)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string name = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { name = n; break; }
            if (name == null)
            {
                if (required) throw new Exception(suffix + " not embedded");
                return;
            }
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (File.Exists(dst) && new FileInfo(dst).Length == s.Length) return;
                if (File.Exists(dst)) File.Delete(dst);
                using (FileStream f = File.Create(dst)) s.CopyTo(f);
            }
        }

        // Российский выход: берём сервер из подписки с кодом страны RU.
        public static string RuExitServer = "", RuExitSni = "";

        public static void WriteIni(Profile p)
        {
            // Раздельного туннелирования больше нет: локальная сеть идёт напрямую,
            // российские адреса — через РУ-сервер, остальное — через основной туннель.
            string extra = "";
            if (RuExitServer.Length > 0)
                extra = "ru-exit = " + RuExitServer + "\n" +
                        "ru-exit-sni = " + (RuExitSni.Length > 0 ? RuExitSni : RuExitServer.Split(':')[0]) + "\n" +
                        "ru-exit-ports = 8443,2083,9443\n";

            File.WriteAllText(IniPath,
                "server = " + p.Server + "\n" +
                "sni    = " + p.Sni + "\n" +
                "psk    = " + p.Psk + "\n" +
                "socks  = 127.0.0.1:1080\n" +
                "fp     = " + p.Fp + "\n" +
                (p.Pin.Length > 0 ? "pin    = " + p.Pin + "\n" : "") +
                "direct  = true\n" + extra, new UTF8Encoding(false));
        }

        public static Process Start()
        {
            ExtractClient();
            // убрать хвосты прошлых запусков: они держат 1080/8081
            foreach (Process old in Process.GetProcessesByName("veil-client"))
            {
                try { old.Kill(); old.WaitForExit(2000); } catch { }
            }
            Thread.Sleep(200);
            string args = "-config \"" + IniPath + "\"";
            if (Settings.Tun) args += " -tun -tun-mtu 1400";
            ProcessStartInfo psi = new ProcessStartInfo(ClientPath, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            psi.RedirectStandardError = true;
            psi.RedirectStandardOutput = true;
            Process p = new Process();
            p.StartInfo = psi;
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { Add(e.Data); };
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { Add(e.Data); };
            p.Start();
            p.BeginErrorReadLine();
            p.BeginOutputReadLine();
            return p;
        }

        static void Add(string line)
        {
            if (line == null) return;
            if (line.Length > 20 && line[4] == '/' && line[7] == '/' && line[13] == ':') line = line.Substring(11);
            lock (Log) { Log.Add(line); if (Log.Count > 400) Log.RemoveAt(0); }
        }

        public static bool WaitListen(int ms)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(ms);
            while (DateTime.UtcNow < end)
            {
                try
                {
                    using (TcpClient c = new TcpClient())
                    {
                        IAsyncResult ar = c.BeginConnect("127.0.0.1", 1080, null, null);
                        if (ar.AsyncWaitHandle.WaitOne(300) && c.Connected) return true;
                    }
                }
                catch { }
                Thread.Sleep(120);
            }
            return false;
        }

        public static string Tail(int n)
        {
            lock (Log)
            {
                int from = Math.Max(0, Log.Count - n);
                StringBuilder sb = new StringBuilder();
                for (int i = from; i < Log.Count; i++) sb.AppendLine(Log[i]);
                return sb.ToString();
            }
        }
    }

    /* ── SOCKS-проба: реальный запрос через туннель ─────────────────────── */

    static class Probe
    {
        public static string Get(string host, string path, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    c.ReceiveTimeout = timeoutMs;
                    c.Connect("127.0.0.1", 1080);
                    NetworkStream s = c.GetStream();
                    s.ReadTimeout = timeoutMs;
                    s.Write(new byte[] { 5, 1, 0 }, 0, 3);
                    byte[] r = new byte[2];
                    if (s.Read(r, 0, 2) != 2 || r[1] != 0) return null;
                    byte[] hb = Encoding.ASCII.GetBytes(host);
                    byte[] req = new byte[5 + hb.Length + 2];
                    req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)hb.Length;
                    Array.Copy(hb, 0, req, 5, hb.Length);
                    req[5 + hb.Length] = 0; req[6 + hb.Length] = 80;
                    s.Write(req, 0, req.Length);
                    byte[] rep = new byte[4];
                    if (s.Read(rep, 0, 4) != 4 || rep[1] != 0) return null;
                    byte[] skip = new byte[6];
                    if (rep[3] == 1) s.Read(skip, 0, 6);
                    else if (rep[3] == 4) { byte[] s16 = new byte[18]; s.Read(s16, 0, 18); }
                    else { int l = s.ReadByte(); byte[] sN = new byte[l + 2]; s.Read(sN, 0, sN.Length); }
                    byte[] get = Encoding.ASCII.GetBytes(
                        "GET " + path + " HTTP/1.0\r\nHost: " + host + "\r\nUser-Agent: veil/1\r\nConnection: close\r\n\r\n");
                    s.Write(get, 0, get.Length);
                    MemoryStream ms = new MemoryStream();
                    byte[] buf = new byte[8192];
                    while (true)
                    {
                        int n = s.Read(buf, 0, buf.Length);
                        if (n <= 0) break;
                        ms.Write(buf, 0, n);
                        if (ms.Length > 65536) break;
                    }
                    string all = Encoding.UTF8.GetString(ms.ToArray());
                    int body = all.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                    return body >= 0 ? all.Substring(body + 4) : all;
                }
            }
            catch { return null; }
        }

        public static int TcpPing(string host, int port, int timeoutMs)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    DateTime t0 = DateTime.Now;
                    IAsyncResult ar = c.BeginConnect(host, port, null, null);
                    if (!ar.AsyncWaitHandle.WaitOne(timeoutMs)) return 0;
                    c.EndConnect(ar);
                    return (int)(DateTime.Now - t0).TotalMilliseconds;
                }
            }
            catch { return 0; }
        }
    }

    /* ── состояние и мост в JS ──────────────────────────────────────────── */

    class Core
    {
        public readonly List<Profile> Profiles = Store.Load();
        public string State = "disconnected";
        public int Selected = 0;
        public bool CheckOk;
        public string CheckText = "";
        public int UptimeSec;
        public int LastPing;
        public readonly List<int> PingSeries = new List<int>();
        public string Cc = "", Country = "", ExitIp = "";
        public Process Proc;

        public Profile Current
        {
            get { return Profiles.Count == 0 ? null : Profiles[Math.Max(0, Math.Min(Selected, Profiles.Count - 1))]; }
        }

        // РУ-выход берём из подписки: сервер с кодом страны RU.
        void ApplyRuExit(Profile current)
        {
            Profile ru = Profiles.Find(x => x.Cc == "RU");
            // если сам туннель уже российский — отдельный выход не нужен
            if (ru != null && current != null && string.Equals(ru.Server, current.Server, StringComparison.OrdinalIgnoreCase))
                ru = null;
            Engine.RuExitServer = ru != null ? ru.Server : "";
            Engine.RuExitSni = ru != null ? ru.Sni : "";
            if (ru != null) Program.Log("ru-exit: " + ru.Server + " (sni=" + ru.Sni + ")");
        }

        public void OpenTunnel()
        {
            if (Profiles.Count == 0) { State = "error"; return; }
            Profile p = Current;
            State = "connecting";
            try
            {
                ApplyRuExit(p);
                Engine.WriteIni(p);
                Proc = Engine.Start();
                if (!Engine.WaitListen(7000) || Proc.HasExited)
                {
                    Program.Log("connect: client died, exit=" + (Proc.HasExited ? Proc.ExitCode.ToString() : "?"));
                    State = "error";
                    Kill();
                    return;
                }
                SysProxy.Enable();
                State = "connected";
                UptimeSec = 0;
                PingSeries.Clear();
                ThreadPool.QueueUserWorkItem(delegate { Measure(); });
            }
            catch (Exception ex)
            {
                Program.Log("connect: " + ex);
                State = "error";
                Kill();
            }
        }

        void Measure()
        {
            string host = Current.Server;
            string h = host.Split(':')[0];
            int port = 443;
            int ci = host.IndexOf(':');
            if (ci > 0) int.TryParse(host.Substring(ci + 1), out port);

            LastPing = Probe.TcpPing(h, port, 4000);

            // каскад поднимается дольше порта 443: если первый замер не удался — повторяем
            for (int attempt = 0; attempt < 3 && string.IsNullOrEmpty(ExitIp); attempt++)
            {
                if (attempt > 0) System.Threading.Thread.Sleep(4000);
                string body = Probe.Get("ip-api.com", "/json/?fields=status,country,countryCode,query", 12000);
                if (body == null) continue;
                Cc = Json(body, "countryCode") ?? "";
                Country = Json(body, "country") ?? "";
                ExitIp = Json(body, "query") ?? "";
                Profile p = Current;
                if (p != null && Cc.Length == 2)
                {
                    p.ExitIp = ExitIp;
                    if (p.Sub.Length == 0) { p.Cc = Cc; p.Country = Country; }   // у подписки страна из пула
                    Store.Save(Profiles);
                }
            }
            // ip-api иногда не отвечает через каскад — берём IP из второго источника
            if (string.IsNullOrEmpty(ExitIp))
            {
                string ip = Probe.Get("api.ipify.org", "/", 8000);
                if (ip != null)
                {
                    ip = ip.Trim();
                    if (ip.Length >= 7 && ip.IndexOf('.') > 0 && ip.Length < 20) ExitIp = ip;
                }
            }

            string probe = Probe.Get("example.com", "/", 12000);
            CheckOk = probe != null && probe.IndexOf("Example Domain", StringComparison.Ordinal) >= 0;
            CheckText = CheckOk ? "проверка ok · " + LastPing + " мс" : "нет ответа через туннель";
        }

        static string Json(string s, string key)
        {
            string pat = "\"" + key + "\":\"";
            int i = s.IndexOf(pat, StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            i += pat.Length;
            int j = s.IndexOf('"', i);
            return j > i ? SshText.Unescape(s.Substring(i, j - i)) : null;
        }

        public void Kill()
        {
            try { if (Proc != null && !Proc.HasExited) { Proc.Kill(); Proc.WaitForExit(2500); } } catch { }
            if (Settings.Tun) CleanupTunRoute();
            Proc = null;
            SysProxy.Disable();
            if (State == "connected" || State == "connecting") State = "disconnected";
        }

        // жёсткое убийство клиента не даёт ему убрать маршрут — чистим сами
        public static void CleanupTunRoute()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("route",
                    "delete 0.0.0.0 mask 0.0.0.0 198.18.0.1");
                psi.CreateNoWindow = true;
                psi.UseShellExecute = false;
                psi.RedirectStandardOutput = true;
                Process.Start(psi);
            }
            catch { }
        }

        public void Tick()
        {
            if (Proc != null && Proc.HasExited && (State == "connected" || State == "connecting"))
            {
                State = "error";
                Kill();
            }
            if (State == "connected")
            {
                UptimeSec++;
                if (LastPing > 0)
                {
                    PingSeries.Add(LastPing + new Random().Next(-3, 4));
                    if (PingSeries.Count > 40) PingSeries.RemoveAt(0);
                }
            }
        }
    }


    /* ── приложения: запуск через прокси ────────────────────────────────── */

    class AppEntry
    {
        public string Name = "", Path = "";
        public bool On = true;
    }

    static class Apps
    {
        static string File2 { get { return System.IO.Path.Combine(Engine.Dir, "apps.json"); } }

        public static List<AppEntry> Load()
        {
            List<AppEntry> list = new List<AppEntry>();
            try
            {
                if (!File.Exists(File2)) return list;
                string raw = File.ReadAllText(File2);
                foreach (string chunk in raw.Split(new[] { "\n" }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string[] f = chunk.Split('\t');
                    if (f.Length < 3) continue;
                    AppEntry a = new AppEntry();
                    a.Name = f[0]; a.Path = f[1]; a.On = f[2] == "1";
                    list.Add(a);
                }
            }
            catch { }
            return list;
        }

        public static void Save(List<AppEntry> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (AppEntry a in list)
                sb.Append(a.Name).Append('\t').Append(a.Path).Append('\t').Append(a.On ? "1" : "0").Append('\n');
            File.WriteAllText(File2, sb.ToString(), new UTF8Encoding(false));
        }

        static readonly string[] Chromium = { "chrome", "msedge", "brave", "opera", "vivaldi", "yandex", "browser" };

        public static string Launch(AppEntry a)
        {
            string exe = System.IO.Path.GetFileNameWithoutExtension(a.Path).ToLowerInvariant();
            ProcessStartInfo psi = new ProcessStartInfo(a.Path);
            if (!a.On) { Process.Start(psi); return exe + " (без туннеля)"; }
            bool chromium = Array.IndexOf(Chromium, exe) >= 0;

            if (chromium)
            {
                psi.Arguments = "--proxy-server=\"http://127.0.0.1:8081\" --proxy-bypass-list=\"<local>\"";
            }
            else if (exe == "firefox")
            {
                string prof = System.IO.Path.Combine(Engine.Dir, "ffprofile");
                Directory.CreateDirectory(prof);
                File.WriteAllText(System.IO.Path.Combine(prof, "prefs.js"),
                    "user_pref(\"network.proxy.type\", 1);\n" +
                    "user_pref(\"network.proxy.http\", \"127.0.0.1\");\n" +
                    "user_pref(\"network.proxy.http_port\", 8081);\n" +
                    "user_pref(\"network.proxy.ssl\", \"127.0.0.1\");\n" +
                    "user_pref(\"network.proxy.ssl_port\", 8081);\n" +
                    "user_pref(\"network.proxy.allow_hijacking_localhost\", true);\n");
                psi.Arguments = "-profile \"" + prof + "\" -no-remote";
            }
            else
            {
                psi.EnvironmentVariables["HTTP_PROXY"] = "http://127.0.0.1:8081";
                psi.EnvironmentVariables["HTTPS_PROXY"] = "http://127.0.0.1:8081";
                psi.EnvironmentVariables["ALL_PROXY"] = "socks5://127.0.0.1:1080";
                psi.EnvironmentVariables["NO_PROXY"] = "localhost,127.0.0.1";
            }
            psi.UseShellExecute = false;
            Process.Start(psi);
            return exe;
        }
    }

    class Shell : Form
    {
        readonly Core core = new Core();
        readonly List<AppEntry> apps = Apps.Load();
        WebView2 web;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool ready;

        readonly bool autoConnect;
        readonly string openTab = "";

        public Shell(string[] args)
        {
            foreach (string a in args)
            {
                if (string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)) autoConnect = true;
                if (a.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase)) openTab = a.Substring(6);
            }
            Text = "veil";
            ClientSize = new Size(420, 860);
            MinimumSize = new Size(430, 700);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 12, 17);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Settings.Load();
            Program.ForceLightTitleBar(Handle, Settings.ResolveDark() ? 1 : 0);
            try
            {
                using (Stream ico = System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("veil.ico"))
                    if (ico != null) Icon = new System.Drawing.Icon(ico);
            }
            catch { }

            web = new WebView2();
            web.Dock = DockStyle.Fill;
            Controls.Add(web);

            Load += async delegate
            {
                try
                {
                    string uiDir = Path.Combine(Engine.Dir, "ui");
                    Directory.CreateDirectory(uiDir);
                    string ui = Path.Combine(uiDir, "index.html");
                    foreach (string f in new[] { "index.html", "tokens.css", "app.css", "app.js" })
                        ExtractUiFile(f, Path.Combine(uiDir, f));
                    Program.Log("ui: " + ui);
                    Program.Log("step: create env");
                    CoreWebView2Environment env = await CoreWebView2Environment.CreateAsync(null, Program.RuntimeDir);
                    Program.Log("step: env ok");
                    await web.EnsureCoreWebView2Async(env);
                    Program.Log("step: core ok");
                    web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
                    web.CoreWebView2.Settings.IsStatusBarEnabled = false;
                    web.CoreWebView2.Settings.AreDevToolsEnabled = false;
                    // Auto: страница получает системный prefers-color-scheme, режим «Системная» работает честно
                    web.CoreWebView2.Profile.PreferredColorScheme = CoreWebView2PreferredColorScheme.Auto;
                    web.CoreWebView2.WebMessageReceived += OnMessage;
                    web.CoreWebView2.Navigate(new Uri(ui).AbsoluteUri);

                    System.Windows.Forms.Timer probeTimer = new System.Windows.Forms.Timer();
                    probeTimer.Interval = 2500;
                    probeTimer.Tick += async delegate
                    {
                        probeTimer.Stop();
                        try
                        {
                            string probe = await web.CoreWebView2.ExecuteScriptAsync(
                                "JSON.stringify({ui:!!window.VEIL_UI,wv:!!(window.chrome&&window.chrome.webview),err:window.__veilErr||null,view:document.getElementById('view').innerHTML.length})");
                            Program.Log("page probe: " + probe);
                            if (openTab.Length > 0)
                                await web.CoreWebView2.ExecuteScriptAsync(
                                    "document.querySelector('.tab[data-tab=" + openTab + "]').click()");
                            // диагностика: выбрать профиль по индексу и подключиться
                            string pickArg = Program.ArgOf(Environment.GetCommandLineArgs(), "--pick=");
                            if (pickArg.Length > 0)
                            {
                                int idx;
                                if (int.TryParse(pickArg, out idx) && idx >= 0 && idx < core.Profiles.Count)
                                {
                                    core.Selected = idx;
                                    Program.Log("debug pick: " + idx + " " + core.Profiles[idx].Server + " sub=" + core.Profiles[idx].Sub);
                                    ThreadPool.QueueUserWorkItem(delegate { core.OpenTunnel(); BeginInvoke(new MethodInvoker(Push)); });
                                }
                                else Program.Log("debug pick: индекса " + pickArg + " нет (профилей " + core.Profiles.Count + ")");
                            }
                            // несколько кликов подряд: --clicks=[data-action=settings];[data-action=server]
                            string clicksArg = Program.ArgOf(Environment.GetCommandLineArgs(), "--clicks=");
                            if (clicksArg.Length > 0)
                            {
                                foreach (string one in clicksArg.Split(';'))
                                {
                                    if (one.Trim().Length == 0) continue;
                                    Program.Log("debug clicks: " + one);
                                    await web.CoreWebView2.ExecuteScriptAsync(
                                        "(() => { const el = document.querySelector(" + Program.QuoteJs(one.Trim()) + "); if (!el) return 'нет элемента'; el.click(); return 'ok'; })()");
                                    await System.Threading.Tasks.Task.Delay(350);
                                }
                            }
                            // отладочный клик по селектору: veil.exe --click=[data-action=settings]
                            string clickSel = Program.ArgOf(Environment.GetCommandLineArgs(), "--click=");
                            if (clickSel.Length > 0)
                            {
                                Program.Log("debug click: " + clickSel);
                                await web.CoreWebView2.ExecuteScriptAsync(
                                    "(() => { const el = document.querySelector(" + Program.QuoteJs(clickSel) + "); if (!el) return 'нет элемента'; el.click(); return 'ok'; })()");
                            // отладочный eval: veil.exe --eval=document.title
                            string evalJs = Program.ArgOf(Environment.GetCommandLineArgs(), "--eval=");
                            if (evalJs.Length > 0)
                            {
                                string res = await web.CoreWebView2.ExecuteScriptAsync(evalJs);
                                Program.Log("eval: " + (res != null && res.Length > 600 ? res.Substring(0, 600) : res));
                            }
                            }
                        }
                        catch (Exception ex) { Program.Log("probe fail: " + ex.Message); }
                    };
                    probeTimer.Start();
                    Program.Log("step: navigate done");
            StartSubRefresh();                     // подписки: сразу и потом раз в 6 часов

                    bool pasteKey = false;
                    foreach (string a0 in Environment.GetCommandLineArgs()) if (a0 == "--paste-key") pasteKey = true;
                    if (pasteKey)
                    {
                        try { AddKeyFromText(Clipboard.ContainsText() ? Clipboard.GetText() : ""); }
                        catch (Exception ex) { Program.Log("paste-key: " + ex.Message); }
                    }

                    if (autoConnect)
                    {
                        System.Windows.Forms.Timer t = new System.Windows.Forms.Timer();
                        t.Interval = 700;
                        t.Tick += delegate
                        {
                            t.Stop();
                            ThreadPool.QueueUserWorkItem(delegate { core.OpenTunnel(); BeginInvoke(new MethodInvoker(Push)); });
                        };
                        t.Start();
                    }
                }
                catch (Exception ex)
                {
                    Program.Log("webview: " + ex);
                    MessageBox.Show("WebView2 не запустился:\n" + ex.Message, "veil");
                    Close();
                }
            };

            FormClosing += delegate { core.Kill(); };

            timer.Interval = 1000;
            timer.Tick += delegate { core.Tick(); if (ready) PushSafe(); };
            timer.Start();
        }

        static string ExtractUiFile(string name, string dst)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string res = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith(name, StringComparison.OrdinalIgnoreCase)) { res = n; break; }
            if (res == null) return "";
            using (Stream s = asm.GetManifestResourceStream(res))
            {
                if (!(File.Exists(dst) && new FileInfo(dst).Length == s.Length))
                    using (FileStream f = File.Create(dst)) s.CopyTo(f);
            }
            return dst;
        }

        // Одно поле ввода: если это существующий файл — ключ, иначе пароль.
        // Пустые host/user/secret подставляются из сохранённых данных.
        SshTarget MakeTarget(string host, string port, string user, string secret)
        {
            host = (host ?? "").Trim();
            user = (user ?? "").Trim();
            secret = (secret ?? "").Trim();
            if (host.Length == 0) host = SshCreds.Host;
            if (user.Length == 0) user = SshCreds.User.Length > 0 ? SshCreds.User : "root";
            if (secret.Length == 0) secret = SshCreds.Secret;

            var t = new SshTarget { Host = host, Port = IntOr(port, SshCreds.Port > 0 ? SshCreds.Port : 22), User = user };
            bool looksPath = secret.Contains("\\") || secret.Contains("/");
            if (looksPath && !File.Exists(secret) && File.Exists(secret.Replace("\\\\", "\\")))
                secret = secret.Replace("\\\\", "\\");          // путь из старой версии с двойными слэшами
            if (looksPath && File.Exists(secret)) t.KeyPath = secret;
            else t.Password = secret;
            return t;
        }

        void RememberSsh(SshTarget t, string secret)
        {
            if (t == null || t.Host.Length == 0) return;
            SshCreds.Host = t.Host; SshCreds.User = t.User; SshCreds.Port = t.Port;
            SshCreds.Secret = t.KeyPath.Length > 0 ? t.KeyPath : (secret ?? "");
            SshCreds.Save();
        }

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;
            string cmd = Json(raw, "cmd");
            Program.Log("msg: " + raw);
            if (cmd == "ready") { ready = true; PushSafe(); return; }
            if (cmd == "connect")
            {
                if (core.State == "connected") core.Kill();
                else if (core.State != "connecting") ThreadPool.QueueUserWorkItem(delegate { core.OpenTunnel(); BeginInvoke(new MethodInvoker(Push)); });
            }
            else if (cmd == "disconnect") { core.Kill(); }
            else if (cmd == "select")
            {
                string id = Json(raw, "id");
                for (int i = 0; i < core.Profiles.Count; i++)
                    if (core.Profiles[i].Server == id) core.Selected = i;
                if (core.State == "connected") { core.Kill(); ThreadPool.QueueUserWorkItem(delegate { core.OpenTunnel(); BeginInvoke(new MethodInvoker(Push)); }); }
            }
            else if (cmd == "fav") { }
            else if (cmd == "key-paste")
            {
                string txt = "";
                try { if (Clipboard.ContainsText()) txt = Clipboard.GetText(); } catch { }
                AddKeyFromText(txt);
            }
            else if (cmd == "key-add") { AddKeyFromText(Json(raw, "key")); }
            else if (cmd == "onboard") { Settings.Onboarded = true; Settings.Save(); PushSafe(); }
            else if (cmd == "srv-install")
            {
                srvTarget = MakeTarget(Json(raw, "host"), Json(raw, "port"), Json(raw, "user"), Json(raw, "secret"));
                if (srvTarget.Host.Length == 0) { srvStatus = "Укажи сервер"; PushSafe(); return; }
                string domain = Json(raw, "domain").Trim();
                srvHost = srvTarget.Host; srvUser = srvTarget.User;
                srvEnteredSecret = (Json(raw, "secret") ?? "").Trim();
                srvBusy = true; srvStatus = "Устанавливаю…"; srvKeys.Clear(); PushSafe();
                ThreadPool.QueueUserWorkItem(delegate { DoInstall(domain); });
            }
            else if (cmd == "srv-connect")
            {
                srvTarget = MakeTarget(Json(raw, "host"), Json(raw, "port"), Json(raw, "user"), Json(raw, "secret"));
                if (srvTarget.Host.Length == 0) { srvStatus = "Укажи сервер"; PushSafe(); return; }
                srvHost = srvTarget.Host; srvUser = srvTarget.User;
                string srvDomain = Json(raw, "domain").Trim();
                srvEnteredSecret = (Json(raw, "secret") ?? "").Trim();
                srvBusy = true; srvStatus = "Соединяюсь по SSH…"; PushSafe();
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        using (SshClient sc = ServerSetup.Connect(srvTarget))
                        {
                            srvStatus = "Проверяю, установлен ли сервер…"; PushSafe();
                            srvInstalled = ServerSetup.Installed(sc);

                            if (!srvInstalled)
                            {
                                // ставим сами: бинарник, сертификат, юнит, первый ключ
                                srvBusy = true; PushSafe();
                                DoInstall(srvDomain);
                                return;
                            }

                            srvKeys = ServerSetup.Keys(sc);
                            srvPin = ServerSetup.Pin(sc);
                            RememberSsh(srvTarget, srvEnteredSecret);
                            srvStatus = "Подключено · ключей: " + srvKeys.Count + " · данные сохранены";
                        }
                    }
                    catch (Exception ex) { srvStatus = SshError(ex); }
                    srvBusy = false; PushSafe();
                });
            }
            else if (cmd == "sub-refresh")
            {
                ThreadPool.QueueUserWorkItem(delegate {
                    int added, dropped;
                    string err = Subs.Refresh(core.Profiles, out added, out dropped);
                    Store.Save(core.Profiles);
                    Program.Log("subs manual: +" + added + " -" + dropped + " " + err);
                    PushSafe();
                });
            }
            else if (cmd == "srv-forget")
            {
                SshCreds.Clear();
                srvTarget = null; srvKeys.Clear(); srvHost = ""; srvUser = "";
                srvStatus = "Данные SSH забыты";
                PushSafe();
            }
            else if (cmd == "srv-keys")
            {
                if (srvTarget == null) { srvStatus = "Сначала подключись к серверу"; PushSafe(); return; }
                srvBusy = true; srvStatus = "Читаю ключи…"; PushSafe();
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        using (SshClient sc = ServerSetup.Connect(srvTarget))
                        {
                            srvKeys = ServerSetup.Keys(sc);
                            srvStatus = "Ключей на сервере: " + srvKeys.Count;
                        }
                    }
                    catch (Exception ex) { srvStatus = SshError(ex); }
                    srvBusy = false; PushSafe();
                });
            }
            else if (cmd == "srv-issue")
            {
                if (srvTarget == null) { srvStatus = "Сначала подключись к серверу"; PushSafe(); return; }
                string who = Json(raw, "name").Trim();
                srvBusy = true; srvStatus = "Выдаю ключ…"; PushSafe();
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        using (SshClient sc = ServerSetup.Connect(srvTarget))
                        {
                            string cn = ServerSetup.CertName(sc);
                            string hostForKey = cn.Length > 0 ? cn : HostOf(srvTarget.Host);
                            string key = ServerSetup.Issue(sc, who, hostForKey, PortOf(srvTarget.Host, 443), hostForKey, srvPin);
                            keyMsg = "Ключ для «" + who + "»: " + key;
                            srvKeys = ServerSetup.Keys(sc);
                            srvStatus = "Ключей на сервере: " + srvKeys.Count;
                        }
                    }
                    catch (Exception ex) { srvStatus = SshError(ex); }
                    srvBusy = false; PushSafe();
                });
            }
            else if (cmd == "srv-revoke")
            {
                if (srvTarget == null) { srvStatus = "Сначала подключись к серверу"; PushSafe(); return; }
                string who = Json(raw, "name").Trim();
                srvBusy = true; srvStatus = "Отзываю ключ…"; PushSafe();
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        using (SshClient sc = ServerSetup.Connect(srvTarget))
                        {
                            ServerSetup.Revoke(sc, who);
                            srvKeys = ServerSetup.Keys(sc);
                            srvStatus = "Ключ «" + who + "» отозван";
                        }
                    }
                    catch (Exception ex) { srvStatus = SshError(ex); }
                    srvBusy = false; PushSafe();
                });
            }

            else if (cmd == "app-add")
            {
                using (OpenFileDialog dlg = new OpenFileDialog())
                {
                    dlg.Title = "Выбери приложение";
                    dlg.Filter = "Программы (*.exe)|*.exe";
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        string path = dlg.FileName;
                        bool dup = false;
                        foreach (AppEntry a in apps) if (a.Path == path) dup = true;
                        if (!dup)
                        {
                            AppEntry ent = new AppEntry();
                            ent.Name = Path.GetFileNameWithoutExtension(path);
                            ent.Path = path;
                            ent.On = true;
                            apps.Add(ent);
                            Apps.Save(apps);
                        }
                    }
                }
            }
            else if (cmd == "app-toggle")
            {
                string path = Json(raw, "path");
                foreach (AppEntry a in apps) if (a.Path == path) a.On = !a.On;
                Apps.Save(apps);
            }
            else if (cmd == "app-del")
            {
                string path = Json(raw, "path");
                apps.RemoveAll(delegate(AppEntry a) { return a.Path == path; });
                Apps.Save(apps);
            }
            else if (cmd == "app-run")
            {
                string path = Json(raw, "path");
                foreach (AppEntry a in apps)
                {
                    if (a.Path != path) continue;
                    try
                    {
                        string kind = Apps.Launch(a);
                        Program.Log("launch: " + a.Name + " via proxy (" + kind + ")");
                    }
                    catch (Exception ex) { Program.Log("launch failed: " + ex.Message); }
                }
            }
            else if (cmd == "setting")
            {
                string k = Json(raw, "key"), v = Json(raw, "value") ?? "";
                if (k == "split") { Settings.Split = v == "true"; Settings.Save(); }
                if (k == "killSwitch") { Settings.KillSwitch = v == "true"; Settings.Save(); }
                if (k == "autostart") { Settings.Autostart = v == "true"; Settings.Save(); Settings.ApplyAutostart(); }
                if (k == "dns") { Settings.Dns = v; Settings.Save(); }
                if (k == "theme")
                {
                    Settings.Theme = v;
                    Settings.Save();
                    Program.ForceLightTitleBar(Handle, Settings.ResolveDark() ? 1 : 0);
                }
                if (k == "tun")
                {
                    bool want = v == "true";
                    if (want && !IsAdmin())
                    {
                        MessageBox.Show(this,
                            "TUN-режим создаёт сетевой адаптер и требует прав администратора.\n\n" +
                            "Закрой приложение и запусти его от имени администратора — тумблер станет рабочим.",
                            "veil", MessageBoxButtons.OK, MessageBoxIcon.Information);
                        Settings.Tun = false;
                    }
                    else Settings.Tun = want;
                    Settings.Save();
                    if (core.State == "connected") { core.Kill(); ThreadPool.QueueUserWorkItem(delegate { core.OpenTunnel(); BeginInvoke(new MethodInvoker(Push)); }); }
                }
            }
            PushSafe();
        }

        static string Json(string s, string key)
        {
            int i = s.IndexOf("\"" + key + "\":", StringComparison.Ordinal);
            if (i < 0) return null;
            i += key.Length + 3;
            if (i >= s.Length) return null;
            if (s[i] == '"')
            {
                int j = s.IndexOf('"', i + 1);
                return j > i ? SshText.Unescape(s.Substring(i + 1, j - i - 1)) : null;
            }
            int k = i;
            while (k < s.Length && ",}]".IndexOf(s[k]) < 0) k++;
            return s.Substring(i, k - i).Trim();
        }

        static bool IsAdmin()
        {
            try
            {
                System.Security.Principal.WindowsIdentity id = System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal p = new System.Security.Principal.WindowsPrincipal(id);
                return p.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch { return false; }
        }

        // Push вызывается и из фоновых потоков — WebView2 требует UI-поток
        void PushSafe()
        {
            try
            {
                if (!IsHandleCreated) return;
                if (InvokeRequired) BeginInvoke(new MethodInvoker(PushSafe));
                else Push();
            }
            catch (Exception ex) { Program.Log("push-safe: " + ex.Message); }
        }

        void Push()
        {
            if (!ready || web.CoreWebView2 == null) return;
            string s = BuildSnapshot();
            try
            {
                web.CoreWebView2.PostWebMessageAsJson(s);
            }
            catch (Exception ex)
            {
                Program.Log("push failed: " + ex.GetType().Name + ": " + ex.Message);
                Program.Log("snapshot: " + (s.Length > 1500 ? s.Substring(0, 1500) : s));
            }
        }

        string keyMsg = "";

        // ключи только с доменом: адрес по IP больше не принимаем
        void AddKeyFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("veil://", StringComparison.OrdinalIgnoreCase) < 0)
            { keyMsg = "В буфере нет ключа veil://"; PushSafe(); return; }

            KeyInfo ki = KeyInfo.Parse(text);

            // Ключ подписки: один ключ — несколько серверов из пула.
            if (ki != null && ki.IsV2 && ki.Sub.Length > 0)
            {
                keyMsg = "Загружаю список серверов…"; PushSafe();
                string raw = text;
                ThreadPool.QueueUserWorkItem(delegate { AddSubscription(raw); });
                return;
            }

            Profile p = Codec.ToProfile(text);
            if (p == null) { keyMsg = "Ключ не распознан — проверь, что скопирован целиком"; PushSafe(); return; }

            string host = p.Server;
            int colon = host.LastIndexOf(':');
            if (colon > 0) host = host.Substring(0, colon);
            IPAddress ip;
            if (IPAddress.TryParse(host, out ip))
            { keyMsg = "Ключи по IP не поддерживаются — нужен домен"; PushSafe(); return; }

            foreach (Profile x in core.Profiles)
                if (string.Equals(x.Server, p.Server, StringComparison.OrdinalIgnoreCase))
                { keyMsg = "Этот ключ уже добавлен"; PushSafe(); return; }

            core.Profiles.Add(p);
            core.Selected = core.Profiles.Count - 1;
            Store.Save(core.Profiles);
            keyMsg = "Добавлено: " + p.Server;
            PushSafe();
        }

        // Развернуть ключ подписки в профили. Пул недоступен — остаётся seed из ключа.
        void AddSubscription(string rawKey)
        {
            try
            {
                KeyInfo ki = KeyInfo.Parse(rawKey);
                if (ki == null) { keyMsg = "Ключ подписки не распознан"; PushSafe(); return; }

                PoolDoc doc = null;
                string err = "";
                try { string used; doc = PoolClient.Fetch(ki.Subs, ki.Psk, ki.Pin, ki.Pk, out used); }
                catch (Exception ex) { err = ex.Message; }

                List<Profile> add = new List<Profile>();
                if (doc != null)
                {
                    add = Subs.Build(rawKey, ki, doc);
                    if (add.Count == 0) { keyMsg = "В пуле нет доступных серверов"; PushSafe(); return; }
                }

                int fresh = 0;
                foreach (Profile np in add)
                {
                    if (core.Profiles.Exists(x => string.Equals(x.Server, np.Server, StringComparison.OrdinalIgnoreCase))) continue;
                    core.Profiles.Add(np); fresh++;
                }

                // пул не ответил — берём запасной сервер из ключа, чтобы ключ не был мёртвым
                if (add.Count == 0 && ki.Seed.Length > 0)
                {
                    Profile seed = Codec.ToProfile(rawKey);
                    if (seed != null)
                    {
                        seed.Pin = ki.Pin;
                        if (!core.Profiles.Exists(x => string.Equals(x.Server, seed.Server, StringComparison.OrdinalIgnoreCase)))
                        { core.Profiles.Add(seed); fresh++; }
                    }
                }

                if (fresh > 0) core.Selected = core.Profiles.Count - 1;
                Store.Save(core.Profiles);
                keyMsg = fresh > 0
                    ? "Добавлено серверов: " + fresh + (doc != null ? " · " + doc.Name : " · пул недоступен, один сервер")
                    : (err.Length > 0 ? "Пул недоступен: " + err : "Эти серверы уже добавлены");
            }
            catch (Exception ex) { keyMsg = "Подписка: " + Short(ex); Program.Log("sub add: " + ex); }
            PushSafe();
        }

        // Периодическое обновление подписок: при старте и раз в 6 часов.
        void StartSubRefresh()
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                while (true)
                {
                    try
                    {
                        int added, dropped;
                        string err = Subs.Refresh(core.Profiles, out added, out dropped);
                        Store.Save(core.Profiles);       // метаданные (страна, порты) тоже могли измениться
                        if (core.Selected >= core.Profiles.Count) core.Selected = Math.Max(0, core.Profiles.Count - 1);
                        if (added > 0 || dropped > 0) Program.Log("subs refresh: +" + added + " -" + dropped);
                        if (err.Length > 0) Program.Log("subs refresh: " + err);
                        else Program.Log("subs refresh: +" + added + " -" + dropped + " (всего " + core.Profiles.Count + ")");
                        PushSafe();
                    }
                    catch (Exception ex) { Program.Log("subs refresh: " + ex.Message); }
                    System.Threading.Thread.Sleep(6 * 60 * 60 * 1000);
                }
            });
        }

        SshTarget srvTarget = null;
        string srvHost = "", srvUser = "", srvPin = "", srvStatus = "", srvEnteredSecret = "";
        List<string> srvKeys = new List<string>();
        bool srvBusy = false, srvInstalled = false;

        static int IntOr(string s, int def) { int v; return int.TryParse(s, out v) && v > 0 && v < 65536 ? v : def; }
        static string Short(Exception ex) { string m = ex.Message; return m.Length > 160 ? m.Substring(0, 160) : m; }

        // Понятный текст вместо «Permission denied (password)»
        static string SshError(Exception ex)
        {
            string m = ex.Message ?? "";
            if (ex is Renci.SshNet.Common.SshAuthenticationException || m.IndexOf("Permission denied", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SSH не принял логин/пароль. На Ubuntu вход root по паролю обычно выключен — укажи путь к ключу или включи PasswordAuthentication";
            if (ex is System.Net.Sockets.SocketException || m.IndexOf("connect", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Не удалось связаться с сервером по SSH: " + m + ". Проверь адрес, порт 22 и что сервер доступен";
            return "Ошибка SSH: " + Short(ex);
        }
        static string HostOf(string s) { int i = s.LastIndexOf(':'); return i > 0 ? s.Substring(0, i) : s; }
        static int PortOf(string s, int def) { int i = s.LastIndexOf(':'); int p; return i > 0 && int.TryParse(s.Substring(i + 1), out p) ? p : def; }
        static string SniOf(string s) { return HostOf(s); }

        void DoInstall(string domain)
        {
            try
            {
                byte[] bin = ReadResource("veil-server-linux");
                string boot = System.Text.Encoding.UTF8.GetString(ReadResource("bootstrap.sh"));
                if (bin == null || bin.Length < 1000) { srvStatus = "В сборке нет linux-бинарника"; srvBusy = false; PushSafe(); return; }

                using (SshClient sc = ServerSetup.Connect(srvTarget, 25))
                {
                    srvStatus = "Загружаю сервер…"; PushSafe();
                    string keyLine;
                    string log = ServerSetup.Install(sc, bin, boot, domain, "", out keyLine);
                    Program.Log("install log:\n" + (log.Length > 4000 ? log.Substring(log.Length - 4000) : log));

                    if (keyLine.Length == 0) { srvStatus = "Установка не вернула ключ, смотри лог"; srvBusy = false; PushSafe(); return; }

                    string[] parts = keyLine.Split('|');
                    string pay = parts[0];                       // server:port|sni|fp|psk
                    string pin = parts.Length > 1 ? parts[1] : "";
                    srvPin = pin;
                    string full = ServerSetup.Encode(pay, pin);
                    Profile p = Codec.ToProfile(full);
                    if (p != null)
                    {
                        bool dup = false;
                        foreach (Profile x in core.Profiles) if (x.Server == p.Server) dup = true;
                        if (!dup) { core.Profiles.Add(p); Store.Save(core.Profiles); }
                        core.Selected = core.Profiles.Count - 1;
                    }
                    srvKeys = ServerSetup.Keys(sc);
                    RememberSsh(srvTarget, srvEnteredSecret);
                    srvInstalled = true;
                    srvStatus = "Сервер готов · ключей: " + srvKeys.Count + (pin.Length > 0 ? " · сертификат по отпечатку" : " · Let's Encrypt");
                    keyMsg = "Ключ добавлен на главную";
                }
            }
            catch (Exception ex) { srvStatus = SshError(ex); Program.Log("install: " + ex); }
            srvBusy = false; PushSafe();
        }

        static byte[] ReadResource(string suffix)
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            string res = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)) { res = n; break; }
            if (res == null) return null;
            using (Stream s = asm.GetManifestResourceStream(res))
            {
                if (s == null) return null;
                var ms = new MemoryStream();
                s.CopyTo(ms);
                return ms.ToArray();
            }
        }

        string BuildSnapshot()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"type\":\"snapshot\",\"servers\":[");
            for (int i = 0; i < core.Profiles.Count; i++)
            {
                Profile p = core.Profiles[i];
                if (i > 0) sb.Append(',');
                sb.Append("{\"id\":\"").Append(Esc(p.Server)).Append("\"")
                  .Append(",\"cc\":\"").Append(Esc(p.Cc.Length > 0 ? p.Cc : "IP")).Append("\"")
                  .Append(",\"country\":\"").Append(Esc(p.Country.Length > 0 ? p.Country : "По IP")).Append("\"")
                  .Append(",\"city\":\"").Append(Esc(p.Sub.Length > 0
                      ? (p.City.Length > 0 ? p.City : p.Sni)
                      : (p.ExitIp.Length > 0 ? p.ExitIp : "по IP-адресу"))).Append("\"")
                  .Append(",\"ping\":").Append(p == core.Current ? core.LastPing : 0)
                  .Append(",\"load\":0,\"fav\":false,\"sub\":").Append(p.Sub.Length > 0 ? "true" : "false")
                  .Append(",\"exitIp\":\"").Append(Esc(p.ExitIp)).Append("\"}");
            }
            string sid = core.Current != null ? core.Current.Server : "";
            sb.Append("],\"snapshot\":{");
            sb.Append("\"state\":\"").Append(core.State).Append("\"");
            sb.Append(",\"serverId\":\"").Append(Esc(sid)).Append("\"");
            sb.Append(",\"subscription\":{\"active\":true,\"plan\":\"Личный\",\"until\":\"—\",\"usedGB\":0,\"totalGB\":0,\"devices\":1,\"deviceLimit\":5}");
            sb.Append(",\"traffic\":{\"up\":0,\"down\":0,\"uptime\":").Append(core.UptimeSec).Append("}");
            sb.Append(",\"settings\":{\"protocol\":\"veil · TLS 1.3\",\"killSwitch\":").Append(Settings.KillSwitch ? "true" : "false")
              .Append(",\"split\":").Append(Settings.Split ? "true" : "false")
              .Append(",\"autostart\":").Append(Settings.Autostart ? "true" : "false")
              .Append(",\"tun\":").Append(Settings.Tun ? "true" : "false")
              .Append(",\"dns\":\"").Append(Esc(Settings.Dns)).Append("\"")
              .Append(",\"theme\":\"").Append(Esc(Settings.Theme)).Append("\"}");
            sb.Append(",\"series\":[");
            for (int i = 0; i < core.PingSeries.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append(core.PingSeries[i]);
            }
            sb.Append("]");
            sb.Append(",\"seriesUnit\":\"ms\"");
            sb.Append(",\"apps\":[");
            for (int i = 0; i < apps.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append("{\"name\":\"").Append(Esc(apps[i].Name)).Append("\"")
                  .Append(",\"path\":\"").Append(Esc(apps[i].Path)).Append("\"")
                  .Append(",\"on\":").Append(apps[i].On ? "true" : "false").Append("}");
            }
            sb.Append("]");
            sb.Append(",\"keyMsg\":\"").Append(Esc(keyMsg)).Append("\"");
            sb.Append(",\"onboarded\":").Append(Settings.Onboarded ? "true" : "false");
            sb.Append(",\"srvStatus\":\"").Append(Esc(srvStatus)).Append("\"");
            sb.Append(",\"srvBusy\":").Append(srvBusy ? "true" : "false");
            sb.Append(",\"srvHost\":\"").Append(Esc(srvHost.Length > 0 ? srvHost : SshCreds.Host))
              .Append("\",\"srvUser\":\"").Append(Esc(srvUser.Length > 0 ? srvUser : (SshCreds.User.Length > 0 ? SshCreds.User : "root")))
              .Append("\",\"srvSaved\":").Append(SshCreds.Has ? "true" : "false")
              .Append(",\"srvInstalled\":").Append(srvInstalled ? "true" : "false");
            sb.Append(",\"srvPoolCount\":").Append(core.Profiles.FindAll(x => x.Sub.Length > 0).Count);
            sb.Append(",\"srvOwnCount\":").Append(core.Profiles.FindAll(x => x.Sub.Length == 0).Count);
            sb.Append(",\"srvKeys\":[");
            for (int i = 0; i < srvKeys.Count; i++) { if (i > 0) sb.Append(','); sb.Append("\"").Append(Esc(srvKeys[i])).Append("\""); }
            sb.Append("]");
            sb.Append(",\"error\":\"").Append(Esc(core.State == "error" ? "Клиент не поднялся, смотри лог" : "")).Append("\"");
            sb.Append("}}");
            return sb.ToString();
        }

        static string Esc(string s)
        {
            if (s == null) return "";
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ").Replace("\r", "");
        }
    }
}
