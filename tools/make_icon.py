"""Build a multi-size ICO of crop marks on a near-black rounded square."""

import struct
from pathlib import Path


def inside_round(x, y, size, radius):
    if radius <= 0:
        return True
    cx = min(max(x, radius), size - 1 - radius)
    cy = min(max(y, radius), size - 1 - radius)
    # treat the pixel center
    dx = (x + 0.5) - (cx + 0.5)
    dy = (y + 0.5) - (cy + 0.5)
    # inside the straight region
    if radius <= x < size - radius or radius <= y < size - radius:
        return 0 <= x < size and 0 <= y < size
    corners = (
        (radius, radius),
        (size - radius, radius),
        (radius, size - radius),
        (size - radius, size - radius),
    )
    px, py = x + 0.5, y + 0.5
    for cx, cy in corners:
        if (px - cx) ** 2 + (py - cy) ** 2 <= radius ** 2:
            return True
    return False


def draw(size):
    pixels = [[(0, 0, 0, 0) for _ in range(size)] for _ in range(size)]
    radius = max(2, size // 5)

    def setp(x, y, color):
        if 0 <= x < size and 0 <= y < size:
            pixels[y][x] = color

    bg = (17, 17, 17, 255)
    white = (244, 244, 245, 255)
    for y in range(size):
        for x in range(size):
            if inside_round(x, y, size, radius):
                pixels[y][x] = bg

    thickness = max(1, size // 14)
    inset = max(2, size // 5)
    arm = max(3, size // 4)

    def hbar(x0, x1, y):
        for x in range(x0, x1):
            for t in range(thickness):
                setp(x, y + t, white)

    def vbar(x, y0, y1):
        for y in range(y0, y1):
            for t in range(thickness):
                setp(x + t, y, white)

    # four crop corners
    vbar(inset, inset, inset + arm)
    hbar(inset, inset + arm, inset)
    vbar(size - inset - thickness, inset, inset + arm)
    hbar(size - inset - arm, size - inset, inset)
    vbar(inset, size - inset - arm, size - inset)
    hbar(inset, inset + arm, size - inset - thickness)
    vbar(size - inset - thickness, size - inset - arm, size - inset)
    hbar(size - inset - arm, size - inset, size - inset - thickness)
    return pixels


def dib(pixels):
    size = len(pixels)
    header = struct.pack(
        "<IiiHHIIiiII",
        40,
        size,
        size * 2,  # XOR + AND
        1,
        32,
        0,
        size * size * 4,
        0,
        0,
        0,
        0,
    )
    xor = bytearray()
    for y in range(size - 1, -1, -1):
        for x in range(size):
            r, g, b, a = pixels[y][x]
            xor += bytes((b, g, r, a))
    mask_row = ((size + 31) // 32) * 4
    mask = bytes(mask_row * size)
    return header + bytes(xor) + mask


def main():
    sizes = (16, 32, 48, 64)
    images = [dib(draw(s)) for s in sizes]
    count = len(sizes)
    header = struct.pack("<HHH", 0, 1, count)
    offset = 6 + 16 * count
    entries = b""
    blobs = b""
    for size, image in zip(sizes, images):
        entries += struct.pack(
            "<BBBBHHII",
            size if size < 256 else 0,
            size if size < 256 else 0,
            0,
            0,
            1,
            32,
            len(image),
            offset,
        )
        offset += len(image)
        blobs += image
    out = Path(__file__).resolve().parents[1] / "Assets" / "snapkit.ico"
    out.write_bytes(header + entries + blobs)
    print(out, out.stat().st_size)


if __name__ == "__main__":
    main()
