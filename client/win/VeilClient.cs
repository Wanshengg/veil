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
                using (SolidBrush b = new SolidBrush(fill)) g.FillPath(b, p);
                using (Pen pen = new Pen(border, 1f)) g.DrawPath(pen, p);
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
    }

    class CardPanel : Panel
    {
        public int Radius = 14;
        public Color Fill = Theme.Card;
        public Color Border = Theme.Line;

        public CardPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint, true);
            BackColor = Color.Transparent;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.Bg1);
            Draw.Card(e.Graphics, new Rectangle(0, 0, Width - 1, Height - 1), Radius, Fill, Border);
            OnPainted(e.Graphics);
        }

        protected virtual void OnPainted(Graphics g) { }
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
            g.Clear(Parent != null ? Parent.BackColor : Theme.Bg1);
            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);

            if (Primary)
            {
                Color a = Accent, b = Accent2;
                if (!Enabled) { a = Color.FromArgb(58, 67, 82); b = Color.FromArgb(48, 56, 70); }
                else if (down) { a = ControlPaint.Dark(Accent, 0.15f); b = ControlPaint.Dark(Accent2, 0.15f); }
                using (GraphicsPath p = Draw.Round(r, 12))
                using (LinearGradientBrush br = new LinearGradientBrush(
                    new Rectangle(0, 0, Width, Height), a, b, 0f))
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
    }

    static class SysProxy
    {
        const string RegPath = @"Software\Microsoft\Windows\CurrentVersion\Internet Settings";
        const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        const int INTERNET_OPTION_REFRESH = 37;

        [DllImport("wininet.dll", SetLastError = true)]
        static extern bool InternetSetOption(IntPtr hInternet, int option, IntPtr buffer, int length);

        static string BackupPath { get { return Path.Combine(Engine.Dir, "proxy.bak"); } }

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
                string d = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "veil");
                Directory.CreateDirectory(d);
                return d;
            }
        }

        public static string ClientPath { get { return Path.Combine(Dir, "veil-client.exe"); } }
        public static string IniPath { get { return Path.Combine(Dir, "veil.ini"); } }
        public static string KeyPath { get { return Path.Combine(Dir, "key.txt"); } }

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

        public static void WriteIni(string server, string sni, string fp, string psk)
        {
            string ini =
                "# generated by veil client\n" +
                "server = " + server + "\n" +
                "sni    = " + sni + "\n" +
                "psk    = " + psk + "\n" +
                "socks  = 127.0.0.1:1080\n" +
                "fp     = " + fp + "\n";
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
            // trim "2026/11/22 05:52:49 " -> "05:52:49 "
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

    class StatusCard : CardPanel
    {
        public bool Connected, Connecting;
        public string Server = "не подключено";
        public double Pulse;

        protected override void OnPainted(Graphics g)
        {
            Color c = Connected ? Theme.Ok : (Connecting ? Theme.Warn : Color.FromArgb(110, 122, 138));
            int alpha = (Connected || Connecting) ? (int)(90 + 90 * Math.Abs(Math.Sin(Pulse))) : 40;
            Draw.Glow(g, new Point(34, 40), 6, c, alpha);

            TextRenderer.DrawText(g, Connected ? "Подключено" : (Connecting ? "Подключение…" : "Отключено"),
                Theme.Big, new Rectangle(56, 20, 250, 26), Theme.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            TextRenderer.DrawText(g, Connected ? Server : "вставь ключ и нажми кнопку",
                Theme.UI, new Rectangle(56, 48, 270, 20), Theme.Muted,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);

            if (Connected)
            {
                string up = (DateTime.Now - Start).ToString(@"hh\:mm\:ss");
                TextRenderer.DrawText(g, up, Theme.Mono, new Rectangle(330, 26, 96, 20), Theme.Text,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
                TextRenderer.DrawText(g, "uptime", Theme.UI, new Rectangle(330, 46, 96, 18), Color.FromArgb(92, 103, 118),
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter);
            }
        }

        public DateTime Start = DateTime.Now;
    }

    class MainForm : Form
    {
        const int W = 480, H = 532;
        readonly TextBox txtKey = new TextBox();
        readonly FlatButton btnGo = new FlatButton();
        readonly FlatButton btnPaste = new FlatButton();
        readonly FlatButton btnMin = new FlatButton();
        readonly FlatButton btnClose = new FlatButton();
        readonly StatusCard card = new StatusCard();
        readonly Panel inputWrap = new Panel();
        readonly Panel logWrap = new Panel();
        readonly TextBox txtLog = new TextBox();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer ui = new System.Windows.Forms.Timer();
        Process proc;
        bool busy;
        bool dragging;
        Point dragFrom;

        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        readonly bool autoConnect;

        public MainForm(string[] args)
        {
            autoConnect = Array.IndexOf(args, "--connect") >= 0;
            FormBorderStyle = FormBorderStyle.None;
            ClientSize = new Size(W, H);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Theme.Bg1;
            DoubleBuffered = true;
            Font = Theme.UI;
            Text = "veil";



            btnMin.Text = "—"; btnMin.Primary = false; btnMin.SetBounds(W - 96, 10, 34, 28);
            btnClose.Text = "✕"; btnClose.Primary = false; btnClose.SetBounds(W - 56, 10, 34, 28);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            btnClose.Click += delegate { Close(); };
            Controls.Add(btnMin);
            Controls.Add(btnClose);

            card.SetBounds(20, 56, W - 40, 92);
            Controls.Add(card);

            Label lKey = new Label();
            lKey.Text = "Ключ доступа";
            lKey.ForeColor = Theme.Muted;
            lKey.BackColor = Color.Transparent;
            lKey.Font = Theme.UI;
            lKey.SetBounds(24, 166, 300, 18);
            Controls.Add(lKey);

            inputWrap.SetBounds(20, 188, W - 40, 46);
            inputWrap.BackColor = Theme.Bg1;
            inputWrap.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Bg1);
                Draw.Card(e.Graphics, new Rectangle(0, 0, inputWrap.Width - 1, inputWrap.Height - 1), 12, Theme.Card, Theme.Line);
            };
            Controls.Add(inputWrap);

            txtKey.BorderStyle = BorderStyle.None;
            txtKey.BackColor = Theme.Card;
            txtKey.ForeColor = Theme.Text;
            txtKey.Font = Theme.Mono;
            txtKey.SetBounds(16, 14, inputWrap.Width - 132, 18);
            inputWrap.Controls.Add(txtKey);

            Label keyHint = new Label();
            keyHint.Text = "veil://…";
            keyHint.ForeColor = Color.FromArgb(96, 108, 124);
            keyHint.BackColor = Theme.Card;
            keyHint.Font = Theme.Mono;
            keyHint.SetBounds(18, 15, 160, 17);
            keyHint.Visible = txtKey.Text.Length == 0;
            keyHint.Click += delegate { txtKey.Focus(); };
            txtKey.TextChanged += delegate { keyHint.Visible = txtKey.Text.Length == 0; };
            inputWrap.Controls.Add(keyHint);
            keyHint.BringToFront();

            btnPaste.Text = "вставить";
            btnPaste.Primary = false;
            btnPaste.SetBounds(inputWrap.Width - 104, 8, 92, 30);
            btnPaste.Click += delegate { try { txtKey.Text = Clipboard.GetText().Trim(); } catch { } };
            inputWrap.Controls.Add(btnPaste);

            btnGo.Text = "Подключиться";
            btnGo.SetBounds(20, 248, W - 40, 46);
            btnGo.Click += delegate { Toggle(); };
            Controls.Add(btnGo);

            Label lLog = new Label();
            lLog.Text = "Журнал";
            lLog.ForeColor = Theme.Muted;
            lLog.BackColor = Color.Transparent;
            lLog.Font = Theme.UI;
            lLog.SetBounds(24, 314, 300, 18);
            Controls.Add(lLog);

            logWrap.SetBounds(20, 336, W - 40, 146);
            logWrap.BackColor = Theme.Bg1;
            logWrap.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.Clear(Theme.Bg1);
                Draw.Card(e.Graphics, new Rectangle(0, 0, logWrap.Width - 1, logWrap.Height - 1), 12, Color.FromArgb(15, 20, 28), Theme.Line);
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
            txtLog.SetBounds(14, 12, logWrap.Width - 28, logWrap.Height - 24);
            logWrap.Controls.Add(txtLog);

            Label logHint = new Label();
            logHint.Text = "соединений пока нет";
            logHint.ForeColor = Color.FromArgb(92, 104, 120);
            logHint.BackColor = Color.FromArgb(15, 20, 28);
            logHint.Font = Theme.UI;
            logHint.TextAlign = ContentAlignment.MiddleCenter;
            logHint.SetBounds(14, logWrap.Height / 2 - 12, logWrap.Width - 28, 24);
            logWrap.Controls.Add(logHint);
            logHint.BringToFront();
            txtLog.TextChanged += delegate { logHint.Visible = txtLog.Text.Length == 0; };

            tray.Text = "veil";
            tray.Icon = SystemIcons.Shield;
            tray.Visible = true;
            tray.DoubleClick += delegate { Restore(); };
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Показать", null, delegate { Restore(); });
            cm.Items.Add("Подключиться / Отключиться", null, delegate { Toggle(); });
            cm.Items.Add("Выход", null, delegate { Close(); });
            tray.ContextMenuStrip = cm;

            if (File.Exists(Engine.KeyPath))
            {
                try { txtKey.Text = File.ReadAllText(Engine.KeyPath).Trim(); } catch { }
            }

            ui.Interval = 40;
            ui.Tick += delegate { Tick(); };
            ui.Start();

            Resize += delegate { if (WindowState == FormWindowState.Minimized) Hide(); };
            FormClosing += delegate { Stop(false); tray.Visible = false; };
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(Handle, 33, ref pref, sizeof(int));
            }
            catch { }
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (autoConnect) BeginInvoke(new MethodInvoker(delegate { Toggle(); }));
        }

        void Restore() { Show(); WindowState = FormWindowState.Normal; Activate(); }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (LinearGradientBrush b = new LinearGradientBrush(
                new Rectangle(0, 0, W, H), Theme.Bg1, Theme.Bg2, 90f))
                e.Graphics.FillRectangle(b, 0, 0, W, H);

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
            string right = proc == null ? "туннель выключен" : "туннель активен";
            TextRenderer.DrawText(e.Graphics, right, Theme.UI,
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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            dragging = false;
            base.OnMouseUp(e);
        }

        void Toggle()
        {
            if (busy) return;
            if (proc != null && !proc.HasExited) { Stop(true); return; }

            string server, sni, fp, psk;
            if (!KeyCodec.TryParse(txtKey.Text, out server, out sni, out fp, out psk))
            {
                MessageBox.Show("Ключ не распознан. Он должен начинаться с veil://", "veil");
                return;
            }
            busy = true;
            btnGo.Enabled = false;
            card.Connecting = true;
            card.Server = server;
            card.Invalidate();
            Application.DoEvents();

            try
            {
                File.WriteAllText(Engine.KeyPath, txtKey.Text.Trim());
                Engine.WriteIni(server, sni, fp, psk);
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
                Invalidate();
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
            card.Server = "не подключено";
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

    static class SelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("usage: veil.exe --selftest <key>"); return 2; }
            string server, sni, fp, psk;
            if (!KeyCodec.TryParse(args[1], out server, out sni, out fp, out psk))
            {
                Console.WriteLine("key: FAIL"); return 3;
            }
            Console.WriteLine("key: ok server=" + server + " sni=" + sni + " fp=" + fp);
            Engine.WriteIni(server, sni, fp, psk);
            Process p = Engine.StartClient();
            bool up = Engine.WaitListen(8000);
            Console.WriteLine("socks: " + (up ? "listening" : "FAIL"));
            bool ok = up && TryFetch("example.com");
            Console.WriteLine("fetch: " + (ok ? "ok" : "FAIL"));
            try { p.Kill(); } catch { }
            return (up && ok) ? 0 : 1;
        }

        static bool TryFetch(string host)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    c.Connect("127.0.0.1", 1080);
                    NetworkStream s = c.GetStream();
                    s.Write(new byte[] { 5, 1, 0 }, 0, 3);
                    byte[] r = new byte[2];
                    if (s.Read(r, 0, 2) != 2 || r[0] != 5 || r[1] != 0) return false;
                    byte[] hb = Encoding.ASCII.GetBytes(host);
                    byte[] req = new byte[5 + hb.Length + 2];
                    req[0] = 5; req[1] = 1; req[2] = 0; req[3] = 3; req[4] = (byte)hb.Length;
                    Array.Copy(hb, 0, req, 5, hb.Length);
                    req[5 + hb.Length] = 0; req[6 + hb.Length] = 80;
                    s.Write(req, 0, req.Length);
                    byte[] rep = new byte[10];
                    if (s.Read(rep, 0, 10) < 2 || rep[1] != 0) return false;
                    byte[] get = Encoding.ASCII.GetBytes("GET / HTTP/1.0\r\nHost: " + host + "\r\n\r\n");
                    s.Write(get, 0, get.Length);
                    byte[] buf = new byte[256];
                    int n = s.Read(buf, 0, buf.Length);
                    return n > 0 && Encoding.ASCII.GetString(buf, 0, Math.Min(n, 12)).StartsWith("HTTP/");
                }
            }
            catch { return false; }
        }
    }
}
