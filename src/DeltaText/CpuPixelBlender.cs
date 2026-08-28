using Delta.Text.Contract;

namespace Delta.Text;

internal static class CpuPixelBlender
{
    internal static void BlendMonochrome(
        byte[] destination,
        int destinationIndex,
        Rgba32 foreground,
        byte alpha)
    {
        var sourceRed = foreground.Red * alpha / 255;
        var sourceGreen = foreground.Green * alpha / 255;
        var sourceBlue = foreground.Blue * alpha / 255;
        BlendPremultiplied(destination, destinationIndex, sourceRed, sourceGreen, sourceBlue, alpha);
    }

    internal static void BlendPremultiplied(
        byte[] destination,
        int destinationIndex,
        int sourceRed,
        int sourceGreen,
        int sourceBlue,
        int sourceAlpha)
    {
        var inverseAlpha = 255 - sourceAlpha;
        destination[destinationIndex] = Clamp(sourceRed + destination[destinationIndex] * inverseAlpha / 255);
        destination[destinationIndex + 1] = Clamp(sourceGreen + destination[destinationIndex + 1] * inverseAlpha / 255);
        destination[destinationIndex + 2] = Clamp(sourceBlue + destination[destinationIndex + 2] * inverseAlpha / 255);
        destination[destinationIndex + 3] = Clamp(sourceAlpha + destination[destinationIndex + 3] * inverseAlpha / 255);
    }

    private static byte Clamp(int value) => (byte)Math.Min(255, value);
}
