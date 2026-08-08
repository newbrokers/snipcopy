using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SavedCode.Licensing;

namespace DrawOverlay
{
    internal static class Program
    {
        internal const string AppName = "Draw Overlay";
        internal const string AppVersion = "0.1.0";
        internal const string ProductSlug = "draw-overlay";
        internal static SavedCodeLicenseClient LicenseClient;
        internal static Icon AppIcon;
        private static NotifyIcon trayIcon;
        private static OverlayForm overlay;
        private static HotkeyWindow hotkeyWindow;
        private const int ToggleHotkeyId = 9277;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LicenseClient = new SavedCodeLicenseClient(new SavedCodeLicenseOptions(ProductSlug, AppName));
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;

            overlay = new OverlayForm();
            overlay.LicenseChanged += delegate { RefreshTrayText(); };

            var context = new ApplicationContext();
            trayIcon = new NotifyIcon();
            trayIcon.Text = AppName;
            trayIcon.Icon = AppIcon;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += delegate { overlay.ToggleOverlay(); };

            var menu = new ContextMenuStrip();
            menu.Items.Add("Show / Hide    Ctrl+H", null, delegate { overlay.ToggleOverlay(); });
            menu.Items.Add("Clear Canvas", null, delegate { overlay.ClearCanvas(); });
            menu.Items.Add("Settings / License", null, delegate { overlay.ShowLicenseDialog(); });
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, delegate { context.ExitThread(); });
            trayIcon.ContextMenuStrip = menu;
            RefreshTrayText();

            hotkeyWindow = new HotkeyWindow(ToggleHotkeyId);
            hotkeyWindow.HotkeyPressed += delegate { overlay.ToggleOverlay(); };
            if (!NativeMethods.RegisterHotKey(hotkeyWindow.Handle, ToggleHotkeyId, NativeMethods.MOD_CONTROL, (uint)Keys.H))
            {
                trayIcon.ShowBalloonTip(5000, AppName, "Ctrl+H could not be registered. Another app may already be using it.", ToolTipIcon.Warning);
            }

            context.ThreadExit += delegate
            {
                NativeMethods.UnregisterHotKey(hotkeyWindow.Handle, ToggleHotkeyId);
                hotkeyWindow.Dispose();
                trayIcon.Visible = false;
                trayIcon.Dispose();
                overlay.CloseForExit();
                overlay.Dispose();
                AppIcon.Dispose();
            };

            overlay.Show();
            Application.Run(context);
        }

        internal static bool IsPro
        {
            get { return LicenseClient != null && LicenseClient.IsPro; }
        }

        internal static string LicenseStatusText()
        {
            if (LicenseClient == null || LicenseClient.Current == null) return "Free";
            return LicenseClient.Current.DisplayText(AppName);
        }

        private static void RefreshTrayText()
        {
            if (trayIcon == null) return;
            string text = AppName + " - " + LicenseStatusText();
            trayIcon.Text = text.Length > 63 ? text.Substring(0, 63) : text;
        }
    }

    internal enum DrawTool
    {
        Pen,
        Highlighter,
        Eraser,
        Line,
        Arrow,
        Rectangle,
        Ellipse,
        Text
    }

    internal sealed class OverlayForm : Form
    {
        private readonly Rectangle virtualScreen;
        private readonly Panel toolbar;
        private readonly Label licenseLabel;
        private readonly Label statusLabel;
        private readonly FlowLayoutPanel toolbarLayout;
        private readonly Dictionary<DrawTool, Button> toolButtons = new Dictionary<DrawTool, Button>();
        private readonly List<Bitmap> undo = new List<Bitmap>();
        private readonly DrawOverlaySettings settings;
        private Bitmap canvasBitmap;
        private Bitmap previewBitmap;
        private DrawTool tool = DrawTool.Pen;
        private Color color = Color.FromArgb(33, 150, 243);
        private int penWidth = 4;
        private int fontSize = 24;
        private bool drawing;
        private bool closingForExit;
        private bool movingToolbar;
        private Point start;
        private Point last;
        private Point toolbarDragOffset;

        internal event EventHandler LicenseChanged;

        internal OverlayForm()
        {
            Text = Program.AppName;
            Icon = Program.AppIcon;
            virtualScreen = SystemInformation.VirtualScreen;
            Bounds = virtualScreen;
            StartPosition = FormStartPosition.Manual;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            TopMost = true;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Color.FromArgb(1, 2, 3);
            TransparencyKey = BackColor;
            Cursor = Cursors.Cross;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

            settings = DrawOverlaySettings.Load();
            tool = settings.Tool;
            color = settings.Color;
            penWidth = settings.PenWidth;
            fontSize = settings.FontSize;

            canvasBitmap = new Bitmap(virtualScreen.Width, virtualScreen.Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(canvasBitmap))
            {
                g.Clear(Color.Transparent);
            }

            toolbar = BuildToolbar(out toolbarLayout);
            Controls.Add(toolbar);

            licenseLabel = MakeLabel(Program.LicenseStatusText(), 11, FontStyle.Regular);
            licenseLabel.Width = 178;
            HookToolbarDrag(licenseLabel);
            toolbarLayout.Controls.Add(licenseLabel);

            statusLabel = MakeLabel("Ctrl+H hide/show. Tab toggles tools. Esc hides.", 9, FontStyle.Regular);
            statusLabel.Width = 178;
            statusLabel.Height = 44;
            HookToolbarDrag(statusLabel);
            toolbarLayout.Controls.Add(statusLabel);

            RefreshToolButtons();
            RefreshLicenseUi();
        }

        internal void ToggleOverlay()
        {
            if (Visible) Hide();
            else
            {
                Bounds = SystemInformation.VirtualScreen;
                ClampToolbarToScreen();
                Show();
                Activate();
            }
        }

        internal void ClearCanvas()
        {
            PushUndo();
            using (Graphics g = Graphics.FromImage(canvasBitmap))
            {
                g.Clear(Color.Transparent);
            }
            Invalidate();
        }

        internal void ShowLicenseDialog()
        {
            bool wasVisible = Visible;
            Hide();
            using (var dialog = new LicenseDialog())
            {
                dialog.StartPosition = FormStartPosition.CenterScreen;
                dialog.ShowDialog();
            }

            Program.LicenseClient.Load();
            RefreshLicenseUi();
            if (LicenseChanged != null) LicenseChanged(this, EventArgs.Empty);
            if (wasVisible) Show();
        }

        internal void CloseForExit()
        {
            closingForExit = true;
            Close();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!closingForExit)
            {
                e.Cancel = true;
                Hide();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            canvasBitmap.Dispose();
            if (previewBitmap != null) previewBitmap.Dispose();
            foreach (Bitmap item in undo) item.Dispose();
            settings.Save();
            base.OnFormClosed(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.DrawImageUnscaled(previewBitmap ?? canvasBitmap, 0, 0);
            base.OnPaint(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || toolbar.Bounds.Contains(e.Location)) return;
            drawing = true;
            start = e.Location;
            last = e.Location;

            if (tool == DrawTool.Pen || tool == DrawTool.Highlighter || tool == DrawTool.Eraser)
            {
                PushUndo();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!drawing) return;

            if (tool == DrawTool.Pen)
            {
                DrawLineSegment(last, e.Location, MakePen(color, penWidth, 255));
                last = e.Location;
                Invalidate();
                return;
            }

            if (tool == DrawTool.Highlighter)
            {
                DrawLineSegment(last, e.Location, MakePen(color, Math.Max(16, penWidth * 4), 90));
                last = e.Location;
                Invalidate();
                return;
            }

            if (tool == DrawTool.Eraser)
            {
                EraseAt(e.Location);
                last = e.Location;
                Invalidate();
                return;
            }

            UpdatePreview(e.Location);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (!drawing) return;
            drawing = false;

            if (tool == DrawTool.Line || tool == DrawTool.Arrow || tool == DrawTool.Rectangle || tool == DrawTool.Ellipse)
            {
                PushUndo();
                DrawShape(canvasBitmap, start, e.Location);
                ClearPreview();
                Invalidate();
                return;
            }

            if (tool == DrawTool.Text)
            {
                string text = TextPromptForm.ShowPrompt("Text to draw:", "Draw Overlay Text");
                if (!String.IsNullOrWhiteSpace(text))
                {
                    PushUndo();
                    using (Graphics g = Graphics.FromImage(canvasBitmap))
                    using (var brush = new SolidBrush(color))
                    using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold))
                    {
                        g.SmoothingMode = SmoothingMode.AntiAlias;
                        g.DrawString(text, font, brush, e.Location);
                    }
                }
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.Control && e.KeyCode == Keys.H)
            {
                ToggleOverlay();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Tab)
            {
                toolbar.Visible = !toolbar.Visible;
                e.SuppressKeyPress = true;
                return;
            }

            if (e.Control && e.KeyCode == Keys.Z)
            {
                Undo();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.C)
            {
                ClearCanvas();
                e.SuppressKeyPress = true;
                return;
            }

            SelectToolFromKey(e.KeyCode);
            base.OnKeyDown(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.H))
            {
                ToggleOverlay();
                return true;
            }

            return base.ProcessCmdKey(ref msg, keyData);
        }

        private Panel BuildToolbar(out FlowLayoutPanel layout)
        {
            var panel = new Panel();
            panel.Width = 228;
            panel.Height = 640;
            panel.Left = Clamp(settings.ToolbarLeft, 0, Math.Max(0, ClientSize.Width - panel.Width - 12));
            panel.Top = Clamp(settings.ToolbarTop, 0, Math.Max(0, ClientSize.Height - panel.Height - 12));
            panel.BackColor = Color.FromArgb(31, 35, 44);
            panel.Padding = new Padding(10);

            layout = new FlowLayoutPanel();
            layout.Dock = DockStyle.Fill;
            layout.FlowDirection = FlowDirection.TopDown;
            layout.WrapContents = false;
            layout.AutoScroll = true;
            layout.BackColor = panel.BackColor;
            panel.Controls.Add(layout);
            HookToolbarDrag(panel);

            var header = MakeHeader("Draw Overlay");
            HookToolbarDrag(header);
            layout.Controls.Add(header);
            var version = MakeLabel("Version " + Program.AppVersion, 9, FontStyle.Regular);
            HookToolbarDrag(version);
            layout.Controls.Add(version);
            layout.Controls.Add(MakeSpacer());

            AddToolButton(layout, DrawTool.Pen, "Pen", "F", false);
            AddToolButton(layout, DrawTool.Highlighter, "Highlighter", "H", false);
            AddToolButton(layout, DrawTool.Eraser, "Eraser", "X", false);
            AddToolButton(layout, DrawTool.Line, "Line Pro", "L", true);
            AddToolButton(layout, DrawTool.Arrow, "Arrow Pro", "A", true);
            AddToolButton(layout, DrawTool.Rectangle, "Rectangle Pro", "R", true);
            AddToolButton(layout, DrawTool.Ellipse, "Ellipse Pro", "E", true);
            AddToolButton(layout, DrawTool.Text, "Text Pro", "T", true);

            layout.Controls.Add(MakeSpacer());

            var colorButton = MakeButton("Select Color");
            colorButton.BackColor = color;
            colorButton.ForeColor = ContrastColor(color);
            colorButton.Click += delegate { ChooseColor(colorButton); };
            layout.Controls.Add(colorButton);

            layout.Controls.Add(MakeLabel("Pen width", 9, FontStyle.Bold));
            var widthSlider = new TrackBar();
            widthSlider.Width = 174;
            widthSlider.Margin = new Padding(0, 0, 0, 6);
            widthSlider.Minimum = 1;
            widthSlider.Maximum = 24;
            widthSlider.TickFrequency = 4;
            widthSlider.Value = penWidth;
            widthSlider.ValueChanged += delegate
            {
                penWidth = widthSlider.Value;
                settings.PenWidth = penWidth;
                settings.Save();
            };
            layout.Controls.Add(widthSlider);

            layout.Controls.Add(MakeLabel("Text size", 9, FontStyle.Bold));
            var fontSlider = new TrackBar();
            fontSlider.Width = 174;
            fontSlider.Margin = new Padding(0, 0, 0, 6);
            fontSlider.Minimum = 12;
            fontSlider.Maximum = 72;
            fontSlider.TickFrequency = 12;
            fontSlider.Value = fontSize;
            fontSlider.ValueChanged += delegate
            {
                fontSize = fontSlider.Value;
                settings.FontSize = fontSize;
                settings.Save();
            };
            layout.Controls.Add(fontSlider);

            layout.Controls.Add(MakeSpacer());
            layout.Controls.Add(MakeActionButton("Undo  Ctrl+Z", delegate { Undo(); }));
            layout.Controls.Add(MakeActionButton("Clear  C", delegate { ClearCanvas(); }));
            layout.Controls.Add(MakeActionButton("Hide  Ctrl+H", delegate { Hide(); }));
            layout.Controls.Add(MakeActionButton("License", delegate { ShowLicenseDialog(); }));
            layout.Controls.Add(MakeActionButton("Exit", delegate { Application.ExitThread(); }));

            return panel;
        }

        private void HookToolbarDrag(Control control)
        {
            control.Cursor = Cursors.SizeAll;
            control.MouseDown += ToolbarMouseDown;
            control.MouseMove += ToolbarMouseMove;
            control.MouseUp += ToolbarMouseUp;
        }

        private void AddToolButton(FlowLayoutPanel layout, DrawTool nextTool, string text, string key, bool proOnly)
        {
            var button = MakeButton(text + "  " + key);
            button.Tag = new ToolButtonTag(nextTool, proOnly);
            button.Click += delegate { SelectTool(nextTool); };
            toolButtons[nextTool] = button;
            layout.Controls.Add(button);
        }

        private Button MakeButton(string text)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 174;
            button.Height = 30;
            button.Margin = new Padding(0, 0, 0, 6);
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = Color.FromArgb(48, 54, 66);
            button.ForeColor = Color.White;
            button.TextAlign = ContentAlignment.MiddleLeft;
            button.FlatAppearance.BorderColor = Color.FromArgb(76, 84, 100);
            return button;
        }

        private Button MakeActionButton(string text, EventHandler click)
        {
            var button = MakeButton(text);
            button.Click += click;
            return button;
        }

        private Label MakeHeader(string text)
        {
            var label = MakeLabel(text, 16, FontStyle.Bold);
            label.Height = 32;
            return label;
        }

        private Label MakeLabel(string text, float size, FontStyle style)
        {
            var label = new Label();
            label.Text = text;
            label.Font = new Font("Segoe UI", size, style);
            label.ForeColor = Color.White;
            label.Width = 174;
            label.Height = 24;
            label.Margin = new Padding(0, 0, 0, 6);
            return label;
        }

        private Control MakeSpacer()
        {
            var spacer = new Panel();
            spacer.Width = 174;
            spacer.Height = 8;
            spacer.Margin = new Padding(0, 0, 0, 6);
            spacer.BackColor = toolbar == null ? Color.FromArgb(31, 35, 44) : toolbar.BackColor;
            HookToolbarDrag(spacer);
            return spacer;
        }

        private void ToolbarMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            movingToolbar = true;
            Point screenPoint = ((Control)sender).PointToScreen(e.Location);
            Point formPoint = PointToClient(screenPoint);
            toolbarDragOffset = new Point(formPoint.X - toolbar.Left, formPoint.Y - toolbar.Top);
            toolbar.Capture = true;
        }

        private void ToolbarMouseMove(object sender, MouseEventArgs e)
        {
            if (!movingToolbar) return;
            Point screenPoint = ((Control)sender).PointToScreen(e.Location);
            Point formPoint = PointToClient(screenPoint);
            MoveToolbarTo(formPoint.X - toolbarDragOffset.X, formPoint.Y - toolbarDragOffset.Y);
        }

        private void ToolbarMouseUp(object sender, MouseEventArgs e)
        {
            if (!movingToolbar) return;
            movingToolbar = false;
            toolbar.Capture = false;
            settings.ToolbarLeft = toolbar.Left;
            settings.ToolbarTop = toolbar.Top;
            settings.Save();
        }

        private void MoveToolbarTo(int left, int top)
        {
            int maxLeft = Math.Max(0, ClientSize.Width - toolbar.Width - 12);
            int maxTop = Math.Max(0, ClientSize.Height - toolbar.Height - 12);
            toolbar.Left = Clamp(left, 0, maxLeft);
            toolbar.Top = Clamp(top, 0, maxTop);
        }

        private void ClampToolbarToScreen()
        {
            if (toolbar == null) return;
            MoveToolbarTo(toolbar.Left, toolbar.Top);
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) return min;
            if (value > max) return max;
            return value;
        }

        private void SelectToolFromKey(Keys key)
        {
            if (key == Keys.F) SelectTool(DrawTool.Pen);
            else if (key == Keys.H) SelectTool(DrawTool.Highlighter);
            else if (key == Keys.X) SelectTool(DrawTool.Eraser);
            else if (key == Keys.L) SelectTool(DrawTool.Line);
            else if (key == Keys.A) SelectTool(DrawTool.Arrow);
            else if (key == Keys.R) SelectTool(DrawTool.Rectangle);
            else if (key == Keys.E) SelectTool(DrawTool.Ellipse);
            else if (key == Keys.T) SelectTool(DrawTool.Text);
        }

        private void SelectTool(DrawTool nextTool)
        {
            if (RequiresPro(nextTool) && !Program.IsPro)
            {
                DialogResult result = MessageBox.Show(this, "This drawing tool is a Draw Overlay Pro feature. Open license settings?", "Draw Overlay Pro", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) ShowLicenseDialog();
                return;
            }

            tool = nextTool;
            settings.Tool = nextTool;
            settings.Save();
            RefreshToolButtons();
        }

        private bool RequiresPro(DrawTool candidate)
        {
            return candidate == DrawTool.Line
                || candidate == DrawTool.Arrow
                || candidate == DrawTool.Rectangle
                || candidate == DrawTool.Ellipse
                || candidate == DrawTool.Text;
        }

        private void RefreshToolButtons()
        {
            foreach (KeyValuePair<DrawTool, Button> pair in toolButtons)
            {
                ToolButtonTag tag = pair.Value.Tag as ToolButtonTag;
                bool selected = pair.Key == tool;
                bool locked = tag != null && tag.ProOnly && !Program.IsPro;
                pair.Value.BackColor = selected ? Color.FromArgb(68, 126, 220) : locked ? Color.FromArgb(42, 46, 56) : Color.FromArgb(48, 54, 66);
                pair.Value.ForeColor = locked ? Color.FromArgb(175, 182, 194) : Color.White;
            }
        }

        private void RefreshLicenseUi()
        {
            if (licenseLabel != null) licenseLabel.Text = Program.LicenseStatusText();
            RefreshToolButtons();
        }

        private void ChooseColor(Button colorButton)
        {
            using (var dialog = new ColorDialog())
            {
                dialog.Color = color;
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                color = dialog.Color;
                settings.Color = color;
                settings.Save();
                colorButton.BackColor = color;
                colorButton.ForeColor = ContrastColor(color);
            }
        }

        private static Color ContrastColor(Color c)
        {
            int brightness = (c.R * 299 + c.G * 587 + c.B * 114) / 1000;
            return brightness > 150 ? Color.Black : Color.White;
        }

        private void PushUndo()
        {
            undo.Add(new Bitmap(canvasBitmap));
            while (undo.Count > 20)
            {
                Bitmap old = undo[0];
                undo.RemoveAt(0);
                old.Dispose();
            }
        }

        private void Undo()
        {
            if (undo.Count == 0) return;
            Bitmap previous = undo[undo.Count - 1];
            undo.RemoveAt(undo.Count - 1);
            canvasBitmap.Dispose();
            canvasBitmap = previous;
            ClearPreview();
            Invalidate();
        }

        private Pen MakePen(Color baseColor, int width, int alpha)
        {
            var pen = new Pen(Color.FromArgb(alpha, baseColor), width);
            pen.StartCap = LineCap.Round;
            pen.EndCap = LineCap.Round;
            pen.LineJoin = LineJoin.Round;
            return pen;
        }

        private void DrawLineSegment(Point a, Point b, Pen pen)
        {
            using (pen)
            using (Graphics g = Graphics.FromImage(canvasBitmap))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawLine(pen, a, b);
            }
        }

        private void EraseAt(Point point)
        {
            using (Graphics g = Graphics.FromImage(canvasBitmap))
            using (var brush = new SolidBrush(Color.Transparent))
            {
                g.CompositingMode = CompositingMode.SourceCopy;
                int size = Math.Max(12, penWidth * 4);
                g.FillEllipse(brush, point.X - size / 2, point.Y - size / 2, size, size);
            }
        }

        private void UpdatePreview(Point end)
        {
            ClearPreview();
            previewBitmap = new Bitmap(canvasBitmap);
            DrawShape(previewBitmap, start, end);
            Invalidate();
        }

        private void ClearPreview()
        {
            if (previewBitmap == null) return;
            previewBitmap.Dispose();
            previewBitmap = null;
        }

        private void DrawShape(Bitmap target, Point a, Point b)
        {
            using (Graphics g = Graphics.FromImage(target))
            using (Pen pen = MakePen(color, penWidth, 255))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = Normalize(a, b);
                if (tool == DrawTool.Line)
                {
                    g.DrawLine(pen, a, b);
                }
                else if (tool == DrawTool.Arrow)
                {
                    DrawArrow(g, pen, a, b);
                }
                else if (tool == DrawTool.Rectangle)
                {
                    g.DrawRectangle(pen, rect);
                }
                else if (tool == DrawTool.Ellipse)
                {
                    g.DrawEllipse(pen, rect);
                }
            }
        }

        private static Rectangle Normalize(Point a, Point b)
        {
            return new Rectangle(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }

        private static void DrawArrow(Graphics g, Pen pen, Point start, Point end)
        {
            g.DrawLine(pen, start, end);
            double angle = Math.Atan2(end.Y - start.Y, end.X - start.X);
            double spread = Math.PI / 7;
            int length = Math.Max(14, (int)pen.Width * 5);
            Point p1 = new Point(
                (int)(end.X - length * Math.Cos(angle - spread)),
                (int)(end.Y - length * Math.Sin(angle - spread)));
            Point p2 = new Point(
                (int)(end.X - length * Math.Cos(angle + spread)),
                (int)(end.Y - length * Math.Sin(angle + spread)));
            g.DrawLine(pen, end, p1);
            g.DrawLine(pen, end, p2);
        }
    }

    internal sealed class ToolButtonTag
    {
        public readonly DrawTool Tool;
        public readonly bool ProOnly;

        public ToolButtonTag(DrawTool tool, bool proOnly)
        {
            Tool = tool;
            ProOnly = proOnly;
        }
    }

    internal sealed class DrawOverlaySettings
    {
        public DrawTool Tool = DrawTool.Pen;
        public Color Color = Color.FromArgb(33, 150, 243);
        public int PenWidth = 4;
        public int FontSize = 24;
        public int ToolbarLeft = 12;
        public int ToolbarTop = 12;

        internal static DrawOverlaySettings Load()
        {
            var settings = new DrawOverlaySettings();
            string path = GetPath();
            if (!File.Exists(path)) return settings;

            try
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    int equals = line.IndexOf('=');
                    if (equals <= 0) continue;
                    string key = line.Substring(0, equals).Trim();
                    string value = line.Substring(equals + 1).Trim();

                    if (String.Equals(key, "tool", StringComparison.OrdinalIgnoreCase))
                    {
                        DrawTool parsed;
                        if (Enum.TryParse(value, true, out parsed)) settings.Tool = parsed;
                    }
                    else if (String.Equals(key, "color", StringComparison.OrdinalIgnoreCase))
                    {
                        settings.Color = ColorTranslator.FromHtml(value);
                    }
                    else if (String.Equals(key, "pen_width", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (Int32.TryParse(value, out parsed)) settings.PenWidth = Math.Max(1, Math.Min(24, parsed));
                    }
                    else if (String.Equals(key, "font_size", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (Int32.TryParse(value, out parsed)) settings.FontSize = Math.Max(12, Math.Min(72, parsed));
                    }
                    else if (String.Equals(key, "toolbar_left", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (Int32.TryParse(value, out parsed)) settings.ToolbarLeft = parsed;
                    }
                    else if (String.Equals(key, "toolbar_top", StringComparison.OrdinalIgnoreCase))
                    {
                        int parsed;
                        if (Int32.TryParse(value, out parsed)) settings.ToolbarTop = parsed;
                    }
                }
            }
            catch
            {
            }

            if ((settings.Tool == DrawTool.Line || settings.Tool == DrawTool.Arrow || settings.Tool == DrawTool.Rectangle || settings.Tool == DrawTool.Ellipse || settings.Tool == DrawTool.Text) && !Program.IsPro)
            {
                settings.Tool = DrawTool.Pen;
            }

            return settings;
        }

        internal void Save()
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var lines = new List<string>();
            lines.Add("tool=" + Tool.ToString());
            lines.Add("color=" + ColorTranslator.ToHtml(Color));
            lines.Add("pen_width=" + PenWidth.ToString());
            lines.Add("font_size=" + FontSize.ToString());
            lines.Add("toolbar_left=" + ToolbarLeft.ToString());
            lines.Add("toolbar_top=" + ToolbarTop.ToString());
            File.WriteAllLines(path, lines.ToArray());
        }

        private static string GetPath()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "SavedCode", "DrawOverlay");
            return Path.Combine(dir, "settings.ini");
        }
    }

    internal sealed class LicenseDialog : Form
    {
        private readonly Label statusLabel;
        private readonly TextBox emailBox;
        private readonly TextBox keyBox;

        internal LicenseDialog()
        {
            Text = "SavedCode License";
            Icon = Program.AppIcon;
            Width = 462;
            Height = 310;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);

            var title = new Label();
            title.Text = Program.AppName;
            title.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            title.Left = 18;
            title.Top = 18;
            title.Width = 360;
            title.Height = 38;
            Controls.Add(title);

            var version = new Label();
            version.Text = "Version " + Program.AppVersion;
            version.Font = new Font("Segoe UI", 9);
            version.Left = 21;
            version.Top = 58;
            version.Width = 200;
            version.Height = 22;
            Controls.Add(version);

            statusLabel = new Label();
            statusLabel.Text = Program.LicenseStatusText();
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusLabel.Left = 21;
            statusLabel.Top = 88;
            statusLabel.Width = 398;
            statusLabel.Height = 42;
            Controls.Add(statusLabel);

            var emailLabel = new Label();
            emailLabel.Text = "Email";
            emailLabel.Left = 21;
            emailLabel.Top = 140;
            emailLabel.Width = 90;
            emailLabel.Height = 22;
            Controls.Add(emailLabel);

            emailBox = new TextBox();
            emailBox.Left = 125;
            emailBox.Top = 136;
            emailBox.Width = 294;
            emailBox.Height = 24;
            Controls.Add(emailBox);

            var keyLabel = new Label();
            keyLabel.Text = "License Key";
            keyLabel.Left = 21;
            keyLabel.Top = 174;
            keyLabel.Width = 90;
            keyLabel.Height = 22;
            Controls.Add(keyLabel);

            keyBox = new TextBox();
            keyBox.Left = 125;
            keyBox.Top = 170;
            keyBox.Width = 294;
            keyBox.Height = 24;
            Controls.Add(keyBox);

            var activate = MakeButton("Activate", 21, 218, delegate { ActivateLicense(); });
            var sync = MakeButton("Sync", 126, 218, delegate { SyncLicense(); });
            var deactivate = MakeButton("Deactivate", 231, 218, delegate { DeactivateLicense(); });
            var close = MakeButton("Close", 336, 218, delegate { Close(); });
            Controls.Add(activate);
            Controls.Add(sync);
            Controls.Add(deactivate);
            Controls.Add(close);

            LoadSavedFields();
        }

        private Button MakeButton(string text, int x, int y, EventHandler click)
        {
            var button = new Button();
            button.Text = text;
            button.Left = x;
            button.Top = y;
            button.Width = 96;
            button.Height = 30;
            button.Click += click;
            return button;
        }

        private void LoadSavedFields()
        {
            SavedCodeLicenseInfo info = Program.LicenseClient.Current;
            if (info == null) return;
            emailBox.Text = info.CustomerEmail;
            keyBox.Text = info.Key;
        }

        private void ActivateLicense()
        {
            string message;
            if (Program.LicenseClient.Activate(emailBox.Text.Trim(), keyBox.Text.Trim(), out message))
            {
                statusLabel.Text = Program.LicenseStatusText();
                MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SyncLicense()
        {
            string message;
            if (Program.LicenseClient.Sync(out message))
            {
                statusLabel.Text = Program.LicenseStatusText();
                MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(this, message, Program.AppName, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void DeactivateLicense()
        {
            Program.LicenseClient.Deactivate();
            emailBox.Text = "";
            keyBox.Text = "";
            statusLabel.Text = Program.LicenseStatusText();
        }
    }

    internal sealed class TextPromptForm : Form
    {
        private readonly TextBox textBox;

        private TextPromptForm(string message, string title)
        {
            Text = title;
            Icon = Program.AppIcon;
            Width = 360;
            Height = 148;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;

            var label = new Label();
            label.Text = message;
            label.Left = 12;
            label.Top = 12;
            label.Width = 320;
            label.Height = 22;
            Controls.Add(label);

            textBox = new TextBox();
            textBox.Left = 12;
            textBox.Top = 38;
            textBox.Width = 320;
            Controls.Add(textBox);

            var ok = new Button();
            ok.Text = "OK";
            ok.Left = 176;
            ok.Top = 72;
            ok.Width = 75;
            ok.DialogResult = DialogResult.OK;
            Controls.Add(ok);

            var cancel = new Button();
            cancel.Text = "Cancel";
            cancel.Left = 257;
            cancel.Top = 72;
            cancel.Width = 75;
            cancel.DialogResult = DialogResult.Cancel;
            Controls.Add(cancel);

            AcceptButton = ok;
            CancelButton = cancel;
        }

        internal static string ShowPrompt(string message, string title)
        {
            using (var form = new TextPromptForm(message, title))
            {
                return form.ShowDialog() == DialogResult.OK ? form.textBox.Text : "";
            }
        }
    }

    internal static class NativeMethods
    {
        internal const uint MOD_CONTROL = 0x0002;
        internal const int WM_HOTKEY = 0x0312;

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        internal static extern bool UnregisterHotKey(IntPtr hWnd, int id);
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
}
