# Regenerates Assets/tik4net-icon.png (the NuGet package icon): python Assets/make-icon.py Assets/tik4net-icon.png
# Needs Pillow and the Windows fonts Segoe UI Semilight Italic ("tik"), Bahnschrift Bold, sheared ("4"), and
# Segoe UI Semilight (".NET").
# Also writes <name>-preview.png on white, for a quick look; that file is not tracked.
import sys
from PIL import Image, ImageDraw, ImageFont

S = 4                      # supersampling
SIZE = 256 * S
GRAY = (102, 102, 104, 255)
F = "C:/Windows/Fonts/"

light = ImageFont.truetype(F + "seguisli.ttf", 92 * S)          # "tik"  ~ "Mikro"
dotnet = ImageFont.truetype(F + "segoeuisl.ttf", 92 * S)       # ".NET" ~ the .NET wordmark, as thin as "tik"
heavy = ImageFont.truetype(F + "bahnschrift.ttf", 92 * S)      # "4"    ~ "Tik"
heavy.set_variation_by_name("Bold")
SHEAR = 0.21                                                   # italic slant for the upright Bahnschrift


def layer(text, font, shear):
    l, t, r, b = font.getbbox(text)
    pad = int((b) * shear) + 8 * S
    im = Image.new("L", (r + pad * 2, b + 8 * S), 0)
    ImageDraw.Draw(im).text((pad, 0), text, font=font, fill=255)
    if shear:
        w, h = im.size
        # x' = x + shear*(h - y): lean right, baseline fixed
        im = im.transform((w, h), Image.AFFINE, (1, shear, -shear * h, 0, 1, 0), Image.BICUBIC)
    bbox = im.getbbox()
    return im.crop(bbox), bbox


tik, tik_box = layer("tik", light, 0)
four, _ = layer("4", heavy, SHEAR)
net, _ = layer(".NET", dotnet, 0)

# i-dot centre in the "tik" layer: the topmost blob in the i's column
t_w = light.getlength("t")
i_w = light.getlength("ti") - t_w
col0, col1 = int(t_w - tik_box[0]), int(t_w + i_w - tik_box[0]) + 10 * S
ys = [y for y in range(tik.size[1]) if any(tik.getpixel((x, y)) > 128 for x in range(max(col0, 0), min(col1, tik.size[0])))]
dot_top = ys[0]
dot_bottom = next(y for y in ys if y + 1 not in ys)
dot_cy = (dot_top + dot_bottom) / 2
xs = [x for x in range(max(col0, 0), min(col1, tik.size[0])) if any(tik.getpixel((x, y)) > 128 for y in range(dot_top, dot_bottom + 1))]
dot_cx = (xs[0] + xs[-1]) / 2
dot_r = (dot_bottom - dot_top) / 2

# (layer, gap before it): "tik" and "4" touch, as in "MikroTik"; ".NET" keeps a little air
# The arcs stand in for the i's dot: measured on "tik" above, drawn on a dotless "tık" of the same width.
tik_dotless, box = layer("tık", light, 0)
assert (box[0], box[2]) == (tik_box[0], tik_box[2]), "dotless i changed the width of tik"
bottom_shift = tik.size[1] - tik_dotless.size[1]         # the dot no longer sets the top of the box
tik = Image.new("L", tik.size, 0)
tik.paste(tik_dotless, (0, bottom_shift))
parts = [(tik, 0), (four, int(-6 * S)), (net, int(4 * S))]
arc_room = int(dot_r * 5)
total_w = sum(im.size[0] + g for im, g in parts)
glyph_h = max(im.size[1] for im, _ in parts)
height = glyph_h + arc_room
scale = min((SIZE * 0.88) / total_w, (SIZE * 0.88) / height, 1.0)

canvas = Image.new("L", (int(total_w), int(height)), 0)
base_y = arc_room + glyph_h                              # common baseline (bottom of glyph boxes)
x = 0
for im, g in parts:
    x += g
    canvas.paste(255, (x, base_y - im.size[1]), im)
    x += im.size[0]

# two arcs above the i, after the MikroTik logo: the lower-left 60 degrees of two concentric circles whose
# centre lies up and to the right, so they bow down toward the i
d = ImageDraw.Draw(canvas)
dot_x, dot_y = dot_cx, base_y - tik.size[1] + dot_cy
cx = dot_x + dot_r * (2.6 + 0.707 * 2.4)
cy = dot_y - dot_r * (2.35 + 0.707 * 2.4)
width = max(int(dot_r * 0.5), 2)
for k in (2.4, 3.9):
    r = dot_r * k
    d.arc([cx - r, cy - r, cx + r, cy + r], start=105, end=165, fill=255, width=width)

canvas = canvas.resize((int(total_w * scale), int(height * scale)), Image.LANCZOS)
out = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
ox = (SIZE - canvas.size[0]) // 2
oy = (SIZE - canvas.size[1]) // 2
fill = Image.new("RGBA", canvas.size, GRAY)
out.paste(fill, (ox, oy), canvas)
out = out.resize((256, 256), Image.LANCZOS)
out.save(sys.argv[1])

preview = Image.new("RGBA", (256, 256), (255, 255, 255, 255))
preview.alpha_composite(out)
preview.convert("RGB").save(sys.argv[1].replace(".png", "-preview.png"))
