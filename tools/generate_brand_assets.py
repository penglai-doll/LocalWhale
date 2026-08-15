from __future__ import annotations

import argparse
import hashlib
import math
import tempfile
from pathlib import Path

from PIL import Image, ImageDraw


BACKGROUND = "#24366F"
MARK = "#BDEEFF"
ACCENT = "#5F8FE6"
SIZES = (16, 20, 24, 32, 48, 64, 128, 256)
SMALL_PNG_SIZES = (16, 24, 32)
CANVAS = 256
SUPERSAMPLE = 4

MARK_COMMANDS: tuple[tuple[object, ...], ...] = (
    ("M", 128.0, 94.0),
    ("C", 111.0, 77.0, 91.0, 64.0, 67.0, 62.0),
    ("C", 70.0, 83.0, 86.0, 99.0, 110.0, 104.0),
    ("C", 92.0, 123.0, 82.0, 144.0, 87.0, 164.0),
    ("C", 93.0, 188.0, 111.0, 207.0, 128.0, 220.0),
    ("C", 145.0, 207.0, 163.0, 188.0, 169.0, 164.0),
    ("C", 174.0, 144.0, 164.0, 123.0, 146.0, 104.0),
    ("C", 170.0, 99.0, 186.0, 83.0, 189.0, 62.0),
    ("C", 165.0, 64.0, 145.0, 77.0, 128.0, 94.0),
    ("Z",),
)

OUTPUTS = (
    Path("src/LocalWhale.App/Assets/Brand/LocalWhaleMark.svg"),
    Path("src/LocalWhale.App/Assets/Brand/LocalWhaleMark-16.png"),
    Path("src/LocalWhale.App/Assets/Brand/LocalWhaleMark-24.png"),
    Path("src/LocalWhale.App/Assets/Brand/LocalWhaleMark-32.png"),
    Path("src/LocalWhale.App/Assets/LocalWhale.ico"),
)


def cubic_point(
    start: tuple[float, float],
    control1: tuple[float, float],
    control2: tuple[float, float],
    end: tuple[float, float],
    t: float,
) -> tuple[float, float]:
    inverse = 1.0 - t
    x = (
        inverse**3 * start[0]
        + 3 * inverse**2 * t * control1[0]
        + 3 * inverse * t**2 * control2[0]
        + t**3 * end[0]
    )
    y = (
        inverse**3 * start[1]
        + 3 * inverse**2 * t * control1[1]
        + 3 * inverse * t**2 * control2[1]
        + t**3 * end[1]
    )
    return x, y


def mark_polygon(scale: float) -> list[tuple[int, int]]:
    points: list[tuple[float, float]] = []
    current = (0.0, 0.0)
    start = (0.0, 0.0)
    for command in MARK_COMMANDS:
        if command[0] == "M":
            current = (float(command[1]), float(command[2]))
            start = current
            points.append(current)
        elif command[0] == "C":
            control1 = (float(command[1]), float(command[2]))
            control2 = (float(command[3]), float(command[4]))
            end = (float(command[5]), float(command[6]))
            for step in range(1, 17):
                points.append(cubic_point(current, control1, control2, end, step / 16.0))
            current = end
        elif command[0] == "Z":
            points.append(start)
    return [(round(x * scale), round(y * scale)) for x, y in points]


def svg_path() -> str:
    fragments: list[str] = []
    for command in MARK_COMMANDS:
        if command[0] == "M":
            fragments.append(f"M {command[1]:g} {command[2]:g}")
        elif command[0] == "C":
            fragments.append(
                f"C {command[1]:g} {command[2]:g} {command[3]:g} {command[4]:g} {command[5]:g} {command[6]:g}"
            )
        else:
            fragments.append("Z")
    return " ".join(fragments)


def render_master() -> Image.Image:
    dimension = CANVAS * SUPERSAMPLE
    scale = float(SUPERSAMPLE)
    image = Image.new("RGBA", (dimension, dimension), (0, 0, 0, 0))
    draw = ImageDraw.Draw(image)
    draw.rounded_rectangle(
        (12 * scale, 12 * scale, 244 * scale, 244 * scale),
        radius=56 * scale,
        fill=BACKGROUND,
    )
    draw.polygon(mark_polygon(scale), fill=MARK)

    width = round(18 * scale)
    l_points = [
        (111 * scale, 126 * scale),
        (111 * scale, 171 * scale),
        (148 * scale, 171 * scale),
    ]
    draw.line(l_points, fill=BACKGROUND, width=width, joint="curve")
    radius = width / 2
    for x, y in (l_points[0], l_points[-1]):
        draw.ellipse((x - radius, y - radius, x + radius, y + radius), fill=BACKGROUND)
    corner_x, corner_y = l_points[1]
    draw.ellipse(
        (corner_x - radius, corner_y - radius, corner_x + radius, corner_y + radius),
        fill=BACKGROUND,
    )
    return image


def write_svg(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(
        "\n".join(
            (
                '<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 256 256">',
                f'  <rect x="12" y="12" width="232" height="232" rx="56" fill="{BACKGROUND}"/>',
                f'  <path d="{svg_path()}" fill="{MARK}"/>',
                f'  <path d="M 111 126 V 171 H 148" fill="none" stroke="{BACKGROUND}" stroke-width="18" stroke-linecap="round" stroke-linejoin="round"/>',
                "</svg>",
                "",
            )
        ),
        encoding="utf-8",
        newline="\n",
    )


def generate(project_root: Path) -> None:
    brand_directory = project_root / "src/LocalWhale.App/Assets/Brand"
    icon_path = project_root / "src/LocalWhale.App/Assets/LocalWhale.ico"
    brand_directory.mkdir(parents=True, exist_ok=True)
    icon_path.parent.mkdir(parents=True, exist_ok=True)

    write_svg(brand_directory / "LocalWhaleMark.svg")
    master = render_master()
    for size in SMALL_PNG_SIZES:
        image = master.resize((size, size), Image.Resampling.LANCZOS)
        image.save(brand_directory / f"LocalWhaleMark-{size}.png", format="PNG", optimize=True)

    icon = master.resize((256, 256), Image.Resampling.LANCZOS)
    icon.save(icon_path, format="ICO", sizes=[(size, size) for size in SIZES])


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def check(project_root: Path) -> None:
    missing = [relative for relative in OUTPUTS if not (project_root / relative).is_file()]
    if missing:
        raise SystemExit(f"Missing committed brand asset: {missing[0]}")

    with tempfile.TemporaryDirectory(prefix="LocalWhale.BrandAssets.") as temporary:
        regenerated_root = Path(temporary)
        generate(regenerated_root)
        mismatched = [
            relative
            for relative in OUTPUTS
            if sha256(project_root / relative) != sha256(regenerated_root / relative)
        ]
        if mismatched:
            raise SystemExit(f"Brand asset is not deterministic: {mismatched[0]}")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate LocalWhale brand assets.")
    parser.add_argument(
        "--project-root",
        type=Path,
        default=Path(__file__).resolve().parents[1],
    )
    parser.add_argument("--check", action="store_true")
    arguments = parser.parse_args()
    project_root = arguments.project_root.resolve()

    if arguments.check:
        check(project_root)
        print("LocalWhale brand assets are deterministic.")
        return

    generate(project_root)
    print(f"Generated LocalWhale brand assets under {project_root}.")


if __name__ == "__main__":
    main()
