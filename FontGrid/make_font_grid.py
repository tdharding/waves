"""Generates a hand-drawn font template: one fixed-size cell per character.

Outputs (next to this script):
  FontGrid_Guides.png  - white sheet with guide lines + labels, draw on a layer above it
  FontGrid_Layout.json - which character sits in which cell, and where the guide lines are
"""
import json
import os
from PIL import Image, ImageDraw, ImageFont

CELL = 256          # px per cell (square)
COLS = 10
CHARS = (
    "ABCDEFGHIJ" "KLMNOPQRST" "UVWXYZ"
    "abcdefghij" "klmnopqrst" "uvwxyz"
    "0123456789"
    ".,:;!?'\"-_" "()[]{}/\\|" "@#$%&*+=<>" "~^`"
)

# Guide lines, as fractions of cell height from the top
ASCENDER = 0.14     # top of tall lowercase (b, d, h) and cap height
X_HEIGHT = 0.38     # top of short lowercase (a, c, x)
BASELINE = 0.72     # where letters sit
DESCENDER = 0.90    # bottom of g, j, p, q, y
SIDE = 0.10         # left/right margin guide

GUIDE = (150, 190, 230)      # light blue
BASE = (90, 140, 210)        # baseline, darker
BORDER = (120, 120, 120)
LABEL = (185, 185, 185)

here = os.path.dirname(os.path.abspath(__file__))
rows = (len(CHARS) + COLS - 1) // COLS
img = Image.new("RGB", (COLS * CELL, rows * CELL), "white")
d = ImageDraw.Draw(img)
font = ImageFont.truetype("C:/Windows/Fonts/arial.ttf", CELL // 9)


def dashed(y, x0, x1, colour, dash=10, gap=8):
    x = x0
    while x < x1:
        d.line([(x, y), (min(x + dash, x1), y)], fill=colour, width=1)
        x += dash + gap


cells = []
for i, ch in enumerate(CHARS):
    cx, cy = (i % COLS) * CELL, (i // COLS) * CELL
    dashed(cy + ASCENDER * CELL, cx, cx + CELL, GUIDE)
    dashed(cy + X_HEIGHT * CELL, cx, cx + CELL, GUIDE)
    d.line([(cx, cy + BASELINE * CELL), (cx + CELL, cy + BASELINE * CELL)], fill=BASE, width=2)
    dashed(cy + DESCENDER * CELL, cx, cx + CELL, GUIDE)
    for sx in (cx + SIDE * CELL, cx + (1 - SIDE) * CELL):
        for y in range(int(cy), int(cy + CELL), 18):
            d.line([(sx, y), (sx, y + 10)], fill=GUIDE, width=1)
    d.text((cx + 8, cy + 4), ch, fill=LABEL, font=font)
    cells.append({"char": ch, "code": ord(ch), "col": i % COLS, "row": i // COLS})

for c in range(COLS + 1):
    d.line([(c * CELL, 0), (c * CELL, rows * CELL)], fill=BORDER, width=2)
for r in range(rows + 1):
    d.line([(0, r * CELL), (COLS * CELL, r * CELL)], fill=BORDER, width=2)

img.save(os.path.join(here, "FontGrid_Guides.png"))
with open(os.path.join(here, "FontGrid_Layout.json"), "w", encoding="utf-8") as f:
    json.dump({
        "cellSize": CELL, "columns": COLS, "rows": rows,
        "origin": "top-left, row 0 is the top row",
        "guides": {"ascenderCap": ASCENDER, "xHeight": X_HEIGHT, "baseline": BASELINE,
                   "descender": DESCENDER, "sideMargin": SIDE,
                   "note": "fractions of cell size from the top (side margin from each edge)"},
        "cells": cells,
    }, f, indent=1, ensure_ascii=False)
print(f"{COLS}x{rows} cells, {img.size[0]}x{img.size[1]}px, {len(CHARS)} characters")
