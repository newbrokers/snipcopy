using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using DrawingImageFormat = System.Drawing.Imaging.ImageFormat;
using System.IO;
using System.Numerics;
using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Forms;
#if SDK_RECORDING
using ScreenRecorderLib;
#endif

namespace SnipCopy
{
    static class Program
    {
        internal const string AppVersion = "0.2.0";
        internal static LicenseInfo License;
        internal static ShortcutConfig Shortcuts;
#if SDK_RECORDING
        internal static RecordingAudioConfig RecordingAudio;
#endif
        internal static Bitmap LastImage;
        internal static string LastImagePath;
        internal static bool OpenEditorAfterSnip;
        internal static NotifyIcon TrayIcon;
        internal static Icon AppIcon;
        private static ToolStripItem snipItem;
        private static ToolStripItem editorItem;
        private static ToolStripMenuItem licenseItem;
        private static ToolStripMenuItem historyItem;
        private static DateTime toastActionUntilUtc = DateTime.MinValue;
        private static ToastAction pendingToastAction = ToastAction.None;
        private static EditorForm editorWindow;
        private static HotkeyWindow hotkeyWindow;
        private static HotkeyWindow editorHotkeyWindow;
#if SDK_RECORDING
        private static HotkeyWindow recordHotkeyWindow;
        private static ToolStripItem recordItem;
#endif
        private const int SnipHotkeyId = 9182;
        private const int EditorHotkeyId = 9184;
#if SDK_RECORDING
        private const int RecordHotkeyId = 9183;
#endif

        private enum ToastAction
        {
            None,
            OpenEditor,
#if SDK_RECORDING
            OpenRecordTab
#endif
        }

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new ApplicationContext();
            License = LicenseStore.Load();
            Shortcuts = ShortcutStore.Load();
#if SDK_RECORDING
            RecordingAudio = RecordingAudioStore.Load();
#endif
            HistoryStore.ImportLegacyTempHistory();
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? CreateAppIcon();
            TrayIcon = new NotifyIcon();
            TrayIcon.Text = "SnipCopy";
            TrayIcon.Icon = AppIcon;
            TrayIcon.Visible = true;
            TrayIcon.BalloonTipClicked += delegate { HandleToastClick(); };

            var menu = new ContextMenuStrip();
            snipItem = menu.Items.Add("");
#if SDK_RECORDING
            recordItem = menu.Items.Add("");
#endif
            editorItem = menu.Items.Add("");
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
            RefreshShortcutMenuText();
            RefreshLicenseMenuText();

            snipItem.Click += delegate { StartSnip(); };
#if SDK_RECORDING
            recordItem.Click += delegate { StartRecordingSelection(); };
#endif
            editorItem.Click += delegate { OpenEditorOrBlank(); };
            historyItem.Click += delegate { ShowHistory(); };
            autoEditorItem.CheckedChanged += delegate { OpenEditorAfterSnip = autoEditorItem.Checked; };
            settingsItem.Click += delegate { ShowSettings(); };
            exitItem.Click += delegate { context.ExitThread(); };
            TrayIcon.ContextMenuStrip = menu;
            TrayIcon.DoubleClick += delegate { StartSnip(); };

            hotkeyWindow = new HotkeyWindow(SnipHotkeyId);
            hotkeyWindow.HotkeyPressed += delegate { StartSnip(); };
            bool registered = RegisterConfiguredHotKey(hotkeyWindow, SnipHotkeyId, Shortcuts.Snip);

            editorHotkeyWindow = new HotkeyWindow(EditorHotkeyId);
            editorHotkeyWindow.HotkeyPressed += delegate { OpenEditorOrBlank(); };
            bool editorRegistered = RegisterConfiguredHotKey(editorHotkeyWindow, EditorHotkeyId, Shortcuts.Editor);

#if SDK_RECORDING
            recordHotkeyWindow = new HotkeyWindow(RecordHotkeyId);
            recordHotkeyWindow.HotkeyPressed += delegate { StartRecordingSelection(); };
            bool recordRegistered = RegisterConfiguredHotKey(recordHotkeyWindow, RecordHotkeyId, Shortcuts.Record);
#endif

            string startupMessage =
#if SDK_RECORDING
                BuildHotkeyMessage(registered, recordRegistered, editorRegistered);
#else
                BuildHotkeyMessage(registered, editorRegistered);
#endif
            ShowToast("SnipCopy is running", startupMessage, ToastAction.OpenEditor);

            context.ThreadExit += delegate
            {
                NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, SnipHotkeyId);
                NativeMethods.UnregisterHotKey(editorHotkeyWindow.Handle, EditorHotkeyId);
#if SDK_RECORDING
                NativeMethods.UnregisterHotKey(recordHotkeyWindow.Handle, RecordHotkeyId);
#endif
                hotkeyWindow.Dispose();
                editorHotkeyWindow.Dispose();
#if SDK_RECORDING
                recordHotkeyWindow.Dispose();
#endif
                TrayIcon.Visible = false;
                TrayIcon.Dispose();
                AppIcon.Dispose();
                if (LastImage != null) LastImage.Dispose();
            };

            Application.Run(context);
        }

        private static string BuildHotkeyMessage(bool snipRegistered, bool editorRegistered)
        {
            if (snipRegistered && editorRegistered) return Shortcuts.Snip.DisplayText + " snips. " + Shortcuts.Editor.DisplayText + " opens the editor.";
            if (snipRegistered) return Shortcuts.Snip.DisplayText + " snips. Editor hotkey failed.";
            if (editorRegistered) return "Snip hotkey failed. " + Shortcuts.Editor.DisplayText + " opens the editor.";
            return "Hotkey registration failed. Use the tray menu.";
        }

#if SDK_RECORDING
        private static string BuildHotkeyMessage(bool snipRegistered, bool recordRegistered, bool editorRegistered)
        {
            if (snipRegistered && recordRegistered && editorRegistered)
            {
                return Shortcuts.Snip.DisplayText + " snips. " + Shortcuts.Record.DisplayText + " records. " + Shortcuts.Editor.DisplayText + " opens the editor.";
            }

            var failed = new List<string>();
            if (!snipRegistered) failed.Add("snip");
            if (!recordRegistered) failed.Add("record");
            if (!editorRegistered) failed.Add("editor");
            return "Some hotkeys failed: " + String.Join(", ", failed.ToArray()) + ". Use the tray menu.";
        }
#endif

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

        internal static void RefreshShortcutMenuText()
        {
            if (Shortcuts == null) Shortcuts = ShortcutStore.Load();
            if (snipItem != null) snipItem.Text = "New snip    " + Shortcuts.Snip.DisplayText;
            if (editorItem != null) editorItem.Text = "Open editor    " + Shortcuts.Editor.DisplayText;
#if SDK_RECORDING
            if (recordItem != null) recordItem.Text = "Record area    " + Shortcuts.Record.DisplayText;
#endif
        }

        internal static void RefreshOpenEditorShortcuts()
        {
            if (editorWindow == null || editorWindow.IsDisposed) return;
            editorWindow.RefreshShortcutState();
        }

        internal static bool UpdateShortcuts(ShortcutConfig next, out string message)
        {
            if (next == null) next = ShortcutConfig.Defaults();
            string validation;
            if (!next.Validate(out validation))
            {
                message = validation;
                return false;
            }

            ShortcutConfig previous = Shortcuts == null ? ShortcutConfig.Defaults() : Shortcuts.Clone();
            UnregisterConfiguredHotkeys();

            string failed;
            if (!TryRegisterConfiguredHotkeys(next, out failed))
            {
                UnregisterConfiguredHotkeys();
                TryRegisterConfiguredHotkeys(previous, out failed);
                message = "Could not register " + failed + ". Another app may already use that shortcut.";
                return false;
            }

            Shortcuts = next.Clone();
            ShortcutStore.Save(Shortcuts);
            RefreshShortcutMenuText();
            RefreshOpenEditorShortcuts();
            message = "Shortcuts updated.";
            return true;
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

#if SDK_RECORDING
        internal static void StartRecordingSelection()
        {
            if (RecordingManager.HasSession)
            {
                RecordingManager.ShowControls();
                return;
            }
            if (CaptureOverlay.IsOpen) return;
            using (var screenshot = CaptureScreen())
            {
                var overlay = new CaptureOverlay(screenshot);
                if (overlay.ShowDialog() == DialogResult.OK && overlay.SelectedScreenBounds.Width > 0)
                {
                    RecordingManager.Start(overlay.SelectedScreenBounds);
                }
                if (overlay.CapturedImage != null) overlay.CapturedImage.Dispose();
            }
        }
#endif

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

        internal static void OpenEditorOrBlank()
        {
            using (Bitmap seed = CreateEditorSeedBitmap())
            {
                OpenEditor(seed);
            }
        }

        private static Bitmap CreateEditorSeedBitmap()
        {
            if (LastImage != null) return new Bitmap(LastImage);

            foreach (HistoryItem item in HistoryStore.GetItems())
            {
                try
                {
                    return new Bitmap(item.Path);
                }
                catch
                {
                }
            }

            var bitmap = new Bitmap(640, 360, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bitmap))
            using (var brush = new SolidBrush(Color.FromArgb(230, 234, 240)))
            using (var textBrush = new SolidBrush(Color.FromArgb(65, 75, 92)))
            using (var font = new Font("Segoe UI", 12, FontStyle.Regular))
            {
                g.Clear(Color.White);
                g.FillRectangle(brush, 0, 0, bitmap.Width, bitmap.Height);
                g.DrawString("No snip captured yet.", font, textBrush, new PointF(24, 24));
            }
            return bitmap;
        }

#if SDK_RECORDING
        internal static void OpenEditorRecordTab()
        {
            if (editorWindow != null && !editorWindow.IsDisposed)
            {
                if (editorWindow.WindowState == FormWindowState.Minimized)
                {
                    editorWindow.WindowState = FormWindowState.Normal;
                }
                editorWindow.Show();
                editorWindow.SelectRecordTab();
                editorWindow.Activate();
                return;
            }

            OpenEditorOrBlank();

            if (editorWindow != null && !editorWindow.IsDisposed)
            {
                editorWindow.SelectRecordTab();
                editorWindow.Activate();
            }
        }
#endif

        internal static void RefreshOpenEditorHistory()
        {
            if (editorWindow == null || editorWindow.IsDisposed) return;
            editorWindow.RefreshHistoryFromStore();
        }

#if SDK_RECORDING
        internal static void RefreshOpenEditorRecordings()
        {
            if (editorWindow == null || editorWindow.IsDisposed) return;
            editorWindow.RefreshRecordingsFromStore();
        }

        internal static void UpdateRecordingAudio(bool systemAudio, bool microphone)
        {
            RecordingAudio = new RecordingAudioConfig
            {
                SystemAudio = systemAudio,
                Microphone = microphone
            };
            RecordingAudioStore.Save(RecordingAudio);
        }
#endif

        internal static void SaveLastImage(Bitmap bitmap)
        {
            string dir = HistoryStore.GetHistoryDirectory();
            string path = Path.Combine(dir, "snip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".png");
            bitmap.Save(path, DrawingImageFormat.Png);
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
            ShowToast(title, text, openEditorOnClick ? ToastAction.OpenEditor : ToastAction.None);
        }

        private static void ShowToast(string title, string text, ToastAction action)
        {
            if (TrayIcon == null) return;
            pendingToastAction = action;
            toastActionUntilUtc = action == ToastAction.None ? DateTime.MinValue : DateTime.UtcNow.AddMinutes(5);
            TrayIcon.BalloonTipTitle = title;
            TrayIcon.BalloonTipText = text;
            TrayIcon.ShowBalloonTip(1500);
        }

#if SDK_RECORDING
        internal static void ShowRecordingSavedToast(string path)
        {
            ShowToast("Recording saved", Path.GetFileName(path), ToastAction.OpenRecordTab);
        }

        internal static void ShowRecordingLimitToast()
        {
            ShowToast("Free limit reached", "Recording saved. Pro unlocks unlimited recording.", ToastAction.OpenRecordTab);
        }
#endif

        private static void HandleToastClick()
        {
            if (DateTime.UtcNow > toastActionUntilUtc) return;

            ToastAction action = pendingToastAction;
            pendingToastAction = ToastAction.None;
            toastActionUntilUtc = DateTime.MinValue;

            if (action == ToastAction.OpenEditor)
            {
                OpenEditorOrBlank();
                return;
            }

#if SDK_RECORDING
            if (action == ToastAction.OpenRecordTab)
            {
                OpenEditorRecordTab();
            }
#endif
        }

        private static bool RegisterConfiguredHotKey(HotkeyWindow window, int id, HotkeySpec shortcut)
        {
            if (window == null || shortcut == null || !shortcut.IsValid) return false;
            return NativeMethods.RegisterHotKey(window.Handle, id, shortcut.Modifiers, (uint)shortcut.Key);
        }

        private static void UnregisterConfiguredHotkeys()
        {
            if (hotkeyWindow != null) NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, SnipHotkeyId);
            if (editorHotkeyWindow != null) NativeMethods.UnregisterHotKey(editorHotkeyWindow.Handle, EditorHotkeyId);
#if SDK_RECORDING
            if (recordHotkeyWindow != null) NativeMethods.UnregisterHotKey(recordHotkeyWindow.Handle, RecordHotkeyId);
#endif
        }

        private static bool TryRegisterConfiguredHotkeys(ShortcutConfig config, out string failed)
        {
            failed = "";
            if (!RegisterConfiguredHotKey(hotkeyWindow, SnipHotkeyId, config.Snip))
            {
                failed = config.Snip.DisplayText;
                return false;
            }

            if (!RegisterConfiguredHotKey(editorHotkeyWindow, EditorHotkeyId, config.Editor))
            {
                failed = config.Editor.DisplayText;
                return false;
            }

#if SDK_RECORDING
            if (!RegisterConfiguredHotKey(recordHotkeyWindow, RecordHotkeyId, config.Record))
            {
                failed = config.Record.DisplayText;
                return false;
            }
#endif

            return true;
        }
    }

    internal static class NativeMethods
    {
        internal const uint MOD_ALT = 0x0001;
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
        private readonly int hotkeyId;
        public event EventHandler HotkeyPressed;

        public HotkeyWindow(int id)
        {
            hotkeyId = id;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_HOTKEY && (int)m.WParam == hotkeyId && HotkeyPressed != null)
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

    internal sealed class HotkeySpec
    {
        public uint Modifiers;
        public Keys Key;

        public bool IsValid
        {
            get { return Modifiers != 0 && Key != Keys.None && !IsModifierKey(Key); }
        }

        public string DisplayText
        {
            get
            {
                var parts = new List<string>();
                if ((Modifiers & NativeMethods.MOD_CONTROL) != 0) parts.Add("Ctrl");
                if ((Modifiers & NativeMethods.MOD_SHIFT) != 0) parts.Add("Shift");
                if ((Modifiers & NativeMethods.MOD_ALT) != 0) parts.Add("Alt");
                parts.Add(KeyText(Key));
                return String.Join("+", parts.ToArray());
            }
        }

        internal HotkeySpec Clone()
        {
            return new HotkeySpec { Modifiers = Modifiers, Key = Key };
        }

        internal static bool TryFromKeyEvent(KeyEventArgs e, out HotkeySpec shortcut, out string message)
        {
            uint modifiers = 0;
            if (e.Control) modifiers |= NativeMethods.MOD_CONTROL;
            if (e.Shift) modifiers |= NativeMethods.MOD_SHIFT;
            if (e.Alt) modifiers |= NativeMethods.MOD_ALT;

            shortcut = new HotkeySpec { Modifiers = modifiers, Key = e.KeyCode };
            if (!shortcut.IsValid)
            {
                message = "Press at least one modifier plus a normal key, for example Ctrl+Shift+W.";
                return false;
            }

            message = "";
            return true;
        }

        internal static bool TryParse(string text, out HotkeySpec shortcut)
        {
            shortcut = null;
            if (String.IsNullOrWhiteSpace(text)) return false;

            uint modifiers = 0;
            Keys key = Keys.None;
            string[] parts = text.Split(new[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (string rawPart in parts)
            {
                string part = rawPart.Trim();
                if (String.Equals(part, "Ctrl", StringComparison.OrdinalIgnoreCase) || String.Equals(part, "Control", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_CONTROL;
                    continue;
                }

                if (String.Equals(part, "Shift", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_SHIFT;
                    continue;
                }

                if (String.Equals(part, "Alt", StringComparison.OrdinalIgnoreCase))
                {
                    modifiers |= NativeMethods.MOD_ALT;
                    continue;
                }

                Keys parsed;
                if (TryParseKey(part, out parsed)) key = parsed;
            }

            var result = new HotkeySpec { Modifiers = modifiers, Key = key };
            if (!result.IsValid) return false;
            shortcut = result;
            return true;
        }

        private static bool TryParseKey(string text, out Keys key)
        {
            key = Keys.None;
            if (String.IsNullOrWhiteSpace(text)) return false;

            string part = text.Trim();
            if (part.Length == 1)
            {
                char ch = Char.ToUpperInvariant(part[0]);
                if (ch >= 'A' && ch <= 'Z')
                {
                    key = (Keys)Enum.Parse(typeof(Keys), ch.ToString());
                    return true;
                }

                if (ch >= '0' && ch <= '9')
                {
                    key = (Keys)Enum.Parse(typeof(Keys), "D" + ch);
                    return true;
                }
            }

            if (String.Equals(part, "Esc", StringComparison.OrdinalIgnoreCase))
            {
                key = Keys.Escape;
                return true;
            }

            object parsed;
            try
            {
                parsed = Enum.Parse(typeof(Keys), part, true);
            }
            catch
            {
                return false;
            }

            key = (Keys)parsed;
            return !IsModifierKey(key);
        }

        private static bool IsModifierKey(Keys key)
        {
            return key == Keys.ControlKey
                || key == Keys.ShiftKey
                || key == Keys.Menu
                || key == Keys.LControlKey
                || key == Keys.RControlKey
                || key == Keys.LShiftKey
                || key == Keys.RShiftKey
                || key == Keys.LMenu
                || key == Keys.RMenu;
        }

        private static string KeyText(Keys key)
        {
            if (key >= Keys.A && key <= Keys.Z) return key.ToString();
            if (key >= Keys.D0 && key <= Keys.D9) return ((int)(key - Keys.D0)).ToString();
            if (key == Keys.Escape) return "Esc";
            return key.ToString();
        }
    }

    internal sealed class ShortcutConfig
    {
        internal const string SnipAction = "snip";
        internal const string RecordAction = "record";
        internal const string EditorAction = "editor";

        public HotkeySpec Snip;
        public HotkeySpec Record;
        public HotkeySpec Editor;

        internal static ShortcutConfig Defaults()
        {
            HotkeySpec snip;
            HotkeySpec record;
            HotkeySpec editor;
            HotkeySpec.TryParse("Ctrl+Shift+S", out snip);
            HotkeySpec.TryParse("Ctrl+Shift+R", out record);
            HotkeySpec.TryParse("Ctrl+E", out editor);
            return new ShortcutConfig { Snip = snip, Record = record, Editor = editor };
        }

        internal ShortcutConfig Clone()
        {
            return new ShortcutConfig
            {
                Snip = Snip == null ? null : Snip.Clone(),
                Record = Record == null ? null : Record.Clone(),
                Editor = Editor == null ? null : Editor.Clone()
            };
        }

        internal HotkeySpec Get(string action)
        {
            if (String.Equals(action, SnipAction, StringComparison.OrdinalIgnoreCase)) return Snip;
            if (String.Equals(action, RecordAction, StringComparison.OrdinalIgnoreCase)) return Record;
            if (String.Equals(action, EditorAction, StringComparison.OrdinalIgnoreCase)) return Editor;
            return null;
        }

        internal void Set(string action, HotkeySpec shortcut)
        {
            if (String.Equals(action, SnipAction, StringComparison.OrdinalIgnoreCase)) Snip = shortcut;
            if (String.Equals(action, RecordAction, StringComparison.OrdinalIgnoreCase)) Record = shortcut;
            if (String.Equals(action, EditorAction, StringComparison.OrdinalIgnoreCase)) Editor = shortcut;
        }

        internal bool Validate(out string message)
        {
            if (Snip == null || !Snip.IsValid || Editor == null || !Editor.IsValid || Record == null || !Record.IsValid)
            {
                message = "Each shortcut needs at least one modifier plus a normal key.";
                return false;
            }

            if (Same(Snip, Editor) || Same(Snip, Record) || Same(Editor, Record))
            {
                message = "Each action needs a different shortcut.";
                return false;
            }

            message = "";
            return true;
        }

        private static bool Same(HotkeySpec a, HotkeySpec b)
        {
            if (a == null || b == null) return false;
            return a.Modifiers == b.Modifiers && a.Key == b.Key;
        }
    }

    internal static class ShortcutStore
    {
        internal static ShortcutConfig Load()
        {
            ShortcutConfig config = ShortcutConfig.Defaults();
            string path = GetPath();
            if (!File.Exists(path)) return config;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string action = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    HotkeySpec shortcut;
                    if (HotkeySpec.TryParse(value, out shortcut))
                    {
                        config.Set(action, shortcut);
                    }
                }

                bool migrated = MigrateOldDefaults(config);

                string message;
                if (!config.Validate(out message)) return ShortcutConfig.Defaults();
                if (migrated) Save(config);
                return config;
            }
            catch
            {
                return ShortcutConfig.Defaults();
            }
        }

        internal static void Save(ShortcutConfig config)
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            lines.Add(ShortcutConfig.SnipAction + "=" + config.Snip.DisplayText);
            lines.Add(ShortcutConfig.RecordAction + "=" + config.Record.DisplayText);
            lines.Add(ShortcutConfig.EditorAction + "=" + config.Editor.DisplayText);
            File.WriteAllLines(path, lines.ToArray());
        }

        private static bool MigrateOldDefaults(ShortcutConfig config)
        {
            bool migrated = false;
            HotkeySpec oldSnipCtrlS;
            HotkeySpec oldSnipCtrlC;
            HotkeySpec oldRecordCtrlR;
            HotkeySpec.TryParse("Ctrl+S", out oldSnipCtrlS);
            HotkeySpec.TryParse("Ctrl+C", out oldSnipCtrlC);
            HotkeySpec.TryParse("Ctrl+R", out oldRecordCtrlR);

            if (Matches(config.Snip, oldSnipCtrlS) || Matches(config.Snip, oldSnipCtrlC))
            {
                config.Snip = ShortcutConfig.Defaults().Snip;
                migrated = true;
            }

            if (Matches(config.Record, oldRecordCtrlR))
            {
                config.Record = ShortcutConfig.Defaults().Record;
                migrated = true;
            }

            return migrated;
        }

        private static bool Matches(HotkeySpec current, HotkeySpec expected)
        {
            if (current == null || expected == null) return false;
            return current.Modifiers == expected.Modifiers && current.Key == expected.Key;
        }

        private static string GetPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnipCopy");
            return Path.Combine(dir, "shortcuts.ini");
        }
    }

#if SDK_RECORDING
    internal sealed class RecordingAudioConfig
    {
        public int Version = 2;
        public bool SystemAudio;
        public bool Microphone;
        public bool LoadedFromFile;

        public bool HasAudio
        {
            get { return SystemAudio || Microphone; }
        }

        public string Summary
        {
            get
            {
                if (SystemAudio && Microphone) return "system audio + microphone";
                if (SystemAudio) return "system audio";
                if (Microphone) return "microphone";
                return "no audio";
            }
        }

        internal static RecordingAudioConfig Defaults()
        {
            return new RecordingAudioConfig { Version = 2, SystemAudio = true, Microphone = false };
        }
    }

    internal static class RecordingAudioStore
    {
        internal static RecordingAudioConfig Load()
        {
            var config = RecordingAudioConfig.Defaults();
            string path = GetPath();
            if (!File.Exists(path)) return config;

            try
            {
                bool sawSystemAudio = false;
                bool sawMicrophone = false;
                bool sawVersion = false;
                foreach (string line in File.ReadAllLines(path))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;

                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();
                    bool enabled = ParseBoolean(value);

                    if (String.Equals(key, "version", StringComparison.OrdinalIgnoreCase))
                    {
                        int version;
                        if (Int32.TryParse(value, out version)) config.Version = version;
                        sawVersion = true;
                    }
                    else if (String.Equals(key, "system_audio", StringComparison.OrdinalIgnoreCase))
                    {
                        config.SystemAudio = enabled;
                        sawSystemAudio = true;
                    }
                    else if (String.Equals(key, "microphone", StringComparison.OrdinalIgnoreCase))
                    {
                        config.Microphone = enabled;
                        sawMicrophone = true;
                    }
                }

                config.LoadedFromFile = true;

                if (!sawVersion && sawSystemAudio && sawMicrophone && !config.SystemAudio && !config.Microphone)
                {
                    config.SystemAudio = true;
                    config.Version = 2;
                    Save(config);
                }
                else if (!sawSystemAudio)
                {
                    config.SystemAudio = RecordingAudioConfig.Defaults().SystemAudio;
                    config.Version = 2;
                    Save(config);
                }
                else if (!sawVersion)
                {
                    config.Version = 2;
                    Save(config);
                }
            }
            catch
            {
                return RecordingAudioConfig.Defaults();
            }

            return config;
        }

        internal static void Save(RecordingAudioConfig config)
        {
            if (config == null) config = RecordingAudioConfig.Defaults();

            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            lines.Add("version=" + config.Version.ToString());
            lines.Add("system_audio=" + config.SystemAudio.ToString());
            lines.Add("microphone=" + config.Microphone.ToString());
            File.WriteAllLines(path, lines.ToArray());
        }

        private static bool ParseBoolean(string value)
        {
            if (String.IsNullOrWhiteSpace(value)) return false;
            if (String.Equals(value, "1", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(value, "yes", StringComparison.OrdinalIgnoreCase)) return true;
            if (String.Equals(value, "true", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string GetPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SnipCopy");
            return Path.Combine(dir, "recording.ini");
        }
    }

    internal static class RecordingManager
    {
        private static readonly TimeSpan FreeRecordingLimit = TimeSpan.FromMinutes(5);
        private static Recorder recorder;
        private static RecordingControlForm controls;
        private static string recordingPath = "";
        private static bool recordingStarted;
        private static bool freeLimitReached;
        private static bool stopping;

        internal static bool IsRecording
        {
            get { return recorder != null && recordingStarted && !stopping; }
        }

        internal static bool HasSession
        {
            get { return recorder != null; }
        }

        internal static void Start(Rectangle region)
        {
            if (HasSession)
            {
                ShowControls();
                return;
            }

            region = NormalizeRegion(region);
            if (region.Width < 4 || region.Height < 4)
            {
                MessageBox.Show("Select a larger area to record.", "SnipCopy Recording", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            Screen screen = FindContainingScreen(region);
            if (screen == null)
            {
                MessageBox.Show("For this first recorder build, select an area that stays inside one monitor.", "SnipCopy Recording", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string folder = RecordingStore.GetRecordingDirectory();
            Directory.CreateDirectory(folder);
            recordingPath = Path.Combine(folder, "record-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".mp4");
            freeLimitReached = false;

            var source = new DisplayRecordingSource(screen.DeviceName);
            source.RecorderApi = RecorderApi.DesktopDuplication;
            source.IsCursorCaptureEnabled = true;
            source.SourceRect = new ScreenRect(
                region.Left - screen.Bounds.Left,
                region.Top - screen.Bounds.Top,
                region.Right - screen.Bounds.Left,
                region.Bottom - screen.Bounds.Top);
            source.OutputSize = new ScreenSize(region.Width, region.Height);
            source.Stretch = StretchMode.None;

            var options = new RecorderOptions();
            options.SourceOptions = new SourceOptions
            {
                RecordingSources = new List<RecordingSourceBase> { source }
            };
            options.OutputOptions = new OutputOptions
            {
                RecorderMode = RecorderMode.Video,
                OutputFrameSize = new ScreenSize(region.Width, region.Height),
                Stretch = StretchMode.None
            };
            RecordingAudioConfig audio = Program.RecordingAudio ?? RecordingAudioConfig.Defaults();
            options.AudioOptions = new AudioOptions
            {
                IsAudioEnabled = audio.HasAudio,
                IsOutputDeviceEnabled = audio.SystemAudio,
                IsInputDeviceEnabled = audio.Microphone,
                AudioOutputDevice = "",
                AudioInputDevice = ""
            };
            options.MouseOptions = new MouseOptions
            {
                MouseClickDetectionMode = MouseDetectionMode.Polling
            };
            options.VideoEncoderOptions = new VideoEncoderOptions
            {
                Encoder = new H264VideoEncoder
                {
                    EncoderProfile = H264Profile.High,
                    BitrateMode = H264BitrateControlMode.CBR
                },
                Framerate = 30,
                Bitrate = CalculateBitrate(region),
                IsHardwareEncodingEnabled = true,
                IsFixedFramerate = true,
                IsMp4FastStartEnabled = true
            };

            try
            {
                stopping = false;
                recordingStarted = false;
                recorder = Recorder.CreateRecorder(options);
                recorder.OnRecordingComplete += Recorder_OnRecordingComplete;
                recorder.OnRecordingFailed += Recorder_OnRecordingFailed;
                recorder.OnStatusChanged += Recorder_OnStatusChanged;

                TimeSpan recordingLimit = Program.IsPro ? TimeSpan.Zero : FreeRecordingLimit;
                controls = new RecordingControlForm(region, recordingPath, audio, recordingLimit);
                controls.StartRequested += delegate { BeginRecording(); };
                controls.PauseRequested += delegate { TogglePause(); };
                controls.LimitReached += delegate { StopForFreeLimit(); };
                controls.StopRequested += delegate { Stop(); };
                controls.FormClosed += delegate
                {
                    if (!recordingStarted && recorder != null)
                    {
                        CleanupRecorder();
                    }

                    if (controls != null && !IsRecording)
                    {
                        controls = null;
                    }
                };
                controls.Show();

                try
                {
                    Recorder.SetExcludeFromCapture(controls.Handle, true);
                }
                catch
                {
                }
            }
            catch (Exception ex)
            {
                CleanupRecorder();
                SafeCloseControls();
                MessageBox.Show("Could not prepare recording." + Environment.NewLine + ex.Message, "SnipCopy Recording", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void BeginRecording()
        {
            if (recorder == null || recordingStarted || stopping) return;

            try
            {
                recordingStarted = true;
                if (controls != null) controls.MarkStarting();
                recorder.Record(recordingPath);
                if (controls != null) controls.MarkRecording();
            }
            catch (Exception ex)
            {
                CleanupRecorder();
                SafeCloseControls();
                MessageBox.Show("Could not start recording." + Environment.NewLine + ex.Message, "SnipCopy Recording", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void Stop()
        {
            if (recorder == null) return;

            if (!recordingStarted)
            {
                CleanupRecorder();
                stopping = false;
                recordingPath = "";
                SafeCloseControls();
                return;
            }

            stopping = true;
            if (controls != null) controls.MarkStopping();
            try
            {
                recorder.Stop();
            }
            catch (Exception ex)
            {
                Finish("", "Could not stop recording. " + ex.Message);
            }
        }

        internal static void StopForFreeLimit()
        {
            if (recorder == null || !recordingStarted || stopping) return;
            freeLimitReached = true;
            Stop();
        }

        internal static void TogglePause()
        {
            if (recorder == null || !recordingStarted || stopping) return;
            try
            {
                if (recorder.Status == RecorderStatus.Paused)
                {
                    recorder.Resume();
                    if (controls != null) controls.MarkRecording();
                    return;
                }

                if (recorder.Status == RecorderStatus.Recording)
                {
                    recorder.Pause();
                    if (controls != null) controls.MarkPaused();
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not pause or resume recording." + Environment.NewLine + ex.Message, "SnipCopy Recording", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        internal static void ShowControls()
        {
            if (controls == null || controls.IsDisposed) return;
            controls.Show();
            if (controls.WindowState == FormWindowState.Minimized)
            {
                controls.WindowState = FormWindowState.Normal;
            }
            controls.Activate();
        }

        private static void Recorder_OnRecordingComplete(object sender, RecordingCompleteEventArgs e)
        {
            RunOnUiThread(delegate { Finish(String.IsNullOrEmpty(e.FilePath) ? recordingPath : e.FilePath, ""); });
        }

        private static void Recorder_OnRecordingFailed(object sender, RecordingFailedEventArgs e)
        {
            RunOnUiThread(delegate { Finish(e.FilePath, e.Error); });
        }

        private static void Recorder_OnStatusChanged(object sender, RecordingStatusEventArgs e)
        {
            RunOnUiThread(delegate
            {
                if (controls != null && !controls.IsDisposed)
                {
                    controls.SetRecorderStatus(e.Status.ToString());
                }
            });
        }

        private static void Finish(string path, string error)
        {
            CleanupRecorder();
            stopping = false;

            if (!String.IsNullOrEmpty(error))
            {
                if (controls != null && !controls.IsDisposed) controls.MarkFailed(error);
                Program.ShowToast("Recording failed", error);
                return;
            }

            if (String.IsNullOrEmpty(path)) path = recordingPath;
            if (controls != null && !controls.IsDisposed)
            {
                if (freeLimitReached)
                {
                    controls.MarkSaved(path, "Saved - Free limit reached");
                }
                else
                {
                    controls.MarkSaved(path);
                }
            }
            Program.RefreshOpenEditorRecordings();
            if (freeLimitReached)
            {
                Program.ShowRecordingLimitToast();
            }
            else
            {
                Program.ShowRecordingSavedToast(path);
            }
            freeLimitReached = false;
        }

        private static void CleanupRecorder()
        {
            if (recorder == null)
            {
                recordingStarted = false;
                return;
            }
            try
            {
                recorder.OnRecordingComplete -= Recorder_OnRecordingComplete;
                recorder.OnRecordingFailed -= Recorder_OnRecordingFailed;
                recorder.OnStatusChanged -= Recorder_OnStatusChanged;
                recorder.Dispose();
            }
            catch
            {
            }
            recorder = null;
            recordingStarted = false;
        }

        private static void SafeCloseControls()
        {
            if (controls == null) return;
            try
            {
                if (!controls.IsDisposed)
                {
                    controls.AllowClose();
                    controls.Close();
                }
            }
            catch
            {
            }
            controls = null;
        }

        private static void RunOnUiThread(Action action)
        {
            if (controls != null && !controls.IsDisposed && controls.IsHandleCreated)
            {
                controls.BeginInvoke(action);
                return;
            }
            action();
        }

        private static Screen FindContainingScreen(Rectangle region)
        {
            foreach (Screen screen in Screen.AllScreens)
            {
                if (screen.Bounds.Contains(region)) return screen;
            }
            return null;
        }

        private static Rectangle NormalizeRegion(Rectangle region)
        {
            int width = region.Width - (region.Width % 2);
            int height = region.Height - (region.Height % 2);
            return new Rectangle(region.Left, region.Top, width, height);
        }

        private static int CalculateBitrate(Rectangle region)
        {
            int suggested = region.Width * region.Height * 4;
            if (suggested < 2500000) return 2500000;
            if (suggested > 12000000) return 12000000;
            return suggested;
        }
    }

    internal sealed class RecordingControlForm : Form
    {
        private DateTime startedAtUtc = DateTime.MinValue;
        private readonly Timer timer;
        private readonly Label timeLabel;
        private readonly Label statusLabel;
        private readonly Label pathLabel;
        private readonly Button pauseButton;
        private readonly Button stopButton;
        private readonly Button folderButton;
        private readonly string folderPath;
        private readonly TimeSpan recordingLimit;
        private string savedPath = "";
        private TimeSpan pausedTotal = TimeSpan.Zero;
        private DateTime pausedAtUtc = DateTime.MinValue;
        private bool hasStarted;
        private bool timerPaused;
        private bool limitRaised;
        private bool canClose;

        internal event EventHandler StartRequested;
        internal event EventHandler PauseRequested;
        internal event EventHandler LimitReached;
        internal event EventHandler StopRequested;

        internal RecordingControlForm(Rectangle region, string path, RecordingAudioConfig audio, TimeSpan recordingLimit)
        {
            this.recordingLimit = recordingLimit;
            folderPath = Path.GetDirectoryName(path);
            if (String.IsNullOrEmpty(folderPath)) folderPath = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);

            Text = "SnipCopy Recording";
            Icon = Program.AppIcon;
            Width = 360;
            Height = 168;
            FormBorderStyle = FormBorderStyle.FixedToolWindow;
            StartPosition = FormStartPosition.Manual;
            TopMost = true;
            ShowInTaskbar = false;
            BackColor = Color.FromArgb(245, 247, 250);
            MaximizeBox = false;
            MinimizeBox = false;

            Screen screen = Screen.FromRectangle(region);
            Rectangle area = screen.WorkingArea;
            Left = Math.Max(area.Left + 12, area.Right - Width - 18);
            Top = Math.Max(area.Top + 12, area.Bottom - Height - 18);

            var title = new Label();
            title.Text = "Ready to record";
            title.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            title.Left = 16;
            title.Top = 14;
            title.Width = 160;
            title.Height = 24;
            Controls.Add(title);

            timeLabel = new Label();
            timeLabel.Text = "00:00";
            timeLabel.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            timeLabel.TextAlign = ContentAlignment.MiddleRight;
            timeLabel.Left = 212;
            timeLabel.Top = 8;
            timeLabel.Width = 112;
            timeLabel.Height = 34;
            Controls.Add(timeLabel);

            statusLabel = new Label();
            string audioText = audio == null ? "no audio" : audio.Summary;
            statusLabel.Text = region.Width + " x " + region.Height + " px selected - " + audioText + " - " + LimitText();
            statusLabel.Font = new Font("Segoe UI", 9);
            statusLabel.ForeColor = Color.FromArgb(65, 75, 92);
            statusLabel.Left = 17;
            statusLabel.Top = 45;
            statusLabel.Width = 310;
            statusLabel.Height = 22;
            Controls.Add(statusLabel);

            pathLabel = new Label();
            pathLabel.Text = CompactPath(path);
            pathLabel.Font = new Font("Segoe UI", 8);
            pathLabel.ForeColor = Color.FromArgb(85, 92, 104);
            pathLabel.Left = 17;
            pathLabel.Top = 68;
            pathLabel.Width = 310;
            pathLabel.Height = 22;
            Controls.Add(pathLabel);

            pauseButton = new Button();
            pauseButton.Text = "Start";
            pauseButton.Left = 17;
            pauseButton.Top = 100;
            pauseButton.Width = 88;
            pauseButton.Height = 28;
            pauseButton.Click += delegate
            {
                if (!hasStarted)
                {
                    if (StartRequested != null) StartRequested(this, EventArgs.Empty);
                    return;
                }

                if (PauseRequested != null) PauseRequested(this, EventArgs.Empty);
            };
            Controls.Add(pauseButton);

            stopButton = new Button();
            stopButton.Text = "Cancel";
            stopButton.Left = 114;
            stopButton.Top = 100;
            stopButton.Width = 88;
            stopButton.Height = 28;
            stopButton.Click += delegate
            {
                if (StopRequested != null) StopRequested(this, EventArgs.Empty);
            };
            Controls.Add(stopButton);

            folderButton = new Button();
            folderButton.Text = "Folder";
            folderButton.Left = 211;
            folderButton.Top = 100;
            folderButton.Width = 100;
            folderButton.Height = 28;
            folderButton.Enabled = true;
            folderButton.Click += delegate { OpenSavedLocation(); };
            Controls.Add(folderButton);

            timer = new Timer();
            timer.Interval = 500;
            timer.Tick += delegate { RefreshTimer(); };
        }

        internal void SetRecorderStatus(string status)
        {
            if (String.IsNullOrEmpty(status)) return;
            if (String.Equals(status, "Paused", StringComparison.OrdinalIgnoreCase))
            {
                MarkPaused();
                return;
            }

            if (String.Equals(status, "Recording", StringComparison.OrdinalIgnoreCase))
            {
                MarkRecording();
                return;
            }

            statusLabel.Text = status;
        }

        internal void MarkStarting()
        {
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            stopButton.Text = "Stop";
            statusLabel.Text = "Starting...";
        }

        internal void MarkPaused()
        {
            if (!hasStarted) return;
            if (!timerPaused)
            {
                timerPaused = true;
                pausedAtUtc = DateTime.UtcNow;
            }
            pauseButton.Text = "Resume";
            pauseButton.Enabled = true;
            stopButton.Enabled = true;
            statusLabel.Text = "Paused";
        }

        internal void MarkRecording()
        {
            if (!hasStarted)
            {
                hasStarted = true;
                canClose = false;
                startedAtUtc = DateTime.UtcNow;
                pausedTotal = TimeSpan.Zero;
                pausedAtUtc = DateTime.MinValue;
                timerPaused = false;
                timer.Start();
            }

            if (timerPaused)
            {
                pausedTotal = pausedTotal.Add(DateTime.UtcNow - pausedAtUtc);
                timerPaused = false;
                pausedAtUtc = DateTime.MinValue;
            }
            pauseButton.Text = "Pause";
            pauseButton.Enabled = true;
            stopButton.Enabled = true;
            stopButton.Text = "Stop";
            statusLabel.Text = "Recording";
        }

        internal void MarkStopping()
        {
            if (timerPaused)
            {
                pausedTotal = pausedTotal.Add(DateTime.UtcNow - pausedAtUtc);
                timerPaused = false;
                pausedAtUtc = DateTime.MinValue;
            }
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            folderButton.Enabled = true;
            statusLabel.Text = "Finishing MP4...";
        }

        internal void MarkSaved(string path)
        {
            MarkSaved(path, "Saved");
        }

        internal void MarkSaved(string path, string status)
        {
            savedPath = path;
            timer.Stop();
            canClose = true;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            folderButton.Enabled = true;
            statusLabel.Text = status;
            pathLabel.Text = CompactPath(path);
            Activate();
        }

        internal void MarkFailed(string error)
        {
            timer.Stop();
            canClose = true;
            pauseButton.Enabled = false;
            stopButton.Enabled = false;
            folderButton.Enabled = Directory.Exists(folderPath);
            statusLabel.Text = "Failed";
            pathLabel.Text = error;
            Activate();
        }

        internal void AllowClose()
        {
            canClose = true;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!hasStarted)
            {
                canClose = true;
            }

            if (!canClose)
            {
                e.Cancel = true;
                WindowState = FormWindowState.Minimized;
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            timer.Dispose();
            base.OnFormClosed(e);
        }

        private void RefreshTimer()
        {
            DateTime effectiveNow = timerPaused ? pausedAtUtc : DateTime.UtcNow;
            TimeSpan elapsed = effectiveNow - startedAtUtc - pausedTotal;
            if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
            timeLabel.Text = ((int)elapsed.TotalMinutes).ToString("00") + ":" + elapsed.Seconds.ToString("00");

            if (recordingLimit > TimeSpan.Zero && elapsed >= recordingLimit && !limitRaised)
            {
                limitRaised = true;
                timeLabel.Text = ((int)recordingLimit.TotalMinutes).ToString("00") + ":" + recordingLimit.Seconds.ToString("00");
                pauseButton.Enabled = false;
                stopButton.Enabled = false;
                statusLabel.Text = "Free limit reached. Saving...";
                if (LimitReached != null) LimitReached(this, EventArgs.Empty);
            }
        }

        private string LimitText()
        {
            if (recordingLimit <= TimeSpan.Zero) return "Pro unlimited";
            return "Free limit " + ((int)recordingLimit.TotalMinutes).ToString("0") + ":" + recordingLimit.Seconds.ToString("00");
        }

        private void OpenSavedLocation()
        {
            try
            {
                if (!String.IsNullOrEmpty(savedPath) && File.Exists(savedPath))
                {
                    Process.Start("explorer.exe", "/select,\"" + savedPath + "\"");
                    return;
                }

                if (!String.IsNullOrEmpty(folderPath) && Directory.Exists(folderPath))
                {
                    Process.Start("explorer.exe", "\"" + folderPath + "\"");
                }
            }
            catch
            {
            }
        }

        private static string CompactPath(string path)
        {
            if (String.IsNullOrEmpty(path)) return "";
            if (path.Length <= 45) return path;
            return "..." + path.Substring(path.Length - 42);
        }
    }
#endif

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
                    + "\"product_slug\":\"" + ProductSlug + "\","
                    + "\"machineHash\":\"" + MachineHash() + "\""
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

#if SDK_RECORDING
    internal sealed class RecordingItem
    {
        public string Path;
        public DateTime CreatedAt;
        public long SizeBytes;

        public string DisplayName
        {
            get { return CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"); }
        }

        public string SizeText
        {
            get
            {
                double size = SizeBytes;
                string[] units = { "B", "KB", "MB", "GB" };
                int unit = 0;
                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }
                return size.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
            }
        }

        public override string ToString()
        {
            return DisplayName;
        }
    }

    internal static class RecordingStore
    {
        internal static string GetRecordingDirectory()
        {
            string videos = Environment.GetFolderPath(Environment.SpecialFolder.MyVideos);
            if (String.IsNullOrEmpty(videos))
            {
                videos = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            }

            string dir = Path.Combine(videos, "SnipCopy");
            Directory.CreateDirectory(dir);
            return dir;
        }

        internal static List<RecordingItem> GetItems()
        {
            var items = new List<RecordingItem>();
            string dir = GetRecordingDirectory();
            foreach (string path in Directory.GetFiles(dir, "*.mp4"))
            {
                try
                {
                    var file = new FileInfo(path);
                    items.Add(new RecordingItem
                    {
                        Path = path,
                        CreatedAt = file.CreationTime,
                        SizeBytes = file.Length
                    });
                }
                catch
                {
                }
            }

            items.Sort(delegate(RecordingItem a, RecordingItem b)
            {
                return b.CreatedAt.CompareTo(a.CreatedAt);
            });
            return items;
        }

        internal static void Delete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                throw;
            }
        }
    }
#endif

    internal sealed class CaptureOverlay : Form
    {
        internal static bool IsOpen;
        internal Bitmap CapturedImage;
        private Rectangle selectedScreenBounds;
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

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            BringToFront();
            Activate();
            Focus();
        }

        internal Rectangle SelectedScreenBounds
        {
            get { return selectedScreenBounds; }
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
            selectedScreenBounds = ToScreenBounds(selection);

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
                CancelSelection();
                e.SuppressKeyPress = true;
            }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CancelSelection();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CancelSelection();
                return true;
            }

            return base.ProcessDialogKey(keyData);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            IsOpen = false;
            screenshot.Dispose();
            base.OnFormClosed(e);
        }

        private void CancelSelection()
        {
            selecting = false;
            selection = Rectangle.Empty;
            selectedScreenBounds = Rectangle.Empty;
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X);
            int y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X);
            int h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }

        private static Rectangle ToScreenBounds(Rectangle clientSelection)
        {
            Rectangle virtualScreen = SystemInformation.VirtualScreen;
            return new Rectangle(
                virtualScreen.Left + clientSelection.Left,
                virtualScreen.Top + clientSelection.Top,
                clientSelection.Width,
                clientSelection.Height);
        }
    }

    internal sealed class EditorForm : Form
    {
        private readonly TabControl tabs;
#if SDK_RECORDING
        private TabPage recordTab;
        private ListBox recordingList;
        private Label recordingStatus;
        private Label recordingPreviewTitle;
        private Label recordingPreviewMeta;
        private Label recordingPreviewPath;
        private Button recordingPreviewPlayButton;
        private Label recordShortcutLabel;
        private Label recordingLimitLabel;
        private CheckBox recordSystemAudioCheck;
        private CheckBox recordMicrophoneCheck;
        private List<RecordingItem> recordingItems = new List<RecordingItem>();
#endif
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
        private Label snipShortcutValue;
#if SDK_RECORDING
        private Label recordShortcutValue;
#endif
        private Label editorShortcutValue;
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
            int preferredWidth = Math.Max(756, (int)Math.Ceiling((image.Width + 48) * 1.05));
            Width = Math.Min(maxWidth, preferredWidth);
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

#if SDK_RECORDING
            recordTab = new TabPage("Record");
            recordTab.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(recordTab);
#endif

            var shortcutsPage = new TabPage("Shortcuts");
            shortcutsPage.BackColor = Color.FromArgb(245, 247, 250);
            tabs.TabPages.Add(shortcutsPage);

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

            scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = Color.FromArgb(230, 234, 240);
            editPage.Controls.Add(scroll);
            editPage.Controls.Add(toolbar);

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
            var saveButton = MakeToolbarButton("Save", delegate { SaveCurrent(); });
            StyleToolbarAccent(saveButton, Color.FromArgb(224, 247, 232), Color.FromArgb(78, 154, 98));
            toolbar.Controls.Add(saveButton, 7, 0);
            Button captureButton = AddFreeToolButton(toolbar, "Capture", 0, delegate { CaptureFromEditor(); });
            AddFreeToolButton(toolbar, "Crop", 1, delegate { tool = "Crop"; tabs.SelectedIndex = 0; });
            StyleToolbarAccent(captureButton, Color.FromArgb(224, 241, 255), Color.FromArgb(74, 145, 210));
            AddProButton(toolbar, "Blur", 2, 2);
            AddProButton(toolbar, "Redact", 4, 2);
            AddProButton(toolbar, "Steps", 6, 1);
            AddResetButton(toolbar, 7, 1);

            original = new Bitmap(image);
            working = new Bitmap(image);
            SetCanvasImage();

            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseUp += CanvasMouseUp;

            BuildHistoryTab(historyPage);
#if SDK_RECORDING
            BuildRecordTab(recordTab);
#endif
            BuildShortcutsTab(shortcutsPage);
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

#if SDK_RECORDING
        internal void SelectRecordTab()
        {
            if (recordTab != null)
            {
                RefreshRecordingsFromStore();
                tabs.SelectedTab = recordTab;
            }
        }
#endif

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

        private static void StyleToolbarAccent(Button button, Color background, Color border)
        {
            button.UseVisualStyleBackColor = false;
            button.BackColor = background;
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = border;
            button.FlatAppearance.MouseOverBackColor = ControlPaint.Light(background);
            button.FlatAppearance.MouseDownBackColor = ControlPaint.Dark(background);
        }

        private Button AddFreeToolButton(TableLayoutPanel toolbar, string text, int column, EventHandler click)
        {
            var button = MakeToolbarButton(text, click);
            toolbarTip.SetToolTip(button, text == "Capture" ? "Take a new snip" : "Crop the current snip");
            toolbar.Controls.Add(button, column, 1);
            return button;
        }

        private void AddProButton(TableLayoutPanel toolbar, string feature, int column, int span)
        {
            var button = MakeToolbarButton(feature + " Pro", delegate { SelectProTool(feature); });
            button.Tag = feature;
            button.UseVisualStyleBackColor = false;
            proButtons.Add(button);
            toolbar.Controls.Add(button, column, 1);
            toolbar.SetColumnSpan(button, span);
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

#if SDK_RECORDING
            RefreshRecordingLimitLabel();
#endif
        }

        private void AddResetButton(TableLayoutPanel toolbar, int column, int span)
        {
            var button = MakeToolbarButton("Reset", delegate { ResetImage(); });
            StyleToolbarAccent(button, Color.FromArgb(255, 232, 232), Color.FromArgb(210, 106, 106));
            toolbarTip.SetToolTip(button, "Reset this edit back to the original snip");
            toolbar.Controls.Add(button, column, 1);
            toolbar.SetColumnSpan(button, span);
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

        private void CaptureFromEditor()
        {
            WindowState = FormWindowState.Minimized;

            var timer = new Timer();
            timer.Interval = 150;
            timer.Tick += delegate
            {
                timer.Stop();
                timer.Dispose();

                bool previousOpenEditorAfterSnip = Program.OpenEditorAfterSnip;
                Program.OpenEditorAfterSnip = true;
                try
                {
                    Program.StartSnip();
                }
                finally
                {
                    Program.OpenEditorAfterSnip = previousOpenEditorAfterSnip;
                    if (!IsDisposed && WindowState == FormWindowState.Minimized)
                    {
                        WindowState = FormWindowState.Normal;
                        Show();
                        Activate();
                    }
                }
            };
            timer.Start();
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

#if SDK_RECORDING
        private void BuildRecordTab(TabPage page)
        {
            var layout = new TableLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.Padding = new Padding(10);
            layout.ColumnCount = 2;
            layout.RowCount = 3;
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 250));
            layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 132));
            layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            page.Controls.Add(layout);

            var header = new Panel();
            header.Dock = DockStyle.Fill;
            header.BackColor = Color.FromArgb(245, 247, 250);
            layout.Controls.Add(header, 0, 0);
            layout.SetColumnSpan(header, 2);

            var title = new Label();
            title.Text = "Region Recording";
            title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            title.Left = 12;
            title.Top = 10;
            title.Width = 420;
            title.Height = 38;
            header.Controls.Add(title);

            var description = new Label();
            description.Text = "Select an area to record. Finished MP4s appear here.";
            description.Font = new Font("Segoe UI", 10);
            description.ForeColor = Color.FromArgb(45, 57, 76);
            description.Left = 14;
            description.Top = 52;
            description.Width = 620;
            description.Height = 26;
            header.Controls.Add(description);

            var startButton = MakeSettingsTabButton("Record Area", 14, 88, delegate { Program.StartRecordingSelection(); });
            startButton.Width = 140;
            header.Controls.Add(startButton);

            recordShortcutLabel = new Label();
            recordShortcutLabel.Text = "Shortcut: " + Program.Shortcuts.Record.DisplayText;
            recordShortcutLabel.Font = new Font("Segoe UI", 9);
            recordShortcutLabel.ForeColor = Color.FromArgb(85, 92, 104);
            recordShortcutLabel.Left = 169;
            recordShortcutLabel.Top = 86;
            recordShortcutLabel.Width = 172;
            recordShortcutLabel.Height = 24;
            header.Controls.Add(recordShortcutLabel);

            RecordingAudioConfig audio = Program.RecordingAudio ?? RecordingAudioConfig.Defaults();

            recordSystemAudioCheck = new CheckBox();
            recordSystemAudioCheck.Text = "System audio";
            recordSystemAudioCheck.Checked = audio.SystemAudio;
            recordSystemAudioCheck.Font = new Font("Segoe UI", 9);
            recordSystemAudioCheck.ForeColor = Color.FromArgb(45, 57, 76);
            recordSystemAudioCheck.Left = 350;
            recordSystemAudioCheck.Top = 84;
            recordSystemAudioCheck.Width = 118;
            recordSystemAudioCheck.Height = 24;
            recordSystemAudioCheck.CheckedChanged += delegate { SaveRecordingAudioFromChecks(); };
            header.Controls.Add(recordSystemAudioCheck);

            recordMicrophoneCheck = new CheckBox();
            recordMicrophoneCheck.Text = "Microphone";
            recordMicrophoneCheck.Checked = audio.Microphone;
            recordMicrophoneCheck.Font = new Font("Segoe UI", 9);
            recordMicrophoneCheck.ForeColor = Color.FromArgb(45, 57, 76);
            recordMicrophoneCheck.Left = 475;
            recordMicrophoneCheck.Top = 84;
            recordMicrophoneCheck.Width = 112;
            recordMicrophoneCheck.Height = 24;
            recordMicrophoneCheck.CheckedChanged += delegate { SaveRecordingAudioFromChecks(); };
            header.Controls.Add(recordMicrophoneCheck);

            recordingLimitLabel = new Label();
            recordingLimitLabel.Text = RecordingLimitText();
            recordingLimitLabel.Font = new Font("Segoe UI", 8);
            recordingLimitLabel.ForeColor = Color.FromArgb(85, 92, 104);
            recordingLimitLabel.Left = 169;
            recordingLimitLabel.Top = 110;
            recordingLimitLabel.Width = 460;
            recordingLimitLabel.Height = 18;
            header.Controls.Add(recordingLimitLabel);

            recordingList = new ListBox();
            recordingList.Dock = DockStyle.Fill;
            recordingList.Font = new Font("Segoe UI", 9);
            recordingList.SelectedIndexChanged += delegate { RefreshRecordingDetails(); };
            layout.Controls.Add(recordingList, 0, 1);

            var details = new Panel();
            details.Dock = DockStyle.Fill;
            details.BackColor = Color.White;
            details.BorderStyle = BorderStyle.FixedSingle;
            layout.Controls.Add(details, 1, 1);

            var detailsLayout = new TableLayoutPanel();
            detailsLayout.Dock = DockStyle.Fill;
            detailsLayout.Padding = new Padding(18);
            detailsLayout.ColumnCount = 1;
            detailsLayout.RowCount = 4;
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));
            detailsLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 52));
            details.Controls.Add(detailsLayout);

            recordingPreviewTitle = new Label();
            recordingPreviewTitle.Dock = DockStyle.Fill;
            recordingPreviewTitle.Font = new Font("Segoe UI", 15, FontStyle.Bold);
            recordingPreviewTitle.ForeColor = Color.FromArgb(18, 30, 52);
            recordingPreviewTitle.Text = "No recording selected";
            recordingPreviewTitle.TextAlign = ContentAlignment.MiddleLeft;
            recordingPreviewTitle.AutoEllipsis = true;
            detailsLayout.Controls.Add(recordingPreviewTitle, 0, 0);

            var previewSurface = new Panel();
            previewSurface.Dock = DockStyle.Fill;
            previewSurface.BackColor = Color.White;
            previewSurface.BorderStyle = BorderStyle.None;
            previewSurface.Margin = new Padding(0, 8, 0, 10);
            detailsLayout.Controls.Add(previewSurface, 0, 1);

            recordingPreviewPlayButton = new Button();
            recordingPreviewPlayButton.Text = "Play Recording";
            recordingPreviewPlayButton.Width = 176;
            recordingPreviewPlayButton.Height = 44;
            recordingPreviewPlayButton.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            recordingPreviewPlayButton.FlatStyle = FlatStyle.Flat;
            recordingPreviewPlayButton.BackColor = Color.White;
            recordingPreviewPlayButton.FlatAppearance.BorderColor = Color.FromArgb(154, 172, 198);
            recordingPreviewPlayButton.Click += delegate { PlayRecordingSelected(); };
            previewSurface.Controls.Add(recordingPreviewPlayButton);
            previewSurface.Resize += delegate { CenterRecordingPreviewButton(previewSurface); };
            CenterRecordingPreviewButton(previewSurface);

            recordingPreviewMeta = new Label();
            recordingPreviewMeta.Dock = DockStyle.Fill;
            recordingPreviewMeta.Font = new Font("Segoe UI", 10);
            recordingPreviewMeta.ForeColor = Color.FromArgb(45, 57, 76);
            recordingPreviewMeta.TextAlign = ContentAlignment.MiddleLeft;
            recordingPreviewMeta.AutoEllipsis = true;
            detailsLayout.Controls.Add(recordingPreviewMeta, 0, 2);

            recordingPreviewPath = new Label();
            recordingPreviewPath.Dock = DockStyle.Fill;
            recordingPreviewPath.Font = new Font("Segoe UI", 9);
            recordingPreviewPath.ForeColor = Color.FromArgb(85, 92, 104);
            recordingPreviewPath.AutoEllipsis = true;
            detailsLayout.Controls.Add(recordingPreviewPath, 0, 3);

            recordingStatus = new Label();
            recordingStatus.Dock = DockStyle.Fill;
            recordingStatus.Font = new Font("Segoe UI", 9);
            recordingStatus.Padding = new Padding(0, 8, 8, 0);
            layout.Controls.Add(recordingStatus, 0, 2);

            var actions = new FlowLayoutPanel();
            actions.Dock = DockStyle.Fill;
            actions.FlowDirection = FlowDirection.LeftToRight;
            actions.WrapContents = false;
            actions.Padding = new Padding(0, 10, 0, 0);
            layout.Controls.Add(actions, 1, 2);

            actions.Controls.Add(MakeHistoryButton("Play", delegate { PlayRecordingSelected(); }));
            actions.Controls.Add(MakeHistoryButton("Open Folder", delegate { OpenRecordingFolderSelected(); }));
            actions.Controls.Add(MakeHistoryButton("Copy Path", delegate { CopyRecordingPathSelected(); }));
            actions.Controls.Add(MakeHistoryButton("Delete", delegate { DeleteRecordingSelected(); }));

            RefreshRecordingHistory();
        }

        private static void CenterRecordingPreviewButton(Panel previewSurface)
        {
            if (previewSurface == null || previewSurface.Controls.Count == 0) return;

            Control button = previewSurface.Controls[0];
            button.Left = Math.Max(12, (previewSurface.ClientSize.Width - button.Width) / 2);
            button.Top = Math.Max(12, (previewSurface.ClientSize.Height - button.Height) / 2);
        }

        private void SaveRecordingAudioFromChecks()
        {
            if (recordSystemAudioCheck == null || recordMicrophoneCheck == null) return;
            Program.UpdateRecordingAudio(recordSystemAudioCheck.Checked, recordMicrophoneCheck.Checked);
        }

        private void RefreshRecordingLimitLabel()
        {
            if (recordingLimitLabel == null) return;
            recordingLimitLabel.Text = RecordingLimitText();
        }

        private static string RecordingLimitText()
        {
            return Program.IsPro ? "Recording limit: unlimited with Pro" : "Free recording limit: 5:00. Pro unlocks unlimited recording.";
        }

        private static Label AddRecordingDetail(Panel panel, string labelText, int top)
        {
            var label = new Label();
            label.Text = labelText;
            label.Font = new Font("Segoe UI", 8, FontStyle.Bold);
            label.ForeColor = Color.FromArgb(85, 92, 104);
            label.Left = 16;
            label.Top = top;
            label.Width = 90;
            label.Height = 18;
            panel.Controls.Add(label);

            var value = new Label();
            value.Text = "";
            value.Font = new Font("Segoe UI", 10);
            value.ForeColor = Color.FromArgb(18, 30, 52);
            value.Left = 16;
            value.Top = top + 18;
            value.Width = 620;
            value.Height = labelText == "Path" ? 48 : 24;
            value.AutoEllipsis = true;
            panel.Controls.Add(value);
            return value;
        }
#endif

        private void BuildShortcutsTab(TabPage page)
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Color.FromArgb(245, 247, 250);
            panel.Padding = new Padding(21);
            page.Controls.Add(panel);

            var title = new Label();
            title.Text = "Shortcuts";
            title.Font = new Font("Segoe UI", 18, FontStyle.Bold);
            title.Left = 21;
            title.Top = 24;
            title.Width = 420;
            title.Height = 38;
            panel.Controls.Add(title);

            var grid = new TableLayoutPanel();
            grid.Left = 23;
            grid.Top = 82;
            grid.Width = 660;
            grid.AutoSize = true;
            grid.ColumnCount = 3;
            grid.RowCount = 0;
            grid.CellBorderStyle = TableLayoutPanelCellBorderStyle.Single;
            grid.BackColor = Color.FromArgb(225, 230, 238);
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 210));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 180));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.Controls.Add(grid);

            snipShortcutValue = AddShortcutRow(grid, "New snip", ShortcutConfig.SnipAction);
#if SDK_RECORDING
            recordShortcutValue = AddShortcutRow(grid, "Record area", ShortcutConfig.RecordAction);
#endif
            editorShortcutValue = AddShortcutRow(grid, "Open editor", ShortcutConfig.EditorAction);
            AddShortcutInfoRow(grid, "Undo editor change", "Ctrl+Z");
            AddShortcutInfoRow(grid, "Redo editor change", "Ctrl+Y");
            AddShortcutInfoRow(grid, "Cancel area selection", "Esc");

            var reset = MakeSettingsTabButton("Reset Defaults", 23, 338, delegate { ResetShortcuts(); });
            reset.Width = 140;
            panel.Controls.Add(reset);

            RefreshShortcutState();
        }

        private Label AddShortcutRow(TableLayoutPanel grid, string actionText, string action)
        {
            int row = grid.RowCount;
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            var actionLabel = new Label();
            actionLabel.Text = actionText;
            actionLabel.Dock = DockStyle.Fill;
            actionLabel.TextAlign = ContentAlignment.MiddleLeft;
            actionLabel.Font = new Font("Segoe UI", 10);
            actionLabel.BackColor = Color.White;
            actionLabel.Padding = new Padding(12, 0, 8, 0);
            grid.Controls.Add(actionLabel, 0, row);

            var keyLabel = new Label();
            keyLabel.Dock = DockStyle.Fill;
            keyLabel.TextAlign = ContentAlignment.MiddleLeft;
            keyLabel.Font = new Font("Consolas", 10, FontStyle.Bold);
            keyLabel.BackColor = Color.White;
            keyLabel.Padding = new Padding(12, 0, 8, 0);
            grid.Controls.Add(keyLabel, 1, row);

            var changeButton = new Button();
            changeButton.Text = "Change";
            changeButton.Dock = DockStyle.Fill;
            changeButton.Margin = new Padding(8, 4, 8, 4);
            changeButton.Tag = action;
            changeButton.Click += delegate { ChangeShortcut(action); };
            grid.Controls.Add(changeButton, 2, row);
            return keyLabel;
        }

        private static void AddShortcutInfoRow(TableLayoutPanel grid, string actionText, string keys)
        {
            int row = grid.RowCount;
            grid.RowCount++;
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

            var actionLabel = new Label();
            actionLabel.Text = actionText;
            actionLabel.Dock = DockStyle.Fill;
            actionLabel.TextAlign = ContentAlignment.MiddleLeft;
            actionLabel.Font = new Font("Segoe UI", 10);
            actionLabel.BackColor = Color.White;
            actionLabel.Padding = new Padding(12, 0, 8, 0);
            grid.Controls.Add(actionLabel, 0, row);

            var keyLabel = new Label();
            keyLabel.Text = keys;
            keyLabel.Dock = DockStyle.Fill;
            keyLabel.TextAlign = ContentAlignment.MiddleLeft;
            keyLabel.Font = new Font("Consolas", 10, FontStyle.Bold);
            keyLabel.BackColor = Color.White;
            keyLabel.Padding = new Padding(12, 0, 8, 0);
            grid.Controls.Add(keyLabel, 1, row);

            var locked = new Label();
            locked.Text = "Built in";
            locked.Dock = DockStyle.Fill;
            locked.TextAlign = ContentAlignment.MiddleLeft;
            locked.Font = new Font("Segoe UI", 9);
            locked.ForeColor = Color.FromArgb(85, 92, 104);
            locked.BackColor = Color.White;
            locked.Padding = new Padding(12, 0, 8, 0);
            grid.Controls.Add(locked, 2, row);
        }

        internal void RefreshShortcutState()
        {
            if (Program.Shortcuts == null) Program.Shortcuts = ShortcutStore.Load();
            if (snipShortcutValue != null) snipShortcutValue.Text = Program.Shortcuts.Snip.DisplayText;
#if SDK_RECORDING
            if (recordShortcutValue != null) recordShortcutValue.Text = Program.Shortcuts.Record.DisplayText;
            if (recordShortcutLabel != null) recordShortcutLabel.Text = "Shortcut: " + Program.Shortcuts.Record.DisplayText;
#endif
            if (editorShortcutValue != null) editorShortcutValue.Text = Program.Shortcuts.Editor.DisplayText;
        }

        private void ChangeShortcut(string action)
        {
            ShortcutConfig config = Program.Shortcuts == null ? ShortcutConfig.Defaults() : Program.Shortcuts.Clone();
            HotkeySpec current = config.Get(action);
            using (var capture = new HotkeyCaptureForm(current))
            {
                if (capture.ShowDialog(this) != DialogResult.OK) return;
                config.Set(action, capture.Shortcut);
            }

            string message;
            if (Program.UpdateShortcuts(config, out message))
            {
                MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            RefreshShortcutState();
        }

        private void ResetShortcuts()
        {
            string message;
            if (Program.UpdateShortcuts(ShortcutConfig.Defaults(), out message))
            {
                MessageBox.Show(this, "Default shortcuts restored.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(this, message, "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
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
            else if (tool == "Crop")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (preview != null) preview.Dispose();
                preview = new Bitmap(working);
                using (Graphics g = Graphics.FromImage(preview))
                {
                    DrawSelectionPreview(g, rect, "Crop");
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
            else if (tool == "Crop")
            {
                Rectangle rect = ClipToCanvas(Normalize(start, e.Location));
                if (rect.Width < 3 || rect.Height < 3)
                {
                    ClearPreview();
                    return;
                }

                PushUndo();
                var cropped = new Bitmap(rect.Width, rect.Height, PixelFormat.Format32bppArgb);
                using (Graphics g = Graphics.FromImage(cropped))
                {
                    g.DrawImage(working, new Rectangle(0, 0, cropped.Width, cropped.Height), rect, GraphicsUnit.Pixel);
                }

                working.Dispose();
                working = cropped;
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
                    working.Save(dialog.FileName, DrawingImageFormat.Png);
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

#if SDK_RECORDING
        internal void RefreshRecordingsFromStore()
        {
            RefreshRecordingHistory();
        }

        private void RefreshRecordingHistory()
        {
            if (recordingList == null) return;

            string selectedPath = null;
            RecordingItem selected = SelectedRecordingItem;
            if (selected != null) selectedPath = selected.Path;

            recordingItems = RecordingStore.GetItems();
            recordingList.BeginUpdate();
            recordingList.Items.Clear();
            foreach (RecordingItem item in recordingItems)
            {
                recordingList.Items.Add(item.DisplayName);
            }
            recordingList.EndUpdate();

            if (recordingItems.Count == 0)
            {
                RefreshRecordingDetails();
                return;
            }

            int selectedIndex = 0;
            if (!String.IsNullOrEmpty(selectedPath))
            {
                for (int i = 0; i < recordingItems.Count; i++)
                {
                    if (String.Equals(recordingItems[i].Path, selectedPath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedIndex = i;
                        break;
                    }
                }
            }
            recordingList.SelectedIndex = selectedIndex;
        }

        private RecordingItem SelectedRecordingItem
        {
            get
            {
                if (recordingList == null) return null;
                if (recordingList.SelectedIndex < 0 || recordingList.SelectedIndex >= recordingItems.Count) return null;
                return recordingItems[recordingList.SelectedIndex];
            }
        }

        private void RefreshRecordingDetails()
        {
            RecordingItem item = SelectedRecordingItem;
            if (item == null)
            {
                if (recordingStatus != null) recordingStatus.Text = "No recordings yet.";
                if (recordingPreviewTitle != null) recordingPreviewTitle.Text = "No recording selected";
                if (recordingPreviewMeta != null) recordingPreviewMeta.Text = "Record an area to create an MP4 preview here.";
                if (recordingPreviewPath != null) recordingPreviewPath.Text = "";
                if (recordingPreviewPlayButton != null) recordingPreviewPlayButton.Enabled = false;
                return;
            }

            if (recordingStatus != null) recordingStatus.Text = recordingItems.Count + " recording" + (recordingItems.Count == 1 ? "" : "s");
            if (recordingPreviewTitle != null) recordingPreviewTitle.Text = Path.GetFileName(item.Path);
            if (recordingPreviewMeta != null) recordingPreviewMeta.Text = item.SizeText + " - " + item.DisplayName;
            if (recordingPreviewPath != null) recordingPreviewPath.Text = item.Path;
            if (recordingPreviewPlayButton != null) recordingPreviewPlayButton.Enabled = true;
        }

        private void PlayRecordingSelected()
        {
            RecordingItem item = SelectedRecordingItem;
            if (item == null) return;

            try
            {
                OpenWithShell(item.Path);
            }
            catch
            {
                MessageBox.Show(this, "Could not open this recording.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OpenRecordingFolderSelected()
        {
            RecordingItem item = SelectedRecordingItem;
            string folder = item == null ? RecordingStore.GetRecordingDirectory() : Path.GetDirectoryName(item.Path);
            if (String.IsNullOrEmpty(folder)) return;

            try
            {
                if (item != null && File.Exists(item.Path))
                {
                    Process.Start("explorer.exe", "/select,\"" + item.Path + "\"");
                    return;
                }

                Process.Start("explorer.exe", "\"" + folder + "\"");
            }
            catch
            {
            }
        }

        private void CopyRecordingPathSelected()
        {
            RecordingItem item = SelectedRecordingItem;
            if (item == null) return;

            Clipboard.SetText(item.Path);
            Program.ShowToast("Copied", "Recording path copied to clipboard");
        }

        private void DeleteRecordingSelected()
        {
            RecordingItem item = SelectedRecordingItem;
            if (item == null) return;

            if (MessageBox.Show(this, "Delete this recording?", "SnipCopy", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                return;
            }

            try
            {
                RecordingStore.Delete(item.Path);
                RefreshRecordingHistory();
            }
            catch
            {
                MessageBox.Show(this, "Could not delete this recording. It may still be open.", "SnipCopy", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private static void OpenWithShell(string path)
        {
            var info = new ProcessStartInfo();
            info.FileName = path;
            info.UseShellExecute = true;
            Process.Start(info);
        }
#endif

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
            Bitmap source = preview ?? working;
            canvas.Width = source.Width;
            canvas.Height = source.Height;
            canvas.Image = new Bitmap(source);
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

    internal sealed class HotkeyCaptureForm : Form
    {
        private readonly Label valueLabel;
        private readonly Label messageLabel;
        private readonly Button okButton;
        internal HotkeySpec Shortcut;

        internal HotkeyCaptureForm(HotkeySpec current)
        {
            Shortcut = current == null ? null : current.Clone();
            Text = "Change Shortcut";
            Icon = Program.AppIcon;
            Width = 420;
            Height = 178;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            KeyPreview = true;

            var label = new Label();
            label.Text = "Press the shortcut you want to use.";
            label.Left = 16;
            label.Top = 16;
            label.Width = 360;
            label.Height = 22;
            Controls.Add(label);

            valueLabel = new Label();
            valueLabel.Text = Shortcut == null ? "" : Shortcut.DisplayText;
            valueLabel.Left = 16;
            valueLabel.Top = 44;
            valueLabel.Width = 368;
            valueLabel.Height = 30;
            valueLabel.Font = new Font("Consolas", 14, FontStyle.Bold);
            valueLabel.BorderStyle = BorderStyle.FixedSingle;
            valueLabel.TextAlign = ContentAlignment.MiddleCenter;
            valueLabel.BackColor = Color.White;
            Controls.Add(valueLabel);

            messageLabel = new Label();
            messageLabel.Text = "Use Ctrl, Shift, or Alt plus a normal key.";
            messageLabel.Left = 16;
            messageLabel.Top = 82;
            messageLabel.Width = 368;
            messageLabel.Height = 22;
            messageLabel.ForeColor = Color.FromArgb(85, 92, 104);
            Controls.Add(messageLabel);

            okButton = new Button();
            okButton.Text = "Save";
            okButton.Left = 228;
            okButton.Top = 112;
            okButton.Width = 75;
            okButton.DialogResult = DialogResult.OK;
            okButton.Enabled = Shortcut != null && Shortcut.IsValid;
            Controls.Add(okButton);

            var cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 309;
            cancel.Top = 112;
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = okButton;
            CancelButton = cancel;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return;
            }

            HotkeySpec shortcut;
            string message;
            if (HotkeySpec.TryFromKeyEvent(e, out shortcut, out message))
            {
                Shortcut = shortcut;
                valueLabel.Text = shortcut.DisplayText;
                messageLabel.Text = "Ready to save.";
                messageLabel.ForeColor = Color.FromArgb(27, 120, 80);
                okButton.Enabled = true;
            }
            else if (e.KeyCode != Keys.Escape)
            {
                messageLabel.Text = message;
                messageLabel.ForeColor = Color.FromArgb(170, 72, 35);
                okButton.Enabled = false;
            }

            e.SuppressKeyPress = true;
            base.OnKeyDown(e);
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
