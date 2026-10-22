#!/usr/bin/env python3
import os
import shutil
import subprocess
from PIL import Image, ImageDraw, ImageFont, ImageFilter

SOURCE_IMG = '/Users/nicklex/.gemini/antigravity/brain/36b26e92-851a-41f0-9215-fd9923b84ac7/.user_uploaded/media_1792677898575_3bfb9ac3.png'
REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), '..'))

LAUNCHER_ASSETS = os.path.join(REPO_ROOT, 'src', 'Moewshare.Launcher', 'Assets')
EDITOR_ASSETS = os.path.join(REPO_ROOT, 'src', 'Moewshare.Editor', 'Assets')

os.makedirs(LAUNCHER_ASSETS, exist_ok=True)
os.makedirs(EDITOR_ASSETS, exist_ok=True)

def create_icns(base_img: Image.Image, output_icns_path: str):
    iconset_dir = output_icns_path + '.iconset'
    if os.path.exists(iconset_dir):
        shutil.rmtree(iconset_dir)
    os.makedirs(iconset_dir)

    sizes = [
        ('icon_16x16.png', 16),
        ('icon_16x16@2x.png', 32),
        ('icon_32x32.png', 32),
        ('icon_32x32@2x.png', 64),
        ('icon_128x128.png', 128),
        ('icon_128x128@2x.png', 256),
        ('icon_256x256.png', 256),
        ('icon_256x256@2x.png', 512),
        ('icon_512x512.png', 512),
        ('icon_512x512@2x.png', 1024),
    ]

    for fname, sz in sizes:
        resized = base_img.resize((sz, sz), Image.Resampling.LANCZOS)
        resized.save(os.path.join(iconset_dir, fname), 'PNG')

    subprocess.run(['iconutil', '-c', 'icns', iconset_dir, '-o', output_icns_path], check=True)
    shutil.rmtree(iconset_dir)
    print(f"Generated ICNS: {output_icns_path} ({os.path.getsize(output_icns_path)} bytes)")

def create_ico(base_img: Image.Image, output_ico_path: str):
    ico_sizes = [(16, 16), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    base_img.save(output_ico_path, format='ICO', sizes=ico_sizes)
    print(f"Generated ICO: {output_ico_path} ({os.path.getsize(output_ico_path)} bytes)")

def build_editor_image(cat_img: Image.Image) -> Image.Image:
    canvas = cat_img.copy()
    w, h = canvas.size

    # Badge metrics
    badge_w = 600
    badge_h = 144
    bx0 = (w - badge_w) // 2
    by0 = 848
    bx1 = bx0 + badge_w
    by1 = by0 + badge_h
    radius = 48

    # Soft ambient drop shadow beneath badge
    shadow = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    sdraw = ImageDraw.Draw(shadow)
    sdraw.rounded_rectangle([bx0, by0 + 8, bx1, by1 + 8], radius=radius, fill=(0, 0, 0, 150))
    shadow = shadow.filter(ImageFilter.GaussianBlur(16))
    canvas = Image.alpha_composite(canvas, shadow)

    # Main badge overlay
    overlay = Image.new('RGBA', (w, h), (0, 0, 0, 0))
    odraw = ImageDraw.Draw(overlay)

    # Deep dark velvet plum/burgundy pill background
    odraw.rounded_rectangle([bx0, by0, bx1, by1], radius=radius, fill=(43, 24, 34, 250), outline=(255, 255, 255, 240), width=6)

    # Inner subtle pastel pink accent border
    odraw.rounded_rectangle([bx0 + 6, by0 + 6, bx1 - 6, by1 - 6], radius=radius - 4, outline=(255, 178, 196, 180), width=3)

    # Font
    font_path = '/System/Library/Fonts/Helvetica.ttc'
    if not os.path.exists(font_path):
        font_path = '/System/Library/Fonts/Supplemental/Arial Bold.ttf'
    font = ImageFont.truetype(font_path, 82, index=1 if font_path.endswith('.ttc') else 0)

    text = "ENGINE"
    tb = odraw.textbbox((0, 0), text, font=font)
    tw = tb[2] - tb[0]
    th = tb[3] - tb[1]
    tx = bx0 + (badge_w - tw) // 2
    ty = by0 + (badge_h - th) // 2 - 6

    # Text shadow
    odraw.text((tx, ty + 3), text, font=font, fill=(15, 5, 10, 160))
    # Crisp white text
    odraw.text((tx, ty), text, font=font, fill=(255, 255, 255, 255))

    canvas = Image.alpha_composite(canvas, overlay)
    return canvas

def main():
    if not os.path.exists(SOURCE_IMG):
        raise FileNotFoundError(f"Source image not found: {SOURCE_IMG}")

    cat_img = Image.open(SOURCE_IMG).convert('RGBA')
    print(f"Loaded source image: {SOURCE_IMG} ({cat_img.size})")

    # 1. Launcher: Pure cat
    print("\n--- Generating Launcher Icons ---")
    launcher_png = os.path.join(LAUNCHER_ASSETS, 'icon.png')
    launcher_ico = os.path.join(LAUNCHER_ASSETS, 'icon.ico')
    launcher_icns = os.path.join(LAUNCHER_ASSETS, 'AppIcon.icns')

    cat_img.save(launcher_png, 'PNG')
    print(f"Saved PNG: {launcher_png}")
    create_ico(cat_img, launcher_ico)
    create_icns(cat_img, launcher_icns)

    # 2. Editor: Cat + Engine badge
    print("\n--- Generating Editor Icons ---")
    editor_img = build_editor_image(cat_img)
    editor_png = os.path.join(EDITOR_ASSETS, 'icon.png')
    editor_ico = os.path.join(EDITOR_ASSETS, 'icon.ico')
    editor_icns = os.path.join(EDITOR_ASSETS, 'AppIcon.icns')

    editor_img.save(editor_png, 'PNG')
    print(f"Saved PNG: {editor_png}")
    create_ico(editor_img, editor_ico)
    create_icns(editor_img, editor_icns)

    print("\nAll icons successfully generated!")

if __name__ == '__main__':
    main()
