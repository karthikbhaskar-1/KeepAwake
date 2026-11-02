"""Render the KeepAwake eye icon with Pillow and write app.ico, tray_active.ico, tray_inactive.ico."""
import pathlib
from PIL import Image, ImageDraw

OUT = pathlib.Path(__file__).resolve().parents[1] / "src" / "KeepAwake" / "Resources"
S = 1024  # supersampled canvas for a 64-unit design
U = S / 64

def bez(p0, p1, p2, p3, n=40):
    for i in range(n):
        t = i / n
        x = (1-t)**3*p0[0] + 3*(1-t)**2*t*p1[0] + 3*(1-t)*t**2*p2[0] + t**3*p3[0]
        y = (1-t)**3*p0[1] + 3*(1-t)**2*t*p1[1] + 3*(1-t)*t**2*p2[1] + t**3*p3[1]
        yield (x*U, y*U)

def eye_polygon():
    pts = []
    pts += bez((12,32),(18,23),(24,19),(32,19))
    pts += bez((32,19),(40,19),(46,23),(52,32))
    pts += bez((52,32),(46,41),(40,45),(32,45))
    pts += bez((32,45),(24,45),(18,41),(12,32))
    return pts

def gradient(c0, c1):
    g = Image.new("RGB", (2, 2))
    g.putpixel((0,0), c0); g.putpixel((1,0), tuple((a+b)//2 for a,b in zip(c0,c1)))
    g.putpixel((0,1), tuple((a+b)//2 for a,b in zip(c0,c1))); g.putpixel((1,1), c1)
    return g.resize((S, S), Image.BILINEAR)

def render(active, round_square=True):
    img = Image.new("RGBA", (S, S), (0,0,0,0))
    mask = Image.new("L", (S, S), 0)
    md = ImageDraw.Draw(mask)
    if round_square:
        md.rounded_rectangle([2*U, 2*U, 62*U, 62*U], radius=16*U, fill=255)
    else:
        md.ellipse([2*U, 2*U, 62*U, 62*U], fill=255)
    bg = gradient((79,140,255), (34,211,238)) if active else Image.new("RGB", (S,S), (75,82,101))
    img.paste(bg, (0,0), mask)
    d = ImageDraw.Draw(img)
    dark = (15,17,21,255)
    d.polygon(eye_polygon(), fill=dark)
    if active:
        pupil = Image.new("RGBA", (S,S), (0,0,0,0))
        pm = Image.new("L", (S,S), 0)
        ImageDraw.Draw(pm).ellipse([25*U, 25*U, 39*U, 39*U], fill=255)
        pupil.paste(bg, (0,0), pm)
        img.alpha_composite(pupil)
        d.ellipse([32.3*U, 27.3*U, 36.7*U, 31.7*U], fill=dark)
    else:
        d.line([(14*U,36*U),(20*U,40*U),(26*U,42.5*U),(32*U,43*U),(38*U,42.5*U),(44*U,40*U),(50*U,36*U)],
               fill=(75,82,101,255), width=int(4*U), joint="curve")
    return img

def save_ico(img, path, sizes):
    frames = [img.resize((s, s), Image.LANCZOS) for s in sizes]
    frames[0].save(path, format="ICO", sizes=[(s, s) for s in sizes], append_images=frames[1:])

OUT.mkdir(parents=True, exist_ok=True)
save_ico(render(True), OUT / "app.ico", [256, 64, 48, 32, 24, 16])
save_ico(render(True), OUT / "tray_active.ico", [32, 24, 16])
save_ico(render(False), OUT / "tray_inactive.ico", [32, 24, 16])
render(True).resize((256,256), Image.LANCZOS).save(OUT / "app-256.png")
print("icons written to", OUT)
