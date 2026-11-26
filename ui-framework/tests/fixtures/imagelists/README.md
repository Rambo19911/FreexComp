# Fixture values of the image-list tests

Own content only, written by the framework's own image-list writer on the development machine. Nothing was
downloaded and nothing is fetched by a test.

| File | Content | Made by |
|---|---|---|
| `alpha4.bin` | the value of the defined binary property `Bitmap` of a 16 x 16 image list with four cells: each cell holds four 8 x 8 blocks - opaque red, blue with alpha 128, green with alpha 1, transparent - rotated by one quadrant per cell (cell 0: red top left, blue top right, green bottom left, transparent bottom right) | `Img_ImageListStream` run with `--write-fixture` |

To make the file again: compile `ui-framework/tests/pascal/Img_ImageListStream.pas` with `opc` (as the test harness
does), set `FXC_UI_TESTS_DIR` to `ui-framework/tests` and run the program with `--write-fixture`. The golden test then
checks that the writer still produces exactly these bytes; the designer's codec tests read a byte-identical copy.
