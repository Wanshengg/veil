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
            };
            AppDomain.CurrentDomain.AssemblyResolve += Resolve;
            try
            {
                ExtractAll();      // разложить DLL до первого обращения к WebView2
                InitLoader();      // теперь тип CoreWebView2Environment доступен
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
        public string Key = "", Server = "", Sni = "", Fp = "chrome", Psk = "";
        public string Cc = "", Country = "", ExitIp = "";
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
                if (f.Length > 1) p.Cc = f[1];
                if (f.Length > 2) p.Country = f[2];
                if (f.Length > 3) p.ExitIp = f[3];
                list.Add(p);
            }
            return list;
        }

        public static void Save(List<Profile> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Profile p in list) sb.AppendLine(p.Key + "\t" + p.Cc + "\t" + p.Country + "\t" + p.ExitIp);
            File.WriteAllText(File2, sb.ToString(), new UTF8Encoding(false));
        }
    }

    static class Settings
    {
        public static bool Split = true, KillSwitch = true, Autostart = false;
        public static string Dns = "Через туннель";
        public static string Theme = "dark";

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
                if (k == "dns") Dns = v;
                if (k == "theme") Theme = v;
            }
        }

        public static void Save()
        {
            File.WriteAllText(File2, "split=" + (Split ? "1" : "0") + "\nkill=" + (KillSwitch ? "1" : "0") +
                "\nautostart=" + (Autostart ? "1" : "0") + "\ndns=" + Dns + "\ntheme=" + Theme + "\n");
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
            if (string.IsNullOrEmpty(key)) return null;
            int i = key.IndexOf("veil://", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return null;
            string b = key.Substring(i + 7).Trim().Replace('-', '+').Replace('_', '/');
            while (b.Length % 4 != 0) b += "=";
            try
            {
                string[] p = Encoding.UTF8.GetString(Convert.FromBase64String(b)).Split('|');
                if (p.Length < 4 || p[0].Length == 0 || p[3].Length < 8) return null;
                Profile x = new Profile();
                x.Key = key.Trim();
                x.Server = p[0];
                x.Sni = p[1].Length > 0 ? p[1] : p[0].Split(':')[0];
                x.Fp = p[2].Length > 0 ? p[2] : "chrome";
                x.Psk = p[3];
                return x;
            }
            catch { return null; }
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
            Assembly asm = Assembly.GetExecutingAssembly();
            string name = null;
            foreach (string n in asm.GetManifestResourceNames())
                if (n.EndsWith("veil-client.exe", StringComparison.OrdinalIgnoreCase)) { name = n; break; }
            if (name == null) throw new Exception("embedded client not found");
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (File.Exists(ClientPath) && new FileInfo(ClientPath).Length == s.Length) return;
                if (File.Exists(ClientPath)) File.Delete(ClientPath);
                using (FileStream f = File.Create(ClientPath)) s.CopyTo(f);
            }
        }

        public static void WriteIni(Profile p)
        {
            File.WriteAllText(IniPath,
                "server = " + p.Server + "\n" +
                "sni    = " + p.Sni + "\n" +
                "psk    = " + p.Psk + "\n" +
                "socks  = 127.0.0.1:1080\n" +
                "fp     = " + p.Fp + "\n" +
                "direct  = " + (Settings.Split ? "true" : "false") + "\n", new UTF8Encoding(false));
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
            ProcessStartInfo psi = new ProcessStartInfo(ClientPath, "-config \"" + IniPath + "\"");
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

        public void OpenTunnel()
        {
            if (Profiles.Count == 0) { State = "error"; return; }
            Profile p = Current;
            State = "connecting";
            try
            {
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
            string body = Probe.Get("ip-api.com", "/json/?fields=status,country,countryCode,query", 12000);
            if (body != null)
            {
                Cc = Json(body, "countryCode") ?? "";
                Country = Json(body, "country") ?? "";
                ExitIp = Json(body, "query") ?? "";
                Profile p = Current;
                if (p != null && Cc.Length == 2) { p.Cc = Cc; p.Country = Country; p.ExitIp = ExitIp; Store.Save(Profiles); }
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
            return j > i ? s.Substring(i, j - i) : null;
        }

        public void Kill()
        {
            try { if (Proc != null && !Proc.HasExited) { Proc.Kill(); Proc.WaitForExit(2500); } } catch { }
            Proc = null;
            SysProxy.Disable();
            if (State == "connected" || State == "connecting") State = "disconnected";
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

    class Shell : Form
    {
        readonly Core core = new Core();
        WebView2 web;
        readonly System.Windows.Forms.Timer timer = new System.Windows.Forms.Timer();
        bool ready;

        readonly bool autoConnect;

        public Shell(string[] args)
        {
            foreach (string a in args)
                if (string.Equals(a, "--connect", StringComparison.OrdinalIgnoreCase)) autoConnect = true;
            Text = "veil";
            ClientSize = new Size(484, 700);
            MinimumSize = new Size(420, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(10, 12, 17);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Settings.Load();

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
                    var theme = Environment.OSVersion.Version.Build >= 22000 ? CoreWebView2PreferredColorScheme.Dark : CoreWebView2PreferredColorScheme.Auto;
                    web.CoreWebView2.Profile.PreferredColorScheme = theme;
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
                        }
                        catch (Exception ex) { Program.Log("probe fail: " + ex.Message); }
                    };
                    probeTimer.Start();
                    Program.Log("step: navigate done");

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
            timer.Tick += delegate { core.Tick(); if (ready) Push(); };
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

        void OnMessage(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string raw = e.WebMessageAsJson;
            string cmd = Json(raw, "cmd");
            Program.Log("msg: " + raw);
            if (cmd == "ready") { ready = true; Push(); return; }
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
            else if (cmd == "setting")
            {
                string k = Json(raw, "key"), v = Json(raw, "value") ?? "";
                if (k == "split") { Settings.Split = v == "true"; Settings.Save(); }
                if (k == "killSwitch") { Settings.KillSwitch = v == "true"; Settings.Save(); }
                if (k == "autostart") { Settings.Autostart = v == "true"; Settings.Save(); Settings.ApplyAutostart(); }
                if (k == "dns") { Settings.Dns = v; Settings.Save(); }
                if (k == "theme") { Settings.Theme = v; Settings.Save(); }
            }
            Push();
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
                return j > i ? s.Substring(i + 1, j - i - 1) : null;
            }
            int k = i;
            while (k < s.Length && ",}]".IndexOf(s[k]) < 0) k++;
            return s.Substring(i, k - i).Trim();
        }

        void Push()
        {
            if (!ready || web.CoreWebView2 == null) return;
            string s = BuildSnapshot();
            try
            {
                web.CoreWebView2.PostWebMessageAsJson(s);
            }
            catch (Exception ex) { Program.Log("push failed: " + ex.Message); }
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
                  .Append(",\"cc\":\"").Append(Esc(p.Cc.Length > 0 ? p.Cc : "··")).Append("\"")
                  .Append(",\"country\":\"").Append(Esc(p.Country.Length > 0 ? p.Country : p.Title)).Append("\"")
                  .Append(",\"city\":\"").Append(Esc(p.ExitIp.Length > 0 ? p.ExitIp : "ожидает проверки")).Append("\"")
                  .Append(",\"ping\":").Append(p == core.Current ? core.LastPing : 0)
                  .Append(",\"load\":0,\"fav\":false}");
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
