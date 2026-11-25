# Image files

A picture (`TPicture`, for example the `Picture` of an image control) loads these files:

| File | Class that holds it |
|---|---|
| `.bmp` | `TBitmap` |
| `.ico` | `TIcon` |
| `.png`, `.jpg`, `.jpeg`, `.gif` | `TWICImage` |

    Image1.Picture.LoadFromFile('logo.png');

The content of the file decides, not its name: a PNG file that was named `logo.bmp` is still loaded as a
PNG. A file that cannot be recognised by its content is loaded by its extension.

## Transparency
A PNG with transparent or half-transparent areas is drawn over what is behind it: the background of the
image control or of the form shows through.

## Using TWICImage directly
`TWICImage` is in the unit `Graphics`. It is decoded by the imaging component that is part of Windows, so
it also reads the other formats installed there (for example TIFF).

    var
      Img: TWICImage;
    begin
      Img := TWICImage.Create;
      try
        Img.LoadFromFile('photo.jpg');
        Canvas.Draw(10, 10, Img);
      finally
        Img.Free;
      end;
    end;

- `AutoOrient := True` turns a photo upright when the camera stored its orientation in the file.
- `ScaleInto(Bitmap, Width, Height)` fills a bitmap with a smoothly resized copy.
- `Bitmap.Assign(Img)` gives a 32-bit bitmap with the pixels of the image.
- Of a GIF with several frames the first is shown.

## Saving
A loaded image is saved exactly as it was loaded: `SaveToFile` writes the bytes of the original file. An
image that was not loaded from a file or stream (for example one assigned from a bitmap) cannot be saved in
a compressed format yet; assign it to a `TBitmap` and save that as a `.bmp` file.

## Errors
A damaged file, or one in a format Windows cannot read, raises `EInvalidGraphic` with the message
`The image is damaged or its format is not supported`. The picture keeps what it showed before.

## In a form
A picture set at design time is stored in the form file with all its bytes, so the program does not need
the image file at run time.
