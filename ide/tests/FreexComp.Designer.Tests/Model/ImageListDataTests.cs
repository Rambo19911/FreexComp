using FreexComp.Designer.Model;
using FreexComp.Designer.Text;

namespace FreexComp.Designer.Tests.Model;

/// <summary>
/// The image-list codec of the designer (D-136; ui-framework phase8/image-list.md UIF-1463, UIF-1472…1474, UIF-1485).
/// Pixels are written as <c>$AARRGGBB</c> (bytes blue, green, red, alpha), premultiplied.
/// </summary>
public sealed class ImageListDataTests
{
    // The four blocks of the framework's fixture cells: quadrant q (top left, top right, bottom left, bottom right)
    // of cell i holds block (q + i) mod 4 of red, blue 128, green 1, transparent.
    private const uint Red = 0xFFFF0000;
    private const uint Blue = 0x80000080;
    private const uint Green = 0x01000100;
    private const uint Clear = 0x00000000;

    private static byte[] Fixture(string name) => File.ReadAllBytes(TestPaths.Combine("imagelists/" + name));

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UIF-1463")]
    [Trait("Req", "UIF-1472")]
    [Trait("Req", "UIF-1473")]
    [Trait("Req", "UIF-1485")]
    public void DecodesFrameworkStrip()
    {
        var result = ImageListData.Decode(Fixture("alpha4.bin"));

        Assert.Null(result.Error);
        var list = Assert.IsType<ImageListData>(result.Data);
        Assert.Equal(4, list.Count);
        Assert.Equal(16, list.Width);
        Assert.Equal(16, list.Height);
        Assert.All(list.Cells, c => Assert.Equal(16 * 16, c.Pixels.Length));

        // The sample points Img_ImageListStream checks for a strip of this layout (cell 0 and cell 1).
        Assert.Equal(Red, list.Cells[0].Pixel(4, 0));     // top row is the last file row: opaque red
        Assert.Equal(Blue, list.Cells[0].Pixel(12, 0));   // blue 128 premultiplied
        Assert.Equal(Green, list.Cells[0].Pixel(4, 12));  // green 1 premultiplied
        Assert.Equal(Clear, list.Cells[0].Pixel(12, 12)); // transparent: all four bytes 0
        Assert.Equal(Blue, list.Cells[1].Pixel(4, 4));    // second cell side by side (strip x 20)
        Assert.Equal(Clear, list.Cells[1].Pixel(4, 15));  // first file row is the bottom row

        // Every pixel of the four arrangements.
        uint[] blocks = [Red, Blue, Green, Clear];
        for (var i = 0; i < 4; i++)
            for (var y = 0; y < 16; y++)
                for (var x = 0; x < 16; x++)
                {
                    var q = (x >= 8 ? 1 : 0) + (y >= 8 ? 2 : 0);
                    Assert.Equal(blocks[(q + i) % 4], list.Cells[i].Pixel(x, y));
                }

        // Width and Height come from the component's properties (defaults 16); cells are cut by UIF-1463.
        var halves = ImageListData.Decode(Fixture("alpha4.bin"), width: 8, height: 8).Data!;
        Assert.Equal(8, halves.Count);
        Assert.Equal(Red, halves.Cells[0].Pixel(0, 0));   // the top rows of the strip
        Assert.Equal(Blue, halves.Cells[1].Pixel(0, 0));
        Assert.Equal(Blue, halves.Cells[2].Pixel(0, 0));  // cell 1 of the strip, top left quadrant
        var tall = ImageListData.Decode(Fixture("alpha4.bin"), width: 60, height: 20).Data!;
        Assert.Equal(1, tall.Count);                      // columns 60…63 are ignored
        Assert.Equal(Red, tall.Cells[0].Pixel(0, 0));
        Assert.Equal(Clear, tall.Cells[0].Pixel(0, 19));  // rows outside the source are transparent
        Assert.Equal(0, ImageListData.Decode(Fixture("alpha4.bin"), width: 65).Data!.Count); // narrower than Width

        var value = new BinaryValue([.. Fixture("alpha4.bin")]);
        var sized = ImageListData.Decode(Images("    Height = 8\n    Width = 32\n").Root.Children[0], value).Data!;
        Assert.Equal((2, 32, 8), (sized.Count, sized.Width, sized.Height));
        var defaults = ImageListData.Decode(Images("").Root.Children[0], value).Data!;
        Assert.Equal((4, 16, 16), (defaults.Count, defaults.Width, defaults.Height));
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UIF-1472")]
    [Trait("Req", "UIF-1474")]
    [Trait("Req", "UIF-1485")]
    public void EncodeIsByteIdentical()
    {
        var fixture = Fixture("alpha4.bin");
        Assert.Equal(fixture, ImageListData.Decode(fixture).Data!.Encode());

        // phase4-two-cells.bin: the value the Phase 4 writer wrote for Img_ImageList's two cells (red, blue; pixel
        // (1, 1) transparent; alpha = mask) = the constant Phase4Value of ui-framework Img_ImageListStream.pas.
        var phase4 = Fixture("phase4-two-cells.bin");
        var list = ImageListData.Decode(phase4).Data!;
        Assert.Equal(2, list.Count);
        Assert.Equal(0xFFFF0000u, list.Cells[0].Pixel(0, 0));
        Assert.Equal(Clear, list.Cells[0].Pixel(1, 1));
        Assert.Equal(0xFF0000FFu, list.Cells[1].Pixel(15, 15));
        Assert.Equal(Clear, list.Cells[1].Pixel(1, 1));
        Assert.Equal(phase4, list.Encode());

        // Cells built in code are canonical (UIF-1461) and encode to the writer's layout; an empty list stores nothing.
        var made = new ImageListData(2, 1, [new ImageListCell(2, [0x800000FF, 0x00123456])]);
        Assert.Equal(new uint[] { 0x80000080, 0 }, made.Cells[0].Pixels);
        byte[] expected =
        [
            0x42, 0x4D, 62, 0, 0, 0, 0, 0, 0, 0, 54, 0, 0, 0,
            40, 0, 0, 0, 2, 0, 0, 0, 1, 0, 0, 0, 1, 0, 32, 0, 0, 0, 0, 0, 8, 0, 0, 0,
            0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0,
            0x80, 0, 0, 0x80, 0, 0, 0, 0,
        ];
        Assert.Equal(expected, made.Encode());
        Assert.Empty(new ImageListData(16, 16, []).Encode());
        Assert.Equal(expected, ImageListData.Decode(expected, 2, 1).Data!.Encode());
    }

    [Fact]
    [Trait("Phase", "8")]
    [Trait("Req", "UIF-1473")]
    [Trait("Req", "UIF-1485")]
    public void RejectsMalformed()
    {
        var phase4 = Fixture("phase4-two-cells.bin");

        // The damaged values of Img_ImageListStream.
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode(Convert.FromHexString("424D3608000000000000")).Error);
        Assert.Equal(ImageListError.BadSignature, ImageListData.Decode(Convert.FromHexString("58594D5A0000000000000000000000000000")).Error);
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode(phase4[..100]).Error);
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode(phase4[..^1]).Error);
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode([]).Error);

        // Formats the bitmap reader does not accept (graphics.md UIF-544).
        Assert.Equal(ImageListError.UnsupportedFormat, ImageListData.Decode(WithField(phase4, 28, 16, 2)).Error);   // 16 bits
        Assert.Equal(ImageListError.UnsupportedFormat, ImageListData.Decode(WithField(phase4, 30, 1, 4)).Error);    // RLE
        Assert.Equal(ImageListError.UnsupportedFormat, ImageListData.Decode(WithField(phase4, 14, 12, 4)).Error);   // core header
        Assert.Equal(ImageListError.UnsupportedFormat, ImageListData.Decode(WithField(phase4, 30, 3, 4)).Error);    // bit fields, no standard masks
        Assert.Equal(ImageListError.InvalidHeader, ImageListData.Decode(WithField(phase4, 14, 20, 4)).Error);       // short header
        Assert.Equal(ImageListError.InvalidHeader, ImageListData.Decode(WithField(phase4, 18, 0, 4)).Error);        // width 0
        Assert.Equal(ImageListError.InvalidHeader, ImageListData.Decode(WithField(phase4, 10, 20, 4)).Error);       // pixels inside the header
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode(WithField(phase4, 22, int.MaxValue, 4)).Error); // huge height
        Assert.Equal(ImageListError.InvalidCellSize, ImageListData.Decode(phase4, width: 0).Error);

        // An error result carries no data; the form keeps the value as it was (the codec only reads it).
        var bad = ImageListData.Decode(phase4[..100]);
        Assert.Null(bad.Data);
        var document = Images("    Bitmap = {\n      424D3608000000000000\n    }\n");
        var images = document.Root.Children[0];
        var value = Assert.IsType<BinaryValue>(images.FindProperty("Bitmap")!.Value);
        Assert.Equal(ImageListError.Truncated, ImageListData.Decode(images, value).Error);
        Assert.Contains("424D3608000000000000", FormWriter.Write(document), StringComparison.Ordinal);

        // Accepted variants: 24-bit and all-zero alpha give opaque cells; bottom-up and top-down rows; a palette.
        var opaque = ImageListData.Decode(Bmp(2, 1, 24, [0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0, 0]), 1, 1).Data!;
        Assert.Equal(new uint[] { 0xFF302010, 0xFF605040 }, opaque.Cells.Select(c => c.Pixel(0, 0)));
        var zeroAlpha = ImageListData.Decode(Bmp(1, 1, 32, [0x10, 0x20, 0x30, 0]), 1, 1).Data!;
        Assert.Equal(0xFF302010u, zeroAlpha.Cells[0].Pixel(0, 0));
        var clamped = ImageListData.Decode(Bmp(1, 2, 32, [0xFF, 0x20, 0x30, 0x40, 0x99, 0x99, 0x99, 0]), 1, 2).Data!;
        Assert.Equal(new uint[] { 0, 0x40302040 }, clamped.Cells[0].Pixels); // file row 0 is the bottom; colour clamped; alpha 0 → 0
        var topDown = ImageListData.Decode(Bmp(1, -2, 32, [0xFF, 0x20, 0x30, 0x40, 0x99, 0x99, 0x99, 0]), 1, 2).Data!;
        Assert.Equal(new uint[] { 0x40302040, 0 }, topDown.Cells[0].Pixels);
        var palette = ImageListData.Decode(Bmp(2, 1, 1, [0b0100_0000, 0, 0, 0], [0x11, 0x22, 0x33, 0, 0x44, 0x55, 0x66, 0]), 1, 1).Data!;
        Assert.Equal(new uint[] { 0xFF332211, 0xFF665544 }, palette.Cells.Select(c => c.Pixel(0, 0)));
    }

    /// <summary>A form with one image list holding the given property lines.</summary>
    private static FormDocument Images(string properties) =>
        FormParser.Parse("object F: TForm\n  object Images: TImageList\n" + properties + "  end\nend\n", "images.form", TestCatalog.Instance).Document!;

    private static byte[] WithField(byte[] value, int offset, int field, int size)
    {
        var copy = (byte[])value.Clone();
        for (var i = 0; i < size; i++)
            copy[offset + i] = (byte)(field >> (8 * i));
        return copy;
    }

    /// <summary>A BMP file image with a 40-byte info header, the given palette and pixel bytes.</summary>
    private static byte[] Bmp(int width, int height, int bits, byte[] pixels, byte[]? palette = null)
    {
        palette ??= [];
        var offset = 54 + palette.Length;
        var bytes = new List<byte> { 0x42, 0x4D };
        void Add(int v, int size)
        {
            for (var i = 0; i < size; i++)
                bytes.Add((byte)(v >> (8 * i)));
        }

        Add(offset + pixels.Length, 4);
        Add(0, 4);
        Add(offset, 4);
        Add(40, 4);
        Add(width, 4);
        Add(height, 4);
        Add(1, 2);
        Add(bits, 2);
        Add(0, 4);
        Add(pixels.Length, 4);
        Add(0, 4);
        Add(0, 4);
        Add(palette.Length / 4, 4);
        Add(0, 4);
        bytes.AddRange(palette);
        bytes.AddRange(pixels);
        return [.. bytes];
    }
}
