# SPDX-License-Identifier: Apache-2.0 WITH LLVM-exception
# One-off generator of progressive.jpg (see README.md): the picture of baseline.jpg written as a progressive
# JPEG by the imaging library of the Python installation on the development machine. Run by hand, never by a test.
import os
from PIL import Image

W, H = 32, 24
COLOURS = [(200, 40, 40), (40, 160, 60), (50, 70, 200), (230, 220, 80)]  # red, green, blue of the four blocks

image = Image.new("RGB", (W, H))
for y in range(H):
    for x in range(W):
        image.putpixel((x, y), COLOURS[(1 if x >= W // 2 else 0) + (2 if y >= H // 2 else 0)])
image.save(os.path.join(os.path.dirname(os.path.abspath(__file__)), "progressive.jpg"), "JPEG",
           quality=95, progressive=True, subsampling=0)
