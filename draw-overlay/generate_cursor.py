"""Generate a crosshair cursor PNG for the draw overlay."""
from PyQt6.QtWidgets import QApplication
from PyQt6.QtGui import QPixmap, QPainter, QPen, QColor
from PyQt6.QtCore import Qt
import sys

app = QApplication(sys.argv)

size = 32
pix = QPixmap(size, size)
pix.fill(QColor(0, 0, 0, 0))  # transparent

p = QPainter(pix)
p.setRenderHint(QPainter.RenderHint.Antialiasing)
mid = size // 2
gap = 3  # gap around center so there's a clear dot area

# White outline (for visibility on dark backgrounds)
outline = QPen(QColor(255, 255, 255, 200), 3)
outline.setCapStyle(Qt.PenCapStyle.RoundCap)
p.setPen(outline)
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)

# Red crosshair lines
line_pen = QPen(QColor(255, 50, 50, 230), 1.5)
line_pen.setCapStyle(Qt.PenCapStyle.RoundCap)
p.setPen(line_pen)
p.drawLine(mid, 0, mid, mid - gap)
p.drawLine(mid, mid + gap, mid, size - 1)
p.drawLine(0, mid, mid - gap, mid)
p.drawLine(mid + gap, mid, size - 1, mid)

# Center dot
p.setPen(Qt.PenStyle.NoPen)
p.setBrush(QColor(255, 50, 50, 255))
p.drawEllipse(mid - 2, mid - 2, 4, 4)

p.end()
pix.save("crosshair.png")
print("Saved crosshair.png (32x32)")
