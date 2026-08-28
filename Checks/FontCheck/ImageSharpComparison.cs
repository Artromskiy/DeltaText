using Delta.Text;
using SixLabors.ImageSharp;
using ImageRgba32 = SixLabors.ImageSharp.PixelFormats.Rgba32;

namespace FontCheck;

internal static class ImageSharpComparison
{
    internal static RenderComparison Compare(
        CpuTextImage actual,
        ReferenceBitmap expected,
        string outputDirectory,
        string name)
    {
        if (actual.IsEmpty || expected.Width == 0 || expected.Height == 0)
        {
            throw new InvalidDataException($"Cannot compare empty render fixture '{name}'.");
        }

        Directory.CreateDirectory(outputDirectory);
        var actualPath = Path.Combine(outputDirectory, $"{name}-ours.png");
        var expectedPath = Path.Combine(outputDirectory, $"{name}-imagesharp-reference.png");
        using (var actualImage = SixLabors.ImageSharp.Image.LoadPixelData<ImageRgba32>(
                   actual.Pixels.ToArray(),
                   actual.Width,
                   actual.Height))
        {
            actualImage.SaveAsPng(actualPath);
        }

        using (var expectedImage = SixLabors.ImageSharp.Image.LoadPixelData<ImageRgba32>(
                   expected.Pixels,
                   expected.Width,
                   expected.Height))
        {
            expectedImage.SaveAsPng(expectedPath);
        }

        var best = FindBestOffset(actual, expected);
        WriteAlignmentPreview(actual, expected, best, outputDirectory, name);
        // The oracle and DeltaText intentionally use different contour
        // flattening and sampling implementations.  32/255 mean error and
        // 224/255 at the 95th percentile allow anti-aliasing differences at
        // contour edges.  The independent paths must still agree on the
        // placement: a broad search is diagnostic, not permission to accept
        // a translated render.
        Require(Math.Abs(best.OffsetX) <= 1 && Math.Abs(best.OffsetY) <= 1,
            $"ImageSharp reference geometry mismatch for '{name}': "
            + $"best offset=({best.OffsetX},{best.OffsetY}), expected at most one pixel.");
        Require(best.MeanAbsoluteError <= 32,
            $"ImageSharp reference mismatch for '{name}': MAE={best.MeanAbsoluteError:0.00}, "
            + $"P95={best.P95Error:0.00}, max={best.MaximumError}, offset=({best.OffsetX},{best.OffsetY}), "
            + $"actual={Describe(actual)}, expected={Describe(expected)}.");
        Require(best.P95Error <= 224,
            $"ImageSharp reference has too many coverage differences for '{name}': "
            + $"P95={best.P95Error:0.00}, offset=({best.OffsetX},{best.OffsetY}).");

        return best with { ActualPath = actualPath, ExpectedPath = expectedPath };
    }

    private static RenderComparison FindBestOffset(CpuTextImage actual, ReferenceBitmap expected)
    {
        var best = new RenderComparison(
            int.MaxValue,
            int.MaxValue,
            0,
            0,
            0,
            string.Empty,
            string.Empty)
        {
            MeanAbsoluteError = float.MaxValue,
        };
        for (var offsetY = -8; offsetY <= 8; offsetY++)
        {
            for (var offsetX = -8; offsetX <= 8; offsetX++)
            {
                var candidate = MeasureOffset(actual, expected, offsetX, offsetY);
                if (candidate.MeanAbsoluteError < best.MeanAbsoluteError)
                {
                    best = candidate;
                }
            }
        }

        if (best.MeanAbsoluteError == float.MaxValue)
        {
            throw new InvalidDataException("The image comparison had no overlapping pixels.");
        }

        return best;
    }

    private static RenderComparison MeasureOffset(
        CpuTextImage actual,
        ReferenceBitmap expected,
        int offsetX,
        int offsetY)
    {
        var errors = new List<byte>(checked(actual.Width * actual.Height + expected.Width * expected.Height));
        var actualPixels = actual.Pixels.Span;
        var total = 0;
        var maximum = 0;
        var covered = 0;
        var unionWidth = Math.Max(actual.Width, expected.Width + Math.Abs(offsetX));
        var unionHeight = Math.Max(actual.Height, expected.Height + Math.Abs(offsetY));
        for (var y = 0; y < unionHeight; y++)
        {
            for (var x = 0; x < unionWidth; x++)
            {
                var actualAlpha = GetAlpha(actualPixels, actual.Width, actual.Height, x, y);
                var expectedX = x - offsetX;
                var expectedY = y - offsetY;
                var expectedAlpha = GetExpectedAlpha(expected, expectedX, expectedY);
                var error = Math.Abs(actualAlpha - expectedAlpha);
                total += error;
                maximum = Math.Max(maximum, error);
                errors.Add((byte)error);
                covered++;
            }
        }

        errors.Sort();
        var p95 = errors[(errors.Count - 1) * 95 / 100];
        return new RenderComparison(total, maximum, p95, offsetX, offsetY, string.Empty, string.Empty)
        {
            MeanAbsoluteError = total / (float)covered,
        };
    }

    private static int GetAlpha(ReadOnlySpan<byte> pixels, int width, int height, int x, int y)
    {
        if ((uint)x >= (uint)width || (uint)y >= (uint)height)
        {
            return 0;
        }

        return pixels[(y * width + x) * 4 + 3];
    }

    private static int GetExpectedAlpha(ReferenceBitmap image, int x, int y)
        => (uint)x < (uint)image.Width && (uint)y < (uint)image.Height
            ? image.Pixels[y * image.Width + x].A
            : 0;

    private static string Describe(CpuTextImage image)
    {
        var pixels = image.Pixels.Span;
        var count = 0;
        var left = image.Width;
        var top = image.Height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (pixels[(y * image.Width + x) * 4 + 3] == 0)
                {
                    continue;
                }

                count++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return $"{image.Width}x{image.Height},alpha={count},bbox={left},{top}-{right},{bottom}";
    }

    private static string Describe(ReferenceBitmap image)
    {
        var count = 0;
        var left = image.Width;
        var top = image.Height;
        var right = 0;
        var bottom = 0;
        for (var y = 0; y < image.Height; y++)
        {
            for (var x = 0; x < image.Width; x++)
            {
                if (image.Pixels[y * image.Width + x].A == 0)
                {
                    continue;
                }

                count++;
                left = Math.Min(left, x);
                top = Math.Min(top, y);
                right = Math.Max(right, x + 1);
                bottom = Math.Max(bottom, y + 1);
            }
        }

        return $"{image.Width}x{image.Height},alpha={count},bbox={left},{top}-{right},{bottom}";
    }

    private static void WriteAlignmentPreview(
        CpuTextImage actual,
        ReferenceBitmap expected,
        RenderComparison comparison,
        string outputDirectory,
        string name)
    {
        var width = Math.Max(actual.Width, expected.Width + Math.Abs(comparison.OffsetX));
        var height = Math.Max(actual.Height, expected.Height + Math.Abs(comparison.OffsetY));
        var pixels = new byte[checked(width * height * 4)];
        var actualPixels = actual.Pixels.Span;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var actualAlpha = (byte)GetAlpha(actualPixels, actual.Width, actual.Height, x, y);
                var expectedAlpha = (byte)GetExpectedAlpha(
                    expected,
                    x - comparison.OffsetX,
                    y - comparison.OffsetY);
                var offset = (y * width + x) * 4;
                pixels[offset] = actualAlpha;
                pixels[offset + 1] = (byte)Math.Min(actualAlpha, expectedAlpha);
                pixels[offset + 2] = expectedAlpha;
                pixels[offset + 3] = 255;
            }
        }

        PngWriter.Write(
            Path.Combine(outputDirectory, $"{name}-alignment-preview.png"),
            width,
            height,
            pixels);
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

internal readonly record struct RenderComparison(
    int TotalAbsoluteError,
    int MaximumError,
    int P95Error,
    int OffsetX,
    int OffsetY,
    string ActualPath,
    string ExpectedPath)
{
    internal float MeanAbsoluteError { get; init; }
}
