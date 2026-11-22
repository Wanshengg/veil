using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
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
            if (args.Length > 1 && args[0] == "--makekey") { Console.WriteLine(Key.Make(args[1], args[2], args[3], args[4])); return; }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }

    static class Key
    {
        // veil://base64url(server|sni|fp|psk)
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

        static string BackupPath
        {
            get { return Path.Combine(Engine.Dir, "proxy.bak"); }
        }

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
            lock (Log)
            {
                Log.Add(line);
                if (Log.Count > 400) Log.RemoveAt(0);
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

    class MainForm : Form
    {
        readonly TextBox txtKey = new TextBox();
        readonly Button btnGo = new Button();
        readonly Label lblStatus = new Label();
        readonly TextBox txtLog = new TextBox();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly System.Windows.Forms.Timer tm = new System.Windows.Forms.Timer();
        Process proc;
        bool busy;

        public MainForm()
        {
            Text = "veil — клиент";
            ClientSize = new Size(470, 380);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            Label l1 = new Label();
            l1.Text = "Ключ доступа (veil://...). Вставь и нажми «Подключиться».";
            l1.SetBounds(14, 12, 440, 18);
            Controls.Add(l1);

            txtKey.SetBounds(14, 34, 442, 24);
            txtKey.Font = new Font("Consolas", 8.5f);
            Controls.Add(txtKey);

            Button btnPaste = new Button();
            btnPaste.Text = "Вставить из буфера";
            btnPaste.SetBounds(14, 66, 150, 28);
            btnPaste.Click += delegate { try { txtKey.Text = Clipboard.GetText(); } catch { } };
            Controls.Add(btnPaste);

            btnGo.Text = "Подключиться";
            btnGo.SetBounds(170, 66, 140, 28);
            btnGo.Click += delegate { Toggle(); };
            Controls.Add(btnGo);

            Button btnCopy = new Button();
            btnCopy.Text = "Сохранить лог";
            btnCopy.SetBounds(316, 66, 140, 28);
            btnCopy.Click += delegate
            {
                try { File.WriteAllText(Path.Combine(Engine.Dir, "client.log"), Engine.Tail(400)); MessageBox.Show("Лог: " + Engine.Dir); }
                catch (Exception ex) { MessageBox.Show(ex.Message); }
            };
            Controls.Add(btnCopy);

            lblStatus.Text = "● отключено";
            lblStatus.ForeColor = Color.Gray;
            lblStatus.SetBounds(14, 104, 442, 20);
            lblStatus.Font = new Font("Segoe UI", 9.5f, FontStyle.Bold);
            Controls.Add(lblStatus);

            txtLog.SetBounds(14, 130, 442, 236);
            txtLog.Multiline = true;
            txtLog.ReadOnly = true;
            txtLog.ScrollBars = ScrollBars.Vertical;
            txtLog.Font = new Font("Consolas", 8f);
            txtLog.BackColor = Color.FromArgb(250, 250, 250);
            Controls.Add(txtLog);

            tray.Text = "veil";
            tray.Icon = SystemIcons.Shield;
            tray.Visible = true;
            tray.DoubleClick += delegate { Show(); WindowState = FormWindowState.Normal; Activate(); };
            ContextMenuStrip cm = new ContextMenuStrip();
            cm.Items.Add("Показать", null, delegate { Show(); WindowState = FormWindowState.Normal; Activate(); });
            cm.Items.Add("Подключиться / Отключиться", null, delegate { Toggle(); });
            cm.Items.Add("Выход", null, delegate { Close(); });
            tray.ContextMenuStrip = cm;

            tm.Interval = 1000;
            tm.Tick += delegate { Tick(); };
            tm.Start();

            string saved = Engine.KeyPath;
            if (File.Exists(saved))
            {
                try { txtKey.Text = File.ReadAllText(saved).Trim(); } catch { }
            }

            Resize += delegate { if (WindowState == FormWindowState.Minimized) { Hide(); } };
            FormClosing += delegate { Stop(false); tray.Visible = false; };
        }

        void Toggle()
        {
            if (busy) return;
            if (proc != null && !proc.HasExited) { Stop(true); return; }

            string server, sni, fp, psk;
            if (!Key.TryParse(txtKey.Text, out server, out sni, out fp, out psk))
            {
                MessageBox.Show("Ключ не распознан. Он должен начинаться с veil://", "veil");
                return;
            }
            busy = true;
            btnGo.Enabled = false;
            lblStatus.Text = "● подключение...";
            lblStatus.ForeColor = Color.DarkOrange;
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
                lblStatus.Text = "● подключено · " + server;
                lblStatus.ForeColor = Color.SeaGreen;
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
                btnGo.Text = (proc != null && !proc.HasExited) ? "Отключиться" : "Подключиться";
            }
        }

        void Stop(bool user)
        {
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(3000); } } catch { }
            proc = null;
            SysProxy.Disable();
            btnGo.Text = "Подключиться";
            lblStatus.Text = "● отключено";
            lblStatus.ForeColor = Color.Gray;
            tray.Text = "veil: отключено";
        }

        void Tick()
        {
            string tail = Engine.Tail(14);
            if (txtLog.Text != tail) txtLog.Text = tail;
            if (proc != null && proc.HasExited)
            {
                Stop(false);
                lblStatus.Text = "● клиент завершился";
                lblStatus.ForeColor = Color.Firebrick;
            }
        }
    }

    static class SelfTest
    {
        public static int Run(string[] args)
        {
            if (args.Length < 2) { Console.WriteLine("usage: veil.exe --selftest <key>"); return 2; }
            string server, sni, fp, psk;
            if (!Key.TryParse(args[1], out server, out sni, out fp, out psk))
            {
                Console.WriteLine("key: FAIL"); return 3;
            }
            Console.WriteLine("key: ok server=" + server + " sni=" + sni + " fp=" + fp);
            Engine.WriteIni(server, sni, fp, psk);
            Process p = Engine.StartClient();
            bool up = Engine.WaitListen(8000);
            Console.WriteLine("socks: " + (up ? "listening" : "FAIL"));
            bool ok = false;
            if (up) ok = TryFetch("example.com");
            Console.WriteLine("fetch: " + (ok ? "ok" : "FAIL"));
            try { p.Kill(); } catch { }
            Console.WriteLine("log:\n" + Engine.Tail(6));
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
