"""
Desktop Drawing Overlay — transparent fullscreen canvas for annotations.
Tools: freehand, line, arrow, rectangle, ellipse, text, highlighter, eraser.
Sidebar toggles with Tab key. All drawing requires Ctrl+Click.
Launch with: python draw_overlay.py
"""

import sys
import math
from enum import Enum, auto
from PyQt6.QtWidgets import (
    QApplication, QMainWindow, QColorDialog,
    QInputDialog, QWidget, QVBoxLayout, QHBoxLayout, QLabel, QSlider,
    QPushButton, QFrame, QSizePolicy, QGridLayout, QSystemTrayIcon, QMenu,
    QComboBox, QScrollArea, QDialog, QFormLayout, QLineEdit, QMessageBox,
)
from PyQt6.QtCore import Qt, QPoint, QRect, QSize, QTimer
from PyQt6.QtGui import (
    QPainter, QPen, QColor, QPixmap, QIcon, QFont,
    QKeySequence, QPainterPath, QShortcut, QAction, QCursor,
)
import os
import json

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

APP_NAME = "Draw Overlay"
APP_VERSION = "0.1.0"
PRODUCT_SLUG = "draw-overlay"
SETTINGS_PATH = os.path.join(os.path.dirname(os.path.abspath(__file__)), "settings.json")

DEFAULT_SETTINGS = {
    "tool": "FREEHAND",
    "color": "#2196F3",
    "pen_width": 3,
    "font_size": 24,
    "cursor": "crosshair",
    "mode": "always_on",
}


def load_settings() -> dict:
    try:
        with open(SETTINGS_PATH, "r") as f:
            saved = json.load(f)
        # Merge with defaults so new keys are always present
        return {**DEFAULT_SETTINGS, **saved}
    except (FileNotFoundError, json.JSONDecodeError):
        return dict(DEFAULT_SETTINGS)


def save_settings(settings: dict):
    with open(SETTINGS_PATH, "w") as f:
        json.dump(settings, f, indent=2)


class LicenseDialog(QDialog):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setWindowTitle("SavedCode License")
        self.setMinimumWidth(430)

        layout = QVBoxLayout(self)
        layout.setContentsMargins(16, 16, 16, 16)
        layout.setSpacing(12)

        title = QLabel(f"{APP_NAME}")
        title.setStyleSheet("font-size: 22px; font-weight: bold;")
        layout.addWidget(title)

        version = QLabel(f"Version {APP_VERSION}")
        layout.addWidget(version)

        self.status_label = QLabel("")
        self.status_label.setWordWrap(True)
        self.status_label.setStyleSheet("font-weight: bold;")
        layout.addWidget(self.status_label)

        form = QFormLayout()
        form.setLabelAlignment(Qt.AlignmentFlag.AlignLeft)
        self.email_input = QLineEdit()
        self.email_input.setPlaceholderText("you@example.com")
        self.license_input = QLineEdit()
        self.license_input.setPlaceholderText("SCP-...")
        form.addRow("Email", self.email_input)
        form.addRow("License Key", self.license_input)
        layout.addLayout(form)

        buttons = QHBoxLayout()
        self.activate_btn = QPushButton("Activate License")
        self.sync_btn = QPushButton("Sync License")
        self.deactivate_btn = QPushButton("Deactivate")
        close_btn = QPushButton("Close")
        buttons.addWidget(self.activate_btn)
        buttons.addWidget(self.sync_btn)
        buttons.addWidget(self.deactivate_btn)
        buttons.addStretch()
        buttons.addWidget(close_btn)
        layout.addLayout(buttons)

        self.activate_btn.clicked.connect(self._activate)
        self.sync_btn.clicked.connect(self._sync)
        self.deactivate_btn.clicked.connect(self._deactivate)
        close_btn.clicked.connect(self.accept)

        if LICENSE_IMPORT_ERROR:
            self.status_label.setText(f"License unavailable: {LICENSE_IMPORT_ERROR}")
            self.activate_btn.setEnabled(False)
            self.sync_btn.setEnabled(False)
            self.deactivate_btn.setEnabled(False)
            return

        self._load_saved_record()
        self._refresh_status()

    def _load_saved_record(self):
        record = load_record(PRODUCT_SLUG) if load_record else {}
        email = record.get("customer_email")
        license_key = record.get("license_key")
        if isinstance(email, str):
            self.email_input.setText(email)
        if isinstance(license_key, str):
            self.license_input.setText(license_key)

    def _refresh_status(self):
        try:
            status = get_license_status(PRODUCT_SLUG)
            self.status_label.setText(f"License status: {status.display_text}")
        except SavedCodeLicenseError as exc:
            self.status_label.setText(f"License status: Free - {exc}")

    def _activate(self):
        email = self.email_input.text().strip()
        license_key = self.license_input.text().strip()
        if not email or not license_key:
            QMessageBox.warning(self, "Activate License", "Enter the email and license key from SavedCode.")
            return

        status = None
        try:
            QApplication.setOverrideCursor(QCursor(Qt.CursorShape.WaitCursor))
            status = activate_license(license_key, email, PRODUCT_SLUG)
        except SavedCodeLicenseError as exc:
            QMessageBox.warning(self, "Activate License", str(exc))
        finally:
            QApplication.restoreOverrideCursor()

        self._refresh_status()
        if status and status.is_pro:
            QMessageBox.information(self, "Activate License", status.display_text)

    def _sync(self):
        status = None
        try:
            QApplication.setOverrideCursor(QCursor(Qt.CursorShape.WaitCursor))
            status = sync_license(PRODUCT_SLUG)
        except SavedCodeLicenseError as exc:
            QMessageBox.warning(self, "Sync License", str(exc))
        finally:
            QApplication.restoreOverrideCursor()

        self._refresh_status()
        if status and status.is_pro:
            QMessageBox.information(self, "Sync License", status.display_text)

    def _deactivate(self):
        if deactivate_license:
            deactivate_license(PRODUCT_SLUG)
        self.email_input.clear()
        self.license_input.clear()
        self._refresh_status()
        QMessageBox.information(self, "Deactivate", "The local SavedCode license token was removed.")


class Tool(Enum):
    FREEHAND = auto()
    LINE = auto()
    ARROW = auto()
    RECT = auto()
    ELLIPSE = auto()
    TEXT = auto()
    SPRAY = auto()
    HIGHLIGHTER = auto()
    ERASER = auto()


TOOL_LABELS = {
    Tool.FREEHAND: ("Freehand", "F"),
    Tool.LINE: ("Line", "L"),
    Tool.ARROW: ("Arrow", "A"),
    Tool.RECT: ("Rectangle", "R"),
    Tool.ELLIPSE: ("Ellipse", "E"),
    Tool.TEXT: ("Text", "T"),
    Tool.SPRAY: ("Spray", "S"),
    Tool.HIGHLIGHTER: ("Highlighter", "H"),
    Tool.ERASER: ("Eraser", "X"),
}

PRESET_COLORS = [
    "#FF0000", "#FF5722", "#FF9800", "#FFEB3B",
    "#4CAF50", "#2196F3", "#3F51B5", "#9C27B0",
    "#FFFFFF", "#000000", "#607D8B", "#795548",
]

SIDEBAR_CSS = """
QFrame#sidebar {
    background: rgba(25, 25, 30, 230);
    border-right: 1px solid #444;
}
QFrame#sidebar QPushButton {
    color: #eee;
    background: rgba(255,255,255,15);
    border: 1px solid #555;
    border-radius: 5px;
    padding: 6px 10px;
    font-size: 12px;
    text-align: left;
}
QFrame#sidebar QPushButton:hover { background: rgba(255,255,255,40); }
QFrame#sidebar QPushButton:checked {
    background: rgba(80,140,255,120);
    border-color: #7ab;
}
QFrame#sidebar QPushButton#colorSwatch {
    min-width: 28px; min-height: 28px;
    max-width: 28px; max-height: 28px;
    border-radius: 4px; padding: 0;
}
QFrame#sidebar QLabel {
    color: #bbb;
    font-size: 11px;
    padding: 0;
}
QFrame#sidebar QLabel#heading {
    color: #ddd;
    font-size: 13px;
    font-weight: bold;
    padding: 4px 0;
}
QFrame#sidebar QSlider::groove:horizontal {
    height: 6px; background: #444; border-radius: 3px;
}
QFrame#sidebar QSlider::handle:horizontal {
    width: 14px; height: 14px; margin: -4px 0;
    background: #ccc; border-radius: 7px;
}
"""


class Canvas(QWidget):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setMouseTracking(True)
        # Opaque enough to capture all clicks, transparent enough to see desktop
        self.setAutoFillBackground(False)

        screen = QApplication.primaryScreen().size()
        self.buffer = QPixmap(screen.width(), screen.height())
        self.buffer.fill(Qt.GlobalColor.transparent)

        self.tool = Tool.FREEHAND
        self.pen_color = QColor("#2196F3")
        self.pen_width = 3
        self.font_size = 24
        self.overlay_opacity = 1  # 1 = barely visible tint over desktop

        self._drawing = False
        self._start = QPoint()
        self._last = QPoint()
        self._path_points: list[QPoint] = []
        self._preview = None

        # Custom crosshair cursor
        cursor_path = os.path.join(os.path.dirname(__file__), "crosshair.png")
        if os.path.exists(cursor_path):
            cursor_pix = QPixmap(cursor_path)
            self._draw_cursor = QCursor(cursor_pix, cursor_pix.width() // 2, cursor_pix.height() // 2)
        else:
            self._draw_cursor = QCursor(Qt.CursorShape.CrossCursor)
        self._normal_cursor = QCursor(Qt.CursorShape.ArrowCursor)
        self._cursor_dir = os.path.dirname(__file__)
        self.setCursor(self._draw_cursor)

    def set_cursor_style(self, name: str):
        """Switch to a different cursor PNG by name."""
        path = os.path.join(self._cursor_dir, f"{name}.png")
        if name == "pen":
            hotspot_x, hotspot_y = 4, 30  # tip of pen
        elif name == "arrow":
            hotspot_x, hotspot_y = 6, 4  # tip of arrow
        else:
            hotspot_x, hotspot_y = None, None  # center

        if os.path.exists(path):
            pix = QPixmap(path)
            hx = hotspot_x if hotspot_x is not None else pix.width() // 2
            hy = hotspot_y if hotspot_y is not None else pix.height() // 2
            self._draw_cursor = QCursor(pix, hx, hy)
        else:
            self._draw_cursor = QCursor(Qt.CursorShape.CrossCursor)
        self.setCursor(self._draw_cursor)

    def paintEvent(self, event):
        p = QPainter(self)
        # Draw semi-transparent overlay so clicks don't pass through
        p.fillRect(self.rect(), QColor(0, 0, 0, self.overlay_opacity))
        # Draw the canvas buffer on top
        p.drawPixmap(0, 0, self._preview if self._preview else self.buffer)
        p.end()

    def mousePressEvent(self, event):
        if event.button() != Qt.MouseButton.LeftButton:
            return
        self._drawing = True
        self._start = event.pos()
        self._last = event.pos()
        self._path_points = [event.pos()]

    def mouseMoveEvent(self, event):
        if not self._drawing:
            return
        pos = event.pos()

        if self.tool == Tool.FREEHAND:
            self._draw_segment(self._last, pos)
            self._last = pos
        elif self.tool == Tool.SPRAY:
            self._draw_segment(self._last, pos, spray=True)
            self._last = pos
        elif self.tool == Tool.ERASER:
            self._erase_at(pos)
            self._last = pos
        elif self.tool == Tool.HIGHLIGHTER:
            self._preview = self.buffer.copy()
            p = QPainter(self._preview)
            p.setRenderHint(QPainter.RenderHint.Antialiasing)
            p.setPen(self._make_highlighter_pen())
            p.drawLine(self._start, pos)
            p.end()
        else:
            self._preview = self.buffer.copy()
            p = QPainter(self._preview)
            p.setRenderHint(QPainter.RenderHint.Antialiasing)
            p.setPen(self._make_pen())
            self._draw_shape(p, self._start, pos)
            p.end()

        self._path_points.append(pos)
        self.update()

    def mouseReleaseEvent(self, event):
        if event.button() != Qt.MouseButton.LeftButton or not self._drawing:
            return
        self._drawing = False
        pos = event.pos()

        if self.tool == Tool.TEXT:
            self._place_text(pos)
        elif self.tool == Tool.HIGHLIGHTER:
            p = QPainter(self.buffer)
            p.setRenderHint(QPainter.RenderHint.Antialiasing)
            p.setPen(self._make_highlighter_pen())
            p.drawLine(self._start, pos)
            p.end()
        elif self.tool in (Tool.LINE, Tool.ARROW, Tool.RECT, Tool.ELLIPSE):
            p = QPainter(self.buffer)
            p.setRenderHint(QPainter.RenderHint.Antialiasing)
            p.setPen(self._make_pen())
            self._draw_shape(p, self._start, pos)
            p.end()

        self._preview = None
        self.update()

    def _make_pen(self, spray=False) -> QPen:
        pen = QPen(self.pen_color, self.pen_width)
        pen.setCapStyle(Qt.PenCapStyle.RoundCap)
        pen.setJoinStyle(Qt.PenJoinStyle.RoundJoin)
        if spray:
            c = QColor(self.pen_color)
            c.setAlpha(60)
            pen.setColor(c)
            pen.setWidth(max(self.pen_width * 6, 20))
        return pen

    def _make_highlighter_pen(self) -> QPen:
        c = QColor(self.pen_color)
        c.setAlpha(75)  # ~30% opacity (70% transparent)
        pen = QPen(c, max(self.pen_width * 5, 18))
        pen.setCapStyle(Qt.PenCapStyle.FlatCap)
        return pen

    def _draw_segment(self, p1: QPoint, p2: QPoint, spray=False):
        p = QPainter(self.buffer)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setPen(self._make_pen(spray))
        p.drawLine(p1, p2)
        p.end()

    def _draw_shape(self, painter: QPainter, start: QPoint, end: QPoint):
        if self.tool == Tool.LINE:
            painter.drawLine(start, end)
        elif self.tool == Tool.ARROW:
            painter.drawLine(start, end)
            self._draw_arrowhead(painter, start, end)
        elif self.tool == Tool.RECT:
            painter.drawRect(QRect(start, end).normalized())
        elif self.tool == Tool.ELLIPSE:
            painter.drawEllipse(QRect(start, end).normalized())

    def _draw_arrowhead(self, painter: QPainter, start: QPoint, end: QPoint):
        angle = math.atan2(end.y() - start.y(), end.x() - start.x())
        arrow_len = max(14, self.pen_width * 4)
        spread = math.radians(25)
        p1 = QPoint(
            int(end.x() - arrow_len * math.cos(angle - spread)),
            int(end.y() - arrow_len * math.sin(angle - spread)),
        )
        p2 = QPoint(
            int(end.x() - arrow_len * math.cos(angle + spread)),
            int(end.y() - arrow_len * math.sin(angle + spread)),
        )
        path = QPainterPath()
        path.moveTo(end.toPointF())
        path.lineTo(p1.toPointF())
        path.lineTo(p2.toPointF())
        path.closeSubpath()
        painter.setBrush(self.pen_color)
        painter.drawPath(path)

    def _erase_at(self, pos: QPoint):
        r = max(self.pen_width * 3, 16)
        p = QPainter(self.buffer)
        p.setCompositionMode(QPainter.CompositionMode.CompositionMode_Clear)
        p.setRenderHint(QPainter.RenderHint.Antialiasing)
        p.setBrush(Qt.GlobalColor.transparent)
        p.setPen(Qt.PenStyle.NoPen)
        p.drawEllipse(pos, r, r)
        p.end()

    def _place_text(self, pos: QPoint):
        text, ok = QInputDialog.getText(self, "Add Text", "Enter text:")
        if not ok or not text:
            return
        p = QPainter(self.buffer)
        p.setRenderHint(QPainter.RenderHint.TextAntialiasing)
        p.setPen(QPen(self.pen_color))
        p.setFont(QFont("Segoe UI", self.font_size))
        p.drawText(pos, text)
        p.end()

    def clear(self):
        self.buffer.fill(Qt.GlobalColor.transparent)
        self._preview = None
        self.update()


class Sidebar(QFrame):
    def __init__(self, parent=None):
        super().__init__(parent)
        self.setObjectName("sidebar")
        self.setFixedWidth(215)
        self.setStyleSheet(SIDEBAR_CSS)

        # Scroll area wrapping all content
        outer = QVBoxLayout(self)
        outer.setContentsMargins(0, 0, 0, 0)
        outer.setSpacing(0)

        scroll = QScrollArea()
        scroll.setWidgetResizable(True)
        scroll.setHorizontalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAlwaysOff)
        scroll.setVerticalScrollBarPolicy(Qt.ScrollBarPolicy.ScrollBarAsNeeded)
        scroll.setStyleSheet("""
            QScrollArea { background: transparent; border: none; }
            QScrollBar:vertical {
                background: rgba(255,255,255,10);
                width: 8px;
                border-radius: 4px;
            }
            QScrollBar::handle:vertical {
                background: rgba(255,255,255,60);
                min-height: 30px;
                border-radius: 4px;
            }
            QScrollBar::add-line:vertical, QScrollBar::sub-line:vertical { height: 0; }
        """)
        outer.addWidget(scroll)

        content = QWidget()
        content.setObjectName("sidebar")  # so CSS selectors still match
        content.setStyleSheet("background: transparent;")
        scroll.setWidget(content)

        layout = QVBoxLayout(content)
        layout.setContentsMargins(10, 10, 10, 10)
        layout.setSpacing(6)

        # Title
        title = QLabel("Draw Overlay")
        title.setObjectName("heading")
        title.setAlignment(Qt.AlignmentFlag.AlignCenter)
        layout.addWidget(title)

        self._hint = QLabel("")
        self._hint.setAlignment(Qt.AlignmentFlag.AlignCenter)
        self._hint.setStyleSheet("color: #ffffff; font-size: 13px;")
        layout.addWidget(self._hint)

        ACTION_BTN_STYLE = """
            QPushButton {
                background: rgba(50,180,50,180);
                color: #fff;
                font-size: 12px;
                font-weight: bold;
                padding: 8px;
                border: 2px solid #4a4;
                border-radius: 6px;
            }
            QPushButton:hover { background: rgba(50,200,50,220); }
        """

        # Mode toggle: "Hold Ctrl to Draw" vs "Always On"
        self._mode_btn = QPushButton("")
        self._mode_btn.setStyleSheet(ACTION_BTN_STYLE)
        self._mode_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._mode_btn.clicked.connect(self._on_mode_toggle)
        layout.addWidget(self._mode_btn)

        # Big overlay toggle button
        self._overlay_btn = QPushButton("  Overlay OFF  (Ctrl+H)")
        self._overlay_btn.setStyleSheet(ACTION_BTN_STYLE)
        self._overlay_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._overlay_btn.clicked.connect(self._on_hide)
        layout.addWidget(self._overlay_btn)

        self._add_separator(layout)

        # Tools heading
        layout.addWidget(self._heading("Tools"))

        self.tool_buttons: dict[Tool, QPushButton] = {}
        for tool, (label, shortcut) in TOOL_LABELS.items():
            btn = QPushButton(f"  {label}  ({shortcut})")
            btn.setCheckable(True)
            btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
            btn.clicked.connect(lambda checked, t=tool: self._on_tool_click(t))
            layout.addWidget(btn)
            self.tool_buttons[tool] = btn
        self.tool_buttons[Tool.FREEHAND].setChecked(True)

        self._add_separator(layout)

        # Colors heading
        layout.addWidget(self._heading("Colors"))

        color_grid = QGridLayout()
        color_grid.setSpacing(4)
        self._color_btns: list[QPushButton] = []
        for i, hex_c in enumerate(PRESET_COLORS):
            btn = QPushButton()
            btn.setObjectName("colorSwatch")
            btn.setStyleSheet(
                f"QPushButton#colorSwatch {{ background: {hex_c}; border: 2px solid #666; }}"
                f"QPushButton#colorSwatch:hover {{ border-color: #fff; }}"
            )
            btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
            btn.clicked.connect(lambda checked, c=hex_c: self._on_color_click(c))
            color_grid.addWidget(btn, i // 4, i % 4)
            self._color_btns.append(btn)
        layout.addLayout(color_grid)

        custom_btn = QPushButton("  Custom Color...")
        custom_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        custom_btn.clicked.connect(self._on_custom_color)
        layout.addWidget(custom_btn)

        # Active color indicator
        self._active_color_label = QLabel("Active:")
        self._active_color_box = QFrame()
        self._active_color_box.setFixedSize(60, 18)
        self._active_color_box.setStyleSheet("background: #2196F3; border: 1px solid #888; border-radius: 3px;")
        row = QHBoxLayout()
        row.addWidget(self._active_color_label)
        row.addWidget(self._active_color_box)
        row.addStretch()
        layout.addLayout(row)

        self._add_separator(layout)

        # Width slider
        layout.addWidget(self._heading("Line Width"))
        self._width_slider = QSlider(Qt.Orientation.Horizontal)
        self._width_slider.setRange(1, 30)
        self._width_slider.setValue(3)
        self._width_slider.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._width_label = QLabel("3")
        row = QHBoxLayout()
        row.addWidget(self._width_slider)
        row.addWidget(self._width_label)
        layout.addLayout(row)

        # Font size slider
        layout.addWidget(self._heading("Font Size"))
        self._font_slider = QSlider(Qt.Orientation.Horizontal)
        self._font_slider.setRange(8, 72)
        self._font_slider.setValue(24)
        self._font_slider.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._font_label = QLabel("24")
        row = QHBoxLayout()
        row.addWidget(self._font_slider)
        row.addWidget(self._font_label)
        layout.addLayout(row)

        # Cursor selector
        layout.addWidget(self._heading("Cursor"))
        self._cursor_combo = QComboBox()
        self._cursor_combo.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        self._cursor_combo.setStyleSheet("""
            QComboBox {
                color: #eee;
                background: rgba(255,255,255,15);
                border: 1px solid #555;
                border-radius: 5px;
                padding: 5px 8px;
                font-size: 12px;
            }
            QComboBox:hover { background: rgba(255,255,255,40); }
            QComboBox::drop-down { border: none; }
            QComboBox::down-arrow { image: none; border: none; }
            QComboBox QAbstractItemView {
                color: #eee;
                background: rgba(30,30,35,240);
                border: 1px solid #555;
                selection-background-color: rgba(80,140,255,120);
            }
        """)
        CURSOR_OPTIONS = [
            ("Crosshair", "crosshair"),
            ("Target", "target"),
            ("Dot", "dot"),
            ("Circle", "circle"),
            ("Plus", "plus"),
            ("Pen", "pen"),
            ("Arrow", "arrow"),
        ]
        for label, _ in CURSOR_OPTIONS:
            self._cursor_combo.addItem(label)
        self._cursor_options = CURSOR_OPTIONS
        self._cursor_combo.currentIndexChanged.connect(self._on_cursor_changed)
        layout.addWidget(self._cursor_combo)

        self._add_separator(layout)

        # License
        layout.addWidget(self._heading("SavedCode Pro"))
        self._license_status = QLabel("Free")
        self._license_status.setWordWrap(True)
        layout.addWidget(self._license_status)

        license_btn = QPushButton("  Settings / License...")
        license_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        license_btn.clicked.connect(self._on_license)
        layout.addWidget(license_btn)

        self._add_separator(layout)

        # Action buttons
        clear_btn = QPushButton("  Clear Canvas  (C)")
        clear_btn.setStyleSheet(ACTION_BTN_STYLE)
        clear_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        clear_btn.clicked.connect(self._on_clear)
        layout.addWidget(clear_btn)

        quit_btn = QPushButton("  Quit  (Esc)")
        quit_btn.setStyleSheet(ACTION_BTN_STYLE)
        quit_btn.setFocusPolicy(Qt.FocusPolicy.NoFocus)
        quit_btn.clicked.connect(self._on_quit)
        layout.addWidget(quit_btn)

        layout.addStretch()

        # Callbacks (set by OverlayWindow)
        self.on_tool_changed = None
        self.on_color_changed = None
        self.on_width_changed = None
        self.on_font_changed = None
        self.on_clear = None
        self.on_hide = None
        self.on_mode_toggle = None
        self.on_cursor_changed = None
        self.on_license = None
        self.on_quit = None

        # Mode: "hold_ctrl" = hold Ctrl to show overlay; "always_on" = overlay stays, Ctrl clicks under
        self._mode = "always_on"
        self._update_mode_ui()

        self._width_slider.valueChanged.connect(self._on_width)
        self._font_slider.valueChanged.connect(self._on_font)

    def _heading(self, text: str) -> QLabel:
        lbl = QLabel(text)
        lbl.setObjectName("heading")
        return lbl

    def _add_separator(self, layout):
        sep = QFrame()
        sep.setFrameShape(QFrame.Shape.HLine)
        sep.setStyleSheet("color: #444;")
        layout.addWidget(sep)

    def _on_tool_click(self, tool: Tool):
        for t, btn in self.tool_buttons.items():
            btn.setChecked(t == tool)
        if self.on_tool_changed:
            self.on_tool_changed(tool)

    def _on_color_click(self, hex_color: str):
        self._active_color_box.setStyleSheet(
            f"background: {hex_color}; border: 1px solid #888; border-radius: 3px;"
        )
        if self.on_color_changed:
            self.on_color_changed(QColor(hex_color))

    def _on_custom_color(self):
        # Pause ctrl polling and hide overlay so dialog is usable
        win = self.window()
        if hasattr(win, '_ctrl_poll_timer'):
            win._ctrl_poll_timer.stop()
        was_visible = win.isVisible()
        win.hide()

        c = QColorDialog.getColor(Qt.GlobalColor.red, None, "Pick Color")

        # Restore overlay
        if was_visible:
            win.showFullScreen()
        if hasattr(win, '_ctrl_poll_timer'):
            win._ctrl_poll_timer.start()

        if c.isValid():
            self._active_color_box.setStyleSheet(
                f"background: {c.name()}; border: 1px solid #888; border-radius: 3px;"
            )
            if self.on_color_changed:
                self.on_color_changed(c)

    def _on_width(self, val):
        self._width_label.setText(str(val))
        if self.on_width_changed:
            self.on_width_changed(val)

    def _on_font(self, val):
        self._font_label.setText(str(val))
        if self.on_font_changed:
            self.on_font_changed(val)

    def _on_cursor_changed(self, index):
        _, cursor_name = self._cursor_options[index]
        if self.on_cursor_changed:
            self.on_cursor_changed(cursor_name)

    def _on_clear(self):
        if self.on_clear:
            self.on_clear()

    def _on_hide(self):
        if self.on_hide:
            self.on_hide()

    def _on_license(self):
        if self.on_license:
            self.on_license()

    def _on_mode_toggle(self):
        if self._mode == "hold_ctrl":
            self._mode = "always_on"
        else:
            self._mode = "hold_ctrl"
        self._update_mode_ui()
        if self.on_mode_toggle:
            self.on_mode_toggle(self._mode)

    def _update_mode_ui(self):
        if self._mode == "hold_ctrl":
            self._mode_btn.setText("  Mode: Hold Ctrl to Draw")
            self._hint.setText("Hold Ctrl = show & draw\nRelease = back to desktop\nTab = sidebar | Ctrl+H = lock")
        else:
            self._mode_btn.setText("  Mode: Always On")
            self._hint.setText("Click = draw\nCtrl = use desktop under\nTab = sidebar | Ctrl+H = hide")

    def _on_quit(self):
        if self.on_quit:
            self.on_quit()

    def select_tool(self, tool: Tool):
        """Programmatically select a tool (from keyboard shortcut)."""
        self._on_tool_click(tool)

    def set_license_status(self, text: str):
        self._license_status.setText(text)


class OverlayWindow(QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle(APP_NAME)
        self.setWindowFlags(
            Qt.WindowType.FramelessWindowHint
            | Qt.WindowType.WindowStaysOnTopHint
            | Qt.WindowType.Tool
        )
        self.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)

        screen = QApplication.primaryScreen().size()
        self.setGeometry(0, 0, screen.width(), screen.height())

        # Central container
        container = QWidget()
        container.setAttribute(Qt.WidgetAttribute.WA_TranslucentBackground)
        hlayout = QHBoxLayout(container)
        hlayout.setContentsMargins(0, 0, 0, 0)
        hlayout.setSpacing(0)

        # Sidebar
        self.sidebar = Sidebar()
        hlayout.addWidget(self.sidebar)

        # Canvas
        self.canvas = Canvas()
        hlayout.addWidget(self.canvas)

        self.setCentralWidget(container)

        # Load saved settings
        self._settings = load_settings()

        # Wire sidebar callbacks (with save-on-change)
        self.sidebar.on_tool_changed = self._on_tool_changed
        self.sidebar.on_color_changed = self._on_color_changed
        self.sidebar.on_width_changed = self._on_width_changed
        self.sidebar.on_font_changed = self._on_font_changed
        self.sidebar.on_clear = self.canvas.clear
        self.sidebar.on_hide = self.hide_overlay
        self.sidebar.on_mode_toggle = self._on_mode_changed
        self.sidebar.on_cursor_changed = self._on_cursor_changed
        self.sidebar.on_license = self._show_license_dialog
        self.sidebar.on_quit = self._quit_app

        # Apply saved settings to canvas and sidebar
        self._apply_settings()

        self._sidebar_visible = True
        self._overlay_visible = True
        self._mode = self._settings["mode"]
        self._locked = False
        self._setup_tray()
        self._setup_shortcuts()
        self._setup_global_hotkey()
        self._setup_ctrl_poll()
        self._refresh_license_ui()

        # Start based on mode
        if self._mode == "always_on":
            self.showFullScreen()
        else:
            self._overlay_visible = False
            self.hide()

    def _setup_tray(self):
        # Create a small colored icon for the tray
        tray_pix = QPixmap(32, 32)
        tray_pix.fill(QColor("#2196F3"))
        p = QPainter(tray_pix)
        p.setPen(QPen(QColor("#FFFFFF"), 3))
        p.setFont(QFont("Segoe UI", 18, QFont.Weight.Bold))
        p.drawText(tray_pix.rect(), Qt.AlignmentFlag.AlignCenter, "D")
        p.end()

        self._tray = QSystemTrayIcon(QIcon(tray_pix), self)
        tray_menu = QMenu()
        show_act = QAction("Show / Hide  (Ctrl+H)", self)
        show_act.triggered.connect(self.toggle_overlay)
        tray_menu.addAction(show_act)
        license_act = QAction("Settings / License...", self)
        license_act.triggered.connect(self._show_license_dialog)
        tray_menu.addAction(license_act)
        quit_act = QAction("Quit", self)
        quit_act.triggered.connect(self._quit_app)
        tray_menu.addAction(quit_act)
        self._tray.setContextMenu(tray_menu)
        self._tray.activated.connect(self._on_tray_click)
        self._tray.setToolTip("Draw Overlay — Ctrl+H to toggle")
        self._tray.show()

    def _on_tray_click(self, reason):
        if reason == QSystemTrayIcon.ActivationReason.Trigger:
            self.toggle_overlay()

    def _setup_global_hotkey(self):
        """Register Ctrl+H as a global hotkey using a dedicated hidden Win32 window
        so messages are never swallowed by Qt's event loop."""
        import ctypes
        import ctypes.wintypes
        import threading

        self._user32 = ctypes.windll.user32
        self._HOTKEY_ID = 1
        self._hotkey_fired = False

        def _hotkey_thread():
            MOD_CONTROL = 0x0002
            VK_H = 0x48
            WM_HOTKEY = 0x0312

            # Register hotkey on this thread's message queue
            self._user32.RegisterHotKey(None, self._HOTKEY_ID, MOD_CONTROL, VK_H)

            msg = ctypes.wintypes.MSG()
            while self._user32.GetMessageW(ctypes.byref(msg), None, 0, 0) != 0:
                if msg.message == WM_HOTKEY and msg.wParam == self._HOTKEY_ID:
                    self._hotkey_fired = True

        t = threading.Thread(target=_hotkey_thread, daemon=True)
        t.start()

        # Timer to check the flag from the main/Qt thread
        self._hotkey_timer = QTimer(self)
        self._hotkey_timer.setInterval(50)
        self._hotkey_timer.timeout.connect(self._check_hotkey_flag)
        self._hotkey_timer.start()

    def _check_hotkey_flag(self):
        if self._hotkey_fired:
            self._hotkey_fired = False
            self.toggle_overlay()

    def _setup_shortcuts(self):
        # Toggle sidebar
        QShortcut(QKeySequence(Qt.Key.Key_Tab), self).activated.connect(self._toggle_sidebar)

        # Tool shortcuts
        for tool, (_, key) in TOOL_LABELS.items():
            QShortcut(QKeySequence(key), self).activated.connect(
                lambda t=tool: self.sidebar.select_tool(t)
            )

        # Clear
        QShortcut(QKeySequence("C"), self).activated.connect(self.canvas.clear)

        # Ctrl+H is handled by the global hotkey thread — no QShortcut needed

        # Quit
        QShortcut(QKeySequence(Qt.Key.Key_Escape), self).activated.connect(self._quit_app)

    def _setup_ctrl_poll(self):
        """Poll Ctrl key to handle both modes."""
        import ctypes
        self._ctrl_user32 = ctypes.windll.user32
        self._ctrl_held = False
        self._ctrl_poll_timer = QTimer(self)
        self._ctrl_poll_timer.setInterval(30)
        self._ctrl_poll_timer.timeout.connect(self._poll_ctrl)
        self._ctrl_poll_timer.start()

    def _poll_ctrl(self):
        VK_CONTROL = 0x11
        pressed = bool(self._ctrl_user32.GetAsyncKeyState(VK_CONTROL) & 0x8000)

        if pressed == self._ctrl_held:
            return
        self._ctrl_held = pressed

        if self._locked:
            return  # Ctrl+H locked — don't auto-toggle

        if self._mode == "hold_ctrl":
            # Hold Ctrl = show overlay to draw, release = hide
            if pressed:
                self._show_no_lock()
            else:
                self._hide_no_lock()
        else:
            # Always On mode: Ctrl = hide overlay to click desktop
            if pressed:
                self._hide_no_lock()
            else:
                self._show_no_lock()

    def _show_no_lock(self):
        if not self._overlay_visible:
            self._overlay_visible = True
            self.showFullScreen()

    def _hide_no_lock(self):
        if self._overlay_visible:
            self._overlay_visible = False
            self.hide()

    def _license_status_text(self) -> str:
        if LICENSE_IMPORT_ERROR:
            return "License unavailable"
        try:
            return get_license_status(PRODUCT_SLUG).display_text
        except SavedCodeLicenseError as exc:
            return f"Free - {exc}"

    def _refresh_license_ui(self):
        status_text = self._license_status_text()
        self.sidebar.set_license_status(status_text)
        if hasattr(self, "_tray"):
            self._tray.setToolTip(f"{APP_NAME} - {status_text}")

    def _show_license_dialog(self):
        if hasattr(self, "_ctrl_poll_timer"):
            self._ctrl_poll_timer.stop()

        was_visible = self._overlay_visible
        self.hide()
        try:
            dialog = LicenseDialog()
            dialog.setWindowModality(Qt.WindowModality.ApplicationModal)
            dialog.exec()
        finally:
            self._refresh_license_ui()
            if hasattr(self, "_ctrl_poll_timer"):
                self._ctrl_poll_timer.start()
            if was_visible:
                self.show_overlay()
            else:
                self.hide_overlay()

    # ---- settings persistence ------------------------------------------------

    def _save(self):
        save_settings(self._settings)

    def _apply_settings(self):
        """Apply loaded settings to canvas and sidebar widgets."""
        s = self._settings

        # Tool
        try:
            tool = Tool[s["tool"]]
        except KeyError:
            tool = Tool.FREEHAND
        self.canvas.tool = tool
        self.sidebar.select_tool(tool)

        # Color
        color = QColor(s["color"])
        self.canvas.pen_color = color
        self.sidebar._active_color_box.setStyleSheet(
            f"background: {color.name()}; border: 1px solid #888; border-radius: 3px;"
        )

        # Width
        self.canvas.pen_width = s["pen_width"]
        self.sidebar._width_slider.setValue(s["pen_width"])

        # Font size
        self.canvas.font_size = s["font_size"]
        self.sidebar._font_slider.setValue(s["font_size"])

        # Cursor
        cursor_name = s["cursor"]
        self.canvas.set_cursor_style(cursor_name)
        for i, (_, name) in enumerate(self.sidebar._cursor_options):
            if name == cursor_name:
                self.sidebar._cursor_combo.setCurrentIndex(i)
                break

        # Mode
        self.sidebar._mode = s["mode"]
        self.sidebar._update_mode_ui()

    def _on_tool_changed(self, tool: Tool):
        self.canvas.tool = tool
        self._settings["tool"] = tool.name
        self._save()

    def _on_color_changed(self, color: QColor):
        self.canvas.pen_color = color
        self._settings["color"] = color.name()
        self._save()

    def _on_width_changed(self, val: int):
        self.canvas.pen_width = val
        self._settings["pen_width"] = val
        self._save()

    def _on_font_changed(self, val: int):
        self.canvas.font_size = val
        self._settings["font_size"] = val
        self._save()

    def _on_cursor_changed(self, cursor_name: str):
        self.canvas.set_cursor_style(cursor_name)
        self._settings["cursor"] = cursor_name
        self._save()

    def _on_mode_changed(self, mode):
        self._mode = mode
        self._locked = False
        self._settings["mode"] = mode
        self._save()
        if mode == "hold_ctrl":
            self._overlay_visible = False
            self.hide()
        else:
            self._overlay_visible = True
            self.showFullScreen()

    def _toggle_sidebar(self):
        self._sidebar_visible = not self._sidebar_visible
        self.sidebar.setVisible(self._sidebar_visible)

    def hide_overlay(self):
        self._overlay_visible = False
        self.hide()

    def show_overlay(self):
        self._overlay_visible = True
        self.showFullScreen()

    def toggle_overlay(self):
        """Ctrl+H — locks/unlocks the overlay state."""
        self._locked = not self._locked
        if self._overlay_visible:
            self.hide_overlay()
        else:
            self.show_overlay()

    def _quit_app(self):
        self._hotkey_timer.stop()
        self._ctrl_poll_timer.stop()
        self._tray.hide()
        QApplication.quit()


def main():
    app = QApplication(sys.argv)
    app.setApplicationName("DrawOverlay")
    # Keep app running when window is hidden
    app.setQuitOnLastWindowClosed(False)
    window = OverlayWindow()
    sys.exit(app.exec())


if __name__ == "__main__":
    main()
