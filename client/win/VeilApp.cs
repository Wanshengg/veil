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
                SysProxy.RestoreIfStale();   // убрать висящий прокси прошлого запуска
                ExtractAll();      // разложить DLL до первого обращения к WebView2
                InitLoader();      // теперь тип CoreWebView2Environment доступен
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
        public static void ForceLightTitleBar(IntPtr hwnd, int mode)
        {
            try
            {
                int dark = mode;                       // 20 — dark mode, 0 = светлая
                DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
                int caption = 0x00F4F6F6;              // BGR под --bg (для сборок без поддержки 20)
                DwmSetWindowAttribute(hwnd, 35, ref caption, sizeof(int));
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
                list.Add(p);
            }
            return list;
        }

        public static void Save(List<Profile> list)
        {
            StringBuilder sb = new StringBuilder();
            foreach (Profile p in list) sb.AppendLine(p.Key + "\t" + p.Cc + "\t" + p.Country + "\t" + p.ExitIp + "\t" + p.Pin);
            File.WriteAllText(File2, sb.ToString(), new UTF8Encoding(false));
        }
    }

    static class Settings
    {
        public static bool Split = true, KillSwitch = true, Autostart = false, Tun = false, Onboarded = false;
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
                if (p.Length > 4) x.Pin = p[4].Trim();
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

        public static void WriteIni(Profile p)
        {
            File.WriteAllText(IniPath,
                "server = " + p.Server + "\n" +
                "sni    = " + p.Sni + "\n" +
                "psk    = " + p.Psk + "\n" +
                "socks  = 127.0.0.1:1080\n" +
                "fp     = " + p.Fp + "\n" +
                (p.Pin.Length > 0 ? "pin    = " + p.Pin + "\n" : "") +
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
            Program.ForceLightTitleBar(Handle, 0);
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
                            if (openTab.Length > 0)
                                await web.CoreWebView2.ExecuteScriptAsync(
                                    "document.querySelector('.tab[data-tab=" + openTab + "]').click()");
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
                srvTarget = new SshTarget {
                    Host = Json(raw, "host").Trim(), Port = IntOr(Json(raw, "port"), 22),
                    User = string.IsNullOrEmpty(Json(raw, "user")) ? "root" : Json(raw, "user").Trim(),
                    Password = Json(raw, "pass"), KeyPath = Json(raw, "keyfile")
                };
                string domain = Json(raw, "domain").Trim();
                srvHost = srvTarget.Host; srvUser = srvTarget.User;
                srvBusy = true; srvStatus = "Устанавливаю…"; srvKeys.Clear(); PushSafe();
                ThreadPool.QueueUserWorkItem(delegate { DoInstall(domain); });
            }
            else if (cmd == "srv-connect")
            {
                srvTarget = new SshTarget {
                    Host = Json(raw, "host").Trim(), Port = IntOr(Json(raw, "port"), 22),
                    User = string.IsNullOrEmpty(Json(raw, "user")) ? "root" : Json(raw, "user").Trim(),
                    Password = Json(raw, "pass"), KeyPath = Json(raw, "keyfile")
                };
                srvHost = srvTarget.Host; srvUser = srvTarget.User;
                srvBusy = true; srvStatus = "Подключаюсь по SSH…"; PushSafe();
                ThreadPool.QueueUserWorkItem(delegate {
                    try
                    {
                        using (SshClient sc = ServerSetup.Connect(srvTarget))
                        {
                            srvKeys = ServerSetup.Keys(sc);
                            srvPin = ServerSetup.Pin(sc);
                            srvStatus = "Подключено · ключей: " + srvKeys.Count;
                        }
                    }
                    catch (Exception ex) { srvStatus = SshError(ex); }
                    srvBusy = false; PushSafe();
                });
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
                if (k == "theme") { Settings.Theme = v; Settings.Save(); }
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
                return j > i ? s.Substring(i + 1, j - i - 1) : null;
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
            catch (Exception ex) { Program.Log("push failed: " + ex.Message); }
        }

        string keyMsg = "";

        // ключи только с доменом: адрес по IP больше не принимаем
        void AddKeyFromText(string text)
        {
            if (string.IsNullOrWhiteSpace(text) || text.IndexOf("veil://", StringComparison.OrdinalIgnoreCase) < 0)
            { keyMsg = "В буфере нет ключа veil://"; PushSafe(); return; }

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

        SshTarget srvTarget = null;
        string srvHost = "", srvUser = "", srvPin = "", srvStatus = "";
        List<string> srvKeys = new List<string>();
        bool srvBusy = false;

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
                  .Append(",\"city\":\"").Append(Esc(p.ExitIp.Length > 0 ? p.ExitIp : "по IP-адресу")).Append("\"")
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
            sb.Append(",\"srvHost\":\"").Append(Esc(srvHost)).Append("\",\"srvUser\":\"").Append(Esc(srvUser)).Append("\"");
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
