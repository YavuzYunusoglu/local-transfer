using Net.Codecrete.QrCodeGenerator;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace LocalTransfer;

internal static class QrBitmapFactory
{
    public static Bitmap Create(string text, int targetSize = 280)
    {
        var code = QrCode.EncodeText(text, QrCode.Ecc.Medium);
        const int quietZone = 4;
        var modules = code.Size + quietZone * 2;
        var scale = Math.Max(1, targetSize / modules);
        var actualSize = modules * scale;

        var bitmap = new Bitmap(actualSize, actualSize, PixelFormat.Format24bppRgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.Clear(Color.White);
        graphics.CompositingMode = CompositingMode.SourceCopy;
        graphics.InterpolationMode = InterpolationMode.NearestNeighbor;
        graphics.PixelOffsetMode = PixelOffsetMode.Half;
        graphics.SmoothingMode = SmoothingMode.None;

        using var brush = new SolidBrush(Color.FromArgb(15, 23, 42));
        for (var y = 0; y < code.Size; y++)
        {
            for (var x = 0; x < code.Size; x++)
            {
                if (code.GetModule(x, y))
                {
                    graphics.FillRectangle(
                        brush,
                        (x + quietZone) * scale,
                        (y + quietZone) * scale,
                        scale,
                        scale);
                }
            }
        }

        return bitmap;
    }
}
