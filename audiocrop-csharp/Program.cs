using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Forms;
using SavedCode.Licensing;

namespace AudioCrop
{
    internal static class Program
    {
        internal const string AppName = "Audio Crop";
        internal const string AppVersion = "0.1.0";
        internal const string ProductSlug = "audio-crop";
        internal static SavedCodeLicenseClient LicenseClient;
        internal static Icon AppIcon;

        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            LicenseClient = new SavedCodeLicenseClient(new SavedCodeLicenseOptions(ProductSlug, AppName));
            AppIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath) ?? SystemIcons.Application;
            Application.Run(new MainForm());
            AppIcon.Dispose();
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
    }

    internal sealed class MainForm : Form
    {
        private static readonly Color Bg = Color.FromArgb(24, 28, 42);
        private static readonly Color Card = Color.FromArgb(35, 42, 61);
        private static readonly Color Field = Color.FromArgb(47, 57, 81);
        private static readonly Color Border = Color.FromArgb(84, 98, 128);
        private static readonly Color Accent = Color.FromArgb(233, 69, 96);
        private static readonly Color Blue = Color.FromArgb(64, 169, 255);
        private static readonly Color Green = Color.FromArgb(82, 196, 126);
        private static readonly Color SelectionGreen = Color.FromArgb(174, 245, 202);
        private static readonly Color Orange = Color.FromArgb(255, 183, 77);
        private static readonly Color TextColor = Color.FromArgb(238, 242, 248);
        private static readonly Color DarkText = Color.FromArgb(15, 24, 39);
        private static readonly Color Muted = Color.FromArgb(164, 174, 197);

        private readonly Timer timer = new Timer();
        private readonly AudioPlayer player = new AudioPlayer();
        private readonly List<Segment> segments = new List<Segment>();

        private string ffmpegPath;
        private string ffprobePath;
        private string ffplayPath;
        private string audioPath;
        private int durationMs;
        private bool sliderDragging;
        private int? segmentPreviewEndMs;

        private Label fileLabel;
        private Label timeLabel;
        private Label durationLabel;
        private Label toolStatusLabel;
        private Label licenseStatusLabel;
        private TrackBar seekBar;
        private Button playButton;
        private Button pauseButton;
        private Button stopButton;
        private Button setStartButton;
        private Button setEndButton;
        private Button addButton;
        private Button exportButton;
        private Button removeButton;
        private Button clearButton;
        private TextBox nameBox;
        private TextBox startBox;
        private TextBox endBox;
        private DataGridView grid;

        internal MainForm()
        {
            Text = Program.AppName;
            Icon = Program.AppIcon;
            ClientSize = new Size(920, 720);
            MinimumSize = new Size(760, 620);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Bg;

            ffmpegPath = FindTool("ffmpeg");
            ffprobePath = FindTool("ffprobe");
            ffplayPath = FindTool("ffplay");

            BuildUi();
            RefreshLicenseState();
            RefreshToolStatus();
            EnableAudioControls(false);

            timer.Interval = 100;
            timer.Tick += delegate { UpdatePlayerUi(); };
            timer.Start();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            player.Stop();
            base.OnFormClosing(e);
        }

        private void BuildUi()
        {
            var root = new TableLayoutPanel();
            root.Dock = DockStyle.Fill;
            root.BackColor = Bg;
            root.Padding = new Padding(18);
            root.ColumnCount = 1;
            root.RowCount = 6;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 62));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 142));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 122));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 64));
            Controls.Add(root);

            root.Controls.Add(BuildHeader(), 0, 0);
            root.Controls.Add(BuildFileBar(), 0, 1);
            root.Controls.Add(BuildPlayerCard(), 0, 2);
            root.Controls.Add(BuildSegmentCard(), 0, 3);
            root.Controls.Add(BuildGrid(), 0, 4);
            root.Controls.Add(BuildBottomBar(), 0, 5);
        }

        private Control BuildHeader()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Bg;

            var title = new Label();
            title.Text = "Audio Crop";
            title.Font = new Font("Segoe UI", 22, FontStyle.Bold);
            title.ForeColor = TextColor;
            title.Left = 0;
            title.Top = 8;
            title.Width = 170;
            title.Height = 38;
            panel.Controls.Add(title);

            var subtitle = new Label();
            subtitle.Text = "split audio into clean clips";
            subtitle.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            subtitle.ForeColor = Muted;
            subtitle.Left = 180;
            subtitle.Top = 22;
            subtitle.Width = 260;
            subtitle.Height = 24;
            panel.Controls.Add(subtitle);

            var open = MakeButton("Open File", Purple(), Color.White);
            open.Width = 120;
            open.Height = 34;
            open.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            open.Left = panel.Width - 120;
            open.Top = 14;
            open.Click += delegate { OpenAudio(); };
            panel.Controls.Add(open);

            var license = MakeButton("License", Field, TextColor);
            license.Width = 100;
            license.Height = 34;
            license.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            license.Left = panel.Width - 232;
            license.Top = 14;
            license.Click += delegate { ShowLicenseDialog(); };
            panel.Controls.Add(license);

            panel.Resize += delegate
            {
                open.Left = panel.ClientSize.Width - open.Width;
                license.Left = open.Left - license.Width - 12;
            };

            return panel;
        }

        private Control BuildFileBar()
        {
            var panel = MakeCardPanel();
            panel.Padding = new Padding(14, 10, 14, 10);

            fileLabel = new Label();
            fileLabel.Text = "No file loaded. Open an MP3, WAV, FLAC, OGG, M4A, or AAC file.";
            fileLabel.Dock = DockStyle.Fill;
            fileLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            fileLabel.ForeColor = Muted;
            fileLabel.TextAlign = ContentAlignment.MiddleLeft;
            panel.Controls.Add(fileLabel);

            panel.AllowDrop = true;
            panel.DragEnter += delegate(object sender, DragEventArgs e)
            {
                if (e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop)) e.Effect = DragDropEffects.Copy;
            };
            panel.DragDrop += delegate(object sender, DragEventArgs e)
            {
                if (e.Data == null) return;
                string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0) LoadAudio(files[0]);
            };

            return panel;
        }

        private Control BuildPlayerCard()
        {
            var panel = MakeCardPanel();
            panel.Padding = new Padding(14);

            timeLabel = new Label();
            timeLabel.Text = "00:00.000";
            timeLabel.Font = new Font("Consolas", 22, FontStyle.Bold);
            timeLabel.ForeColor = TextColor;
            timeLabel.Left = 14;
            timeLabel.Top = 10;
            timeLabel.Width = 190;
            timeLabel.Height = 40;
            panel.Controls.Add(timeLabel);

            durationLabel = new Label();
            durationLabel.Text = " / 00:00";
            durationLabel.Font = new Font("Consolas", 14, FontStyle.Regular);
            durationLabel.ForeColor = Muted;
            durationLabel.Left = 200;
            durationLabel.Top = 20;
            durationLabel.Width = 160;
            durationLabel.Height = 26;
            panel.Controls.Add(durationLabel);

            seekBar = new TrackBar();
            seekBar.Minimum = 0;
            seekBar.Maximum = 1000;
            seekBar.TickStyle = TickStyle.None;
            seekBar.Left = 14;
            seekBar.Top = 56;
            seekBar.Width = panel.Width - 28;
            seekBar.Height = 32;
            seekBar.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            seekBar.MouseDown += delegate { sliderDragging = true; };
            seekBar.MouseUp += delegate
            {
                sliderDragging = false;
                SeekFromSlider();
            };
            seekBar.KeyUp += delegate { SeekFromSlider(); };
            panel.Controls.Add(seekBar);

            var row = new FlowLayoutPanel();
            row.Left = 14;
            row.Top = 94;
            row.Width = panel.Width - 28;
            row.Height = 38;
            row.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top;
            row.BackColor = Card;
            row.WrapContents = false;
            panel.Controls.Add(row);

            playButton = MakeButton("Play", Green, Color.White);
            pauseButton = MakeButton("Pause", Orange, Color.Black);
            stopButton = MakeButton("Stop", Accent, Color.White);
            setStartButton = MakeButton("Set Start", Blue, Color.White);
            setEndButton = MakeButton("Set End", Orange, Color.Black);

            playButton.Click += delegate { PlayAudio(); };
            pauseButton.Click += delegate { PauseOrResumeAudio(); };
            stopButton.Click += delegate { StopAudio(); };
            setStartButton.Click += delegate { SetStartFromPlayer(); };
            setEndButton.Click += delegate { SetEndFromPlayer(); };

            row.Controls.Add(playButton);
            row.Controls.Add(pauseButton);
            row.Controls.Add(stopButton);
            row.Controls.Add(MakeSpacer(20, 1));
            row.Controls.Add(setStartButton);
            row.Controls.Add(setEndButton);

            return panel;
        }

        private Control BuildSegmentCard()
        {
            var panel = MakeCardPanel();
            panel.Padding = new Padding(14);

            var heading = MakeSectionLabel("NEW SEGMENT");
            heading.Left = 14;
            heading.Top = 10;
            panel.Controls.Add(heading);

            var nameLabel = MakeSmallLabel("Name");
            nameLabel.Left = 14;
            nameLabel.Top = 40;
            panel.Controls.Add(nameLabel);

            nameBox = MakeTextBox();
            nameBox.Left = 14;
            nameBox.Top = 62;
            nameBox.Width = 250;
            panel.Controls.Add(nameBox);

            var startLabel = MakeSmallLabel("Start");
            startLabel.Left = 284;
            startLabel.Top = 40;
            panel.Controls.Add(startLabel);

            startBox = MakeTextBox();
            startBox.Left = 284;
            startBox.Top = 62;
            startBox.Width = 140;
            panel.Controls.Add(startBox);

            var endLabel = MakeSmallLabel("End");
            endLabel.Left = 444;
            endLabel.Top = 40;
            panel.Controls.Add(endLabel);

            endBox = MakeTextBox();
            endBox.Left = 444;
            endBox.Top = 62;
            endBox.Width = 140;
            panel.Controls.Add(endBox);

            addButton = MakeButton("Add Segment", Green, Color.White);
            addButton.Left = 604;
            addButton.Top = 60;
            addButton.Width = 130;
            addButton.Click += delegate { AddSegment(); };
            panel.Controls.Add(addButton);

            var hint = MakeSmallLabel("Use MM:SS.ms or HH:MM:SS.ms. Double-click a segment to preview it.");
            hint.Left = 14;
            hint.Top = 96;
            hint.Width = 540;
            panel.Controls.Add(hint);

            return panel;
        }

        private Control BuildGrid()
        {
            var panel = MakeCardPanel();
            panel.Padding = new Padding(12);

            grid = new DataGridView();
            grid.Dock = DockStyle.Fill;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToResizeRows = false;
            grid.BackgroundColor = Field;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.SingleHorizontal;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.RowHeadersVisible = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = true;
            grid.EditMode = DataGridViewEditMode.EditOnKeystrokeOrF2;
            grid.DefaultCellStyle.BackColor = Field;
            grid.DefaultCellStyle.ForeColor = TextColor;
            grid.DefaultCellStyle.SelectionBackColor = SelectionGreen;
            grid.DefaultCellStyle.SelectionForeColor = DarkText;
            grid.RowsDefaultCellStyle.SelectionBackColor = SelectionGreen;
            grid.RowsDefaultCellStyle.SelectionForeColor = DarkText;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = SelectionGreen;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = DarkText;
            grid.DefaultCellStyle.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            grid.ColumnHeadersDefaultCellStyle.BackColor = Card;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = Muted;
            grid.ColumnHeadersDefaultCellStyle.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            grid.EnableHeadersVisualStyles = false;
            grid.Columns.Add("name", "Name");
            grid.Columns.Add("start", "Start");
            grid.Columns.Add("end", "End");
            grid.Columns.Add("duration", "Duration");
            grid.Columns[0].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
            grid.Columns[1].Width = 130;
            grid.Columns[2].Width = 130;
            grid.Columns[3].Width = 130;
            grid.Columns[3].ReadOnly = true;
            grid.CellEndEdit += GridCellEndEdit;
            grid.CellDoubleClick += GridCellDoubleClick;
            panel.Controls.Add(grid);

            return panel;
        }

        private Control BuildBottomBar()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Bg;

            removeButton = MakeButton("Remove", Field, Muted);
            removeButton.Left = 0;
            removeButton.Top = 14;
            removeButton.Click += delegate { RemoveSelectedSegments(); };
            panel.Controls.Add(removeButton);

            clearButton = MakeButton("Clear All", Field, Muted);
            clearButton.Left = 112;
            clearButton.Top = 14;
            clearButton.Click += delegate { ClearSegments(); };
            panel.Controls.Add(clearButton);

            licenseStatusLabel = new Label();
            licenseStatusLabel.Left = 232;
            licenseStatusLabel.Top = 20;
            licenseStatusLabel.Width = 360;
            licenseStatusLabel.Height = 24;
            licenseStatusLabel.ForeColor = Muted;
            licenseStatusLabel.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            panel.Controls.Add(licenseStatusLabel);

            toolStatusLabel = new Label();
            toolStatusLabel.Left = 232;
            toolStatusLabel.Top = 42;
            toolStatusLabel.Width = 500;
            toolStatusLabel.Height = 18;
            toolStatusLabel.ForeColor = Muted;
            toolStatusLabel.Font = new Font("Segoe UI", 8, FontStyle.Regular);
            panel.Controls.Add(toolStatusLabel);

            exportButton = MakeButton("Export All", Accent, Color.White);
            exportButton.Width = 130;
            exportButton.Height = 38;
            exportButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            exportButton.Left = panel.Width - exportButton.Width;
            exportButton.Top = 12;
            exportButton.Click += delegate { ExportAll(); };
            panel.Controls.Add(exportButton);

            panel.Resize += delegate
            {
                exportButton.Left = panel.ClientSize.Width - exportButton.Width;
            };

            return panel;
        }

        private Panel MakeCardPanel()
        {
            var panel = new Panel();
            panel.Dock = DockStyle.Fill;
            panel.BackColor = Card;
            panel.Margin = new Padding(0, 0, 0, 10);
            return panel;
        }

        private Button MakeButton(string text, Color backColor, Color foreColor)
        {
            var button = new Button();
            button.Text = text;
            button.Width = 100;
            button.Height = 32;
            button.Margin = new Padding(0, 0, 8, 0);
            button.FlatStyle = FlatStyle.Flat;
            button.BackColor = backColor;
            button.ForeColor = foreColor;
            button.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            button.TextAlign = ContentAlignment.MiddleCenter;
            button.Padding = new Padding(0, 3, 0, 0);
            button.FlatAppearance.BorderColor = Border;
            button.UseVisualStyleBackColor = false;
            return button;
        }

        private Control MakeSpacer(int width, int height)
        {
            var spacer = new Panel();
            spacer.Width = width;
            spacer.Height = height;
            spacer.BackColor = Card;
            return spacer;
        }

        private Label MakeSectionLabel(string text)
        {
            var label = MakeSmallLabel(text);
            label.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            return label;
        }

        private Label MakeSmallLabel(string text)
        {
            var label = new Label();
            label.Text = text;
            label.ForeColor = Muted;
            label.BackColor = Card;
            label.Font = new Font("Segoe UI", 9, FontStyle.Regular);
            label.Width = 360;
            label.Height = 20;
            return label;
        }

        private TextBox MakeTextBox()
        {
            var box = new TextBox();
            box.BackColor = Field;
            box.ForeColor = TextColor;
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            box.Height = 26;
            return box;
        }

        private static Color Purple()
        {
            return Color.FromArgb(153, 102, 255);
        }

        private void EnableAudioControls(bool enabled)
        {
            playButton.Enabled = enabled;
            pauseButton.Enabled = enabled;
            stopButton.Enabled = enabled;
            setStartButton.Enabled = enabled;
            setEndButton.Enabled = enabled;
            addButton.Enabled = enabled;
            exportButton.Enabled = enabled;
            removeButton.Enabled = enabled;
            clearButton.Enabled = enabled;
        }

        private void RefreshLicenseState()
        {
            if (licenseStatusLabel != null)
            {
                licenseStatusLabel.Text = "SavedCode: " + Program.LicenseStatusText();
            }
        }

        private void RefreshToolStatus()
        {
            if (toolStatusLabel == null) return;
            string tools = MissingToolsText();
            toolStatusLabel.Text = String.IsNullOrEmpty(tools) ? "Ready. Free exports up to 3 segments. Pro exports 4 or more." : tools;
        }

        private string MissingToolsText()
        {
            var missing = new List<string>();
            if (String.IsNullOrEmpty(ffmpegPath)) missing.Add("ffmpeg");
            if (String.IsNullOrEmpty(ffprobePath)) missing.Add("ffprobe");
            if (String.IsNullOrEmpty(ffplayPath)) missing.Add("ffplay");
            if (missing.Count == 0) return "";
            return "Missing " + String.Join(", ", missing.ToArray()) + ". Install FFmpeg or place the tools beside AudioCrop.exe.";
        }

        private void OpenAudio()
        {
            if (!EnsureTools()) return;
            using (var dialog = new OpenFileDialog())
            {
                dialog.Filter = "Audio files|*.mp3;*.wav;*.flac;*.ogg;*.m4a;*.aac|All files|*.*";
                dialog.Title = "Open audio file";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;
                LoadAudio(dialog.FileName);
            }
        }

        private void LoadAudio(string path)
        {
            if (!EnsureTools()) return;
            if (!File.Exists(path)) return;

            try
            {
                Cursor = Cursors.WaitCursor;
                durationMs = GetDurationMs(path, ffprobePath);
                audioPath = path;
                player.Load(path, durationMs, ffplayPath);
                string duration = FormatTime(durationMs);
                fileLabel.Text = Path.GetFileName(path) + "   " + duration;
                fileLabel.ForeColor = TextColor;
                durationLabel.Text = " / " + duration;
                seekBar.Value = 0;
                timeLabel.Text = FormatTime(0);
                startBox.Text = "00:00";
                endBox.Text = duration;
                nameBox.Text = Path.GetFileNameWithoutExtension(path) + "-clip";
                segments.Clear();
                grid.Rows.Clear();
                EnableAudioControls(true);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not load audio:\r\n" + ex.Message, "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally
            {
                Cursor = Cursors.Default;
            }
        }

        private bool EnsureTools()
        {
            RefreshToolStatus();
            string missing = MissingToolsText();
            if (String.IsNullOrEmpty(missing)) return true;
            MessageBox.Show(this, missing + "\r\n\r\nInstall with: winget install Gyan.FFmpeg", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return false;
        }

        private void PlayAudio()
        {
            if (String.IsNullOrEmpty(audioPath)) return;
            segmentPreviewEndMs = null;
            player.Play(player.GetPosition());
        }

        private void PauseOrResumeAudio()
        {
            if (String.IsNullOrEmpty(audioPath)) return;
            if (player.IsPlaying && !player.IsPaused) player.Pause();
            else if (player.IsPaused) player.Resume();
        }

        private void StopAudio()
        {
            segmentPreviewEndMs = null;
            player.Stop();
        }

        private void SetStartFromPlayer()
        {
            startBox.Text = FormatTime(player.GetPosition());
        }

        private void SetEndFromPlayer()
        {
            endBox.Text = FormatTime(player.GetPosition());
        }

        private void SeekFromSlider()
        {
            if (String.IsNullOrEmpty(audioPath) || durationMs <= 0) return;
            int ms = (int)Math.Round((seekBar.Value / 1000.0) * durationMs);
            segmentPreviewEndMs = null;
            player.Seek(ms);
        }

        private void AddSegment()
        {
            if (String.IsNullOrEmpty(audioPath)) return;

            string name = nameBox.Text.Trim();
            if (String.IsNullOrWhiteSpace(name)) name = "clip-" + (segments.Count + 1).ToString("00");

            int startMs;
            int endMs;
            if (!TryParseTime(startBox.Text, out startMs) || !TryParseTime(endBox.Text, out endMs))
            {
                MessageBox.Show(this, "Use MM:SS.ms or HH:MM:SS.ms format.", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!ValidateRange(startMs, endMs)) return;

            var segment = new Segment { Name = name, StartMs = startMs, EndMs = endMs };
            segments.Add(segment);
            AddGridRow(segment);

            nameBox.Text = "clip-" + (segments.Count + 1).ToString("00");
            startBox.Text = "";
            endBox.Text = "";
            nameBox.Focus();
        }

        private bool ValidateRange(int startMs, int endMs)
        {
            if (startMs >= endMs)
            {
                MessageBox.Show(this, "Start must be before end.", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (startMs < 0 || endMs > durationMs)
            {
                MessageBox.Show(this, "Segment must be within 00:00 and " + FormatTime(durationMs) + ".", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void AddGridRow(Segment segment)
        {
            int index = grid.Rows.Add(segment.Name, FormatTime(segment.StartMs), FormatTime(segment.EndMs), FormatTime(segment.DurationMs));
            grid.Rows[index].Tag = segment;
        }

        private void GridCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            Segment segment = grid.Rows[e.RowIndex].Tag as Segment;
            if (segment == null) return;

            try
            {
                string value = Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value) ?? "";
                if (e.ColumnIndex == 0)
                {
                    segment.Name = String.IsNullOrWhiteSpace(value) ? segment.Name : value.Trim();
                }
                else if (e.ColumnIndex == 1)
                {
                    int parsed;
                    if (!TryParseTime(value, out parsed) || !ValidateRange(parsed, segment.EndMs)) throw new FormatException();
                    segment.StartMs = parsed;
                }
                else if (e.ColumnIndex == 2)
                {
                    int parsed;
                    if (!TryParseTime(value, out parsed) || !ValidateRange(segment.StartMs, parsed)) throw new FormatException();
                    segment.EndMs = parsed;
                }
            }
            catch
            {
                MessageBox.Show(this, "That edit could not be applied. Use a valid name or time.", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            RefreshGridRow(e.RowIndex);
        }

        private void RefreshGridRow(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= grid.Rows.Count) return;
            Segment segment = grid.Rows[rowIndex].Tag as Segment;
            if (segment == null) return;
            grid.Rows[rowIndex].SetValues(segment.Name, FormatTime(segment.StartMs), FormatTime(segment.EndMs), FormatTime(segment.DurationMs));
        }

        private void GridCellDoubleClick(object sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0 || e.RowIndex >= grid.Rows.Count) return;
            Segment segment = grid.Rows[e.RowIndex].Tag as Segment;
            if (segment == null || String.IsNullOrEmpty(audioPath)) return;
            segmentPreviewEndMs = segment.EndMs;
            player.Play(segment.StartMs);
        }

        private void RemoveSelectedSegments()
        {
            var rows = new List<DataGridViewRow>();
            foreach (DataGridViewRow row in grid.SelectedRows)
            {
                if (!row.IsNewRow) rows.Add(row);
            }
            rows.Sort((a, b) => b.Index.CompareTo(a.Index));
            foreach (DataGridViewRow row in rows)
            {
                Segment segment = row.Tag as Segment;
                if (segment != null) segments.Remove(segment);
                grid.Rows.Remove(row);
            }
        }

        private void ClearSegments()
        {
            segments.Clear();
            grid.Rows.Clear();
        }

        private void ExportAll()
        {
            if (String.IsNullOrEmpty(audioPath) || segments.Count == 0)
            {
                MessageBox.Show(this, "Load a file and add at least one segment first.", "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (!EnsureTools()) return;
            if (segments.Count >= 4 && !Program.IsPro)
            {
                DialogResult result = MessageBox.Show(this, "Batch export of 4 or more segments is an Audio Crop Pro feature. Open license settings?", "Audio Crop Pro", MessageBoxButtons.YesNo, MessageBoxIcon.Information);
                if (result == DialogResult.Yes) ShowLicenseDialog();
                return;
            }

            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Choose output folder";
                if (dialog.ShowDialog(this) != DialogResult.OK) return;

                var errors = new List<string>();
                Cursor = Cursors.WaitCursor;
                try
                {
                    foreach (Segment segment in segments)
                    {
                        try
                        {
                            string output = UniqueOutputPath(dialog.SelectedPath, SafeFileName(segment.Name), ".mp3");
                            CropAudio(audioPath, segment.StartMs, segment.EndMs, output, ffmpegPath);
                        }
                        catch (Exception ex)
                        {
                            errors.Add(segment.Name + ": " + ex.Message);
                        }
                    }
                }
                finally
                {
                    Cursor = Cursors.Default;
                }

                if (errors.Count > 0)
                {
                    MessageBox.Show(this, "Some exports failed:\r\n" + String.Join("\r\n", errors.ToArray()), "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
                else
                {
                    MessageBox.Show(this, "Exported " + segments.Count + " segment(s) to:\r\n" + dialog.SelectedPath, "Audio Crop", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
        }

        private void UpdatePlayerUi()
        {
            if (String.IsNullOrEmpty(audioPath)) return;

            int position = player.GetPosition();
            if (segmentPreviewEndMs.HasValue && player.IsPlaying && position >= segmentPreviewEndMs.Value)
            {
                player.Pause();
                player.Seek(segmentPreviewEndMs.Value);
                segmentPreviewEndMs = null;
                position = player.GetPosition();
            }

            timeLabel.Text = FormatTime(position);
            if (!sliderDragging && durationMs > 0)
            {
                int value = (int)Math.Round((position / (double)durationMs) * 1000);
                seekBar.Value = Math.Max(seekBar.Minimum, Math.Min(seekBar.Maximum, value));
            }
            pauseButton.Text = player.IsPaused ? "Resume" : "Pause";
        }

        private void ShowLicenseDialog()
        {
            using (var dialog = new LicenseDialog())
            {
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.ShowDialog(this);
            }
            Program.LicenseClient.Load();
            RefreshLicenseState();
        }

        private static string FindTool(string name)
        {
            string exe = name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name : name + ".exe";
            string appDir = AppDomain.CurrentDomain.BaseDirectory;
            string[] candidates = new string[]
            {
                Path.Combine(appDir, exe),
                Path.Combine(appDir, "ffmpeg", exe),
                Path.Combine("C:\\ffmpeg\\bin", exe),
                Path.Combine("C:\\Program Files\\ffmpeg\\bin", exe),
                exe
            };

            foreach (string candidate in candidates)
            {
                if (candidate == exe)
                {
                    string pathValue = Environment.GetEnvironmentVariable("PATH") ?? "";
                    foreach (string part in pathValue.Split(Path.PathSeparator))
                    {
                        try
                        {
                            string full = Path.Combine(part.Trim(), exe);
                            if (File.Exists(full)) return full;
                        }
                        catch
                        {
                        }
                    }
                }
                else if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            return "";
        }

        private static int GetDurationMs(string path, string ffprobePath)
        {
            string output = RunAndCapture(ffprobePath, "-v quiet -print_format json -show_format " + Quote(path));
            using (JsonDocument doc = JsonDocument.Parse(output))
            {
                string duration = doc.RootElement.GetProperty("format").GetProperty("duration").GetString();
                double seconds = Double.Parse(duration, CultureInfo.InvariantCulture);
                return (int)Math.Round(seconds * 1000);
            }
        }

        private static void CropAudio(string inputPath, int startMs, int endMs, string outputPath, string ffmpegPath)
        {
            double start = startMs / 1000.0;
            double duration = (endMs - startMs) / 1000.0;
            string args = "-y -ss " + start.ToString("0.###", CultureInfo.InvariantCulture)
                + " -i " + Quote(inputPath)
                + " -t " + duration.ToString("0.###", CultureInfo.InvariantCulture)
                + " -vn -acodec libmp3lame -q:a 2 "
                + Quote(outputPath);
            RunAndCapture(ffmpegPath, args);
        }

        private static string RunAndCapture(string fileName, string arguments)
        {
            var info = new ProcessStartInfo();
            info.FileName = fileName;
            info.Arguments = arguments;
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = true;
            info.RedirectStandardError = true;

            using (Process process = Process.Start(info))
            {
                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();
                process.WaitForExit();
                if (process.ExitCode != 0)
                {
                    throw new ApplicationException(String.IsNullOrWhiteSpace(stderr) ? "External audio tool failed." : stderr.Trim());
                }
                return stdout;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
        }

        private static bool TryParseTime(string text, out int ms)
        {
            ms = 0;
            if (String.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Trim().Split(':');
            try
            {
                double totalSeconds;
                if (parts.Length == 2)
                {
                    totalSeconds = Int32.Parse(parts[0], CultureInfo.InvariantCulture) * 60
                        + Double.Parse(parts[1], CultureInfo.InvariantCulture);
                }
                else if (parts.Length == 3)
                {
                    totalSeconds = Int32.Parse(parts[0], CultureInfo.InvariantCulture) * 3600
                        + Int32.Parse(parts[1], CultureInfo.InvariantCulture) * 60
                        + Double.Parse(parts[2], CultureInfo.InvariantCulture);
                }
                else
                {
                    return false;
                }

                ms = (int)Math.Round(totalSeconds * 1000);
                return ms >= 0;
            }
            catch
            {
                return false;
            }
        }

        private static string FormatTime(int ms)
        {
            if (ms < 0) ms = 0;
            int minutes = ms / 60000;
            int seconds = (ms % 60000) / 1000;
            int millis = ms % 1000;
            return minutes.ToString("00") + ":" + seconds.ToString("00") + "." + millis.ToString("000");
        }

        private static string SafeFileName(string value)
        {
            string text = String.IsNullOrWhiteSpace(value) ? "clip" : value.Trim();
            foreach (char invalid in Path.GetInvalidFileNameChars())
            {
                text = text.Replace(invalid, '_');
            }
            text = Regex.Replace(text, "\\s+", " ");
            return text.Length == 0 ? "clip" : text;
        }

        private static string UniqueOutputPath(string folder, string name, string extension)
        {
            string path = Path.Combine(folder, name + extension);
            int index = 2;
            while (File.Exists(path))
            {
                path = Path.Combine(folder, name + "-" + index.ToString(CultureInfo.InvariantCulture) + extension);
                index++;
            }
            return path;
        }
    }

    internal sealed class Segment
    {
        public string Name;
        public int StartMs;
        public int EndMs;

        public int DurationMs
        {
            get { return EndMs - StartMs; }
        }
    }

    internal sealed class AudioPlayer
    {
        private string ffplayPath;
        private string filePath;
        private int durationMs;
        private Process process;
        private DateTime startedAtUtc;
        private int startOffsetMs;
        private int pausedAtMs;

        public bool IsPlaying { get; private set; }
        public bool IsPaused { get; private set; }

        public void Load(string filePath, int durationMs, string ffplayPath)
        {
            Stop();
            this.filePath = filePath;
            this.durationMs = durationMs;
            this.ffplayPath = ffplayPath;
        }

        public void Play(int fromMs)
        {
            if (String.IsNullOrEmpty(filePath) || String.IsNullOrEmpty(ffplayPath)) return;
            KillProcess();
            if (fromMs >= durationMs) fromMs = 0;

            double start = Math.Max(0, fromMs) / 1000.0;
            var info = new ProcessStartInfo();
            info.FileName = ffplayPath;
            info.Arguments = "-nodisp -autoexit -ss " + start.ToString("0.###", CultureInfo.InvariantCulture) + " -i " + Quote(filePath);
            info.UseShellExecute = false;
            info.CreateNoWindow = true;
            info.RedirectStandardOutput = false;
            info.RedirectStandardError = false;

            process = Process.Start(info);
            startedAtUtc = DateTime.UtcNow;
            startOffsetMs = Math.Max(0, fromMs);
            pausedAtMs = startOffsetMs;
            IsPlaying = true;
            IsPaused = false;
        }

        public void Pause()
        {
            if (!IsPlaying || IsPaused) return;
            pausedAtMs = GetPosition();
            KillProcess();
            IsPlaying = false;
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsPaused) return;
            Play(pausedAtMs);
        }

        public void Stop()
        {
            KillProcess();
            IsPlaying = false;
            IsPaused = false;
            pausedAtMs = 0;
            startOffsetMs = 0;
        }

        public void Seek(int ms)
        {
            bool wasPlaying = IsPlaying && !IsPaused;
            KillProcess();
            pausedAtMs = Math.Max(0, Math.Min(durationMs, ms));
            IsPlaying = false;
            IsPaused = true;
            if (wasPlaying) Play(pausedAtMs);
        }

        public int GetPosition()
        {
            if (IsPlaying && process != null && process.HasExited)
            {
                IsPlaying = false;
                IsPaused = true;
                pausedAtMs = durationMs;
                return durationMs;
            }

            if (IsPlaying && !IsPaused)
            {
                int elapsed = (int)Math.Round((DateTime.UtcNow - startedAtUtc).TotalMilliseconds);
                return Math.Max(0, Math.Min(durationMs, startOffsetMs + elapsed));
            }

            return Math.Max(0, Math.Min(durationMs, pausedAtMs));
        }

        private void KillProcess()
        {
            if (process == null) return;
            try
            {
                if (!process.HasExited)
                {
                    process.Kill(true);
                    process.WaitForExit(1000);
                }
            }
            catch
            {
            }
            finally
            {
                process.Dispose();
                process = null;
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\"", "\\\"") + "\"";
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
            ClientSize = new Size(470, 310);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            BackColor = Color.FromArgb(245, 247, 250);

            var title = new Label();
            title.Text = Program.AppName;
            title.Font = new Font("Segoe UI", 20, FontStyle.Bold);
            title.Left = 18;
            title.Top = 18;
            title.Width = 380;
            title.Height = 38;
            Controls.Add(title);

            var version = new Label();
            version.Text = "Version " + Program.AppVersion;
            version.Font = new Font("Segoe UI", 9);
            version.Left = 21;
            version.Top = 58;
            version.Width = 220;
            version.Height = 22;
            Controls.Add(version);

            var domain = new Label();
            domain.Text = "Licenses and portal: savedcode.com";
            domain.Font = new Font("Segoe UI", 9);
            domain.ForeColor = Color.FromArgb(70, 82, 105);
            domain.Left = 21;
            domain.Top = 78;
            domain.Width = 360;
            domain.Height = 22;
            Controls.Add(domain);

            statusLabel = new Label();
            statusLabel.Text = Program.LicenseStatusText();
            statusLabel.Font = new Font("Segoe UI", 9, FontStyle.Bold);
            statusLabel.Left = 21;
            statusLabel.Top = 108;
            statusLabel.Width = 410;
            statusLabel.Height = 42;
            Controls.Add(statusLabel);

            var emailLabel = new Label();
            emailLabel.Text = "Email";
            emailLabel.Left = 21;
            emailLabel.Top = 160;
            emailLabel.Width = 90;
            emailLabel.Height = 22;
            Controls.Add(emailLabel);

            emailBox = new TextBox();
            emailBox.Left = 125;
            emailBox.Top = 156;
            emailBox.Width = 315;
            emailBox.Height = 24;
            Controls.Add(emailBox);

            var keyLabel = new Label();
            keyLabel.Text = "License Key";
            keyLabel.Left = 21;
            keyLabel.Top = 194;
            keyLabel.Width = 90;
            keyLabel.Height = 22;
            Controls.Add(keyLabel);

            keyBox = new TextBox();
            keyBox.Left = 125;
            keyBox.Top = 190;
            keyBox.Width = 315;
            keyBox.Height = 24;
            Controls.Add(keyBox);

            Controls.Add(MakeButton("Activate", 21, 238, delegate { ActivateLicense(); }));
            Controls.Add(MakeButton("Sync", 126, 238, delegate { SyncLicense(); }));
            Controls.Add(MakeButton("Deactivate", 231, 238, delegate { DeactivateLicense(); }));
            Controls.Add(MakeButton("Close", 361, 238, delegate { Close(); }));

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
}
