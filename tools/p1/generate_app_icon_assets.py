"""Generate the T17.6 app icon assets from the two master images.

Usage: python generate_app_icon_assets.py <output folder>
Needs Pillow. The masters are docs/p1/app-icon/app-icon-master.png (the full
icon) and app-icon-small-master.png (the small-size drawing for 16 to 32 px).
"""

import sys
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
MASTERS = REPO / "docs" / "p1" / "app-icon"
SCALES = (100, 125, 150, 200, 400)
TARGET_SIZES = (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256)
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 256)
SMALL_LIMIT = 32


def main(output: Path) -> None:
    full = Image.open(MASTERS / "app-icon-master.png").convert("RGBA")
    small = Image.open(MASTERS / "app-icon-small-master.png").convert("RGBA")

    def icon(size: int) -> Image.Image:
        return (small if size <= SMALL_LIMIT else full).resize((size, size), Image.LANCZOS)

    def centred(width: int, height: int, size: int) -> Image.Image:
        canvas = Image.new("RGBA", (width, height), (0, 0, 0, 0))
        image = icon(size)
        canvas.paste(image, ((width - size) // 2, (height - size) // 2), image)
        return canvas

    files = {}
    for scale in SCALES:
        f = scale / 100
        files[f"Square44x44Logo.scale-{scale}.png"] = icon(round(44 * f))
        files[f"Square150x150Logo.scale-{scale}.png"] = centred(
            round(150 * f), round(150 * f), round(150 * f * 0.66))
        files[f"Wide310x150Logo.scale-{scale}.png"] = centred(
            round(310 * f), round(150 * f), round(150 * f * 0.66))
        files[f"StoreLogo.scale-{scale}.png"] = icon(round(50 * f))
        files[f"SplashScreen.scale-{scale}.png"] = centred(
            round(620 * f), round(300 * f), round(300 * f * 0.6))
    for size in TARGET_SIZES:
        for suffix in ("", "_altform-unplated", "_altform-lightunplated"):
            files[f"Square44x44Logo.targetsize-{size}{suffix}.png"] = icon(size)

    output.mkdir(parents=True, exist_ok=True)
    for name, image in files.items():
        image.save(output / name, optimize=True)
    frames = [icon(size) for size in sorted(ICO_SIZES, reverse=True)]
    frames[0].save(output / "AppIcon.ico", format="ICO",
                   sizes=[(s, s) for s in ICO_SIZES], append_images=frames[1:])
    print(f"Wrote {len(files)} PNG logos and AppIcon.ico to {output}.")


if __name__ == "__main__":
    if len(sys.argv) != 2:
        sys.exit(__doc__)
    main(Path(sys.argv[1]))
