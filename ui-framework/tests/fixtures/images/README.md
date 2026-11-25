# Fixture images of the image tests

Own content only: flat-colour blocks drawn by the scripts of this folder on the development machine. Nothing was
downloaded and nothing is fetched by a test. The tests only read the files; the generators are run by hand.
A block layout is always: top left, top right, bottom left, bottom right, each a quarter of the picture.

| File | Size | Content | Made by |
|---|---|---|---|
| `alpha.png` | 16 x 12 | opaque red (255, 0, 0), opaque green (0, 255, 0), blue (0, 0, 255) with alpha 128, fully transparent | `make-fixtures.ps1` (PNG encoder of the operating system) |
| `plain.bmp` | 16 x 12 | 24 bits per pixel: red, green, blue, yellow (255, 255, 0) | `MakePlainBmp.pas` (the framework's own `TBitmap.SaveToFile`) |
| `blocks.gif` | 16 x 12 | red, green, blue, yellow through a palette of these four colours | `make-fixtures.ps1` (GIF encoder of the operating system) |
| `baseline.jpg` | 32 x 24 | (200, 40, 40), (40, 160, 60), (50, 70, 200), (230, 220, 80); baseline JPEG, quality 95 | `make-fixtures.ps1` (JPEG encoder of the operating system) |
| `progressive.jpg` | 32 x 24 | the picture of `baseline.jpg` as a progressive JPEG, quality 95 | `make-progressive.py` (the imaging library of the Python installation on the development machine; the encoder of the operating system has no progressive mode) |
| `rotated.jpg` | 32 x 24 | the picture of `baseline.jpg` with the orientation value 6 in its metadata (the upright picture is 24 x 32: the stored picture turned a quarter clockwise) | `make-fixtures.ps1` (JPEG encoder and metadata writer of the operating system) |
| `broken.png` | - | the first 40 bytes of `alpha.png` | `make-fixtures.ps1` |
| `not-an-image.png` | - | 64 bytes of text | `make-fixtures.ps1` |

To make the files again:

1. `powershell -ExecutionPolicy Bypass -File make-fixtures.ps1` (Windows PowerShell with the desktop imaging
   assemblies of .NET).
2. `python make-progressive.py`.
3. Compile `MakePlainBmp.pas` with `opc` (`-U ui-framework/src`) and run it with the path of `plain.bmp`.

The encoders may write different bytes on another system version; the tests depend on the decoded pixels and on
the files being read back unchanged, not on particular file bytes — except `Ctl_ImagePng.form`, which holds the
bytes of `alpha.png` and must be made again when that file changes.
