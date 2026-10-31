using System.Drawing;
using System.Drawing.Imaging;

namespace CoreVision.Engine;

public static class JpegFrame
{
    const long MinQuality = 20;
    const long QualityStep = 5;
    static readonly ImageCodecInfo Encoder = ImageCodecInfo.GetImageEncoders().First(c => c.MimeType == "image/jpeg");

    /// <summary>Encodes at the best quality that fits the screen's frame limit.</summary>
    public static byte[] Encode(Bitmap bitmap, long startQuality = ScreenProtocol.DefaultJpegQuality)
    {
        for (var quality = startQuality; ; quality -= QualityStep)
        {
            using var stream = new MemoryStream();
            using var parameters = new EncoderParameters(1);
            parameters.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, quality);
            bitmap.Save(stream, Encoder, parameters);
            if (stream.Length < ScreenProtocol.MaxJpegBytes) return stream.ToArray();
            if (quality <= MinQuality) throw new InvalidOperationException("Frame is too detailed to fit the screen's limit.");
        }
    }
}
