"""DUCK MOW - Fonts/ bitmap numeral atlas.

Nothing under C:\\Windows\\Fonts can legally ship inside a WebGL build except the
SIL OFL items (Noto Sans/Serif KR, Sans Serif Collection) - and none of those is a
chunky friendly display face. So the big score readouts get a hand-built bitmap
set instead, drawn from the stroke alphabet in letters.py.

Layout: 4 x 4 grid of 128 px cells in a 512 px sheet, row-major from the top-left.

    row 0:  0  1  2  3
    row 1:  4  5  6  7
    row 2:  8  9  X  %
    row 3:  /  +  -  .

Cell (col, row) -> UV rect (col/4, 1 - (row+1)/4, 0.25, 0.25).
"""
import numpy as np
from PIL import Image, ImageDraw
import duckart as D
import uikit as U
import letters as L

ORDER = ["0", "1", "2", "3", "4", "5", "6", "7", "8", "9", "X", "%", "/", "+", "-", "."]


def numerals_atlas(size=512, cols=4):
    cell = size // cols
    SSF = 4
    S = size * SSF
    C = cell * SSF

    face_im = Image.new("L", (S, S), 0)
    out_im = Image.new("L", (S, S), 0)
    fd = ImageDraw.Draw(face_im)
    od = ImageDraw.Draw(out_im)

    gh = C * 0.70                     # glyph height inside the cell
    stroke = C * 0.135
    for i, ch in enumerate(ORDER):
        r, c = divmod(i, cols)
        # narrow glyphs get nudged so every cell looks optically centred
        aspect = 0.80 if ch in "1./" else (0.92 if ch in "-+" else 1.0)
        gx = c * C + (C - gh * aspect * 0.86) / 2 - gh * 0.03
        gy = r * C + (C - gh) / 2
        L.draw_glyph(od, ch, gx, gy, gh, stroke * 2.05, 255, wobble=0.004, seed=i * 13)
        L.draw_glyph(fd, ch, gx, gy, gh, stroke, 255, wobble=0.004, seed=i * 13)

    face = np.asarray(face_im.resize((size, size), Image.LANCZOS), dtype=np.float64) / 255.0
    outl = np.asarray(out_im.resize((size, size), Image.LANCZOS), dtype=np.float64) / 255.0

    rgb, a = U.blank(size, size)
    sh = U.soft_shadow(outl, dy=4, blur=3.2, opacity=0.40)
    rgb, a = U.over(rgb, a, U.SHADOW, sh)
    rgb, a = U.over(rgb, a, D.hexcol(U.INK), outl * 0.97)

    # painted cream face with a warm gradient down each glyph
    g = np.tile(np.linspace(0, 1, size)[:, None], (1, size))
    gg = np.mod(g * (size / (size // 4)), 1.0)     # restart the ramp in every row of cells
    fill = D.mix(D.tint("tent_cream", 0.30), D.mix("tent_cream", "duck_orange", 0.30), gg ** 1.1)
    fill = fill * U.grain(size, size, seed=5, strength=0.05, freq=64)[..., None]
    rgb, a = U.over(rgb, a, fill, face * 0.99)

    U.save_ui("numerals_atlas_512.png", rgb, a, subdir="Fonts")
    print("  numerals_atlas_512.png  16 cells of %d px, order: %s" % (cell, " ".join(ORDER)))


if __name__ == "__main__":
    numerals_atlas()
