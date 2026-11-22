using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net.Sockets;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Veil
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "--selftest") { Environment.Exit(SelfTest.Run(args)); }
            if (args.Length > 1 && args[0] == "--makekey") { Console.WriteLine(KeyCodec.Make(args[1], args[2], args[3], args[4])); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(args));
        }
    }

    static class Theme
    {
        public static readonly Color Bg1 = Color.FromArgb(11, 14, 20);
        public static readonly Color Bg2 = Color.FromArgb(18, 23, 32);
        public static readonly Color Card = Color.FromArgb(21, 27, 37);
        public static readonly Color CardHi = Color.FromArgb(28, 36, 49);
        public static readonly Color Line = Color.FromArgb(38, 48, 63);
        public static readonly Color Text = Color.FromArgb(231, 238, 246);
        public static readonly Color Muted = Color.FromArgb(139, 152, 168);
        public static readonly Color Accent = Color.FromArgb(79, 140, 255);
        public static readonly Color Accent2 = Color.FromArgb(124, 96, 255);
        public static readonly Color Ok = Color.FromArgb(63, 185, 128);
        public static readonly Color Warn = Color.FromArgb(240, 178, 85);
        public static readonly Font UI = new Font("Segoe UI", 9.5f);
        public static readonly Font UIBold = new Font("Segoe UI Semibold", 10.5f);
        public static readonly Font Title = new Font("Segoe UI Semibold", 11f);
        public static readonly Font Big = new Font("Segoe UI Semibold", 13.5f);
        public static readonly Font Mono = new Font("Consolas", 8f);
        public static readonly Font Small = new Font("Segoe UI", 8.5f);
        // glass
        public static readonly Color Glass = Color.FromArgb(150, 32, 40, 54);
        public static readonly Color GlassHi = Color.FromArgb(180, 44, 54, 72);
        public static readonly Color GlassEdge = Color.FromArgb(46, 255, 255, 255);
        public static readonly Color GlassText = Color.FromArgb(238, 244, 252);
    }

    static class Draw
    {
        public static GraphicsPath Round(Rectangle r, int radius)
        {
            GraphicsPath p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        public static void Card(Graphics g, Rectangle r, int radius, Color fill, Color border)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath p = Round(r, radius))
            {
                Color top = Color.FromArgb(fill.A, Math.Min(255, fill.R + 14), Math.Min(255, fill.G + 16), Math.Min(255, fill.B + 20));
                using (LinearGradientBrush b = new LinearGradientBrush(
                    new Rectangle(r.X, r.Y, r.Width, Math.Max(2, r.Height)), top, fill, 90f))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, p);
            }
        }

        public static void Backdrop(Graphics g, int w, int h)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(new Rectangle(0, 0, w, h),
                Color.FromArgb(9, 12, 18), Color.FromArgb(19, 25, 36), 100f))
                g.FillRectangle(b, 0, 0, w, h);
            Radial(g, new Point(-40, -30), 300, Color.FromArgb(64, Theme.Accent));
            Radial(g, new Point(w + 30, h + 40), 340, Color.FromArgb(56, Theme.Accent2));
            Radial(g, new Point(w / 2, h / 2), 260, Color.FromArgb(22, 90, 160, 255));
        }

        static void Radial(Graphics g, Point c, int r, Color color)
        {
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(c.X - r, c.Y - r, r * 2, r * 2);
                using (PathGradientBrush pg = new PathGradientBrush(gp))
                {
                    pg.CenterColor = color;
                    pg.SurroundColors = new Color[] { Color.FromArgb(0, color) };
                    g.FillPath(pg, gp);
                }
            }
        }

        public static void Glow(Graphics g, Point c, int radius, Color color, int alpha)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            for (int i = 6; i >= 1; i--)
            {
                int rr = radius + i * 2;
                int a = Math.Max(0, alpha / (i * 2));
                using (SolidBrush b = new SolidBrush(Color.FromArgb(a, color)))
                    g.FillEllipse(b, c.X - rr, c.Y - rr, rr * 2, rr * 2);
            }
            using (SolidBrush b = new SolidBrush(color))
                g.FillEllipse(b, c.X - radius, c.Y - radius, radius * 2, radius * 2);
        }

        public static void Chip(Graphics g, Rectangle r, string code, Color accent)
        {
            using (GraphicsPath p = Round(r, 7))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(38, accent)))
                    g.FillPath(b, p);
                using (Pen pen = new Pen(Color.FromArgb(90, accent), 1f))
                    g.DrawPath(pen, p);
            }
            TextRenderer.DrawText(g, code, new Font("Segoe UI Semibold", 8.5f), r, accent,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }
    }

    class FlatButton : Control
    {
        public Color Accent = Theme.Accent;
        public Color Accent2 = Theme.Accent2;
        public bool Primary = true;
        bool hover, down;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Theme.Bg1;
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; down = false; Invalidate(); base.OnMouseLeave(e); }
        protected override void OnMouseDown(MouseEventArgs e) { down = true; Invalidate(); base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { down = false; Invalidate(); base.OnMouseUp(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Theme.Bg1);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Primary)
            {
                Color a = Accent, b = Accent2;
                if (!Enabled) { a = Color.FromArgb(58, 67, 82); b = Color.FromArgb(48, 56, 70); }
                else if (down) { a = ControlPaint.Dark(Accent, 0.15f); b = ControlPaint.Dark(Accent2, 0.15f); }
                using (GraphicsPath p = Draw.Round(r, 12))
                using (LinearGradientBrush br = new LinearGradientBrush(new Rectangle(0, 0, Width, Height), a, b, 0f))
                    g.FillPath(br, p);
                if (hover && Enabled)
                    using (GraphicsPath p = Draw.Round(r, 12))
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(20, 255, 255, 255)))
                        g.FillPath(br, p);
                TextRenderer.DrawText(g, Text, Theme.UIBold, r, Color.White,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            else
            {
                Color fill = hover ? Theme.CardHi : Theme.Card;
                Draw.Card(g, r, 10, fill, Theme.Line);
                TextRenderer.DrawText(g, Text, Theme.UI, r, Enabled ? Theme.Text : Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
        }
    }

    static class Countries
    {
        static readonly Dictionary<string, string> Ru = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            {"ES","Испания"},{"DE","Германия"},{"NL","Нидерланды"},{"FR","Франция"},{"US","США"},
            {"GB","Великобритания"},{"FI","Финляндия"},{"SE","Швеция"},{"PL","Польша"},{"TR","Турция"},
            {"RU","Россия"},{"KZ","Казахстан"},{"UA","Украина"},{"CH","Швейцария"},{"AT","Австрия"},
            {"CZ","Чехия"},{"RO","Румыния"},{"BG","Болгария"},{"LT","Литва"},{"LV","Латвия"},
            {"EE","Эстония"},{"MD","Молдова"},{"GE","Грузия"},{"AM","Армения"},{"AE","ОАЭ"},
            {"SG","Сингапур"},{"JP","Япония"},{"KR","Корея"},{"CN","Китай"},{"IN","Индия"},
            {"CA","Канада"},{"BR","Бразилия"},{"AU","Австралия"},{"IT","Италия"},{"PT","Португалия"},
            {"NO","Норвегия"},{"DK","Дания"},{"IE","Ирландия"},{"BE","Бельгия"},{"HU","Венгрия"},
            {"RS","Сербия"},{"HR","Хорватия"},{"SK","Словакия"},{"SI","Словения"},{"GR","Греция"},
            {"IL","Израиль"},{"HK","Гонконг"},{"TW","Тайвань"},{"TH","Таиланд"},{"VN","Вьетнам"}
        };

        public static string Name(string cc, string fallback)
        {
            if (string.IsNullOrEmpty(cc)) return fallback;
            string n;
            return Ru.TryGetValue(cc, out n) ? n : (string.IsNullOrEmpty(fallback) ? cc : fallback);
        }
    }

    class Profile
    {
        public string Key = "";          // veil://...
        public string Server = "";       // host:port
        public string Sni = "";
        public string Fp = "chrome";
        public string Psk = "";
        public string Cc = "";           // country code, e.g. ES
        public string Country = "";      // country name
        public string ExitIp = "";

        public string Title
        {
            get
            {
                int i = Server.IndexOf(':');
                return i > 0 ? Server.Substring(0, i) : Server;
            }
        }
    }

    static class KeyCodec
    {
        public static string Make(string server, string sni, string fp, string psk)
        {
            byte[] raw = Encoding.UTF8.GetBytes(server + "|" + sni + "|" + fp + "|" + psk);
            return "veil://" + Convert.ToBase64String(raw).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        }

        public static bool TryParse(string text, out string server, out string sni, out string fp, out string psk)
        {
            server = sni = fp = psk = null;
            if (text == null) return false;
            text = text.Trim();
            int i = text.IndexOf("veil://", StringComparison.OrdinalIgnoreCase);
            if (i < 0) return false;
            string b = text.Substring(i + 7).Trim().Replace('-', '+').Replace('_', '/');
            while (b.Length % 4 != 0) b += "=";
            try
            {
                string s = Encoding.UTF8.GetString(Convert.FromBase64String(b));
                string[] p = s.Split('|');
                if (p.Length < 4) return false;
                server = p[0]; sni = p[1]; fp = string.IsNullOrEmpty(p[2]) ? "chrome" : p[2]; psk = p[3];
                if (server.Length == 0 || psk.Length < 8) return false;
                if (sni.Length == 0) sni = server.Split(':')[0];
                return true;
            }
            catch { return false; }
        }

        public static Profile ToProfile(string key)
        {
            string server, sni, fp, psk;
            if (!TryParse(key, out server, out sni, out fp, out psk)) return null;
            Profile p = new Profile();
            p.Key = key.Trim(); p.Server = server; p.Sni = sni; p.Fp = fp; p.Psk = psk;
            return p;
        }
    }

    static class Store
    {
        // one profile per line: veil://...\tCC\tCountry\tIP
        static string PathFile { get { return System.IO.Path.Combine(Engine.Dir, "profiles.txt"); } }

        public static List<Profile> Load()
        {
            List<Profile> list = new List<Profile>();
            if (!File.Exists(PathFile)) return list;
            foreach (string line in File.ReadAllLines(PathFile))
            {
                if (line.Trim().Length == 0) continue;
                string[] f = line.Split('\t');
                Profile p = KeyCodec.ToProfile(f[0]);
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
            foreach (Profile p in list)
                sb.AppendLine(p.Key + "\t" + p.Cc + "\t" + p.Country + "\t" + p.ExitIp);
            File.WriteAllText(PathFile, sb.ToString(), new UTF8Encoding(false));
        }
    }

    static class SysProxy
    {
        const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        const int INTERNET_OPTION_REFRESH = 37;

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

        static string BackupPath { get { return System.IO.Path.Combine(Engine.Dir, "proxy.bak"); } }

        public static void Enable(string socks)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RegPath, true))
                {
                    if (k == null) return;
                    if (!File.Exists(BackupPath))
                    {
                        object en = k.GetValue("ProxyEnable", 0);
                        object srv = k.GetValue("ProxyServer", "");
                        File.WriteAllText(BackupPath, en + "\n" + srv);
                    }
                    k.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                    k.SetValue("ProxyServer", "socks=" + socks, RegistryValueKind.String);
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
                    int en = 0;
                    string srv = "";
                    if (File.Exists(BackupPath))
                    {
                        string[] ln = File.ReadAllLines(BackupPath);
                        if (ln.Length > 0) int.TryParse(ln[0], out en);
                        if (ln.Length > 1) srv = ln[1];
                        File.Delete(BackupPath);
                    }
                    k.SetValue("ProxyEnable", en, RegistryValueKind.DWord);
                    if (!string.IsNullOrEmpty(srv)) k.SetValue("ProxyServer", srv, RegistryValueKind.String);
                }
            }
            catch { }
            Refresh();
        }

        static void Refresh()
        {
            try
            {
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
                InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
            }
            catch { }
        }
    }

    static class Engine
    {
        public static readonly List<string> Log = new List<string>();

        public static string Dir
        {
            get
            {
                string d = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string ClientPath { get { return System.IO.Path.Combine(Dir, "veil-client.exe"); } }
        public static string IniPath { get { return System.IO.Path.Combine(Dir, "veil.ini"); } }

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
                using (FileStream f = File.Create(ClientPath)) s.CopyTo(f);
            }
        }

        public static void WriteIni(Profile p)
        {
            string ini =
                "# generated by veil client\n" +
                "server = " + p.Server + "\n" +
                "sni    = " + p.Sni + "\n" +
                "psk    = " + p.Psk + "\n" +
                "socks  = 127.0.0.1:1080\n" +
                "fp     = " + p.Fp + "\n" +
                "direct  = " + (Settings.DirectRu ? "true" : "false") + "\n";
            File.WriteAllText(IniPath, ini, new UTF8Encoding(false));
        }

        public static Process StartClient()
        {
            ExtractClient();
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
            if (line.Length > 20 && line[4] == '/' && line[7] == '/' && line[13] == ':')
                line = line.Substring(11);
            lock (Log)
            {
                Log.Add(line);
                if (Log.Count > 500) Log.RemoveAt(0);
            }
        }

        public static bool WaitListen(int timeoutMs)
        {
            DateTime end = DateTime.UtcNow.AddMilliseconds(timeoutMs);
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
                Thread.Sleep(150);
            }
            return false;
        }

        public static string Tail(int lines)
        {
            lock (Log)
            {
                int from = Math.Max(0, Log.Count - lines);
                StringBuilder sb = new StringBuilder();
                for (int i = from; i < Log.Count; i++) sb.AppendLine(Log[i]);
                return sb.ToString();
            }
        }
    }

    static class Socks
    {
        // Minimal SOCKS5 HTTP GET through the local tunnel.
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
                    if (rep[3] == 1) { byte[] sk = new byte[6]; s.Read(sk, 0, 6); }
                    else if (rep[3] == 4) { byte[] sk = new byte[18]; s.Read(sk, 0, 18); }
                    else { int l = s.ReadByte(); byte[] sk = new byte[l + 2]; s.Read(sk, 0, sk.Length); }

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

        public static void LookupCountry(Profile p, Action<Profile> done)
        {
            ThreadPool.QueueUserWorkItem(delegate
            {
                string body = Get("ip-api.com", "/json/?fields=status,country,countryCode,query", 15000);
                if (string.IsNullOrEmpty(body)) { done(p); return; }
                string cc = Json(body, "countryCode");
                string country = Json(body, "country");
                string ip = Json(body, "query");
                if (cc != null && cc.Length == 2)
                {
                    p.Cc = cc.ToUpperInvariant();
                    p.Country = country ?? "";
                    p.ExitIp = ip ?? "";
                }
                done(p);
            });
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
    }

    class StatusCard : Panel
    {
        public bool Connected, Connecting;
        public string Server = "не подключено";
        public string Cc = "", Country = "", ExitIp = "";
        public double Pulse;
        public DateTime Start = DateTime.Now;

        public StatusCard()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            Draw.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 20, Theme.Glass, Theme.GlassEdge);

            Color c = Connected ? Theme.Ok : (Connecting ? Theme.Warn : Color.FromArgb(110, 122, 138));
            int alpha = (Connected || Connecting) ? (int)(90 + 90 * Math.Abs(Math.Sin(Pulse))) : 40;
            Draw.Glow(g, new Point(34, 40), 6, c, alpha);

            TextRenderer.DrawText(g, Connected ? "Подключено" : (Connecting ? "Подключение…" : "Отключено"),
                Theme.Big, new Rectangle(56, 20, 240, 26), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            string sub = Connected
                ? Server + (Country.Length > 0 ? "  ·  " + Countries.Name(Cc, Country) : "")
                : "выбери сервер и нажми кнопку";
            TextRenderer.DrawText(g, sub, Theme.UI, new Rectangle(56, 48, 300, 20), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Cc.Length > 0)
            {
                Rectangle chip = new Rectangle(Width - 108, 22, 38, 20);
                Draw.Chip(g, chip, Cc, Connected ? Theme.Ok : Theme.Muted);
                if (ExitIp.Length > 0)
                    TextRenderer.DrawText(g, ExitIp, Theme.Mono, new Rectangle(Width - 190, 48, 120, 18),
                        Color.FromArgb(110, 122, 138), TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
            if (Connected)
            {
                string up = (DateTime.Now - Start).ToString(@"hh\:mm\:ss");
                TextRenderer.DrawText(g, up, Theme.Mono, new Rectangle(Width - 190, 24, 74, 18), Theme.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }
    }

    class ProfileList : Panel
    {
        public readonly List<Profile> Items = new List<Profile>();
        public int Selected = -1;
        public event EventHandler Activate;
        int hover = -1, scroll = 0;
        const int RowH = 48, Pad = 8;

        public ProfileList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg1;
        }

        int VisibleRows { get { return Math.Max(1, (Height - Pad * 2) / RowH); } }

        public void SetItems(List<Profile> items, int sel)
        {
            Items.Clear();
            Items.AddRange(items);
            Selected = sel;
            scroll = 0;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            int idx = IndexAt(e.Y);
            if (idx != hover) { hover = idx; Invalidate(); }
            Cursor = idx >= 0 ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseLeave(EventArgs e) { hover = -1; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            int idx = IndexAt(e.Y);
            if (idx >= 0)
            {
                if (e.X > Width - 40 && Items.Count > 1) { Items.RemoveAt(idx); Selected = Math.Min(Selected, Items.Count - 1); scroll = 0; Invalidate(); return; }
                Selected = idx;
                Invalidate();
                if (e.Clicks > 1 && Activate != null) Activate(this, EventArgs.Empty);
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            int max = Math.Max(0, Items.Count - VisibleRows);
            scroll = Math.Max(0, Math.Min(max, scroll - Math.Sign(e.Delta)));
            Invalidate();
            base.OnMouseWheel(e);
        }

        int IndexAt(int y)
        {
            int i = (y - Pad) / RowH;
            if (i < 0 || i >= VisibleRows) return -1;
            int real = scroll + i;
            return real < Items.Count ? real : -1;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            Draw.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 20, Theme.Glass, Theme.GlassEdge);

            if (Items.Count == 0)
            {
                TextRenderer.DrawText(g, "нет серверов — добавь ключ", Theme.UI,
                    new Rectangle(0, 0, Width, Height), Theme.Muted,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }

            for (int i = 0; i < VisibleRows; i++)
            {
                int real = scroll + i;
                if (real >= Items.Count) break;
                Profile p = Items[real];
                Rectangle row = new Rectangle(Pad, Pad + i * RowH, Width - Pad * 2, RowH - 4);
                bool sel = real == Selected, hov = real == hover;

                if (sel)
                    Draw.Card(g, row, 12, Color.FromArgb(170, 52, 74, 120), Color.FromArgb(110, 130, 190, 255));
                else if (hov)
                    Draw.Card(g, row, 12, Theme.GlassHi, Theme.GlassEdge);

                if (sel)
                {
                    using (GraphicsPath pth = Draw.Round(new Rectangle(row.X + 6, row.Y + 12, 3, row.Height - 24), 2))
                    using (SolidBrush b = new SolidBrush(Theme.Accent))
                        g.FillPath(b, pth);
                }

                TextRenderer.DrawText(g, p.Title, Theme.UIBold, new Rectangle(row.X + 18, row.Y + 7, row.Width - 90, 20),
                    Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
                string sub;
                if (p.Cc.Length == 0) sub = p.Server + "  ·  страна не проверена";
                else sub = Countries.Name(p.Cc, p.Country) + "  ·  " + p.ExitIp;
                TextRenderer.DrawText(g, sub, Theme.Small, new Rectangle(row.X + 18, row.Y + 25, row.Width - 90, 16),
                    Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

                if (p.Cc.Length > 0)
                    Draw.Chip(g, new Rectangle(row.Right - 82, row.Y + 13, 36, 20), p.Cc, Theme.Ok);
                else
                    Draw.Chip(g, new Rectangle(row.Right - 84, row.Y + 13, 38, 20), "?", Color.FromArgb(120, 132, 148));

                TextRenderer.DrawText(g, "×", Theme.UI, new Rectangle(row.Right - 30, row.Y + 12, 22, 22),
                    Color.FromArgb(110, 120, 136), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }

            if (Items.Count > VisibleRows)
            {
                int h = Height - Pad * 2;
                int th = Math.Max(18, h * VisibleRows / Items.Count);
                int ty = Pad + (h - th) * scroll / Math.Max(1, Items.Count - VisibleRows);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(60, 72, 88)))
                    g.FillRectangle(b, Width - 6, ty, 3, th);
            }
        }
    }

    class MainForm : Form
    {
        const int W = 480, H = 640;
        readonly ProfileList list = new ProfileList();
        readonly FlatButton btnAdd = new FlatButton();
        readonly FlatButton btnGo = new FlatButton();
        readonly FlatButton btnMin = new FlatButton();
        readonly FlatButton btnSettings = new FlatButton();
        readonly SettingsPanel setPanel = new SettingsPanel();
        readonly FlatButton btnClose = new FlatButton();
        readonly StatusCard card = new StatusCard();
        readonly Panel logWrap = new Panel();
        readonly TextBox txtLog = new TextBox();
        readonly Label logHint = new Label();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer ui = new System.Windows.Forms.Timer();
        readonly List<Profile> profiles = new List<Profile>();
        Process proc;
        bool busy, autoConnect, openSettings;
        bool dragging;
        Point dragFrom;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        public MainForm(string[] args)
        {
            autoConnect = Array.IndexOf(args, "--connect") >= 0;
            openSettings = Array.IndexOf(args, "--settings") >= 0;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(W, H);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg1;
            DoubleBuffered = true;
            Font = Theme.UI;
            Text = "veil";

            btnSettings.Text = "⚙"; btnSettings.Primary = false; btnSettings.SetBounds(W - 136, 10, 34, 28);
            btnSettings.Click += delegate
            {
                setPanel.Visible = !setPanel.Visible;
                setPanel.BringToFront();
                if (setPanel.Visible) setPanel.Refresh();
            };
            Controls.Add(btnSettings);

            btnMin.Text = "—"; btnMin.Primary = false; btnMin.SetBounds(W - 96, 10, 34, 28);
            btnClose.Text = "✕"; btnClose.Primary = false; btnClose.SetBounds(W - 56, 10, 34, 28);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnMin);
            Controls.Add(btnClose);

            card.SetBounds(20, 56, W - 40, 92);
            Controls.Add(card);

            Label lSrv = new Label();
            lSrv.Text = "Серверы";
            lSrv.ForeColor = Theme.Muted;
            lSrv.BackColor = Color.Transparent;
            lSrv.Font = Theme.UI;
            lSrv.SetBounds(24, 162, 200, 18);
            Controls.Add(lSrv);

            list.SetBounds(20, 184, W - 40, 152);
            list.Activate += delegate { if (!busy) Toggle(); };
            Controls.Add(list);

            btnAdd.Text = "＋  Добавить ключ из буфера";
            btnAdd.Primary = false;
            btnAdd.SetBounds(20, 344, W - 40, 38);
            btnAdd.Click += delegate { AddFromClipboard(true); };
            Controls.Add(btnAdd);

            btnGo.Text = "Подключиться";
            btnGo.SetBounds(20, 390, W - 40, 46);
            btnGo.Click += delegate { Toggle(); };
            Controls.Add(btnGo);

            Label lLog = new Label();
            lLog.Text = "Журнал";
            lLog.ForeColor = Theme.Muted;
            lLog.BackColor = Color.Transparent;
            lLog.Font = Theme.UI;
            lLog.SetBounds(24, 452, 200, 18);
            Controls.Add(lLog);

            logWrap.SetBounds(20, 474, W - 40, 122);
            logWrap.BackColor = Theme.Bg1;
            logWrap.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Bg1);
                Draw.Card(e.Graphics, new Rectangle(0, 0, logWrap.Width - 1, logWrap.Height - 1), 18,
                    Color.FromArgb(150, 14, 18, 26), Theme.GlassEdge);
            };
            Controls.Add(logWrap);

            txtLog.BorderStyle = BorderStyle.None;
            txtLog.BackColor = Color.FromArgb(15, 20, 28);
            txtLog.ForeColor = Color.FromArgb(150, 164, 182);
            txtLog.Font = Theme.Mono;
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.None;
            txtLog.WordWrap = true;
            txtLog.SetBounds(14, 10, logWrap.Width - 28, logWrap.Height - 20);
            logWrap.Controls.Add(txtLog);

            logHint.Text = "соединений пока нет";
            logHint.ForeColor = Color.FromArgb(92, 104, 120);
            logHint.BackColor = Color.FromArgb(15, 20, 28);
            logHint.Font = Theme.UI;
            logHint.TextAlign = ContentAlignment.MiddleCenter;
            logHint.SetBounds(14, logWrap.Height / 2 - 12, logWrap.Width - 28, 24);
            logWrap.Controls.Add(logHint);
            logHint.BringToFront();
            txtLog.TextChanged += delegate { logHint.Visible = txtLog.Text.Length == 0; };

            setPanel.SetBounds(20, 158, W - 40, 360);
            setPanel.Visible = false;
            setPanel.Closed += delegate
            {
                setPanel.Visible = false;
                if (proc != null && !proc.HasExited) ApplyRouting();
            };
            Controls.Add(setPanel);

            tray.Text = "veil";
            tray.Icon = SystemIcons.Shield;
            tray.Visible = true;
            tray.DoubleClick += delegate { Restore(); };
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Показать", null, delegate { Restore(); });
            cm.Items.Add("Подключиться / Отключиться", null, delegate { Toggle(); });
            cm.Items.Add("Выход", null, delegate { Close(); });
            tray.ContextMenuStrip = cm;

            Settings.Load();
            setPanel.SwRoute.On = Settings.DirectRu;
            profiles.AddRange(Store.Load());
            list.SetItems(profiles, profiles.Count > 0 ? 0 : -1);
            ShowCurrent();

            ui.Interval = 40;
            ui.Tick += delegate { Tick(); };
            ui.Start();

            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate { Stop(false); tray.Visible = false; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try { int pref = 2; DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int)); }
            catch { }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (autoConnect && profiles.Count > 0) BeginInvoke(new MethodInvoker(delegate { Toggle(); }));
            if (openSettings) BeginInvoke(new MethodInvoker(delegate
            {
                setPanel.Visible = true;
                setPanel.BringToFront();
            }));
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x20000;
                return cp;
            }
        }

        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        // Routing switch changed in settings: reconnect so the new rules apply.
        public void ApplyRouting()
        {
            if (proc != null && !proc.HasExited)
            {
                Stop(true);
                Toggle();
            }
        }

        Profile Current
        {
            get
            {
                if (list.Selected < 0 || list.Selected >= profiles.Count) return null;
                return profiles[list.Selected];
            }
        }

        void ShowCurrent()
        {
            Profile p = Current;
            btnGo.Text = (proc != null && !proc.HasExited) ? "Отключиться" : "Подключиться";
            if (p == null) { card.Server = "не подключено"; card.Cc = ""; card.Country = ""; card.ExitIp = ""; }
            else if (!card.Connected) { card.Server = p.Server; card.Cc = p.Cc; card.Country = p.Country; card.ExitIp = p.ExitIp; }
            card.Invalidate();
        }

        void AddFromClipboard(bool report)
        {
            string text = "";
            try { text = Clipboard.GetText(); } catch { }
            int added = 0;
            if (!string.IsNullOrEmpty(text))
            {
                string[] lines = text.Replace("\r", "\n").Split('\n');
                List<string> keys = new List<string>();
                foreach (string l in lines)
                {
                    int at = l.IndexOf("veil://", StringComparison.OrdinalIgnoreCase);
                    if (at >= 0) keys.Add(l.Substring(at).Trim());
                }
                foreach (string k in keys)
                {
                    Profile p = KeyCodec.ToProfile(k);
                    if (p == null) continue;
                    bool dup = false;
                    foreach (Profile e in profiles) if (e.Server == p.Server && e.Psk == p.Psk) dup = true;
                    if (dup) continue;
                    profiles.Add(p);
                    added++;
                }
            }
            if (added > 0)
            {
                Store.Save(profiles);
                list.SetItems(profiles, profiles.Count - 1);
                ShowCurrent();
            }
            else if (report)
            {
                MessageBox.Show("В буфере нет ключа veil:// (или он уже добавлен).", "veil");
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Draw.Backdrop(e.Graphics, W, H);

            Rectangle logo = new Rectangle(20, 12, 26, 26);
            using (GraphicsPath lp = Draw.Round(logo, 8))
            using (LinearGradientBrush lb = new LinearGradientBrush(logo, Theme.Accent, Theme.Accent2, 45f))
                e.Graphics.FillPath(lb, lp);
            TextRenderer.DrawText(e.Graphics, "v", new Font("Segoe UI Semibold", 11f), logo, Color.White,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(e.Graphics, "veil", Theme.Title, new Rectangle(54, 14, 60, 22),
                Theme.Text, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, "tls tunnel", Theme.UI, new Rectangle(94, 15, 160, 20),
                Color.FromArgb(100, 112, 130), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

            TextRenderer.DrawText(e.Graphics, "socks5  127.0.0.1:1080", Theme.Mono,
                new Rectangle(24, H - 32, 240, 18), Color.FromArgb(92, 104, 120),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(e.Graphics, profiles.Count + " сервер(ов)", Theme.UI,
                new Rectangle(W - 200, H - 32, 176, 18), Color.FromArgb(92, 104, 120),
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Y < 52 && e.Button == MouseButtons.Left) { dragging = true; dragFrom = e.Location; }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (dragging) Location = new Point(Location.X + e.X - dragFrom.X, Location.Y + e.Y - dragFrom.Y);
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; base.OnMouseUp(e); }

        void Toggle()
        {
            if (busy) return;
            if (proc != null && !proc.HasExited) { Stop(true); return; }

            Profile p = Current;
            if (p == null)
            {
                if (!string.IsNullOrEmpty(Clipboard.GetText())) { AddFromClipboard(true); p = Current; }
                if (p == null) { MessageBox.Show("Добавь ключ: «＋ Добавить ключ из буфера».", "veil"); return; }
            }

            busy = true;
            btnGo.Enabled = false;
            card.Connecting = true;
            card.Server = p.Server;
            card.Invalidate();
            Application.DoEvents();

            try
            {
                Engine.WriteIni(p);
                proc = Engine.StartClient();
                if (!Engine.WaitListen(6000))
                {
                    string tail = Engine.Tail(6);
                    Stop(false);
                    MessageBox.Show("Клиент не поднялся.\n\n" + tail, "veil");
                    return;
                }
                SysProxy.Enable("127.0.0.1:1080");
                card.Connecting = false;
                card.Connected = true;
                card.Start = DateTime.Now;
                btnGo.Text = "Отключиться";
                tray.Text = "veil: подключено";
                tray.ShowBalloonTip(1500, "veil", "Туннель поднят, прокси включён", ToolTipIcon.Info);
                card.Invalidate();

                Socks.LookupCountry(p, delegate(Profile done)
                {
                    BeginInvoke(new MethodInvoker(delegate
                    {
                        Store.Save(profiles);
                        list.Invalidate();
                        card.Cc = done.Cc;
                        card.Country = done.Country;
                        card.ExitIp = done.ExitIp;
                        card.Invalidate();
                    }));
                });
            }
            catch (Exception ex)
            {
                Stop(false);
                MessageBox.Show(ex.Message, "veil");
            }
            finally
            {
                busy = false;
                btnGo.Enabled = true;
                btnGo.Invalidate();
                card.Invalidate();
            }
        }

        void Stop(bool user)
        {
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(3000); } } catch { }
            proc = null;
            SysProxy.Disable();
            btnGo.Text = "Подключиться";
            card.Connected = false;
            card.Connecting = false;
            card.Invalidate();
            btnGo.Invalidate();
            tray.Text = "veil: отключено";
            Invalidate();
        }

        void Tick()
        {
            card.Pulse += 0.09;
            if (card.Connected || card.Connecting) card.Invalidate();

            string tail = Engine.Tail(200);
            if (txtLog.Text != tail)
            {
                txtLog.Text = tail;
                txtLog.SelectionStart = txtLog.TextLength;
                txtLog.ScrollToCaret();
            }
            if (proc != null && proc.HasExited)
            {
                Stop(false);
                card.Server = "клиент завершился";
                card.Invalidate();
            }
        }
    }


    static class Settings
    {
        static string File2 { get { return System.IO.Path.Combine(Engine.Dir, "settings.ini"); } }

        public static bool DirectRu = true;
        public static bool Tun = false;

        public static void Load()
        {
            try
            {
                if (!File.Exists(File2)) return;
                foreach (string l in File.ReadAllLines(File2))
                {
                    string[] kv = l.Split('=');
                    if (kv.Length != 2) continue;
                    string k = kv[0].Trim(), v = kv[1].Trim();
                    if (k == "direct") DirectRu = v != "0";
                    if (k == "tun") Tun = v != "0";
                }
            }
            catch { }
        }

        public static void Save()
        {
            try { File.WriteAllText(File2, "direct=" + (DirectRu ? "1" : "0") + "\ntun=" + (Tun ? "1" : "0") + "\n"); }
            catch { }
        }
    }

    class GlassSwitch : Control
    {
        public bool On;
        public bool Disabled;
        public event EventHandler Toggled;

        public GlassSwitch()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg1;
            Size = new Size(46, 26);
            Cursor = Cursors.Hand;
        }

        protected override void OnClick(EventArgs e)
        {
            if (Disabled) return;
            On = !On;
            Invalidate();
            if (Toggled != null) Toggled(this, EventArgs.Empty);
            base.OnClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);
            Rectangle track = new Rectangle(0, 2, Width - 1, Height - 5);
            Color on = Disabled ? Color.FromArgb(52, 62, 78) : Theme.Ok;
            Color off = Color.FromArgb(58, 68, 84);
            using (GraphicsPath p = Draw.Round(track, track.Height / 2))
            using (LinearGradientBrush b = new LinearGradientBrush(track,
                On ? on : off, On ? ControlPaint.Dark(on, 0.25f) : off, 90f))
                g.FillPath(b, p);
            int knob = track.Height - 6;
            int kx = On ? track.Right - knob - 3 : track.X + 3;
            using (SolidBrush b = new SolidBrush(Disabled ? Color.FromArgb(150, 230, 236, 245) : Color.White))
                g.FillEllipse(b, kx, track.Y + 3, knob, knob);
        }
    }

    class SettingsPanel : Panel
    {
        public readonly GlassSwitch SwRoute = new GlassSwitch();
        public readonly GlassSwitch SwTun = new GlassSwitch();
        public event EventHandler Closed;
        Rectangle back = new Rectangle(0, 0, 10, 10);

        public SettingsPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Theme.Bg1;
            SwRoute.On = Settings.DirectRu;
            SwTun.On = Settings.Tun;
            SwTun.Disabled = true;
            SwRoute.Toggled += delegate
            {
                Settings.DirectRu = SwRoute.On;
                Settings.Save();
                if (Closed != null) Closed(this, EventArgs.Empty);
            };
            Controls.Add(SwRoute);
            Controls.Add(SwTun);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (back.Contains(e.Location) && Closed != null) Closed(this, EventArgs.Empty);
            base.OnMouseDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(Theme.Bg1);
            Draw.Card(g, new Rectangle(0, 0, Width - 1, Height - 1), 20, Color.FromArgb(246, 18, 23, 33), Theme.GlassEdge);

            TextRenderer.DrawText(g, "Настройки", Theme.Title, new Rectangle(22, 16, 200, 24), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            back = new Rectangle(Width - 96, 16, 76, 24);
            TextRenderer.DrawText(g, "назад  ›", Theme.UI, back, Theme.Accent,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter);

            int y = 56;
            Draw.Card(g, new Rectangle(16, y, Width - 32, 72), 16, Theme.Glass, Theme.GlassEdge);
            TextRenderer.DrawText(g, "Российские сайты напрямую", Theme.UIBold, new Rectangle(32, y + 12, Width - 130, 20),
                Theme.GlassText, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "яндекс, госуслуги, банки, .ru — без туннеля", Theme.Small,
                new Rectangle(32, y + 34, Width - 120, 18), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            SwRoute.SetBounds(Width - 74, y + 22, 46, 26);

            y = 140;
            Draw.Card(g, new Rectangle(16, y, Width - 32, 96), 16, Color.FromArgb(110, 34, 42, 58), Theme.GlassEdge);
            TextRenderer.DrawText(g, "TUN-режим", Theme.UIBold, new Rectangle(32, y + 12, Width - 130, 20),
                Color.FromArgb(150, 165, 182), TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "весь трафик ОС, игры, UDP", Theme.Small, new Rectangle(32, y + 34, Width - 120, 18),
                Theme.Muted, TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, "нужен wintun + права администратора", Theme.Small,
                new Rectangle(32, y + 54, Width - 60, 18), Color.FromArgb(206, 168, 96),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            SwTun.SetBounds(Width - 74, y + 22, 46, 26);

            TextRenderer.DrawText(g, "маршрутизация применится при следующем подключении", Theme.Small,
                new Rectangle(22, Height - 34, Width - 44, 18), Color.FromArgb(104, 116, 134),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
        }
    }

    static class SelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("usage: veil.exe --selftest <key>"); return 2; }
            Profile p = KeyCodec.ToProfile(args[1]);
            if (p == null) { Console.WriteLine("key: FAIL"); return 3; }
            Console.WriteLine("key: ok server=" + p.Server + " sni=" + p.Sni + " fp=" + p.Fp);
            Engine.WriteIni(p);
            Process cl = Engine.StartClient();
            bool up = Engine.WaitListen(8000);
            Console.WriteLine("socks: " + (up ? "listening" : "FAIL"));
            string body = up ? Socks.Get("ip-api.com", "/json/?fields=status,country,countryCode,query", 15000) : null;
            bool ok = up && body != null && body.IndexOf("\"success\"", StringComparison.Ordinal) >= 0;
            Console.WriteLine("exit: " + (body == null ? "FAIL" : body.Trim()));
            try { cl.Kill(); } catch { }
            return (up && ok) ? 0 : 1;
        }
    }
}
