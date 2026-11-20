namespace FreexComp.Designer;

/// <summary>A size in logical pixels at 96 DPI (DSG-ARCH-005).</summary>
public readonly record struct Size(int Width, int Height);

/// <summary>A rectangle in logical pixels at 96 DPI (DSG-ARCH-005).</summary>
public readonly record struct Rect(int Left, int Top, int Width, int Height)
{
    public int Right => Left + Width;

    public int Bottom => Top + Height;
}
