using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SnipCopy
{
    static class Program
    {
        internal static Bitmap LastImage;
        internal static string LastImagePath;
        internal static bool OpenEditorAfterSnip;
        internal static NotifyIcon TrayIcon;
        internal static Icon AppIcon;
        private static HotkeyWindow hotkeyWindow;
        private const int HotkeyId = 9182;

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var context = new ApplicationContext();
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? CreateAppIcon();
            TrayIcon = new NotifyIcon();
            TrayIcon.Text = "SnipCopy";
            TrayIcon.Icon = AppIcon;
            TrayIcon.Visible = true;

            var menu = new ContextMenuStrip();
            var newItem = menu.Items.Add("New snip    Ctrl+Shift+S");
            var editItem = menu.Items.Add("Open last in editor");
            var autoEditorItem = new ToolStripMenuItem("Open editor after snip");
            autoEditorItem.CheckOnClick = true;
            menu.Items.Add(autoEditorItem);
            menu.Items.Add("-");
            var exitItem = menu.Items.Add("Exit");

            newItem.Click += delegate { StartSnip(); };
            editItem.Click += delegate { OpenEditor(LastImage); };
            autoEditorItem.CheckedChanged += delegate { OpenEditorAfterSnip = autoEditorItem.Checked; };
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
                    ShowToast("Snip copied", LastImage.Width + " x " + LastImage.Height + " copied to clipboard");

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

            var editor = new EditorForm(bitmap);
            editor.Show();
        }

        internal static void SaveLastImage(Bitmap bitmap)
        {
            string dir = Path.Combine(Path.GetTempPath(), "SnipCopy");
            Directory.CreateDirectory(dir);
            string path = Path.Combine(dir, "snip-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".png");
            bitmap.Save(path, ImageFormat.Png);
            LastImagePath = path;
        }

        internal static void ShowToast(string title, string text)
        {
            if (TrayIcon == null) return;
            TrayIcon.BalloonTipTitle = title;
            TrayIcon.BalloonTipText = text;
            TrayIcon.ShowBalloonTip(1500);
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
        private readonly Panel scroll;
        private readonly PictureBox canvas;
        private Bitmap working;
        private Bitmap preview;
        private readonly List<Bitmap> undo = new List<Bitmap>();
        private readonly List<Bitmap> redo = new List<Bitmap>();
        private Button colorButton;
        private string tool = "Pen";
        private Color color = Color.FromArgb(220, 45, 45);
        private int stroke = 4;
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
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(245, 247, 250);

            var toolbar = new Panel();
            toolbar.Height = 46;
            toolbar.Dock = DockStyle.Top;
            toolbar.BackColor = Color.White;
            Controls.Add(toolbar);

            scroll = new Panel();
            scroll.Dock = DockStyle.Fill;
            scroll.AutoScroll = true;
            scroll.BackColor = Color.FromArgb(230, 234, 240);
            Controls.Add(scroll);

            canvas = new PictureBox();
            canvas.Left = 16;
            canvas.Top = 16;
            canvas.Width = image.Width;
            canvas.Height = image.Height;
            canvas.SizeMode = PictureBoxSizeMode.Normal;
            canvas.BackColor = Color.White;
            scroll.Controls.Add(canvas);

            colorButton = MakeButton("", 8, delegate { ChooseColor(); });
            colorButton.BackColor = color;
            colorButton.FlatStyle = FlatStyle.Flat;
            colorButton.FlatAppearance.BorderColor = Color.FromArgb(90, 98, 110);
            toolbar.Controls.Add(colorButton);
            toolbar.Controls.Add(MakeButton("Pen", 104, delegate { tool = "Pen"; }));
            toolbar.Controls.Add(MakeButton("Arrow", 200, delegate { tool = "Arrow"; }));
            toolbar.Controls.Add(MakeButton("Text", 296, delegate { tool = "Text"; }));
            toolbar.Controls.Add(MakeButton("Undo", 392, delegate { Undo(); }));
            toolbar.Controls.Add(MakeButton("Redo", 488, delegate { Redo(); }));
            toolbar.Controls.Add(MakeButton("Copy", 584, delegate { CopyCurrent(); }));
            toolbar.Controls.Add(MakeButton("Save", 680, delegate { SaveCurrent(); }));

            working = new Bitmap(image);
            SetCanvasImage();

            canvas.MouseDown += CanvasMouseDown;
            canvas.MouseMove += CanvasMouseMove;
            canvas.MouseUp += CanvasMouseUp;
        }

        private static Button MakeButton(string text, int x, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 88;
            button.Height = 30;
            button.Left = x;
            button.Top = 8;
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

        private void SetCanvasImage()
        {
            if (canvas.Image != null) canvas.Image.Dispose();
            canvas.Image = new Bitmap(preview ?? working);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (canvas.Image != null) canvas.Image.Dispose();
            if (preview != null) preview.Dispose();
            foreach (Bitmap item in undo) item.Dispose();
            foreach (Bitmap item in redo) item.Dispose();
            working.Dispose();
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
