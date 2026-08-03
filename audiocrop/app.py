import tkinter as tk
from tkinter import filedialog, messagebox, ttk
import subprocess
import os
import json
import shutil
import sys
import threading
import time as _time

try:
    import customtkinter as ctk
    HAS_CTK = True
except ImportError:
    HAS_CTK = False

SHARED_PYTHON_PATH = os.path.join(os.path.dirname(os.path.dirname(os.path.abspath(__file__))), "shared-python")
if SHARED_PYTHON_PATH not in sys.path:
    sys.path.insert(0, SHARED_PYTHON_PATH)

try:
    from savedcode_license import (
        SavedCodeLicenseError,
        activate_license,
        deactivate_license,
        get_license_status,
        load_record,
        sync_license,
    )
    LICENSE_IMPORT_ERROR = None
except Exception as exc:
    SavedCodeLicenseError = Exception
    activate_license = None
    deactivate_license = None
    get_license_status = None
    load_record = None
    sync_license = None
    LICENSE_IMPORT_ERROR = exc

APP_NAME = "Audio Crop"
APP_VERSION = "0.1.0"
PRODUCT_SLUG = "audio-crop"

# --- Colors / Theme ---
BG = "#1a1a2e"
BG_CARD = "#16213e"
BG_INPUT = "#0f3460"
FG = "#e0e0e0"
FG_DIM = "#8892b0"
ACCENT = "#e94560"
ACCENT_HOVER = "#ff6b81"
BLUE = "#4fc3f7"
BLUE_HOVER = "#81d4fa"
ORANGE = "#ffb74d"
ORANGE_HOVER = "#ffd54f"
GREEN = "#66bb6a"
GREEN_HOVER = "#81c784"
PURPLE = "#bb86fc"
BORDER = "#233554"

FONT = ("Segoe UI", 11)
FONT_BOLD = ("Segoe UI", 11, "bold")
FONT_SMALL = ("Segoe UI", 9)
FONT_TIME = ("Consolas", 22, "bold")
FONT_TIME_DIM = ("Consolas", 14)
FONT_TITLE = ("Segoe UI", 20, "bold")
FONT_SECTION = ("Segoe UI", 12, "bold")


# --- ffmpeg helpers ---

def find_tool(name):
    path = shutil.which(name)
    if path:
        return path
    for p in [
        os.path.join(r"C:\ffmpeg\bin", f"{name}.exe"),
        os.path.join(r"C:\Program Files\ffmpeg\bin", f"{name}.exe"),
        os.path.join(os.path.dirname(os.path.abspath(__file__)), f"{name}.exe"),
    ]:
        if os.path.isfile(p):
            return p
    return None


def get_duration(file_path, ffprobe_path):
    cmd = [
        ffprobe_path, "-v", "quiet", "-print_format", "json",
        "-show_format", file_path
    ]
    result = subprocess.run(cmd, capture_output=True, text=True,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    info = json.loads(result.stdout)
    return int(float(info["format"]["duration"]) * 1000)


def crop_audio(file_path, start_ms, end_ms, output_path, ffmpeg_path):
    start_sec = start_ms / 1000.0
    duration_sec = (end_ms - start_ms) / 1000.0
    cmd = [
        ffmpeg_path, "-y", "-i", file_path,
        "-ss", str(start_sec), "-t", str(duration_sec),
        "-acodec", "libmp3lame", "-q:a", "2",
        output_path
    ]
    result = subprocess.run(cmd, capture_output=True, text=True,
                            creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0)
    if result.returncode != 0:
        raise RuntimeError(result.stderr)


def parse_time(time_str):
    text = time_str.strip()
    parts = text.split(":")
    if len(parts) == 2:
        minutes = int(parts[0])
        seconds = float(parts[1])
        return int((minutes * 60 + seconds) * 1000)
    elif len(parts) == 3:
        hours, minutes = int(parts[0]), int(parts[1])
        seconds = float(parts[2])
        return int((hours * 3600 + minutes * 60 + seconds) * 1000)
    else:
        raise ValueError("Use MM:SS.ms or HH:MM:SS.ms format")


def format_time(ms):
    total_ms = int(ms)
    minutes = total_ms // 60000
    remaining = total_ms % 60000
    seconds = remaining // 1000
    millis = remaining % 1000
    if millis:
        return f"{minutes:02d}:{seconds:02d}.{millis:03d}"
    return f"{minutes:02d}:{seconds:02d}"


# --- Audio player using ffplay ---

class FFPlayPlayer:
    def __init__(self, ffplay_path):
        self.ffplay = ffplay_path
        self.process = None
        self.file_path = None
        self.duration_ms = 0
        self._playing = False
        self._paused = False
        self._start_time = 0
        self._start_offset = 0
        self._pause_position = 0
        self._lock = threading.Lock()

    def load(self, file_path, duration_ms):
        self.stop()
        self.file_path = file_path
        self.duration_ms = duration_ms
        self._pause_position = 0

    def play(self, from_ms=0):
        with self._lock:
            self._kill_process()
            start_sec = from_ms / 1000.0
            self.process = subprocess.Popen(
                [self.ffplay, "-nodisp", "-autoexit", "-ss", str(start_sec), "-i", self.file_path],
                stdin=subprocess.PIPE, stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL,
                creationflags=subprocess.CREATE_NO_WINDOW if os.name == "nt" else 0,
            )
            self._start_offset = from_ms
            self._start_time = _time.time()
            self._playing = True
            self._paused = False

    def pause(self):
        with self._lock:
            if self._playing and not self._paused:
                self._pause_position = self._get_position_unlocked()
                self._kill_process()
                self._playing = False
                self._paused = True

    def resume(self):
        if self._paused:
            self.play(from_ms=self._pause_position)

    def stop(self):
        with self._lock:
            self._kill_process()
            self._playing = False
            self._paused = False
            self._pause_position = 0

    def seek(self, ms):
        was_playing = self._playing and not self._paused
        if was_playing:
            self.play(from_ms=ms)
        else:
            with self._lock:
                self._kill_process()
                self._pause_position = ms
                self._paused = True
                self._playing = False

    def get_position(self):
        with self._lock:
            return self._get_position_unlocked()

    def _get_position_unlocked(self):
        if self._playing and not self._paused:
            if self.process and self.process.poll() is not None:
                self._playing = False
                self._pause_position = self.duration_ms
                return self.duration_ms
            elapsed = (_time.time() - self._start_time) * 1000
            return min(self._start_offset + elapsed, self.duration_ms)
        return self._pause_position

    def is_playing(self):
        with self._lock:
            if self._playing and self.process and self.process.poll() is not None:
                self._playing = False
                self._pause_position = self.duration_ms
            return self._playing

    def is_paused(self):
        return self._paused

    def _kill_process(self):
        if self.process:
            try:
                self.process.terminate()
                self.process.wait(timeout=1)
            except Exception:
                try:
                    self.process.kill()
                except Exception:
                    pass
            self.process = None


# --- Styled button (works without customtkinter) ---

class StyledButton(tk.Canvas):
    def __init__(self, parent, text, command=None, width=120, height=36,
                 bg_color=ACCENT, hover_color=ACCENT_HOVER, fg_color="#fff",
                 font=FONT_BOLD, radius=12, **kwargs):
        super().__init__(parent, width=width, height=height,
                         bg=parent.cget("bg") if hasattr(parent, "cget") else BG,
                         highlightthickness=0, **kwargs)
        self.command = command
        self.bg_color = bg_color
        self.hover_color = hover_color
        self.fg_color = fg_color
        self._text = text
        self._font = font
        self._width = width
        self._height = height
        self._radius = radius
        self._disabled = False
        self._draw(bg_color)
        self.bind("<Enter>", lambda e: self._on_enter())
        self.bind("<Leave>", lambda e: self._on_leave())
        self.bind("<Button-1>", lambda e: self._on_click())

    def _round_rect(self, x1, y1, x2, y2, r, **kwargs):
        points = [
            x1 + r, y1, x2 - r, y1, x2, y1, x2, y1 + r,
            x2, y2 - r, x2, y2, x2 - r, y2, x1 + r, y2,
            x1, y2, x1, y2 - r, x1, y1 + r, x1, y1,
        ]
        return self.create_polygon(points, smooth=True, **kwargs)

    def _draw(self, color):
        self.delete("all")
        self._round_rect(0, 0, self._width, self._height, self._radius,
                         fill=color, outline="")
        self.create_text(self._width // 2, self._height // 2, text=self._text,
                         fill=self.fg_color if not self._disabled else FG_DIM,
                         font=self._font)

    def _on_enter(self):
        if not self._disabled:
            self._draw(self.hover_color)

    def _on_leave(self):
        self._draw(self.bg_color if not self._disabled else BORDER)

    def _on_click(self):
        if not self._disabled and self.command:
            self.command()

    def configure_state(self, state):
        self._disabled = (state == "disabled")
        self._draw(BORDER if self._disabled else self.bg_color)

    def set_text(self, text):
        self._text = text
        self._draw(self.bg_color if not self._disabled else BORDER)


class LicenseDialog(tk.Toplevel):
    def __init__(self, parent, on_change=None):
        super().__init__(parent)
        self.on_change = on_change
        self.title("SavedCode License")
        self.configure(bg=BG)
        self.resizable(False, False)
        self.transient(parent)
        self.grab_set()

        container = tk.Frame(self, bg=BG, padx=20, pady=18)
        container.pack(fill=tk.BOTH, expand=True)

        tk.Label(container, text=APP_NAME, font=FONT_TITLE, bg=BG, fg=ACCENT).grid(
            row=0, column=0, columnspan=3, sticky=tk.W
        )
        tk.Label(container, text=f"Version {APP_VERSION}", font=FONT_SMALL, bg=BG, fg=FG_DIM).grid(
            row=1, column=0, columnspan=3, sticky=tk.W, pady=(0, 14)
        )

        self.status_var = tk.StringVar(value="")
        tk.Label(
            container,
            textvariable=self.status_var,
            font=FONT_BOLD,
            bg=BG,
            fg=FG,
            wraplength=420,
            justify=tk.LEFT,
        ).grid(row=2, column=0, columnspan=3, sticky=tk.W, pady=(0, 14))

        tk.Label(container, text="Email", font=FONT_SMALL, bg=BG, fg=FG_DIM).grid(
            row=3, column=0, sticky=tk.W, pady=(0, 4)
        )
        self.email_entry = tk.Entry(
            container,
            width=42,
            font=FONT,
            bg=BG_INPUT,
            fg=FG,
            insertbackground=FG,
            relief=tk.FLAT,
            highlightthickness=1,
            highlightcolor=ACCENT,
            highlightbackground=BORDER,
        )
        self.email_entry.grid(row=4, column=0, columnspan=3, sticky=tk.EW, pady=(0, 10))

        tk.Label(container, text="License Key", font=FONT_SMALL, bg=BG, fg=FG_DIM).grid(
            row=5, column=0, sticky=tk.W, pady=(0, 4)
        )
        self.license_entry = tk.Entry(
            container,
            width=42,
            font=FONT,
            bg=BG_INPUT,
            fg=FG,
            insertbackground=FG,
            relief=tk.FLAT,
            highlightthickness=1,
            highlightcolor=ACCENT,
            highlightbackground=BORDER,
        )
        self.license_entry.grid(row=6, column=0, columnspan=3, sticky=tk.EW, pady=(0, 16))

        self.activate_btn = tk.Button(
            container,
            text="Activate License",
            command=self._activate,
            bg=ACCENT,
            fg="#fff",
            activebackground=ACCENT_HOVER,
            activeforeground="#fff",
            relief=tk.FLAT,
            padx=12,
            pady=8,
            font=FONT_BOLD,
        )
        self.activate_btn.grid(row=7, column=0, sticky=tk.W, padx=(0, 8))

        self.sync_btn = tk.Button(
            container,
            text="Sync License",
            command=self._sync,
            bg=BG_INPUT,
            fg=FG,
            activebackground=BORDER,
            activeforeground="#fff",
            relief=tk.FLAT,
            padx=12,
            pady=8,
            font=FONT_BOLD,
        )
        self.sync_btn.grid(row=7, column=1, sticky=tk.W, padx=(0, 8))

        self.deactivate_btn = tk.Button(
            container,
            text="Deactivate",
            command=self._deactivate,
            bg=BG_INPUT,
            fg=FG,
            activebackground=BORDER,
            activeforeground="#fff",
            relief=tk.FLAT,
            padx=12,
            pady=8,
            font=FONT_BOLD,
        )
        self.deactivate_btn.grid(row=7, column=2, sticky=tk.W)

        close_btn = tk.Button(
            container,
            text="Close",
            command=self.destroy,
            bg=BG_CARD,
            fg=FG,
            activebackground=BORDER,
            activeforeground="#fff",
            relief=tk.FLAT,
            padx=18,
            pady=8,
            font=FONT_BOLD,
        )
        close_btn.grid(row=8, column=2, sticky=tk.E, pady=(18, 0))

        container.columnconfigure(0, weight=1)
        container.columnconfigure(1, weight=0)
        container.columnconfigure(2, weight=0)

        if LICENSE_IMPORT_ERROR:
            self.status_var.set(f"License unavailable: {LICENSE_IMPORT_ERROR}")
            for button in [self.activate_btn, self.sync_btn, self.deactivate_btn]:
                button.config(state=tk.DISABLED)
        else:
            self._load_saved_record()
            self._refresh_status()

        self.update_idletasks()
        x = parent.winfo_rootx() + max(0, (parent.winfo_width() - self.winfo_width()) // 2)
        y = parent.winfo_rooty() + max(0, (parent.winfo_height() - self.winfo_height()) // 2)
        self.geometry(f"+{x}+{y}")

    def _load_saved_record(self):
        record = load_record(PRODUCT_SLUG) if load_record else {}
        email = record.get("customer_email")
        license_key = record.get("license_key")
        if isinstance(email, str):
            self.email_entry.insert(0, email)
        if isinstance(license_key, str):
            self.license_entry.insert(0, license_key)

    def _refresh_status(self):
        try:
            status = get_license_status(PRODUCT_SLUG)
            self.status_var.set(f"License status: {status.display_text}")
        except SavedCodeLicenseError as exc:
            self.status_var.set(f"License status: Free - {exc}")
        self._notify_change()

    def _notify_change(self):
        if self.on_change:
            self.on_change()

    def _activate(self):
        email = self.email_entry.get().strip()
        license_key = self.license_entry.get().strip()
        if not email or not license_key:
            messagebox.showwarning("Activate License", "Enter the email and license key from SavedCode.", parent=self)
            return

        try:
            self.config(cursor="watch")
            self.update_idletasks()
            status = activate_license(license_key, email, PRODUCT_SLUG)
        except SavedCodeLicenseError as exc:
            messagebox.showwarning("Activate License", str(exc), parent=self)
            status = None
        finally:
            self.config(cursor="")

        self._refresh_status()
        if status and status.is_pro:
            messagebox.showinfo("Activate License", status.display_text, parent=self)

    def _sync(self):
        try:
            self.config(cursor="watch")
            self.update_idletasks()
            status = sync_license(PRODUCT_SLUG)
        except SavedCodeLicenseError as exc:
            messagebox.showwarning("Sync License", str(exc), parent=self)
            status = None
        finally:
            self.config(cursor="")

        self._refresh_status()
        if status and status.is_pro:
            messagebox.showinfo("Sync License", status.display_text, parent=self)

    def _deactivate(self):
        if deactivate_license:
            deactivate_license(PRODUCT_SLUG)
        self.email_entry.delete(0, tk.END)
        self.license_entry.delete(0, tk.END)
        self._refresh_status()
        messagebox.showinfo("Deactivate", "The local SavedCode license token was removed.", parent=self)


# --- Main app ---

class AudioCropperApp:
    def __init__(self, root):
        self.root = root
        self.root.title(APP_NAME)
        self.root.geometry("820x720")
        self.root.resizable(True, True)
        self.root.configure(bg=BG)

        # Try to set dark title bar on Windows
        try:
            from ctypes import windll, c_int, byref
            hwnd = windll.user32.GetParent(self.root.winfo_id())
            windll.dwmapi.DwmSetWindowAttribute(hwnd, 20, byref(c_int(2)), 4)
        except Exception:
            pass

        self.ffmpeg = find_tool("ffmpeg")
        self.ffprobe = find_tool("ffprobe")
        self.ffplay = find_tool("ffplay")

        missing = [t for t, v in [("ffmpeg", self.ffmpeg), ("ffprobe", self.ffprobe), ("ffplay", self.ffplay)] if not v]
        if missing:
            messagebox.showerror("Missing tools",
                f"Required: {', '.join(missing)}\n\nInstall: winget install Gyan.FFmpeg")
            root.destroy()
            return

        self.player = FFPlayPlayer(self.ffplay)
        self.audio_path = None
        self.audio_duration_ms = 0
        self.segments = []
        self.slider_dragging = False
        self.license_window = None

        self._build_ui()
        self.refresh_license_status()
        self.update_player_ui()
        self.root.protocol("WM_DELETE_WINDOW", self.on_close)

    def _build_ui(self):
        # --- Header ---
        header = tk.Frame(self.root, bg=BG, pady=15)
        header.pack(fill=tk.X, padx=20)

        tk.Label(header, text="audiocrop", font=FONT_TITLE, bg=BG, fg=ACCENT).pack(side=tk.LEFT)
        tk.Label(header, text="split your audio, your way", font=FONT_SMALL,
                 bg=BG, fg=FG_DIM).pack(side=tk.LEFT, padx=15, pady=(6, 0))

        self.upload_btn = StyledButton(header, "Open File", command=self.upload_audio,
                                        width=110, height=34, bg_color=PURPLE,
                                        hover_color="#ce93d8", radius=10)
        self.upload_btn.pack(side=tk.RIGHT)

        self.license_btn = StyledButton(header, "License", command=self.show_license_dialog,
                                         width=90, height=34, bg_color=BG_INPUT,
                                         hover_color=BORDER, fg_color=FG, radius=10)
        self.license_btn.pack(side=tk.RIGHT, padx=(0, 8))

        # --- File info bar ---
        self.file_bar = tk.Frame(self.root, bg=BG_CARD, pady=8, padx=15)
        self.file_bar.pack(fill=tk.X, padx=20, pady=(0, 10))

        self.file_label = tk.Label(self.file_bar, text="No file loaded — click Open File to start",
                                    font=FONT, bg=BG_CARD, fg=FG_DIM)
        self.file_label.pack(side=tk.LEFT)

        # --- Player card ---
        player_card = tk.Frame(self.root, bg=BG_CARD, padx=20, pady=15)
        player_card.pack(fill=tk.X, padx=20, pady=(0, 10))

        # Time row
        time_row = tk.Frame(player_card, bg=BG_CARD)
        time_row.pack(fill=tk.X, pady=(0, 8))

        self.time_label = tk.Label(time_row, text="00:00.000", font=FONT_TIME, bg=BG_CARD, fg=FG)
        self.time_label.pack(side=tk.LEFT)

        self.duration_label = tk.Label(time_row, text=" / 00:00", font=FONT_TIME_DIM, bg=BG_CARD, fg=FG_DIM)
        self.duration_label.pack(side=tk.LEFT, pady=(6, 0))

        # Seek slider — style it
        style = ttk.Style()
        style.theme_use("clam")
        style.configure("Custom.Horizontal.TScale", background=BG_CARD, troughcolor=BG_INPUT,
                         sliderthickness=18, borderwidth=0)
        style.map("Custom.Horizontal.TScale", background=[("active", ACCENT)])

        self.seek_var = tk.DoubleVar(value=0)
        self.seek_slider = ttk.Scale(player_card, from_=0, to=1000, orient=tk.HORIZONTAL,
                                      variable=self.seek_var, style="Custom.Horizontal.TScale")
        self.seek_slider.pack(fill=tk.X, pady=(0, 12))
        self.seek_slider.bind("<ButtonPress-1>", self.on_slider_press)
        self.seek_slider.bind("<ButtonRelease-1>", self.on_slider_release)

        # Transport buttons row
        transport = tk.Frame(player_card, bg=BG_CARD)
        transport.pack(fill=tk.X)

        self.play_btn = StyledButton(transport, "Play", command=self.play_audio,
                                      width=80, height=32, bg_color=GREEN, hover_color=GREEN_HOVER, radius=8)
        self.play_btn.pack(side=tk.LEFT, padx=(0, 6))
        self.play_btn.configure_state("disabled")

        self.pause_btn = StyledButton(transport, "Pause", command=self.pause_audio,
                                       width=80, height=32, bg_color=ORANGE, hover_color=ORANGE_HOVER,
                                       radius=8)
        self.pause_btn.pack(side=tk.LEFT, padx=(0, 6))
        self.pause_btn.configure_state("disabled")

        self.stop_btn = StyledButton(transport, "Stop", command=self.stop_audio,
                                      width=80, height=32, bg_color=ACCENT, hover_color=ACCENT_HOVER, radius=8)
        self.stop_btn.pack(side=tk.LEFT, padx=(0, 20))
        self.stop_btn.configure_state("disabled")

        # Set start/end
        self.set_start_btn = StyledButton(transport, "Set Start", command=self.set_as_start,
                                           width=100, height=32, bg_color=BLUE,
                                           hover_color=BLUE_HOVER, radius=8)
        self.set_start_btn.pack(side=tk.LEFT, padx=(0, 6))
        self.set_start_btn.configure_state("disabled")

        self.set_end_btn = StyledButton(transport, "Set End", command=self.set_as_end,
                                         width=100, height=32, bg_color=ORANGE,
                                         hover_color=ORANGE_HOVER, radius=8)
        self.set_end_btn.pack(side=tk.LEFT)
        self.set_end_btn.configure_state("disabled")

        # --- Add segment card ---
        add_card = tk.Frame(self.root, bg=BG_CARD, padx=20, pady=12)
        add_card.pack(fill=tk.X, padx=20, pady=(0, 10))

        tk.Label(add_card, text="NEW SEGMENT", font=("Segoe UI", 9, "bold"),
                 bg=BG_CARD, fg=FG_DIM).pack(anchor=tk.W)

        fields = tk.Frame(add_card, bg=BG_CARD, pady=8)
        fields.pack(fill=tk.X)

        # Name
        name_frame = tk.Frame(fields, bg=BG_CARD)
        name_frame.pack(side=tk.LEFT, padx=(0, 10))
        tk.Label(name_frame, text="Name", font=FONT_SMALL, bg=BG_CARD, fg=FG_DIM).pack(anchor=tk.W)
        self.name_entry = tk.Entry(name_frame, width=22, font=FONT, bg=BG_INPUT,
                                    fg=FG, insertbackground=FG, relief=tk.FLAT,
                                    highlightthickness=1, highlightcolor=ACCENT, highlightbackground=BORDER)
        self.name_entry.pack()

        # Start
        start_frame = tk.Frame(fields, bg=BG_CARD)
        start_frame.pack(side=tk.LEFT, padx=(0, 10))
        tk.Label(start_frame, text="Start", font=FONT_SMALL, bg=BG_CARD, fg=FG_DIM).pack(anchor=tk.W)
        self.start_entry = tk.Entry(start_frame, width=14, font=FONT, bg=BG_INPUT,
                                     fg=FG, insertbackground=FG, relief=tk.FLAT,
                                     highlightthickness=1, highlightcolor=ACCENT, highlightbackground=BORDER)
        self.start_entry.pack()

        # End
        end_frame = tk.Frame(fields, bg=BG_CARD)
        end_frame.pack(side=tk.LEFT, padx=(0, 15))
        tk.Label(end_frame, text="End", font=FONT_SMALL, bg=BG_CARD, fg=FG_DIM).pack(anchor=tk.W)
        self.end_entry = tk.Entry(end_frame, width=14, font=FONT, bg=BG_INPUT,
                                   fg=FG, insertbackground=FG, relief=tk.FLAT,
                                   highlightthickness=1, highlightcolor=ACCENT, highlightbackground=BORDER)
        self.end_entry.pack()

        self.add_btn = StyledButton(fields, "+ Add", command=self.add_segment,
                                     width=80, height=32, bg_color=GREEN, hover_color=GREEN_HOVER, radius=8)
        self.add_btn.pack(side=tk.LEFT, pady=(14, 0))
        self.add_btn.configure_state("disabled")

        # --- Segments list ---
        list_card = tk.Frame(self.root, bg=BG_CARD, padx=20, pady=12)
        list_card.pack(fill=tk.BOTH, expand=True, padx=20, pady=(0, 10))

        list_header = tk.Frame(list_card, bg=BG_CARD)
        list_header.pack(fill=tk.X, pady=(0, 8))

        tk.Label(list_header, text="SEGMENTS", font=("Segoe UI", 9, "bold"),
                 bg=BG_CARD, fg=FG_DIM).pack(side=tk.LEFT)

        # Treeview with dark style
        style.configure("Dark.Treeview",
                         background=BG_INPUT, foreground=FG, fieldbackground=BG_INPUT,
                         borderwidth=0, font=FONT, rowheight=30)
        style.configure("Dark.Treeview.Heading",
                         background=BG_CARD, foreground=FG_DIM, borderwidth=0,
                         font=FONT_BOLD, relief=tk.FLAT)
        style.map("Dark.Treeview",
                   background=[("selected", ACCENT)],
                   foreground=[("selected", "#fff")])
        style.map("Dark.Treeview.Heading",
                   background=[("active", BG_CARD)])
        style.layout("Dark.Treeview", [("Treeview.treearea", {"sticky": "nswe"})])

        tree_frame = tk.Frame(list_card, bg=BG_INPUT)
        tree_frame.pack(fill=tk.BOTH, expand=True)

        columns = ("name", "start", "end", "duration")
        self.tree = ttk.Treeview(tree_frame, columns=columns, show="headings",
                                  height=6, style="Dark.Treeview")
        self.tree.heading("name", text="Name")
        self.tree.heading("start", text="Start")
        self.tree.heading("end", text="End")
        self.tree.heading("duration", text="Duration")
        self.tree.column("name", width=200)
        self.tree.column("start", width=110)
        self.tree.column("end", width=110)
        self.tree.column("duration", width=110)
        self.tree.pack(fill=tk.BOTH, expand=True, side=tk.LEFT)

        scrollbar = ttk.Scrollbar(tree_frame, orient=tk.VERTICAL, command=self.tree.yview)
        scrollbar.pack(side=tk.RIGHT, fill=tk.Y)
        self.tree.configure(yscrollcommand=scrollbar.set)
        self.tree.bind("<Double-1>", self.on_double_click)

        # --- Bottom bar ---
        bottom = tk.Frame(self.root, bg=BG, padx=20, pady=10)
        bottom.pack(fill=tk.X)

        self.remove_btn = StyledButton(bottom, "Remove", command=self.remove_segment,
                                        width=90, height=32, bg_color=BG_INPUT,
                                        hover_color=BORDER, fg_color=FG_DIM, radius=8)
        self.remove_btn.pack(side=tk.LEFT, padx=(0, 6))

        self.clear_btn = StyledButton(bottom, "Clear All", command=self.clear_segments,
                                       width=90, height=32, bg_color=BG_INPUT,
                                       hover_color=BORDER, fg_color=FG_DIM, radius=8)
        self.clear_btn.pack(side=tk.LEFT)

        self.license_status_var = tk.StringVar(value="SavedCode: Free")
        self.license_status_label = tk.Label(bottom, textvariable=self.license_status_var,
                                             font=FONT_SMALL, bg=BG, fg=FG_DIM)
        self.license_status_label.pack(side=tk.LEFT, padx=14)

        self.export_btn = StyledButton(bottom, "Export All", command=self.export_all,
                                        width=130, height=38, bg_color=ACCENT,
                                        hover_color=ACCENT_HOVER, radius=10,
                                        font=("Segoe UI", 12, "bold"))
        self.export_btn.pack(side=tk.RIGHT)
        self.export_btn.configure_state("disabled")

    def _license_status_text(self):
        if LICENSE_IMPORT_ERROR:
            return "License unavailable"
        try:
            return get_license_status(PRODUCT_SLUG).display_text
        except SavedCodeLicenseError as exc:
            return f"Free - {exc}"

    def refresh_license_status(self):
        if hasattr(self, "license_status_var"):
            self.license_status_var.set(f"SavedCode: {self._license_status_text()}")

    def show_license_dialog(self):
        if self.license_window and self.license_window.winfo_exists():
            self.license_window.lift()
            self.license_window.focus_force()
            return
        self.license_window = LicenseDialog(self.root, on_change=self.refresh_license_status)

    def on_close(self):
        self.player.stop()
        self.root.destroy()

    # --- Player ---

    def update_player_ui(self):
        if self.audio_path:
            pos = self.player.get_position()
            self.time_label.config(text=format_time(pos))
            if not self.slider_dragging and self.audio_duration_ms > 0:
                self.seek_var.set((pos / self.audio_duration_ms) * 1000)
            if self.player.is_paused():
                self.pause_btn.set_text("Resume")
            elif self.player.is_playing():
                self.pause_btn.set_text("Pause")
        self.root.after(50, self.update_player_ui)

    def on_slider_press(self, event):
        self.slider_dragging = True

    def on_slider_release(self, event):
        self.slider_dragging = False
        if not self.audio_path or self.audio_duration_ms == 0:
            return
        pos_ms = (self.seek_var.get() / 1000.0) * self.audio_duration_ms
        self.player.seek(pos_ms)

    def play_audio(self):
        if not self.audio_path:
            return
        if self.player.is_paused():
            self.player.resume()
        elif not self.player.is_playing():
            self.player.play(from_ms=self.player.get_position())

    def pause_audio(self):
        if not self.audio_path:
            return
        if self.player.is_playing() and not self.player.is_paused():
            self.player.pause()
        elif self.player.is_paused():
            self.player.resume()

    def stop_audio(self):
        if self.audio_path:
            self.player.stop()

    def set_as_start(self):
        pos = self.player.get_position()
        self.start_entry.delete(0, tk.END)
        self.start_entry.insert(0, format_time(pos))

    def set_as_end(self):
        pos = self.player.get_position()
        self.end_entry.delete(0, tk.END)
        self.end_entry.insert(0, format_time(pos))

    # --- File / segments ---

    def _enable_controls(self):
        for btn in [self.play_btn, self.pause_btn, self.stop_btn,
                     self.set_start_btn, self.set_end_btn, self.add_btn, self.export_btn]:
            btn.configure_state("normal")

    def upload_audio(self):
        file_path = filedialog.askopenfilename(
            filetypes=[("Audio files", "*.mp3;*.wav;*.ogg;*.flac"), ("All files", "*.*")])
        if not file_path:
            return
        try:
            self.audio_duration_ms = get_duration(file_path, self.ffprobe)
            self.audio_path = file_path
            duration = format_time(self.audio_duration_ms)
            filename = os.path.basename(file_path)
            self.file_label.config(text=f"{filename}   {duration}", fg=FG)
            self.duration_label.config(text=f" / {duration}")
            self.player.load(file_path, self.audio_duration_ms)
            self._enable_controls()
        except Exception as e:
            messagebox.showerror("Error", f"Could not load audio:\n{e}")

    def add_segment(self):
        if not self.audio_path:
            return
        name = self.name_entry.get().strip()
        if not name:
            messagebox.showerror("Error", "Enter a name for this segment.")
            return
        try:
            start_ms = parse_time(self.start_entry.get())
            end_ms = parse_time(self.end_entry.get())
        except ValueError:
            messagebox.showerror("Error", "Use MM:SS.ms format (e.g. 01:30.500)")
            return
        if start_ms >= end_ms:
            messagebox.showerror("Error", "Start must be before end.")
            return
        if start_ms < 0 or end_ms > self.audio_duration_ms:
            messagebox.showerror("Error", f"Must be within 00:00 - {format_time(self.audio_duration_ms)}")
            return

        self.segments.append({"name": name, "start": start_ms, "end": end_ms})
        self.tree.insert("", tk.END, values=(
            name, format_time(start_ms), format_time(end_ms), format_time(end_ms - start_ms)
        ))
        self.name_entry.delete(0, tk.END)
        self.start_entry.delete(0, tk.END)
        self.end_entry.delete(0, tk.END)
        self.name_entry.focus()

    def remove_segment(self):
        selected = self.tree.selection()
        for item in selected:
            idx = self.tree.index(item)
            self.tree.delete(item)
            if idx < len(self.segments):
                self.segments.pop(idx)

    def clear_segments(self):
        self.segments.clear()
        for item in self.tree.get_children():
            self.tree.delete(item)

    def on_double_click(self, event):
        region = self.tree.identify("region", event.x, event.y)
        if region != "cell":
            return
        col = self.tree.identify_column(event.x)
        col_idx = int(col.replace("#", "")) - 1
        if col_idx == 3:
            return
        item = self.tree.identify_row(event.y)
        if not item:
            return
        bbox = self.tree.bbox(item, col)
        if not bbox:
            return

        current_value = self.tree.item(item, "values")[col_idx]
        entry = tk.Entry(self.tree, font=FONT, bg=BG_INPUT, fg=FG,
                          insertbackground=FG, relief=tk.FLAT)
        entry.insert(0, current_value)
        entry.select_range(0, tk.END)
        entry.place(x=bbox[0], y=bbox[1], width=bbox[2], height=bbox[3])
        entry.focus()

        def save_edit(e=None):
            new_value = entry.get().strip()
            entry.destroy()
            if not new_value:
                return
            idx = self.tree.index(item)
            if idx >= len(self.segments):
                return
            seg = self.segments[idx]
            col_key = ["name", "start", "end"][col_idx]
            if col_key in ("start", "end"):
                try:
                    seg[col_key] = parse_time(new_value)
                except ValueError:
                    messagebox.showerror("Error", "Use MM:SS.ms format.")
                    return
            else:
                seg[col_key] = new_value
            self.tree.item(item, values=(
                seg["name"], format_time(seg["start"]), format_time(seg["end"]),
                format_time(seg["end"] - seg["start"])
            ))

        entry.bind("<Return>", save_edit)
        entry.bind("<Escape>", lambda e: entry.destroy())
        entry.bind("<FocusOut>", save_edit)

    def export_all(self):
        if not self.audio_path or not self.segments:
            messagebox.showerror("Error", "Load a file and add segments first.")
            return
        output_dir = filedialog.askdirectory(title="Choose output folder")
        if not output_dir:
            return
        errors = []
        for seg in self.segments:
            try:
                safe_name = "".join(c if c.isalnum() or c in " _-" else "_" for c in seg["name"])
                out_path = os.path.join(output_dir, f"{safe_name}.mp3")
                crop_audio(self.audio_path, seg["start"], seg["end"], out_path, self.ffmpeg)
            except Exception as e:
                errors.append(f"{seg['name']}: {e}")
        if errors:
            messagebox.showwarning("Partial Export", "Some failed:\n" + "\n".join(errors))
        else:
            messagebox.showinfo("Done", f"Exported {len(self.segments)} segments to:\n{output_dir}")


if __name__ == "__main__":
    root = tk.Tk()
    app = AudioCropperApp(root)
    root.mainloop()
