using System.Windows.Media;
using System.Windows.Media.Imaging;
using PaperNote.Desktop.Models;

namespace PaperNote.Desktop.Services;

public static class PageBackgroundService
{
    public static ImageSource? CreateImageSource(NotebookPage page)
    {
        return CreateImageSource(
            page.BackgroundImageData,
            page.BackgroundRotation,
            page.BackgroundCropLeft,
            page.BackgroundCropTop,
            page.BackgroundCropRight,
            page.BackgroundCropBottom);
    }

    public static BitmapSource? CreateImageSource(
        string? imageData,
        int rotation = 0,
        double cropLeft = 0,
        double cropTop = 0,
        double cropRight = 0,
        double cropBottom = 0)
    {
        var source = PageThumbnailService.DecodeImageData(imageData);
        if (source is null || source.PixelWidth <= 0 || source.PixelHeight <= 0) return null;

        try
        {
            var left = Math.Clamp(cropLeft, 0, 0.45);
            var top = Math.Clamp(cropTop, 0, 0.45);
            var right = Math.Clamp(cropRight, 0, 0.45);
            var bottom = Math.Clamp(cropBottom, 0, 0.45);
            var x = Math.Clamp((int)Math.Round(source.PixelWidth * left), 0, source.PixelWidth - 1);
            var y = Math.Clamp((int)Math.Round(source.PixelHeight * top), 0, source.PixelHeight - 1);
            var width = Math.Max(1, source.PixelWidth - x - (int)Math.Round(source.PixelWidth * right));
            var height = Math.Max(1, source.PixelHeight - y - (int)Math.Round(source.PixelHeight * bottom));
            width = Math.Min(width, source.PixelWidth - x);
            height = Math.Min(height, source.PixelHeight - y);

            BitmapSource result = x == 0 && y == 0 && width == source.PixelWidth && height == source.PixelHeight
                ? source
                : new CroppedBitmap(source, new System.Windows.Int32Rect(x, y, width, height));

            var normalizedRotation = ((rotation % 360) + 360) % 360;
            if (normalizedRotation != 0)
            {
                result = new TransformedBitmap(result, new RotateTransform(normalizedRotation));
            }

            if (result.CanFreeze) result.Freeze();
            return result;
        }
        catch
        {
            return source;
        }
    }
}
