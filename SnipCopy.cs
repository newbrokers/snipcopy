using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Numerics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace SnipCopy
{
    static class Program
    {
        internal const string AppVersion = "0.2.0";
        internal static LicenseInfo License;
        internal static Bitmap LastImage;
        internal static string LastImagePath;
        internal static bool OpenEditorAfterSnip;
        internal static NotifyIcon TrayIcon;
        internal static Icon AppIcon;
        private static ToolStripMenuItem licenseItem;
        private static ToolStripMenuItem historyItem;
        private static DateTime openEditorToastUntilUtc = DateTime.MinValue;
        private static EditorForm editorWindow;
        private static HotkeyWindow hotkeyWindow;
        private const int HotkeyId = 9182;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new ApplicationContext();
            License = LicenseStore.Load();
            HistoryStore.ImportLegacyTempHistory();
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? CreateAppIcon();
            TrayIcon = new NotifyIcon();
            TrayIcon.Text = "SnipCopy";
            TrayIcon.Icon = AppIcon;
            TrayIcon.Visible = true;
            TrayIcon.BalloonTipClicked += delegate { HandleToastClick(); };

            var menu = new ContextMenuStrip();
            var newItem = menu.Items.Add("New snip    Ctrl+Shift+S");
            var editItem = menu.Items.Add("Open last in editor");
            historyItem = new ToolStripMenuItem();
            menu.Items.Add(historyItem);
            var autoEditorItem = new ToolStripMenuItem("Open editor after snip");
            autoEditorItem.CheckOnClick = true;
            menu.Items.Add(autoEditorItem);
            menu.Items.Add("-");
            licenseItem = new ToolStripMenuItem();
            licenseItem.Enabled = false;
            menu.Items.Add(licenseItem);
            var settingsItem = menu.Items.Add("Settings / About");
            menu.Items.Add("-");
            var exitItem = menu.Items.Add("Exit");
            RefreshLicenseMenuText();

            newItem.Click += delegate { StartSnip(); };
            editItem.Click += delegate { OpenEditor(LastImage); };
            historyItem.Click += delegate { ShowHistory(); };
            autoEditorItem.CheckedChanged += delegate { OpenEditorAfterSnip = autoEditorItem.Checked; };
            settingsItem.Click += delegate { ShowSettings(); };
            exitItem.Click += delegate { context.ExitThread(); };
            TrayIcon.ContextMenuStrip = menu;
            TrayIcon.DoubleClick += delegate { StartSnip(); };

            hotkeyWindow = new HotkeyWindow(HotkeyId);
            hotkeyWindow.HotkeyPressed += delegate { StartSnip(); };
            bool registered = NativeMethods.RegisterHotKey(
                hotkeyWindow.Handle,
                HotkeyId,
                NativeMethods.MOD_CONTROL | NativeMethods.MOD_SHIFT,
                (uint)Keys.S);

            ShowToast(
                "SnipCopy is running",
                registered ? "Press Ctrl+Shift+S or double-click the tray icon." : "Hotkey registration failed. Use the tray icon to snip.");

            context.ThreadExit += delegate
            {
                NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, HotkeyId);
                hotkeyWindow.Dispose();
                TrayIcon.Visible = false;
                TrayIcon.Dispose();
                AppIcon.Dispose();
                if (LastImage != null) LastImage.Dispose();
            };

            Application.Run(context);
        }

        internal static bool IsPro
        {
            get { return License != null && License.IsActive; }
        }

        internal static bool CanUseProTools
        {
            get { return IsPro; }
        }

        internal static void RefreshLicenseMenuText()
        {
            if (licenseItem == null) return;
            licenseItem.Text = "License: " + (IsPro ? "Pro" : "Free");
            RefreshHistoryMenuText();
            RefreshOpenEditorLicenseState();
        }

        internal static void RefreshOpenEditorLicenseState()
        {
            if (editorWindow == null || editorWindow.IsDisposed) return;
            editorWindow.RefreshLicenseState();
        }

        internal static void RefreshHistoryMenuText()
        {
            if (historyItem == null) return;
            int count = HistoryStore.GetItems().Count;
            historyItem.Text = "History (" + count + ")";
        }

        internal static void ShowSettings()
        {
            using (var form = new SettingsForm())
            {
                form.ShowDialog();
            }
            RefreshLicenseMenuText();
        }

        internal static void ShowHistory()
        {
            using (var form = new HistoryForm())
            {
                form.ShowDialog();
            }
            RefreshHistoryMenuText();
        }

        internal static Icon CreateAppIcon()
        {
            using (var bitmap = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var background = new LinearGradientBrush(new Rectangle(0, 0, 32, 32), Color.FromArgb(36, 132, 255), Color.FromArgb(20, 184, 166), 45f))
            using (var whitePen = new Pen(Color.White, 2.6f))
            using (var darkPen = new Pen(Color.FromArgb(22, 52, 78), 2.2f))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.Clear(Color.Transparent);
                using (GraphicsPath shape = RoundedRect(new Rectangle(3, 3, 26, 26), 7))
                {
                    g.FillPath(background, shape);
                }

                whitePen.StartCap = LineCap.Round;
                whitePen.EndCap = LineCap.Round;
                g.DrawLine(whitePen, 10, 19, 22, 7);
                g.DrawLine(whitePen, 10, 13, 22, 25);
                g.DrawEllipse(darkPen, 6, 10, 7, 7);
                g.DrawEllipse(darkPen, 6, 17, 7, 7);
                g.DrawLine(darkPen, 15, 16, 25, 16);

                IntPtr handle = bitmap.GetHicon();
                try
                {
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                finally
                {
                    NativeMethods.DestroyIcon(handle);
                }
            }
        }

        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            int diameter = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
            path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
            path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
            path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            return path;
        }

        internal static void StartSnip()
        {
            if (CaptureOverlay.IsOpen) return;
            using (var screenshot = CaptureScreen())
            {
                var overlay = new CaptureOverlay(screenshot);
                if (overlay.ShowDialog() == DialogResult.OK && overlay.CapturedImage != null)
                {
                    Clipboard.SetImage(overlay.CapturedImage);
                    if (LastImage != null) LastImage.Dispose();
                    LastImage = new Bitmap(overlay.CapturedImage);
                    SaveLastImage(LastImage);
                    ShowToast("Snip copied", LastImage.Width + " x " + LastImage.Height + " copied to clipboard", !OpenEditorAfterSnip);

                    if (OpenEditorAfterSnip)
                    {
                        OpenEditor(LastImage);
                    }
                }
                if (overlay.CapturedImage != null) overlay.CapturedImage.Dispose();
            }
        }

        internal static Bitmap CaptureScreen()
        {
            Rectangle bounds = SystemInformation.VirtualScreen;
            var bitmap = new Bitmap(bounds.Width, bounds.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            {
                g.CopyFromScreen(bounds.Location, Point.Empty, bounds.Size);
            }
            return bitmap;
        }

        internal static void OpenEditor(Bitmap bitmap)
        {
            if (bitmap == null)
            {
                MessageBox.Show("No snip has been captured yet.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            if (editorWindow != null && !editorWindow.IsDisposed)
            {
                editorWindow.LoadImage(bitmap);
                editorWindow.RefreshHistoryFromStore();
                if (editorWindow.WindowState == FormWindowState.Minimized)
                {
                    editorWindow.WindowState = FormWindowState.Normal;
                }
                editorWindow.Show();
                editorWindow.Activate();
                return;
            }

            editorWindow = new EditorForm(bitmap);
            editorWindow.FormClosed += delegate { editorWindow = null; };
            editorWindow.Show();
        }

        internal static void RefreshOpenEditorHistory()
        {
            if (editorWindow == null || editorWindow.IsDisposed) return;
            editorWindow.RefreshHistoryFromStore();
        }

        internal static void SaveLastImage(Bitmap bitmap)
        {
            string dir = HistoryStore.GetHistoryDirectory();
            string path = Path.Combine(dir, "snip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            bitmap.Save(path, ImageFormat.Png);
            LastImagePath = path;
            HistoryStore.TrimToLicenseLimit();
            RefreshHistoryMenuText();
            RefreshOpenEditorHistory();
        }

        internal static void ShowToast(string title, string text)
        {
            ShowToast(title, text, false);
        }

        internal static void ShowToast(string title, string text, bool openEditorOnClick)
        {
            if (TrayIcon == null) return;
            openEditorToastUntilUtc = openEditorOnClick ? DateTime.UtcNow.AddMinutes(5) : DateTime.MinValue;
            TrayIcon.BalloonTipTitle = title;
            TrayIcon.BalloonTipText = text;
            TrayIcon.ShowBalloonTip(1500);
        }

        private static void HandleToastClick()
        {
            if (DateTime.UtcNow > openEditorToastUntilUtc) return;
            openEditorToastUntilUtc = DateTime.MinValue;
            OpenEditor(LastImage);
        }
    }

    internal static class NativeMethods
    {
        internal const uint MOD_CONTROL = 0x0002;
        internal const uint MOD_SHIFT = 0x0004;
        internal const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll")]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [DllImport("user32.dll")]
        internal static extern bool DestroyIcon(IntPtr hIcon);
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event EventHandler HotkeyPressed;

        public HotkeyWindow(int id)
        {
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && HotkeyPressed != null)
            {
                HotkeyPressed(this, EventArgs.Empty);
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }

    internal sealed class LicenseInfo
    {
        public string Key = "";
        public string CustomerEmail = "";
        public string ProductSlug = "";
        public string Status = "";
        public string Token = "";
        public string Reason = "";
        public DateTime ExpiresAt = DateTime.MinValue;

        public bool IsActive
        {
            get
            {
                return !String.IsNullOrEmpty(Key)
                    && String.Equals(ProductSlug, "snipcopy", StringComparison.OrdinalIgnoreCase)
                    && String.Equals(Status, "active", StringComparison.OrdinalIgnoreCase)
                    && ExpiresAt.ToUniversalTime() >= DateTime.UtcNow;
            }
        }

        public string StatusText
        {
            get
            {
                if (IsActive) return "Pro until " + ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd");
                if (!String.IsNullOrEmpty(Reason)) return Reason;
                if (!String.IsNullOrEmpty(Key)) return "Expired";
                return "Free";
            }
        }
    }

    internal static class LicenseStore
    {
        private const string ProductSlug = "snipcopy";
        private const string PublicKeyRawBase64 = "UqcQjsC1QuHqNc9OdBmCFAayLFWw0aNDwj6CmhQgJOU=";

        internal static LicenseInfo Load()
        {
            LicenseInfo saved = LoadSavedCodeRecord();
            if (saved != null) return saved;
            return new LicenseInfo();
        }

        internal static bool Activate(string email, string licenseKey, out string message)
        {
            try
            {
                string body = "{"
                    + "\"licenseKey\":\"" + JsonEscape(licenseKey.Trim()) + "\","
                    + "\"email\":\"" + JsonEscape(email.Trim()) + "\","
                    + "\"product_slug\":\"" + ProductSlug + "\","
                    + "\"machineHash\":\"" + MachineHash() + "\""
                    + "}";
                string json = PostJson(ApiBaseUrl() + "/api/license/activate", body);
                LicenseInfo info;
                string reason;
                if (!VerifyTokenFromResponse(json, out info, out reason))
                {
                    message = reason;
                    return false;
                }

                SaveSavedCodeRecord(info);
                Program.License = info;
                message = "SnipCopy Pro is active until " + info.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd") + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        internal static bool Sync(out string message)
        {
            if (Program.License == null || String.IsNullOrEmpty(Program.License.Key))
            {
                message = "Activate a SavedCode license first.";
                return false;
            }

            try
            {
                string body = "{"
                    + "\"licenseKey\":\"" + JsonEscape(Program.License.Key) + "\","
                    + "\"product_slug\":\"" + ProductSlug + "\""
                    + "}";
                string json = PostJson(ApiBaseUrl() + "/api/license/sync", body);
                LicenseInfo info;
                string reason;
                if (!VerifyTokenFromResponse(json, out info, out reason))
                {
                    message = reason;
                    return false;
                }

                SaveSavedCodeRecord(info);
                Program.License = info;
                message = "License synced. SnipCopy Pro is active until " + info.ExpiresAt.ToLocalTime().ToString("yyyy-MM-dd") + ".";
                return true;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        internal static void Deactivate()
        {
            Program.License = new LicenseInfo();
            string path = GetSavedCodePath();
            if (File.Exists(path)) File.Delete(path);
        }

        private static LicenseInfo LoadSavedCodeRecord()
        {
            string path = GetSavedCodePath();
            if (!File.Exists(path)) return null;

            try
            {
                string envelope = File.ReadAllText(path);
                string protectedData = ExtractJsonString(envelope, "protected_data");
                if (String.IsNullOrEmpty(protectedData)) return new LicenseInfo();

                byte[] raw = ProtectedData.Unprotect(Convert.FromBase64String(protectedData), null, DataProtectionScope.CurrentUser);
                string record = Encoding.UTF8.GetString(raw);
                string token = ExtractJsonString(record, "token");
                string savedKey = ExtractJsonString(record, "license_key");
                string savedEmail = ExtractJsonString(record, "customer_email");
                LicenseInfo info;
                string reason;
                if (!LicenseTokenVerifier.TryVerify(token, ProductSlug, PublicKeyRawBase64, out info, out reason))
                {
                    return new LicenseInfo { Key = savedKey, CustomerEmail = savedEmail, ProductSlug = ProductSlug, Reason = reason };
                }
                if (!String.IsNullOrEmpty(savedKey)) info.Key = savedKey;
                if (!String.IsNullOrEmpty(savedEmail)) info.CustomerEmail = savedEmail;
                info.Token = token;
                return info;
            }
            catch
            {
                return new LicenseInfo();
            }
        }

        private static bool VerifyTokenFromResponse(string json, out LicenseInfo info, out string reason)
        {
            string token = ExtractJsonString(json, "token");
            if (String.IsNullOrEmpty(token))
            {
                info = null;
                reason = "SavedCode did not return a license token.";
                return false;
            }
            if (!LicenseTokenVerifier.TryVerify(token, ProductSlug, PublicKeyRawBase64, out info, out reason))
            {
                return false;
            }
            info.Token = token;
            return true;
        }

        private static string ApiBaseUrl()
        {
            string value = Environment.GetEnvironmentVariable("SAVEDCODE_API_BASE_URL");
            if (String.IsNullOrWhiteSpace(value)) return "https://www.savedcode.com";
            value = value.Trim().TrimEnd('/');
            if (String.Equals(value, "https://savedcode.com", StringComparison.OrdinalIgnoreCase)) return "https://www.savedcode.com";
            if (String.Equals(value, "http://savedcode.com", StringComparison.OrdinalIgnoreCase)) return "https://www.savedcode.com";
            return value;
        }

        private static string PostJson(string url, string body)
        {
            return PostJson(url, body, 0);
        }

        private static string PostJson(string url, string body, int redirectCount)
        {
            ServicePointManager.SecurityProtocol = ServicePointManager.SecurityProtocol | (SecurityProtocolType)3072;
            byte[] data = Encoding.UTF8.GetBytes(body);
            var request = (HttpWebRequest)WebRequest.Create(url);
            request.Method = "POST";
            request.ContentType = "application/json";
            request.Accept = "application/json";
            request.Timeout = 20000;
            request.AllowAutoRedirect = false;
            request.ContentLength = data.Length;
            using (Stream stream = request.GetRequestStream())
            {
                stream.Write(data, 0, data.Length);
            }

            try
            {
                using (var response = (HttpWebResponse)request.GetResponse())
                {
                    if (IsRedirectStatus(response) && redirectCount < 3)
                    {
                        string redirectUrl = ResolveRedirectUrl(url, response.Headers["Location"]);
                        if (!String.IsNullOrEmpty(redirectUrl)) return PostJson(redirectUrl, body, redirectCount + 1);
                    }

                    using (var reader = new StreamReader(response.GetResponseStream()))
                    {
                        return reader.ReadToEnd();
                    }
                }
            }
            catch (WebException ex)
            {
                var response = ex.Response as HttpWebResponse;
                if (response != null && IsRedirectStatus(response) && redirectCount < 3)
                {
                    string redirectUrl = ResolveRedirectUrl(url, response.Headers["Location"]);
                    response.Close();
                    if (!String.IsNullOrEmpty(redirectUrl)) return PostJson(redirectUrl, body, redirectCount + 1);
                }

                string error = "";
                if (ex.Response != null)
                {
                    using (var reader = new StreamReader(ex.Response.GetResponseStream()))
                    {
                        error = ExtractJsonString(reader.ReadToEnd(), "error");
                    }
                }
                if (String.IsNullOrEmpty(error)) error = ex.Message;
                throw new ApplicationException(error);
            }
        }

        private static bool IsRedirectStatus(HttpWebResponse response)
        {
            int status = (int)response.StatusCode;
            return status == 301 || status == 302 || status == 303 || status == 307 || status == 308;
        }

        private static string ResolveRedirectUrl(string currentUrl, string location)
        {
            if (String.IsNullOrWhiteSpace(location)) return "";
            Uri redirectUri;
            if (Uri.TryCreate(location, UriKind.Absolute, out redirectUri)) return redirectUri.ToString();

            Uri currentUri;
            if (Uri.TryCreate(currentUrl, UriKind.Absolute, out currentUri) && Uri.TryCreate(currentUri, location, out redirectUri))
            {
                return redirectUri.ToString();
            }
            return "";
        }

        private static void SaveSavedCodeRecord(LicenseInfo info)
        {
            string path = GetSavedCodePath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string json = "{"
                + "\"license_key\":\"" + JsonEscape(info.Key) + "\","
                + "\"customer_email\":\"" + JsonEscape(info.CustomerEmail) + "\","
                + "\"token\":\"" + JsonEscape(info.Token) + "\","
                + "\"activated_at\":\"" + DateTime.UtcNow.ToString("o") + "\""
                + "}";
            byte[] protectedBytes = ProtectedData.Protect(Encoding.UTF8.GetBytes(json), null, DataProtectionScope.CurrentUser);
            File.WriteAllText(path, "{\"protected_data\":\"" + Convert.ToBase64String(protectedBytes) + "\"}");
        }

        private static string GetSavedCodePath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SavedCode", "Licenses");
            return Path.Combine(dir, ProductSlug + ".json");
        }

        private static string MachineHash()
        {
            string text = Environment.MachineName + "|" + Environment.OSVersion.VersionString + "|" + Environment.UserDomainName;
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(text));
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        internal static string ExtractJsonString(string json, string name)
        {
            if (String.IsNullOrEmpty(json)) return "";
            Match match = Regex.Match(json, "\"" + Regex.Escape(name) + "\"\\s*:\\s*\"((?:\\\\.|[^\"\\\\])*)\"");
            if (!match.Success) return "";
            return JsonUnescape(match.Groups[1].Value);
        }

        private static string JsonEscape(string value)
        {
            if (value == null) return "";
            return value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\r", "\\r").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static string JsonUnescape(string value)
        {
            var builder = new StringBuilder();
            for (int i = 0; i < value.Length; i++)
            {
                char c = value[i];
                if (c != '\\' || i + 1 >= value.Length)
                {
                    builder.Append(c);
                    continue;
                }

                char next = value[++i];
                if (next == '"' || next == '\\' || next == '/') builder.Append(next);
                else if (next == 'b') builder.Append('\b');
                else if (next == 'f') builder.Append('\f');
                else if (next == 'n') builder.Append('\n');
                else if (next == 'r') builder.Append('\r');
                else if (next == 't') builder.Append('\t');
                else if (next == 'u' && i + 4 < value.Length)
                {
                    string hex = value.Substring(i + 1, 4);
                    int code;
                    if (Int32.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out code))
                    {
                        builder.Append((char)code);
                        i += 4;
                    }
                }
            }
            return builder.ToString();
        }

    }

    internal static class LicenseTokenVerifier
    {
        private struct EdPoint
        {
            public BigInteger X;
            public BigInteger Y;

            public EdPoint(BigInteger x, BigInteger y)
            {
                X = x;
                Y = y;
            }
        }

        private static readonly BigInteger P = BigInteger.Pow(new BigInteger(2), 255) - 19;
        private static readonly BigInteger L = BigInteger.Pow(new BigInteger(2), 252) + BigInteger.Parse("27742317777372353535851937790883648493");
        private static readonly BigInteger D = Mod(BigInteger.Parse("-121665") * Invert(BigInteger.Parse("121666")));
        private static readonly BigInteger I = BigInteger.ModPow(new BigInteger(2), (P - 1) / 4, P);
        private static readonly EdPoint Identity = new EdPoint(BigInteger.Zero, BigInteger.One);
        private static readonly EdPoint BasePoint = new EdPoint(
            BigInteger.Parse("15112221349535400772501151409588531511454012693041857206046113283949847762202"),
            BigInteger.Parse("46316835694926478169428394003475163141307993866256225615783033603165251855960"));

        internal static bool TryVerify(string token, string expectedProductSlug, string publicKeyRawBase64, out LicenseInfo info, out string reason)
        {
            info = null;
            reason = "Invalid license token.";

            if (String.IsNullOrEmpty(token))
            {
                reason = "No local license token.";
                return false;
            }

            string[] parts = token.Split('.');
            if (parts.Length != 2)
            {
                reason = "Malformed license token.";
                return false;
            }

            byte[] publicKey;
            byte[] signature;
            byte[] payloadBytes;
            try
            {
                publicKey = Convert.FromBase64String(publicKeyRawBase64);
                signature = Base64UrlDecode(parts[1]);
                payloadBytes = Base64UrlDecode(parts[0]);
            }
            catch
            {
                reason = "Malformed license token.";
                return false;
            }

            byte[] signedBody = Encoding.ASCII.GetBytes(parts[0]);
            if (!VerifyEd25519(publicKey, signedBody, signature))
            {
                reason = "Invalid license signature.";
                return false;
            }

            string payload = Encoding.UTF8.GetString(payloadBytes);
            string productSlug = LicenseStore.ExtractJsonString(payload, "product_slug");
            string status = LicenseStore.ExtractJsonString(payload, "status");
            string expiresText = LicenseStore.ExtractJsonString(payload, "expires_at");
            string licenseKey = LicenseStore.ExtractJsonString(payload, "license_key");
            string email = LicenseStore.ExtractJsonString(payload, "customer_email");

            if (!String.Equals(productSlug, expectedProductSlug, StringComparison.OrdinalIgnoreCase))
            {
                reason = "License belongs to a different product.";
                return false;
            }
            if (!String.Equals(status, "active", StringComparison.OrdinalIgnoreCase))
            {
                reason = "License status is " + (String.IsNullOrEmpty(status) ? "unknown" : status) + ".";
                return false;
            }

            DateTime expiresAt;
            if (!DateTime.TryParse(expiresText, null, System.Globalization.DateTimeStyles.AdjustToUniversal, out expiresAt))
            {
                reason = "License expiry is missing or invalid.";
                return false;
            }
            expiresAt = expiresAt.ToUniversalTime();
            if (expiresAt < DateTime.UtcNow)
            {
                reason = "License expired.";
                info = new LicenseInfo
                {
                    Key = licenseKey,
                    CustomerEmail = email,
                    ProductSlug = productSlug,
                    Status = status,
                    ExpiresAt = expiresAt,
                    Reason = reason
                };
                return false;
            }

            info = new LicenseInfo
            {
                Key = licenseKey,
                CustomerEmail = email,
                ProductSlug = productSlug,
                Status = status,
                ExpiresAt = expiresAt
            };
            reason = "Active";
            return true;
        }

        private static bool VerifyEd25519(byte[] publicKey, byte[] message, byte[] signature)
        {
            if (publicKey == null || publicKey.Length != 32 || signature == null || signature.Length != 64) return false;

            byte[] rBytes = new byte[32];
            byte[] sBytes = new byte[32];
            Buffer.BlockCopy(signature, 0, rBytes, 0, 32);
            Buffer.BlockCopy(signature, 32, sBytes, 0, 32);

            BigInteger s = FromLittleEndian(sBytes);
            if (s < 0 || s >= L) return false;

            EdPoint a;
            EdPoint r;
            if (!DecodePoint(publicKey, out a)) return false;
            if (!DecodePoint(rBytes, out r)) return false;

            byte[] hashInput = new byte[64 + message.Length];
            Buffer.BlockCopy(rBytes, 0, hashInput, 0, 32);
            Buffer.BlockCopy(publicKey, 0, hashInput, 32, 32);
            Buffer.BlockCopy(message, 0, hashInput, 64, message.Length);

            BigInteger h;
            using (SHA512 sha = SHA512.Create())
            {
                h = FromLittleEndian(sha.ComputeHash(hashInput)) % L;
            }

            EdPoint left = ScalarMultiply(s, BasePoint);
            EdPoint right = Add(r, ScalarMultiply(h, a));
            return Equal(left, right);
        }

        private static bool DecodePoint(byte[] encoded, out EdPoint point)
        {
            point = Identity;
            if (encoded == null || encoded.Length != 32) return false;

            byte[] yBytes = new byte[32];
            Buffer.BlockCopy(encoded, 0, yBytes, 0, 32);
            int sign = (yBytes[31] & 0x80) != 0 ? 1 : 0;
            yBytes[31] = (byte)(yBytes[31] & 0x7f);

            BigInteger y = FromLittleEndian(yBytes);
            if (y >= P) return false;

            BigInteger yy = Mod(y * y);
            BigInteger xx = Mod((yy - 1) * Invert(Mod(D * yy + 1)));
            BigInteger x = BigInteger.ModPow(xx, (P + 3) / 8, P);
            if (Mod(x * x - xx) != 0) x = Mod(x * I);
            if (Mod(x * x - xx) != 0) return false;
            if (x.IsZero && sign == 1) return false;
            if ((x.IsEven ? 0 : 1) != sign) x = Mod(P - x);

            point = new EdPoint(x, y);
            return true;
        }

        private static EdPoint Add(EdPoint p, EdPoint q)
        {
            BigInteger x1x2 = Mod(p.X * q.X);
            BigInteger y1y2 = Mod(p.Y * q.Y);
            BigInteger dxxyy = Mod(D * x1x2 * y1y2);
            BigInteger x = Mod((p.X * q.Y + q.X * p.Y) * Invert(Mod(1 + dxxyy)));
            BigInteger y = Mod((y1y2 + x1x2) * Invert(Mod(1 - dxxyy)));
            return new EdPoint(x, y);
        }

        private static EdPoint ScalarMultiply(BigInteger scalar, EdPoint point)
        {
            EdPoint result = Identity;
            EdPoint addend = point;
            while (scalar > 0)
            {
                if (!scalar.IsEven) result = Add(result, addend);
                addend = Add(addend, addend);
                scalar = scalar >> 1;
            }
            return result;
        }

        private static bool Equal(EdPoint a, EdPoint b)
        {
            return Mod(a.X - b.X).IsZero && Mod(a.Y - b.Y).IsZero;
        }

        private static BigInteger Invert(BigInteger value)
        {
            return BigInteger.ModPow(Mod(value), P - 2, P);
        }

        private static BigInteger Mod(BigInteger value)
        {
            value %= P;
            if (value < 0) value += P;
            return value;
        }

        private static BigInteger FromLittleEndian(byte[] bytes)
        {
            byte[] positive = new byte[bytes.Length + 1];
            Buffer.BlockCopy(bytes, 0, positive, 0, bytes.Length);
            positive[bytes.Length] = 0;
            return new BigInteger(positive);
        }

        private static byte[] Base64UrlDecode(string value)
        {
            string padded = value.Replace('-', '+').Replace('_', '/');
            switch (padded.Length % 4)
            {
                case 2:
                    padded += "==";
                    break;
                case 3:
                    padded += "=";
                    break;
                case 0:
                    break;
                default:
                    throw new FormatException("Invalid base64url.");
            }
            return Convert.FromBase64String(padded);
        }
    }

    internal sealed class HistoryItem
    {
        public string Path;
        public DateTime CreatedAt;

        public string DisplayName
        {
            get { return CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }
        }
    }

    internal static class HistoryStore
    {
        internal static string GetHistoryDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnipCopy", "History");
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static void ImportLegacyTempHistory()
        {
            string oldDir = Path.Combine(Path.GetTempPath(), "SnipCopy");
            if (!Directory.Exists(oldDir)) return;

            string newDir = GetHistoryDirectory();
            foreach (string path in Directory.GetFiles(oldDir, "*.png"))
            {
                try
                {
                    string name = Path.GetFileName(path);
                    string target = Path.Combine(newDir, name);
                    if (!File.Exists(target))
                    {
                        File.Copy(path, target);
                    }
                }
                catch
                {
                }
            }

            TrimToLicenseLimit();
        }

        internal static List<HistoryItem> GetItems()
        {
            var items = new List<HistoryItem>();
            string dir = GetHistoryDirectory();
            foreach (string path in Directory.GetFiles(dir, "*.png"))
            {
                try
                {
                    items.Add(new HistoryItem
                    {
                        Path = path,
                        CreatedAt = File.GetCreationTime(path)
                    });
                }
                catch
                {
                }
            }

            items.Sort(delegate(HistoryItem a, HistoryItem b)
            {
                return b.CreatedAt.CompareTo(a.CreatedAt);
            });
            return items;
        }

        internal static void TrimToLicenseLimit()
        {
            int limit = Program.IsPro ? 500 : 5;
            List<HistoryItem> items = GetItems();
            for (int i = limit; i < items.Count; i++)
            {
                TryDelete(items[i].Path);
            }
        }

        internal static void Delete(string path)
        {
            TryDelete(path);
            if (String.Equals(Program.LastImagePath, path, StringComparison.OrdinalIgnoreCase))
            {
                Program.LastImagePath = "";
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
            }
        }
    }

    internal sealed class CaptureOverlay : Form
    {
        internal static bool IsOpen;
        internal Bitmap CapturedImage;
        private readonly Bitmap screenshot;
        private bool selecting;
        private Point start;
        private Rectangle selection;

        public CaptureOverlay(Bitmap source)
        {
            IsOpen = true;
            screenshot = new Bitmap(source);
            Bounds = SystemInformation.VirtualScreen;
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            Cursor = Cursors.Cross;
            KeyPreview = true;
            DoubleBuffered = true;
            BackgroundImage = screenshot;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (var shade = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
            {
                e.Graphics.FillRectangle(shade, ClientRectangle);
            }

            if (selection.Width > 0 && selection.Height > 0)
            {
                e.Graphics.DrawImage(screenshot, selection, selection, GraphicsUnit.Pixel);
                using (var pen = new Pen(Color.FromArgb(70, 160, 255), 2))
                {
                    e.Graphics.DrawRectangle(pen, selection);
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            selecting = true;
            start = e.Location;
            selection = Rectangle.Empty;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!selecting) return;
            selection = Normalize(start, e.Location);
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!selecting) return;
            selecting = false;
            selection = Normalize(start, e.Location);

            if (selection.Width < 3 || selection.Height < 3)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            CapturedImage = new Bitmap(selection.Width, selection.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(CapturedImage))
            {
                g.DrawImage(screenshot, new Rectangle(0, 0, CapturedImage.Width, CapturedImage.Height), selection, GraphicsUnit.Pixel);
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            IsOpen = false;
            screenshot.Dispose();
            base.OnFormClosed(e);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }
    }

    internal sealed class EditorForm : Form
    {
        private readonly TabControl tabs;
        private readonly Panel scroll;
        private readonly PictureBox canvas;
        private ListBox historyList;
        private PictureBox historyPreview;
        private Label historyStatus;
        private List<HistoryItem> historyItems = new List<HistoryItem>();
        private Bitmap original;
        private Bitmap working;
        private Bitmap preview;
        private readonly List<Bitmap> undo = new List<Bitmap>();
        private readonly List<Bitmap> redo = new List<Bitmap>();
        private readonly List<Button> proButtons = new List<Button>();
        private Label editorLicenseValue;
        private Button colorButton;
        private ToolTip toolbarTip;
        private string tool = "Pen";
        private Color color = Color.FromArgb(220, 45, 45);
        private int stroke = 4;
        private int nextStepNumber = 1;
        private bool drawing;
        private bool penChanged;
        private Point start;
        private Point last;

        public EditorForm(Bitmap image)
        {
            Text = "SnipCopy Editor";
            Icon = Program.AppIcon;
            int maxWidth = Screen.PrimaryScreen.WorkingArea.Width - 80;
            int maxHeight = Screen.PrimaryScreen.WorkingArea.Height - 80;
            Width = Math.Min(maxWidth, Math.Max(720, image.Width + 48));
            Height = Math.Min(maxHeight, Math.Max(520, image.Height + 104));
            MinimumSize = new Size(620, 420);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);
            KeyPreview = true;
            toolbarTip = new ToolTip();

            tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;
            Controls.Add(tabs);

            var editPage = new TabPage("Edit");
            editPage.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(editPage);

            var historyPage = new TabPage("History");
            historyPage.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(historyPage);

            var settingsPage = new TabPage("Settings");
            settingsPage.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(settingsPage);

            var toolbar = new TableLayoutPanel();
            toolbar.Height = 86;
            toolbar.Dock = DockStyle.Top;
            toolbar.BackColor = Color.White;
            toolbar.Padding = new Padding(8, 8, 8, 8);
            toolbar.ColumnCount = 8;
            toolbar.RowCount = 2;
            toolbar.GrowStyle = TableLayoutPanelGrowStyle.FixedSize;
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            toolbar.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            for (int i = 0; i < toolbar.ColumnCount; i++)
            {
                toolbar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 12.5f));
            }
            editPage.Controls.Add(toolbar);

            scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = Color.FromArgb(230, 234, 240);
            editPage.Controls.Add(scroll);

            canvas = new PictureBox();
            canvas.Left = 16;
            canvas.Top = 16;
            canvas.Width = image.Width;
            canvas.Height = image.Height;
            canvas.SizeMode = PictureBoxSizeMode.Normal;
            canvas.BackColor = Color.White;
            scroll.Controls.Add(canvas);

            colorButton = MakeToolbarButton("", delegate { ChooseColor(); });
            colorButton.BackColor = color;
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.FlatAppearance.BorderColor = Color.FromArgb(90, 98, 110);
            toolbar.Controls.Add(colorButton, 0, 0);
            toolbar.Controls.Add(MakeToolbarButton("Pen", delegate { tool = "Pen"; }), 1, 0);
            toolbar.Controls.Add(MakeToolbarButton("Arrow", delegate { tool = "Arrow"; }), 2, 0);
            toolbar.Controls.Add(MakeToolbarButton("Text", delegate { tool = "Text"; }), 3, 0);
            var undoButton = MakeToolbarButton("\u21B6", delegate { Undo(); });
            undoButton.Font = new Font("Segoe UI Symbol", 13, FontStyle.Regular);
            toolbarTip.SetToolTip(undoButton, "Undo (Ctrl+Z)");
            toolbar.Controls.Add(undoButton, 4, 0);

            var redoButton = MakeToolbarButton("\u21B7", delegate { Redo(); });
            redoButton.Font = new Font("Segoe UI Symbol", 13, FontStyle.Regular);
            toolbarTip.SetToolTip(redoButton, "Redo (Ctrl+Y)");
            toolbar.Controls.Add(redoButton, 5, 0);
            toolbar.Controls.Add(MakeToolbarButton("Copy", delegate { CopyCurrent(); }), 6, 0);
            toolbar.Controls.Add(MakeToolbarButton("Save", delegate { SaveCurrent(); }), 7, 0);
            AddProButton(toolbar, "Blur", 0);
            AddProButton(toolbar, "Redact", 2);
            AddProButton(toolbar, "Steps", 4);
            AddResetButton(toolbar, 6);

            original = new Bitmap(image);
            working = new Bitmap(image);
            SetCanvasImage();

            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseUp += CanvasMouseUp;

            BuildHistoryTab(historyPage);
            BuildSettingsTab(settingsPage);
            RefreshEditorHistory();
            RefreshLicenseState();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Y)
            {
                Redo();
                e.SuppressKeyPress = true;
                return;
            }

            base.OnKeyDown(e);
        }

        private static Button MakeToolbarButton(string text, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Dock = DockStyle.Fill;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.System;
            button.Click += click;
            return button;
        }

        private void AddProButton(TableLayoutPanel toolbar, string feature, int column)
        {
            var button = MakeToolbarButton(feature + " Pro", delegate { SelectProTool(feature); });
            button.Tag = feature;
            button.UseVisualStyleBackColor = false;
            proButtons.Add(button);
            toolbar.Controls.Add(button, column, 1);
            toolbar.SetColumnSpan(button, 2);
            RefreshProButton(button);
        }

        private void RefreshProButton(Button button)
        {
            string feature = button.Tag as string ?? "Pro";
            bool isPro = Program.CanUseProTools;
            button.ForeColor = isPro ? SystemColors.ControlText : Color.FromArgb(95, 99, 108);
            button.BackColor = isPro ? SystemColors.Control : Color.FromArgb(238, 241, 245);
            toolbarTip.SetToolTip(button, isPro ? feature + " Pro is active" : feature + " requires a SavedCode Pro license");
        }

        internal void RefreshLicenseState()
        {
            foreach (Button button in proButtons)
            {
                RefreshProButton(button);
            }

            if (editorLicenseValue != null)
            {
                editorLicenseValue.Text = Program.License == null ? "Free" : Program.License.StatusText;
            }
        }

        private void AddResetButton(TableLayoutPanel toolbar, int column)
        {
            var button = MakeToolbarButton("Reset", delegate { ResetImage(); });
            toolbarTip.SetToolTip(button, "Reset this edit back to the original snip");
            toolbar.Controls.Add(button, column, 1);
            toolbar.SetColumnSpan(button, 2);
        }

        private void SelectProTool(string feature)
        {
            if (Program.CanUseProTools)
            {
                tool = feature;
                tabs.SelectedIndex = 0;
                return;
            }

            if (MessageBox.Show(this, feature + " is a SnipCopy Pro feature. Open Settings / About to activate Pro?", "SnipCopy Pro", MessageBoxButtons.YesNo, MessageBoxIcon.Information) == DialogResult.Yes)
            {
                Program.ShowSettings();
                RefreshLicenseState();
            }
        }

        private void BuildHistoryTab(TabPage page)
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(10);
            layout.ColumnCount = 2;
            layout.RowCount = 2;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            page.Controls.Add(layout);

            historyList = new ListBox();
            historyList.Dock = DockStyle.Fill;
            historyList.Font = new Font("Segoe UI", 9);
            historyList.SelectedIndexChanged += delegate { RefreshHistoryPreview(); };
            layout.Controls.Add(historyList, 0, 0);

            historyPreview = new PictureBox();
            historyPreview.Dock = DockStyle.Fill;
            historyPreview.BackColor = Color.White;
            historyPreview.BorderStyle = BorderStyle.FixedSingle;
            historyPreview.SizeMode = PictureBoxSizeMode.Zoom;
            layout.Controls.Add(historyPreview, 1, 0);

            historyStatus = new Label();
            historyStatus.Dock = DockStyle.Fill;
            historyStatus.Font = new Font("Segoe UI", 9);
            historyStatus.Padding = new Padding(0, 8, 8, 0);
            layout.Controls.Add(historyStatus, 0, 1);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 10, 0, 0);
            layout.Controls.Add(actions, 1, 1);

            actions.Controls.Add(MakeHistoryButton("Load in Editor", delegate { LoadHistorySelected(); }));
            actions.Controls.Add(MakeHistoryButton("Copy", delegate { CopyHistorySelected(); }));
            actions.Controls.Add(MakeHistoryButton("Save As", delegate { SaveHistorySelectedAs(); }));
            actions.Controls.Add(MakeHistoryButton("Delete", delegate { DeleteHistorySelected(); }));
        }

        private void BuildSettingsTab(TabPage page)
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(245, 247, 250);
            page.Controls.Add(panel);

            var title = new Label();
            title.Text = "SnipCopy";
            title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            title.Left = 18;
            title.Top = 18;
            title.Width = 420;
            title.Height = 34;
            panel.Controls.Add(title);

            var version = new Label();
            version.Text = "Version " + Program.AppVersion;
            version.Font = new Font("Segoe UI", 9);
            version.Left = 21;
            version.Top = 56;
            version.Width = 420;
            version.Height = 20;
            panel.Controls.Add(version);

            var licenseLabel = new Label();
            licenseLabel.Text = "License";
            licenseLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            licenseLabel.Left = 21;
            licenseLabel.Top = 98;
            licenseLabel.Width = 120;
            licenseLabel.Height = 22;
            panel.Controls.Add(licenseLabel);

            editorLicenseValue = new Label();
            editorLicenseValue.Font = new Font("Segoe UI", 10);
            editorLicenseValue.Left = 145;
            editorLicenseValue.Top = 97;
            editorLicenseValue.Width = 340;
            editorLicenseValue.Height = 24;
            panel.Controls.Add(editorLicenseValue);

            var activate = MakeSettingsTabButton("Activate License", 21, 142, delegate { ActivateLicenseFromEditor(); });
            panel.Controls.Add(activate);

            var sync = MakeSettingsTabButton("Sync License", 166, 142, delegate { SyncLicenseFromEditor(); });
            panel.Controls.Add(sync);

            var deactivate = MakeSettingsTabButton("Deactivate", 311, 142, delegate { DeactivateLicenseFromEditor(); });
            panel.Controls.Add(deactivate);

            var note = new Label();
            note.Text = "Pro unlocks Blur, Redact, Steps, and expanded history after activation.";
            note.Font = new Font("Segoe UI", 9);
            note.ForeColor = Color.FromArgb(85, 92, 104);
            note.Left = 21;
            note.Top = 198;
            note.Width = 560;
            note.Height = 24;
            panel.Controls.Add(note);
        }

        private static Button MakeSettingsTabButton(string text, int x, int y, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Left = x;
            button.Top = y;
            button.Width = 126;
            button.Height = 30;
            button.FlatStyle = FlatStyle.System;
            button.Click += click;
            return button;
        }

        private void ActivateLicenseFromEditor()
        {
            string email = Prompt.Show("Email used for purchase:", "Activate SnipCopy");
            if (String.IsNullOrEmpty(email)) return;

            string key = Prompt.Show("License key:", "Activate SnipCopy");
            if (String.IsNullOrEmpty(key)) return;

            string message;
            if (LicenseStore.Activate(email, key, out message))
            {
                RefreshLicenseState();
                Program.RefreshLicenseMenuText();
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SyncLicenseFromEditor()
        {
            string message;
            if (LicenseStore.Sync(out message))
            {
                RefreshLicenseState();
                Program.RefreshLicenseMenuText();
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeactivateLicenseFromEditor()
        {
            if (Program.License == null || String.IsNullOrEmpty(Program.License.Key)) return;

            if (MessageBox.Show(this, "Deactivate SnipCopy Pro on this device?", "SnipCopy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            LicenseStore.Deactivate();
            RefreshLicenseState();
            Program.RefreshLicenseMenuText();
        }

        private static Button MakeHistoryButton(string text, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 110;
            button.Height = 30;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.System;
            button.Click += click;
            return button;
        }

        private void CanvasMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            drawing = true;
            penChanged = false;
            start = e.Location;
            last = e.Location;

            if (tool == "Text")
            {
                drawing = false;
                string text = Prompt.Show("Text to add:", "SnipCopy Text");
                if (!String.IsNullOrEmpty(text))
                {
                    PushUndo();
                    using (Graphics g = Graphics.FromImage(working))
                    using (var font = new Font("Segoe UI", 18, FontStyle.Bold))
                    using (var brush = new SolidBrush(color))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawString(text, font, brush, e.Location);
                    }
                    SetCanvasImage();
                }
            }
            else if (tool == "Steps")
            {
                drawing = false;
                PushUndo();
                using (Graphics g = Graphics.FromImage(working))
                {
                    DrawStep(g, e.Location, nextStepNumber);
                }
                nextStepNumber++;
                SetCanvasImage();
            }
        }

        private void CanvasMouseMove(object sender, MouseEventArgs e)
        {
            if (!drawing) return;

            if (tool == "Pen")
            {
                if (!penChanged)
                {
                    PushUndo();
                    penChanged = true;
                }

                using (Graphics g = Graphics.FromImage(working))
                using (var pen = new Pen(color, stroke))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    pen.StartCap = LineCap.Round;
                    pen.EndCap = LineCap.Round;
                    g.DrawLine(pen, last, e.Location);
                }
                last = e.Location;
                SetCanvasImage();
            }
            else if (tool == "Arrow")
            {
                if (preview != null) preview.Dispose();
                preview = new Bitmap(working);
                using (Graphics g = Graphics.FromImage(preview))
                {
                    DrawArrow(g, start, e.Location);
                }
                SetCanvasImage();
            }
            else if (tool == "Redact")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (preview != null) preview.Dispose();
                preview = new Bitmap(working);
                using (Graphics g = Graphics.FromImage(preview))
                {
                    DrawRedaction(g, rect, true);
                }
                SetCanvasImage();
            }
            else if (tool == "Blur")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (preview != null) preview.Dispose();
                preview = new Bitmap(working);
                using (Graphics g = Graphics.FromImage(preview))
                {
                    DrawSelectionPreview(g, rect, "Blur");
                }
                SetCanvasImage();
            }
        }

        private void CanvasMouseUp(object sender, MouseEventArgs e)
        {
            if (!drawing) return;
            drawing = false;

            if (tool == "Arrow")
            {
                if (start == e.Location)
                {
                    if (preview != null)
                    {
                        preview.Dispose();
                        preview = null;
                        SetCanvasImage();
                    }
                    return;
                }
                PushUndo();
                using (Graphics g = Graphics.FromImage(working))
                {
                    DrawArrow(g, start, e.Location);
                }
                if (preview != null)
                {
                    preview.Dispose();
                    preview = null;
                }
                SetCanvasImage();
            }
            else if (tool == "Pen")
            {
            }
            else if (tool == "Redact")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (rect.Width < 3 || rect.Height < 3)
                {
                    ClearPreview();
                    return;
                }

                PushUndo();
                using (Graphics g = Graphics.FromImage(working))
                {
                    DrawRedaction(g, rect, false);
                }
                ClearPreview();
                SetCanvasImage();
            }
            else if (tool == "Blur")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (rect.Width < 3 || rect.Height < 3)
                {
                    ClearPreview();
                    return;
                }

                PushUndo();
                ApplyBlur(working, rect);
                ClearPreview();
                SetCanvasImage();
            }
        }

        private void DrawArrow(Graphics g, Point from, Point to)
        {
            using (var pen = new Pen(color, stroke))
            using (var arrowCap = new AdjustableArrowCap(stroke * 1.55f, stroke * 2.15f, true))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                pen.StartCap = LineCap.Round;
                pen.CustomEndCap = arrowCap;
                g.DrawLine(pen, from, to);
            }
        }

        private void DrawRedaction(Graphics g, Rectangle rect, bool isPreview)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (var brush = new SolidBrush(isPreview ? Color.FromArgb(210, color) : color))
            {
                g.FillRectangle(brush, rect);
            }

            if (isPreview)
            {
                using (var pen = new Pen(Color.White, 2))
                {
                    g.DrawRectangle(pen, rect);
                }
            }
        }

        private void DrawSelectionPreview(Graphics g, Rectangle rect, string label)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (var fill = new SolidBrush(Color.FromArgb(40, 36, 132, 255)))
            using (var pen = new Pen(Color.FromArgb(36, 132, 255), 2))
            using (var font = new Font("Segoe UI", 10, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.FromArgb(22, 52, 78)))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillRectangle(fill, rect);
                g.DrawRectangle(pen, rect);
                g.DrawString(label, font, textBrush, rect.Left + 6, rect.Top + 6);
            }
        }

        private void DrawStep(Graphics g, Point center, int number)
        {
            const int diameter = 30;
            var rect = new Rectangle(center.X - diameter / 2, center.Y - diameter / 2, diameter, diameter);
            rect = ClipToCanvas(rect);
            if (rect.Width < 12 || rect.Height < 12) return;

            using (var fill = new SolidBrush(color))
            using (var outline = new Pen(Color.White, 3))
            using (var font = new Font("Segoe UI", 12, FontStyle.Bold))
            using (var textBrush = new SolidBrush(Color.White))
            using (var format = new StringFormat())
            {
                format.Alignment = StringAlignment.Center;
                format.LineAlignment = StringAlignment.Center;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.FillEllipse(fill, rect);
                g.DrawEllipse(outline, rect);
                g.DrawString(number.ToString(), font, textBrush, rect, format);
            }
        }

        private void ApplyBlur(Bitmap bitmap, Rectangle rect)
        {
            rect = ClipToCanvas(rect);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            int smallWidth = Math.Max(1, rect.Width / 12);
            int smallHeight = Math.Max(1, rect.Height / 12);

            using (var small = new Bitmap(smallWidth, smallHeight, PixelFormat.Format32bppArgb))
            using (Graphics smallGraphics = Graphics.FromImage(small))
            {
                smallGraphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                smallGraphics.DrawImage(bitmap, new Rectangle(0, 0, smallWidth, smallHeight), rect, GraphicsUnit.Pixel);

                using (Graphics g = Graphics.FromImage(bitmap))
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(small, rect);
                }
            }
        }

        private void ClearPreview()
        {
            if (preview == null) return;
            preview.Dispose();
            preview = null;
        }

        private Rectangle ClipToCanvas(Rectangle rect)
        {
            Rectangle bounds = new Rectangle(0, 0, working.Width, working.Height);
            return Rectangle.Intersect(bounds, rect);
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            return new Rectangle(x, y, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private void PushUndo()
        {
            undo.Add(new Bitmap(working));
            ClearRedo();
            if (undo.Count > 20)
            {
                undo[0].Dispose();
                undo.RemoveAt(0);
            }
        }

        private void AddUndoSnapshot(Bitmap snapshot)
        {
            undo.Add(snapshot);
            if (undo.Count > 20)
            {
                undo[0].Dispose();
                undo.RemoveAt(0);
            }
        }

        private void Undo()
        {
            if (undo.Count == 0) return;
            redo.Add(new Bitmap(working));
            working.Dispose();
            int index = undo.Count - 1;
            working = undo[index];
            undo.RemoveAt(index);
            SetCanvasImage();
        }

        private void Redo()
        {
            if (redo.Count == 0) return;
            AddUndoSnapshot(new Bitmap(working));
            working.Dispose();
            int index = redo.Count - 1;
            working = redo[index];
            redo.RemoveAt(index);
            SetCanvasImage();
        }

        private void ResetImage()
        {
            if (original == null || working == null) return;
            PushUndo();
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }
            working.Dispose();
            working = new Bitmap(original);
            nextStepNumber = 1;
            SetCanvasImage();
            tabs.SelectedIndex = 0;
        }

        private void ClearRedo()
        {
            foreach (Bitmap item in redo) item.Dispose();
            redo.Clear();
        }

        private void ChooseColor()
        {
            using (var dialog = new ColorDialog())
            {
                dialog.Color = color;
                dialog.FullOpen = true;
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    color = dialog.Color;
                    colorButton.BackColor = color;
                }
            }
        }

        private void CopyCurrent()
        {
            Clipboard.SetImage(working);
            if (Program.LastImage != null) Program.LastImage.Dispose();
            Program.LastImage = new Bitmap(working);
            Program.SaveLastImage(Program.LastImage);
            Program.ShowToast("Copied", "Edited snip copied to clipboard");
            RefreshEditorHistory();
        }

        private void SaveCurrent()
        {
            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG image (*.png)|*.png";
                dialog.FileName = "snip.png";
                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    working.Save(dialog.FileName, ImageFormat.Png);
                }
            }
        }

        private void RefreshEditorHistory()
        {
            if (historyList == null) return;

            string selectedPath = null;
            HistoryItem selected = SelectedHistoryItem;
            if (selected != null) selectedPath = selected.Path;

            historyItems = HistoryStore.GetItems();
            historyList.BeginUpdate();
            historyList.Items.Clear();
            foreach (HistoryItem item in historyItems)
            {
                historyList.Items.Add(item.DisplayName);
            }
            historyList.EndUpdate();

            if (historyItems.Count == 0)
            {
                RefreshHistoryPreview();
                return;
            }

            int selectedIndex = 0;
            if (!String.IsNullOrEmpty(selectedPath))
            {
                for (int i = 0; i < historyItems.Count; i++)
                {
                    if (String.Equals(historyItems[i].Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
            historyList.SelectedIndex = selectedIndex;
        }

        private HistoryItem SelectedHistoryItem
        {
            get
            {
                if (historyList == null) return null;
                if (historyList.SelectedIndex < 0 || historyList.SelectedIndex >= historyItems.Count) return null;
                return historyItems[historyList.SelectedIndex];
            }
        }

        private void RefreshHistoryPreview()
        {
            if (historyPreview != null && historyPreview.Image != null)
            {
                historyPreview.Image.Dispose();
                historyPreview.Image = null;
            }

            HistoryItem item = SelectedHistoryItem;
            if (item == null)
            {
                if (historyStatus != null) historyStatus.Text = Program.IsPro ? "No snips yet." : "No snips yet. Free keeps the last 5 snips.";
                return;
            }

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    historyPreview.Image = new Bitmap(bitmap);
                    historyStatus.Text = bitmap.Width + " x " + bitmap.Height + Environment.NewLine + item.DisplayName;
                }
            }
            catch
            {
                historyStatus.Text = "Could not load this snip.";
            }
        }

        private void LoadHistorySelected()
        {
            HistoryItem item = SelectedHistoryItem;
            if (item == null) return;

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    LoadImage(bitmap);
                    tabs.SelectedIndex = 0;
                }
            }
            catch
            {
                MessageBox.Show(this, "Could not open this history snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void CopyHistorySelected()
        {
            HistoryItem item = SelectedHistoryItem;
            if (item == null) return;

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    Clipboard.SetImage(bitmap);
                }
                Program.ShowToast("Copied", "History snip copied to clipboard");
            }
            catch
            {
                MessageBox.Show(this, "Could not copy this snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveHistorySelectedAs()
        {
            HistoryItem item = SelectedHistoryItem;
            if (item == null) return;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG image (*.png)|*.png";
                dialog.FileName = Path.GetFileName(item.Path);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.Copy(item.Path, dialog.FileName, true);
                }
                catch
                {
                    MessageBox.Show(this, "Could not save this snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteHistorySelected()
        {
            HistoryItem item = SelectedHistoryItem;
            if (item == null) return;

            if (MessageBox.Show(this, "Delete this snip from history?", "SnipCopy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            HistoryStore.Delete(item.Path);
            Program.RefreshHistoryMenuText();
            RefreshEditorHistory();
        }

        internal void LoadImage(Bitmap image)
        {
            if (preview != null)
            {
                preview.Dispose();
                preview = null;
            }

            foreach (Bitmap item in undo) item.Dispose();
            foreach (Bitmap item in redo) item.Dispose();
            undo.Clear();
            redo.Clear();

            Bitmap next = new Bitmap(image);
            if (original != null) original.Dispose();
            original = new Bitmap(image);
            working.Dispose();
            working = next;
            nextStepNumber = 1;
            canvas.Width = working.Width;
            canvas.Height = working.Height;
            SetCanvasImage();
            tabs.SelectedIndex = 0;
            RefreshEditorHistory();
        }

        internal void RefreshHistoryFromStore()
        {
            RefreshEditorHistory();
        }

        private void SetCanvasImage()
        {
            if (canvas.Image != null) canvas.Image.Dispose();
            canvas.Image = new Bitmap(preview ?? working);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (canvas.Image != null) canvas.Image.Dispose();
            if (preview != null) preview.Dispose();
            if (historyPreview != null && historyPreview.Image != null) historyPreview.Image.Dispose();
            if (toolbarTip != null) toolbarTip.Dispose();
            foreach (Bitmap item in undo) item.Dispose();
            foreach (Bitmap item in redo) item.Dispose();
            if (original != null) original.Dispose();
            working.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class SettingsForm : Form
    {
        private Label licenseValue;

        public SettingsForm()
        {
            Text = "SnipCopy Settings";
            Icon = Program.AppIcon;
            ClientSize = new Size(460, 280);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);

            var title = new Label();
            title.Text = "SnipCopy";
            title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            title.Left = 18;
            title.Top = 16;
            title.Width = 400;
            title.Height = 34;
            Controls.Add(title);

            var version = new Label();
            version.Text = "Version " + Program.AppVersion;
            version.Font = new Font("Segoe UI", 9);
            version.Left = 21;
            version.Top = 54;
            version.Width = 400;
            version.Height = 20;
            Controls.Add(version);

            var licenseLabel = new Label();
            licenseLabel.Text = "License";
            licenseLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            licenseLabel.Left = 21;
            licenseLabel.Top = 96;
            licenseLabel.Width = 120;
            licenseLabel.Height = 22;
            Controls.Add(licenseLabel);

            licenseValue = new Label();
            licenseValue.Font = new Font("Segoe UI", 10);
            licenseValue.Left = 145;
            licenseValue.Top = 95;
            licenseValue.Width = 270;
            licenseValue.Height = 24;
            Controls.Add(licenseValue);

            var activate = MakeButton("Activate License", 21, 140, delegate { ActivateLicense(); });
            Controls.Add(activate);

            var sync = MakeButton("Sync License", 166, 140, delegate { SyncLicense(); });
            Controls.Add(sync);

            var deactivate = MakeButton("Deactivate", 311, 140, delegate { DeactivateLicense(); });
            Controls.Add(deactivate);

            var close = MakeButton("Close", 311, 228, delegate { Close(); });
            Controls.Add(close);

            RefreshStatus();
        }

        private static Button MakeButton(string text, int x, int y, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Left = x;
            button.Top = y;
            button.Width = 126;
            button.Height = 30;
            button.FlatStyle = FlatStyle.System;
            button.Click += click;
            return button;
        }

        private void ActivateLicense()
        {
            string email = Prompt.Show("Email used for purchase:", "Activate SnipCopy");
            if (String.IsNullOrEmpty(email)) return;

            string key = Prompt.Show("License key:", "Activate SnipCopy");
            if (String.IsNullOrEmpty(key)) return;

            string message;
            if (LicenseStore.Activate(email, key, out message))
            {
                RefreshStatus();
                Program.RefreshLicenseMenuText();
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SyncLicense()
        {
            string message;
            if (LicenseStore.Sync(out message))
            {
                RefreshStatus();
                Program.RefreshLicenseMenuText();
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeactivateLicense()
        {
            if (Program.License == null || String.IsNullOrEmpty(Program.License.Key)) return;

            if (MessageBox.Show(this, "Deactivate SnipCopy Pro on this device?", "SnipCopy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            LicenseStore.Deactivate();
            RefreshStatus();
            Program.RefreshLicenseMenuText();
        }

        private void RefreshStatus()
        {
            licenseValue.Text = Program.License == null ? "Free" : Program.License.StatusText;
        }
    }

    internal sealed class HistoryForm : Form
    {
        private readonly ListBox list;
        private readonly PictureBox preview;
        private readonly Label status;
        private List<HistoryItem> items = new List<HistoryItem>();

        public HistoryForm()
        {
            Text = "SnipCopy History";
            Icon = Program.AppIcon;
            Width = 860;
            Height = 560;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);

            var header = new Label();
            header.Text = Program.IsPro ? "Screenshot History" : "Screenshot History - Free keeps the last 5 snips";
            header.Font = new Font("Segoe UI", 12, FontStyle.Bold);
            header.Left = 14;
            header.Top = 12;
            header.Width = 800;
            header.Height = 26;
            Controls.Add(header);

            list = new ListBox();
            list.Left = 14;
            list.Top = 48;
            list.Width = 240;
            list.Height = 408;
            list.Font = new Font("Segoe UI", 9);
            list.SelectedIndexChanged += delegate { RefreshPreview(); };
            Controls.Add(list);

            preview = new PictureBox();
            preview.Left = 270;
            preview.Top = 48;
            preview.Width = 560;
            preview.Height = 408;
            preview.BackColor = Color.White;
            preview.BorderStyle = BorderStyle.FixedSingle;
            preview.SizeMode = PictureBoxSizeMode.Zoom;
            Controls.Add(preview);

            status = new Label();
            status.Left = 14;
            status.Top = 466;
            status.Width = 520;
            status.Height = 24;
            status.Font = new Font("Segoe UI", 9);
            Controls.Add(status);

            Controls.Add(MakeButton("Copy", 270, 494, delegate { CopySelected(); }));
            Controls.Add(MakeButton("Open Editor", 376, 494, delegate { OpenSelectedInEditor(); }));
            Controls.Add(MakeButton("Save As", 482, 494, delegate { SaveSelectedAs(); }));
            Controls.Add(MakeButton("Delete", 588, 494, delegate { DeleteSelected(); }));
            Controls.Add(MakeButton("Close", 694, 494, delegate { Close(); }));

            LoadItems();
        }

        private static Button MakeButton(string text, int x, int y, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Left = x;
            button.Top = y;
            button.Width = 94;
            button.Height = 30;
            button.FlatStyle = FlatStyle.System;
            button.Click += click;
            return button;
        }

        private void LoadItems()
        {
            items = HistoryStore.GetItems();
            list.Items.Clear();
            foreach (HistoryItem item in items)
            {
                list.Items.Add(item.DisplayName);
            }

            if (items.Count > 0)
            {
                list.SelectedIndex = 0;
            }
            else
            {
                RefreshPreview();
            }
        }

        private HistoryItem SelectedItem
        {
            get
            {
                if (list.SelectedIndex < 0 || list.SelectedIndex >= items.Count) return null;
                return items[list.SelectedIndex];
            }
        }

        private void RefreshPreview()
        {
            if (preview.Image != null)
            {
                preview.Image.Dispose();
                preview.Image = null;
            }

            HistoryItem item = SelectedItem;
            if (item == null)
            {
                status.Text = "No snips yet.";
                return;
            }

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    preview.Image = new Bitmap(bitmap);
                    status.Text = bitmap.Width + " x " + bitmap.Height + " - " + item.Path;
                }
            }
            catch
            {
                status.Text = "Could not load this snip.";
            }
        }

        private void CopySelected()
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    Clipboard.SetImage(bitmap);
                }
                Program.ShowToast("Copied", "History snip copied to clipboard");
            }
            catch
            {
                MessageBox.Show(this, "Could not copy this snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenSelectedInEditor()
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;

            try
            {
                using (var bitmap = new Bitmap(item.Path))
                {
                    Program.OpenEditor(bitmap);
                }
            }
            catch
            {
                MessageBox.Show(this, "Could not open this snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SaveSelectedAs()
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;

            using (var dialog = new SaveFileDialog())
            {
                dialog.Filter = "PNG image (*.png)|*.png";
                dialog.FileName = Path.GetFileName(item.Path);
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                try
                {
                    File.Copy(item.Path, dialog.FileName, true);
                }
                catch
                {
                    MessageBox.Show(this, "Could not save this snip.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        private void DeleteSelected()
        {
            HistoryItem item = SelectedItem;
            if (item == null) return;

            if (MessageBox.Show(this, "Delete this snip from history?", "SnipCopy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            HistoryStore.Delete(item.Path);
            LoadItems();
            Program.RefreshHistoryMenuText();
            Program.RefreshOpenEditorHistory();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (preview.Image != null) preview.Image.Dispose();
            base.OnFormClosed(e);
        }
    }

    internal sealed class Prompt : Form
    {
        private readonly TextBox textBox;
        private string result = "";

        private Prompt(string message, string title)
        {
            Text = title;
            Icon = Program.AppIcon;
            Width = 420;
            Height = 140;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;

            var label = new Label();
            label.Text = message;
            label.Left = 12;
            label.Top = 12;
            label.Width = 380;
            Controls.Add(label);

            textBox = new TextBox();
            textBox.Left = 12;
            textBox.Top = 36;
            textBox.Width = 380;
            Controls.Add(textBox);

            var ok = new Button();
            ok.Text = "OK";
            ok.Left = 236;
            ok.Top = 70;
            ok.Width = 75;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 317;
            cancel.Top = 70;
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal static string Show(string message, string title)
        {
            using (var prompt = new Prompt(message, title))
            {
                if (prompt.ShowDialog() == DialogResult.OK)
                {
                    prompt.result = prompt.textBox.Text;
                }
                return prompt.result;
            }
        }
    }
}
