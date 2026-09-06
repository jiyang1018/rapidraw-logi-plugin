#!/usr/bin/env python3
"""
Generates the per-action SVG icons for the RapidRAW plugin.

Options+ treats an action icon as an alpha mask: a 32x32 SVG of *filled* paths
(strokes are not recoloured, which is why the first build showed a black dot).
Every icon here is built from a handful of filled primitives so it renders as a
white glyph on the Actions Ring and a dark glyph in the action list, exactly
like the stock Adobe plugins' icons.

File naming (Logi Actions SDK convention, verified against the installed
Lightroom / Premiere plugins):
    actionsymbols/<Namespace>.<Class>___<parameter>.svg   list / picker glyph
    actionicons/<Namespace>.<Class>___<parameter>.svg     device / ring glyph
Both folders get the same file.

Run from the repo root:  python tools/make-icons.py [--install]
Writes the set to tools/icons-original (the backup / redistributable set) and,
with --install, also into src/package/actionsymbols and actionicons. A contact
sheet lands at tools/icons-contact-sheet.png for review.
tools/use-logi-icons.ps1 (end-user, optional) swaps in Logitech's Lightroom icons
where a counterpart exists; the plugin itself ships only these.
"""

import math
import os
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
PKG = os.path.join(ROOT, "src", "package")
NS = "Loupedeck.RapidRawPlugin"
ADJ = f"{NS}.RapidRawAdjustments"
CMD = f"{NS}.RapidRawCommands"
FILL = "#001C2F"

# ---------------------------------------------------------------- primitives
# Every primitive returns SVG path data (d=...). Rings and holes rely on the
# evenodd fill rule, so all paths of one icon are merged into one <path>.

def circle(cx, cy, r):
    return (f"M{cx - r:.3f},{cy:.3f} a{r:.3f},{r:.3f} 0 1,0 {2 * r:.3f},0 "
            f"a{r:.3f},{r:.3f} 0 1,0 {-2 * r:.3f},0 Z")


def ring(cx, cy, r, w):
    return circle(cx, cy, r) + " " + circle(cx, cy, r - w)


def rect(x, y, w, h, rx=0):
    if rx <= 0:
        return f"M{x},{y} h{w} v{h} h{-w} Z"
    return (f"M{x + rx},{y} h{w - 2 * rx} a{rx},{rx} 0 0,1 {rx},{rx} v{h - 2 * rx} "
            f"a{rx},{rx} 0 0,1 {-rx},{rx} h{-(w - 2 * rx)} a{rx},{rx} 0 0,1 {-rx},{-rx} "
            f"v{-(h - 2 * rx)} a{rx},{rx} 0 0,1 {rx},{-rx} Z")


def poly(points):
    return "M" + " L".join(f"{x:.3f},{y:.3f}" for x, y in points) + " Z"


def half_disc(cx, cy, r, side):
    """Filled half of a disc. side: 'left' 'right' 'top' 'bottom'."""
    if side == "right":
        return f"M{cx},{cy - r} a{r},{r} 0 0,1 0,{2 * r} Z"
    if side == "left":
        return f"M{cx},{cy - r} a{r},{r} 0 0,0 0,{2 * r} Z"
    if side == "top":
        return f"M{cx - r},{cy} a{r},{r} 0 0,1 {2 * r},0 Z"
    return f"M{cx - r},{cy} a{r},{r} 0 0,0 {2 * r},0 Z"


def sector(cx, cy, r, a0, a1):
    """Filled pie sector from angle a0 to a1 (degrees, clockwise from 12 o'clock)."""
    def pt(a):
        t = math.radians(a - 90)
        return cx + r * math.cos(t), cy + r * math.sin(t)
    x0, y0 = pt(a0)
    x1, y1 = pt(a1)
    large = 1 if (a1 - a0) % 360 > 180 else 0
    return f"M{cx},{cy} L{x0:.3f},{y0:.3f} A{r},{r} 0 {large},1 {x1:.3f},{y1:.3f} Z"


def arc_band(cx, cy, r, w, a0, a1):
    """Thick arc (annulus sector) from a0 to a1 degrees, clockwise from 12."""
    ri = r - w

    def pt(rad, a):
        t = math.radians(a - 90)
        return cx + rad * math.cos(t), cy + rad * math.sin(t)
    ox0, oy0 = pt(r, a0)
    ox1, oy1 = pt(r, a1)
    ix0, iy0 = pt(ri, a0)
    ix1, iy1 = pt(ri, a1)
    large = 1 if (a1 - a0) % 360 > 180 else 0
    return (f"M{ox0:.3f},{oy0:.3f} A{r},{r} 0 {large},1 {ox1:.3f},{oy1:.3f} "
            f"L{ix1:.3f},{iy1:.3f} A{ri},{ri} 0 {large},0 {ix0:.3f},{iy0:.3f} Z")


def line(x0, y0, x1, y1, w):
    """A stroke drawn as a filled rectangle with round-ish ends."""
    dx, dy = x1 - x0, y1 - y0
    length = math.hypot(dx, dy) or 1
    nx, ny = -dy / length * w / 2, dx / length * w / 2
    return poly([(x0 + nx, y0 + ny), (x1 + nx, y1 + ny), (x1 - nx, y1 - ny), (x0 - nx, y0 - ny)])


def star(cx, cy, r_out, r_in, n=5):
    pts = []
    for i in range(2 * n):
        rad = r_out if i % 2 == 0 else r_in
        a = math.radians(i * 180 / n - 90)
        pts.append((cx + rad * math.cos(a), cy + rad * math.sin(a)))
    return poly(pts)


def drop(cx, cy, r):
    """Water drop: point at top, round bottom."""
    return (f"M{cx},{cy - 1.9 * r} C{cx + 0.2 * r},{cy - 1.2 * r} {cx + r},{cy - 0.55 * r} {cx + r},{cy} "
            f"a{r},{r} 0 1,1 {-2 * r},0 C{cx - r},{cy - 0.55 * r} {cx - 0.2 * r},{cy - 1.2 * r} {cx},{cy - 1.9 * r} Z")


def rays(cx, cy, r0, r1, w, n=8, offset=0):
    return " ".join(
        line(cx + r0 * math.cos(math.radians(offset + i * 360 / n)),
             cy + r0 * math.sin(math.radians(offset + i * 360 / n)),
             cx + r1 * math.cos(math.radians(offset + i * 360 / n)),
             cy + r1 * math.sin(math.radians(offset + i * 360 / n)), w)
        for i in range(n))


def plus(cx, cy, s, w):
    return line(cx - s, cy, cx + s, cy, w) + " " + line(cx, cy - s, cx, cy + s, w)


def minus(cx, cy, s, w):
    return line(cx - s, cy, cx + s, cy, w)


def arrow_head(x, y, angle, size):
    """Filled triangle pointing along `angle` (degrees, 0 = right), tip at (x, y)."""
    t = math.radians(angle)
    bx, by = x - size * math.cos(t), y - size * math.sin(t)
    px, py = -math.sin(t) * size * 0.7, math.cos(t) * size * 0.7
    return poly([(x, y), (bx + px, by + py), (bx - px, by - py)])


def curved_arrow(cx, cy, r, w, a0, a1, clockwise=True):
    band = arc_band(cx, cy, r, w, a0, a1)
    a = a1 if clockwise else a0
    t = math.radians(a - 90)
    mid = r - w / 2
    tip = (cx + mid * math.cos(t), cy + mid * math.sin(t))
    tangent = a + (90 if clockwise else -90)
    return band + " " + arrow_head(tip[0] + 2.2 * math.cos(math.radians(tangent)),
                                   tip[1] + 2.2 * math.sin(math.radians(tangent)), tangent, 6)


def svg(*parts):
    d = " ".join(p for p in parts if p)
    return ('<svg width="32" height="32" viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">\n'
            f'<path fill-rule="evenodd" clip-rule="evenodd" d="{d}" fill="{FILL}"/>\n</svg>\n')


# ---------------------------------------------------------------- glyphs
C = 16  # centre

def g_exposure():
    return svg(ring(C, C, 11, 2.2), plus(12.3, 12.3, 2.2, 1.8), minus(19.7, 19.7, 2.2, 1.8))


def g_brightness():
    return svg(circle(C, C, 5), rays(C, C, 8, 11.5, 2))


def g_contrast():
    return svg(ring(C, C, 11, 2.2), half_disc(C, C, 8, "right"))


def g_highlights():
    return svg(ring(C, C, 11, 2.2), sector(C, C, 8.2, 0, 180))


def g_shadows():
    return svg(ring(C, C, 11, 2.2), sector(C, C, 8.2, 180, 360))


def g_whites():
    return svg(ring(C, C, 11, 2.2), circle(19.5, 12.5, 3))


def g_blacks():
    return svg(circle(C, C, 11) + " " + circle(12.5, 19.5, 3))


def g_temperature():
    return svg(rect(13.5, 4, 5, 15, 2.5) + " " + rect(15.2, 7, 1.6, 10) + " " + circle(C, 22, 5.5) + " " + circle(C, 22, 3.2),
               rect(15.2, 14, 1.6, 6))


def g_tint():
    return svg(drop(C, 18, 7.5))


def g_vibrance():
    return svg(drop(C, 18, 7.5) + " " + drop(C, 18, 4.2))


def g_saturation():
    return svg(drop(C, 18, 7.5), half_disc(C, 18, 4.2, "right"))


def g_hue():
    d = ring(C, C, 11, 2.2)
    for i in range(3):
        a = math.radians(i * 120 - 90)
        d += " " + circle(C + 6 * math.cos(a), C + 6 * math.sin(a), 2.4)
    return svg(d)


def g_luminance():
    return svg(ring(C, C, 5, 1.8), rays(C, C, 7.5, 11, 1.8))


def g_grading(part):
    # three stacked tones, the active one filled
    d = ""
    for i, y in enumerate((8.5, 16, 23.5)):
        d += " " + (circle(C, y, 3.6) if part == i else ring(C, y, 3.6, 1.6))
    return svg(d)


def g_grading_global():
    return svg(circle(C, 8.5, 3.6), circle(C, 16, 3.6), circle(C, 23.5, 3.6))


def g_blending():
    return svg(circle(12, 16, 7.5) + " " + circle(20, 16, 7.5))


def g_balance():
    return svg(line(6, 22, 26, 22, 2), poly([(16, 9), (11, 22), (21, 22)]), circle(16, 8, 2.5))


def g_calibration(which):
    pts = [(16, 8), (9, 21), (23, 21)]
    d = poly([(16, 6), (7, 23), (25, 23)]) + " " + poly([(16, 10.5), (10.2, 21), (21.8, 21)])
    d += " " + circle(*pts[which], 3)
    return svg(d)


def g_sharpness():
    return svg(poly([(5, 26), (14, 8), (19, 17), (22, 13), (27, 26)]))


def g_threshold():
    return svg(poly([(5, 22), (14, 6), (19, 15), (22, 11), (27, 22)]), line(5, 26, 27, 26, 2))


def g_clarity():
    return svg(poly([(16, 5), (27, 16), (16, 27), (5, 16)]) + " " + poly([(16, 10), (22, 16), (16, 22), (10, 16)]))


def g_dehaze():
    return svg(circle(C, 11, 5), line(6, 20, 26, 20, 2.2), line(9, 24.5, 23, 24.5, 2.2), line(12, 29, 20, 29, 2.2))


def g_structure():
    return svg(rect(6, 6, 8, 8, 1.5), rect(18, 6, 8, 8, 1.5), rect(6, 18, 8, 8, 1.5), rect(18, 18, 8, 8, 1.5))


def g_centre():
    return svg(ring(C, C, 11, 2), ring(C, C, 6.5, 1.8), circle(C, C, 2.2))


def g_noise(color):
    d = ""
    for i in range(4):
        for j in range(4):
            r = 1.9 if (i + j) % 2 == 0 or not color else 1.2
            d += " " + circle(7 + i * 6, 7 + j * 6, r)
    return svg(d)


def g_ca(vertical):
    if vertical:
        return svg(ring(C, 13, 8, 2.2) + " " + ring(C, 19, 8, 2.2))
    return svg(ring(13, C, 8, 2.2) + " " + ring(19, C, 8, 2.2))


def g_glow():
    return svg(circle(C, C, 5.5), ring(C, C, 10.5, 1.6))


def g_halation():
    return svg(circle(C, C, 4.5), ring(C, C, 8.5, 1.4), ring(C, C, 12, 1.2))


def g_flare():
    return svg(star(C, C, 12, 4, 4), circle(C, C, 2.6))


def g_lens_blur():
    return svg(circle(12, 13, 7), circle(21, 19, 4.5), circle(23, 9, 2.5))


def g_lens_diffusion():
    return svg(ring(C, C, 10, 3.5), circle(C, C, 3))


def g_vignette(part):
    outer = rect(4, 6, 24, 20, 2)
    hole = f"M{16 - 8},{16} a8,6 0 1,0 16,0 a8,6 0 1,0 -16,0 Z"
    if part == "amount":
        return svg(outer + " " + hole)
    if part == "midpoint":
        return svg(outer + " " + hole, circle(C, C, 2.5))
    if part == "roundness":
        return svg(outer + " " + rect(9, 11, 14, 10, 4))
    return svg(outer + " " + hole, ring(C, C, 5, 1.2))  # feather


def g_grain(part):
    import random
    rnd = random.Random(7 if part == "amount" else 11 if part == "size" else 13)
    d = ""
    n = {"amount": 14, "size": 8, "roughness": 12}[part]
    for _ in range(n):
        x, y = rnd.uniform(6, 26), rnd.uniform(6, 26)
        r = {"amount": 1.6, "size": 2.6, "roughness": 1.2}[part] * rnd.uniform(0.7, 1.3)
        d += " " + circle(x, y, r)
    return svg(d)


def g_lut():
    d = ""
    for i in range(3):
        for j in range(3):
            if (i + j) % 2 == 0:
                d += " " + rect(6 + i * 7, 6 + j * 7, 6, 6, 1)
            else:
                d += " " + rect(6 + i * 7, 6 + j * 7, 6, 6, 1) + " " + rect(7.5 + i * 7, 7.5 + j * 7, 3, 3)
    return svg(d)


def g_rotate(cw):
    return svg(curved_arrow(C, C, 11, 2.6, 40 if cw else -220, 300 if cw else 40, cw), circle(C, C, 2.5))


def g_straighten():
    return svg(line(5, 21, 27, 11, 2.4), line(5, 25, 27, 25, 1.4), circle(C, C, 2.4))


def g_keystone(vertical):
    if vertical:
        return svg(poly([(9, 6), (23, 6), (27, 26), (5, 26)]) + " " + poly([(11.5, 9), (20.5, 9), (23.5, 23), (8.5, 23)]))
    return svg(poly([(6, 9), (26, 5), (26, 27), (6, 23)]) + " " + poly([(9, 11.5), (23, 8.5), (23, 23.5), (9, 20.5)]))


def g_distortion():
    return svg("M8,8 Q16,4 24,8 Q28,16 24,24 Q16,28 8,24 Q4,16 8,8 Z "
               "M11,11 Q16,8.5 21,11 Q23.5,16 21,21 Q16,23.5 11,21 Q8.5,16 11,11 Z")


def g_aspect():
    return svg(rect(4, 8, 24, 16, 2) + " " + rect(7, 11, 18, 10, 1), rect(11, 5, 10, 2.2, 1), rect(11, 24.8, 10, 2.2, 1))


def g_scale():
    return svg(rect(5, 11, 16, 16, 1.5) + " " + rect(8, 14, 10, 10, 1), line(19, 13, 26, 6, 2.2), arrow_head(27, 5, -45, 6))


def g_offset(horizontal):
    box = rect(10, 10, 12, 12, 1.5) + " " + rect(13, 13, 6, 6, 1)
    if horizontal:
        return svg(box, arrow_head(4, 16, 180, 5.5), arrow_head(28, 16, 0, 5.5))
    return svg(box, arrow_head(16, 4, -90, 5.5), arrow_head(16, 28, 90, 5.5))


# ---- commands ----------------------------------------------------------

def g_undo():
    return svg(curved_arrow(C, 17, 10, 3, -160, 60, False), )


def g_redo():
    return svg(curved_arrow(C, 17, 10, 3, -60, 160, True))


def g_copy():
    return svg(rect(6, 6, 14, 16, 2) + " " + rect(9, 9, 8, 10, 1), rect(12, 11, 14, 16, 2) + " " + rect(15, 14, 8, 10, 1))


def g_paste():
    return svg(rect(6, 7, 20, 20, 2) + " " + rect(9, 10, 14, 14, 1), rect(11, 4, 10, 5, 2))


def g_before_after():
    return svg(ring(C, C, 11, 2.2), half_disc(C, C, 8.5, "left"), line(16, 4, 16, 28, 1.6))


def g_prev():
    return svg(poly([(21, 6), (21, 26), (8, 16)]), rect(23, 6, 3, 20, 1))


def g_next():
    return svg(poly([(11, 6), (11, 26), (24, 16)]), rect(6, 6, 3, 20, 1))


def g_open():
    return svg(rect(5, 7, 22, 18, 2) + " " + rect(8, 10, 16, 12, 1), poly([(10, 21), (14, 15), (17, 18), (19, 16), (23, 21)]))


def g_rate(n):
    if n == 0:
        return svg(star(C, 14, 10, 4.2) + " " + star(C, 14, 6.5, 2.7), line(6, 26, 26, 6, 2.2))
    d = star(C, 13, 9.5, 4)
    gap = 4.2
    x0 = 16 - (n - 1) * gap / 2
    for i in range(n):
        d += " " + circle(x0 + i * gap, 26, 1.6)
    return svg(d)


def g_label(color):
    tag = poly([(6, 8), (18, 8), (27, 16), (18, 24), (6, 24)]) + " " + circle(11, 16, 2)
    if color == "none":
        return svg(tag, line(4, 27, 28, 5, 2.2))
    return svg(tag)


def g_zoom(kind):
    glass = ring(13.5, 13.5, 8.5, 2.4) + " " + line(20, 20, 27, 27, 3.2)
    if kind == "in":
        return svg(glass, plus(13.5, 13.5, 3.2, 1.8))
    if kind == "out":
        return svg(glass, minus(13.5, 13.5, 3.2, 1.8))
    if kind == "100":
        return svg(glass, rect(12.6, 9.5, 1.8, 8), poly([(10.5, 12), (12.6, 9.5), (12.6, 12)]))
    if kind == "fit":
        return svg(glass, rect(9.5, 9.5, 8, 8, 1) + " " + rect(11.5, 11.5, 4, 4))
    return svg(glass, arc_band(13.5, 13.5, 4.5, 1.8, 20, 300), arrow_head(16.3, 9.9, -10, 3.5))  # cycle


def g_fullscreen():
    d = ""
    for sx, sy in ((1, 1), (-1, 1), (1, -1), (-1, -1)):
        cx, cy = 16 - sx * 10, 16 - sy * 10
        d += " " + poly([(cx, cy), (cx + sx * 8, cy), (cx + sx * 8, cy + sy * 2.4), (cx + sx * 2.4, cy + sy * 2.4),
                         (cx + sx * 2.4, cy + sy * 8), (cx, cy + sy * 8)])
    return svg(d)


def g_panel(side):
    outer = rect(4, 6, 24, 20, 2) + " " + rect(6.5, 8.5, 19, 15, 1)
    if side == "left":
        return svg(outer, rect(6.5, 8.5, 6, 15))
    if side == "right":
        return svg(outer, rect(19, 8.5, 6.5, 15))
    if side == "bottom":
        return svg(outer, rect(6.5, 18, 19, 5.5))
    return svg(outer)


def g_tool(kind):
    if kind == "adjustments":
        return svg(line(6, 10, 26, 10, 2.2), circle(11, 10, 3.2), line(6, 16, 26, 16, 2.2), circle(20, 16, 3.2),
                   line(6, 22, 26, 22, 2.2), circle(14, 22, 3.2))
    if kind == "crop":
        return svg(rect(9, 4, 2.4, 19), rect(9, 20.6, 19, 2.4), rect(4, 9, 19, 2.4), rect(20.6, 9, 2.4, 19))
    if kind == "masks":
        return svg(ring(C, C, 11, 2.2), circle(C, C, 5.5))
    if kind == "presets":
        return svg(rect(5, 6, 22, 5, 1.5), rect(5, 13.5, 22, 5, 1.5), rect(5, 21, 22, 5, 1.5), circle(9, 8.5, 1.3), circle(9, 16, 1.3), circle(9, 23.5, 1.3))
    if kind == "export":
        return svg(rect(5, 19, 22, 8, 2) + " " + rect(8, 22, 16, 2), rect(14.8, 4, 2.4, 12), arrow_head(16, 17, 90, 6))
    return svg(rect(6, 5, 20, 22, 2) + " " + rect(9, 8, 14, 16, 1), rect(11, 11, 10, 1.8), rect(11, 15, 10, 1.8), rect(11, 19, 6, 1.8))  # metadata


def g_brush(up):
    return svg(circle(12, 16, 7 if up else 4.5), arrow_head(27, 10 if up else 22, -90 if up else 90, 6),
               rect(25.8, 12 if up else 8, 2.4, 10))


def g_reset_armed():
    return svg(curved_arrow(C, C, 10.5, 2.8, -260, 20, True), circle(C, C, 3))


def g_reset(param_glyph):
    """Small reset arrow in the corner of the parameter's own glyph."""
    inner = param_glyph.split('d="', 1)[1].split('"', 1)[0]
    scaled = f'<g transform="translate(2 2) scale(0.72)"><path fill-rule="evenodd" clip-rule="evenodd" d="{inner}" fill="{FILL}"/></g>'
    arrow = curved_arrow(24.5, 24.5, 6, 2, -250, 30, True)
    return ('<svg width="32" height="32" viewBox="0 0 32 32" fill="none" xmlns="http://www.w3.org/2000/svg">\n'
            f'{scaled}\n<path fill-rule="evenodd" clip-rule="evenodd" d="{arrow} {circle(24.5, 24.5, 1.5)}" fill="{FILL}"/>\n</svg>\n')


def g_delete():
    return svg(rect(7, 9, 18, 18, 2) + " " + rect(10, 12, 12, 12, 1), rect(5, 6, 22, 2.4, 1), rect(12, 3.5, 8, 3, 1))


def g_select_all():
    return svg(rect(5, 5, 22, 22, 2) + " " + rect(8, 8, 16, 16, 1), poly([(10, 16), (14, 20), (22, 11), (20, 9.5), (14, 17), (11.5, 14.5)]))


# ---------------------------------------------------------------- tables

HSL_COLORS = ["reds", "oranges", "yellows", "greens", "aquas", "blues", "purples", "magentas"]

ADJUSTMENT_ICONS = {
    "exposure": g_exposure, "brightness": g_brightness, "contrast": g_contrast,
    "highlights": g_highlights, "shadows": g_shadows, "whites": g_whites, "blacks": g_blacks,
    "temperature": g_temperature, "tint": g_tint, "vibrance": g_vibrance, "saturation": g_saturation,
    "hue": g_hue,
    "colorGrading.blending": g_blending, "colorGrading.balance": g_balance,
    "colorCalibration.shadowsTint": lambda: g_calibration(0),
    "colorCalibration.redHue": lambda: g_calibration(1), "colorCalibration.redSaturation": lambda: g_calibration(1),
    "colorCalibration.greenHue": lambda: g_calibration(2), "colorCalibration.greenSaturation": lambda: g_calibration(2),
    "colorCalibration.blueHue": lambda: g_calibration(0), "colorCalibration.blueSaturation": lambda: g_calibration(0),
    "sharpness": g_sharpness, "sharpnessThreshold": g_threshold, "clarity": g_clarity, "dehaze": g_dehaze,
    "structure": g_structure, "centré": g_centre,
    "lumaNoiseReduction": lambda: g_noise(False), "colorNoiseReduction": lambda: g_noise(True),
    "chromaticAberrationRedCyan": lambda: g_ca(False), "chromaticAberrationBlueYellow": lambda: g_ca(True),
    "glowAmount": g_glow, "halationAmount": g_halation, "flareAmount": g_flare,
    "lensBlurAmount": g_lens_blur, "lensBlurDiffusion": g_lens_diffusion,
    "vignetteAmount": lambda: g_vignette("amount"), "vignetteMidpoint": lambda: g_vignette("midpoint"),
    "vignetteRoundness": lambda: g_vignette("roundness"), "vignetteFeather": lambda: g_vignette("feather"),
    "grainAmount": lambda: g_grain("amount"), "grainSize": lambda: g_grain("size"), "grainRoughness": lambda: g_grain("roughness"),
    "lutIntensity": g_lut,
    "rotation": g_straighten, "transformRotate": lambda: g_rotate(True),
    "transformVertical": lambda: g_keystone(True), "transformHorizontal": lambda: g_keystone(False),
    "transformDistortion": g_distortion, "transformAspect": g_aspect, "transformScale": g_scale,
    "transformXOffset": lambda: g_offset(True), "transformYOffset": lambda: g_offset(False),
}
for c in HSL_COLORS:
    ADJUSTMENT_ICONS[f"hsl.{c}.hue"] = g_hue
    ADJUSTMENT_ICONS[f"hsl.{c}.saturation"] = g_saturation
    ADJUSTMENT_ICONS[f"hsl.{c}.luminance"] = g_luminance
for i, rng in enumerate(["shadows", "midtones", "highlights"]):
    ADJUSTMENT_ICONS[f"colorGrading.{rng}.hue"] = (lambda i=i: g_grading(2 - i))
    ADJUSTMENT_ICONS[f"colorGrading.{rng}.saturation"] = (lambda i=i: g_grading(2 - i))
    ADJUSTMENT_ICONS[f"colorGrading.{rng}.luminance"] = (lambda i=i: g_grading(2 - i))
for part in ("hue", "saturation", "luminance"):
    ADJUSTMENT_ICONS[f"colorGrading.global.{part}"] = g_grading_global

COMMAND_ICONS = {
    "reset_active": g_reset_armed,
    "undo": g_undo, "redo": g_redo, "copy_adjustments": g_copy, "paste_adjustments": g_paste,
    "show_original": g_before_after, "rotate_left": lambda: g_rotate(False), "rotate_right": lambda: g_rotate(True),
    "preview_prev": g_prev, "preview_next": g_next, "open_image": g_open,
    "zoom_fit": lambda: g_zoom("fit"), "zoom_100": lambda: g_zoom("100"), "zoom_in": lambda: g_zoom("in"),
    "zoom_out": lambda: g_zoom("out"), "cycle_zoom": lambda: g_zoom("cycle"), "toggle_fullscreen": g_fullscreen,
    "toggle_left_panel": lambda: g_panel("left"), "toggle_right_panel": lambda: g_panel("right"),
    "toggle_bottom_panel": lambda: g_panel("bottom"),
    "toggle_adjustments": lambda: g_tool("adjustments"), "toggle_crop_panel": lambda: g_tool("crop"),
    "toggle_masks": lambda: g_tool("masks"), "toggle_presets": lambda: g_tool("presets"),
    "toggle_export": lambda: g_tool("export"), "toggle_metadata": lambda: g_tool("metadata"),
    "brush_size_up": lambda: g_brush(True), "brush_size_down": lambda: g_brush(False),
    "delete_selected": g_delete, "select_all": g_select_all,
}
for n in range(6):
    COMMAND_ICONS[f"rate_{n}"] = (lambda n=n: g_rate(n))
for col in ("none", "red", "yellow", "green", "blue", "purple"):
    COMMAND_ICONS[f"color_label_{col}"] = (lambda col=col: g_label(col))
# Per-slider reset keys exist for Basic / Color / Details (see RapidRawCommands.cs).
for pid in ["exposure", "brightness", "contrast", "highlights", "shadows", "whites", "blacks",
            "temperature", "tint", "vibrance", "saturation", "hue",
            "sharpness", "sharpnessThreshold", "clarity", "dehaze", "structure", "centré",
            "lumaNoiseReduction", "colorNoiseReduction", "chromaticAberrationRedCyan", "chromaticAberrationBlueYellow"]:
    COMMAND_ICONS[f"reset-{pid}"] = (lambda pid=pid: g_reset(ADJUSTMENT_ICONS[pid]()))

CLASS_ICONS = {ADJ: g_exposure, CMD: g_reset_armed}


def main():
    written = 0
    files = {}
    for action, table in ((ADJ, ADJUSTMENT_ICONS), (CMD, COMMAND_ICONS)):
        for pid, fn in table.items():
            files[f"{action}___{pid}.svg"] = fn()
    for action, fn in CLASS_ICONS.items():
        files[f"{action}.svg"] = fn()

    targets = [os.path.join(ROOT, "tools", "icons-original")]
    if "--install" in sys.argv:
        targets += [os.path.join(PKG, "actionsymbols"), os.path.join(PKG, "actionicons")]
    for out in targets:
        os.makedirs(out, exist_ok=True)
        # Overwrite in place rather than wiping the folder: the folder may be on a
        # mount that forbids deletes, and stale files are harmless to the SDK.
        for name, content in files.items():
            with open(os.path.join(out, name), "w", encoding="utf-8") as f:
                f.write(content)
            written += 1
    print(f"wrote {written} files ({len(files)} icons) to: " + ", ".join(targets))

    try:
        import cairosvg
        from PIL import Image, ImageDraw
    except ImportError:
        print("contact sheet skipped (needs cairosvg + pillow)")
        return
    names = sorted(files)
    cols = 8
    cell = 96
    rows = (len(names) + cols - 1) // cols
    sheet = Image.new("RGB", (cols * cell, rows * (cell + 22)), "white")
    draw = ImageDraw.Draw(sheet)
    for i, name in enumerate(names):
        png = cairosvg.svg2png(bytestring=files[name].encode(), output_width=64, output_height=64)
        import io
        img = Image.open(io.BytesIO(png)).convert("RGBA")
        x, y = (i % cols) * cell, (i // cols) * (cell + 22)
        sheet.paste(img, (x + 16, y + 8), img)
        label = name.split("___")[-1].replace(".svg", "")[:16]
        draw.text((x + 4, y + 76), label, fill="black")
    path = os.path.join(ROOT, "tools", "icons-contact-sheet.png")
    sheet.save(path)
    print("contact sheet:", path)


if __name__ == "__main__":
    sys.exit(main())
