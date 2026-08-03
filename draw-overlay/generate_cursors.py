"""Generate all cursor PNGs for the draw overlay."""
from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QPixmap, QPainter, QPen, QColor, QFont, QPainterPath
from PyQt6.QtCore import Qt, QPointF
import sys
import math

app = QApplication(sys.argv)
size = 32
mid = size // 2


def new_pixmap():
    pix = QPixmap(size, size)
    pix.fill(QColor(0, 0, 0, 0))
    return pix


def save(pix, name):
    pix.save(f"{name}.png")
    print(f"  Saved {name}.png")


# 1. Crosshair (+)
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
gap = 3
outline = QPen(QColor(255, 255, 255, 200), 3)
outline.setCapStyle(Qt.PenCapStyle.RoundCap)
p.setPen(outline)
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)
line_pen = QPen(QColor(255, 50, 50, 230), 1.5)
line_pen.setCapStyle(Qt.PenCapStyle.RoundCap)
p.setPen(line_pen)
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)
p.setPen(Qt.PenStyle.NoPen)
p.setBrush(QColor(255, 50, 50, 255))
p.drawEllipse(mid - 2, mid - 2, 4, 4)
p.end()
save(pix, "crosshair")

# 2. Dot
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
p.setPen(QPen(QColor(255, 255, 255, 200), 2))
p.setBrush(QColor(255, 60, 60, 230))
p.drawEllipse(mid - 5, mid - 5, 10, 10)
p.end()
save(pix, "dot")

# 3. Circle outline
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
p.setPen(QPen(QColor(255, 255, 255, 200), 3))
p.setBrush(Qt.BrushStyle.NoBrush)
p.drawEllipse(4, 4, size - 8, size - 8)
p.setPen(QPen(QColor(255, 50, 50, 230), 1.5))
p.drawEllipse(4, 4, size - 8, size - 8)
# center dot
p.setPen(Qt.PenStyle.NoPen)
p.setBrush(QColor(255, 50, 50, 255))
p.drawEllipse(mid - 1, mid - 1, 3, 3)
p.end()
save(pix, "circle")

# 4. Pen (ballpoint style, rounded tip at bottom-left)
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
# Pen body (angled barrel)
body = QPainterPath()
body.moveTo(8, 26)        # where tip meets body
body.lineTo(20, 6)        # top-left of barrel
body.lineTo(26, 10)       # top-right of barrel
body.lineTo(14, 30)       # bottom-right
body.closeSubpath()
p.setPen(QPen(QColor(255, 255, 255, 180), 1.5))
p.setBrush(QColor(60, 60, 70, 230))
p.drawPath(body)
# Pen tip — rounded ball point
p.setPen(QPen(QColor(255, 255, 255, 180), 1.5))
p.setBrush(QColor(180, 180, 190, 240))
p.drawEllipse(3, 27, 8, 8)  # round ball at tip
# Gold clip/band
p.setPen(QPen(QColor(220, 180, 50, 230), 2))
p.drawLine(19, 8, 25, 12)
# Grip lines
p.setPen(QPen(QColor(90, 90, 100, 150), 1))
p.drawLine(15, 18, 21, 22)
p.drawLine(14, 20, 20, 24)
p.end()
save(pix, "pen")

# 5. Target (crosshair with circle)
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
r = 10
# white outline
p.setPen(QPen(QColor(255, 255, 255, 200), 3))
p.setBrush(Qt.BrushStyle.NoBrush)
p.drawEllipse(mid - r, mid - r, r * 2, r * 2)
gap = 3
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)
# red lines
p.setPen(QPen(QColor(255, 50, 50, 230), 1.5))
p.drawEllipse(mid - r, mid - r, r * 2, r * 2)
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)
# center dot
p.setPen(Qt.PenStyle.NoPen)
p.setBrush(QColor(255, 50, 50, 255))
p.drawEllipse(mid - 2, mid - 2, 4, 4)
p.end()
save(pix, "target")

# 6. Minimal plus (small, thin)
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
arm = 6
p.setPen(QPen(QColor(255, 255, 255, 220), 3))
p.drawLine(mid, mid - arm, mid, mid + arm)
p.drawLine(mid - arm, mid, mid + arm, mid)
p.setPen(QPen(QColor(0, 0, 0, 230), 1.5))
p.drawLine(mid, mid - arm, mid, mid + arm)
p.drawLine(mid - arm, mid, mid + arm, mid)
p.end()
save(pix, "plus")

# 7. Arrow pointer (smaller, classic)
pix = new_pixmap()
p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
path = QPainterPath()
path.moveTo(6, 4)
path.lineTo(6, 20)
path.lineTo(10, 16)
path.lineTo(14, 22)
path.lineTo(17, 20)
path.lineTo(13, 14)
path.lineTo(18, 13)
path.closeSubpath()
p.setPen(QPen(QColor(255, 255, 255, 220), 1.5))
p.setBrush(QColor(30, 30, 30, 230))
p.drawPath(path)
p.end()
save(pix, "arrow")

print("\nAll cursors generated!")
