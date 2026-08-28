using Delta.Text;
using Delta.Text.Contract;

namespace FontCheck;

internal static class NativeConformance
{
    private static readonly float[] PixelSizes = [24, 32, 48, 64];
    private static readonly System.Text.Json.JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
    };
    private const string CorpusAlphabet = "Doto Delta-built different 0123456789.!?,+-";
    private const int ReferenceAlignmentRadius = 2;
    private const int ImageSharpSampleStride = 64;

    internal static NativeCorpusSummary Run(
        SixLaborsTextService service,
        FontInstanceId font,
        string fontPath,
        ReadOnlySpan<byte> fontBytes,
        string outputDirectory,
        int requestedCaseCount,
        bool skip)
    {
        if (skip)
        {
            return NativeCorpusSummary.Skipped(requestedCaseCount, "disabled by --skip-native");
        }

        if (!NativeCoreTextRenderer.IsSupported)
        {
            return NativeCorpusSummary.Skipped(requestedCaseCount, "CoreText is available only on macOS");
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(requestedCaseCount);

        var outputPath = Path.Combine(outputDirectory, "native-conformance");
        Directory.CreateDirectory(outputPath);
        var oursVsNative = new PixelAccumulator();
        var oursVsImageSharp = new PixelAccumulator();
        var nativeVsImageSharp = new PixelAccumulator();
        var renderer = new CpuTextRenderer(service);
        var firstMismatchWritten = false;
        var firstMismatchCase = -1;
        var firstMismatchText = string.Empty;
        var firstMismatchSize = 0f;

        using var nativeRenderer = new NativeCoreTextRenderer(fontPath);
        for (var caseIndex = 0; caseIndex < requestedCaseCount; caseIndex++)
        {
            var pixelsPerEm = PixelSizes[caseIndex % PixelSizes.Length];
            var caseText = BuildCorpusText(caseIndex);
            var request = new TextShapeRequest(caseText.AsMemory(), pixelsPerEm, new[] { font });
            var shaped = service.Shape(request);
            var ours = renderer.Render(request, new CpuTextRenderOptions(
                GlyphImageMode.Coverage,
                0,
                new Rgba32(255, 255, 255, 255)));
            if (ours.IsEmpty)
            {
                throw new InvalidDataException($"Native conformance case {caseIndex} rendered no pixels.");
            }

            var native = nativeRenderer.Render(ours, shaped, pixelsPerEm);
            var nativeMeasurement = PixelComparison.MeasureRgba(ours, native);
            oursVsNative.Add(nativeMeasurement);
            PixelMeasurement imageSharpMeasurement = default;
            PixelMeasurement nativeReferenceMeasurement = default;
            ReferenceBitmap reference = default;
            var hasImageSharpSample = caseIndex % ImageSharpSampleStride == 0;
            var needsReference = hasImageSharpSample || (!firstMismatchWritten && !nativeMeasurement.IsExact);
            if (needsReference)
            {
                reference = ReferenceFontRenderer.Render(fontBytes, caseText, pixelsPerEm);
                if (hasImageSharpSample)
                {
                    imageSharpMeasurement = PixelComparison.MeasureAlpha(
                        ours.Pixels.Span,
                        ours.Width,
                        ours.Height,
                        reference,
                        ReferenceAlignmentRadius);
                    nativeReferenceMeasurement = PixelComparison.MeasureAlpha(
                        native.Pixels,
                        native.Width,
                        native.Height,
                        reference,
                        ReferenceAlignmentRadius);
                    oursVsImageSharp.Add(imageSharpMeasurement);
                    nativeVsImageSharp.Add(nativeReferenceMeasurement);
                }
            }

            if (!firstMismatchWritten
                && (!nativeMeasurement.IsExact
                    || (hasImageSharpSample
                        && (imageSharpMeasurement.TotalAbsoluteError > 0
                            || nativeReferenceMeasurement.TotalAbsoluteError > 0))))
            {
                WriteFirstMismatch(
                    outputPath,
                    caseIndex,
                    caseText,
                    pixelsPerEm,
                    ours,
                    native,
                    reference);
                firstMismatchWritten = true;
                firstMismatchCase = caseIndex;
                firstMismatchText = caseText;
                firstMismatchSize = pixelsPerEm;
            }
        }

        return new NativeCorpusSummary(
            true,
            requestedCaseCount,
            requestedCaseCount,
            PixelSizes.Length,
            "macOS CoreText/CoreGraphics",
            string.Empty,
            ImageSharpSampleStride,
            firstMismatchCase,
            firstMismatchText,
            firstMismatchSize,
            oursVsNative.CreateSummary(),
            oursVsImageSharp.CreateSummary(),
            nativeVsImageSharp.CreateSummary());
    }

    private static string BuildCorpusText(int caseIndex)
    {
        var length = 4 + caseIndex % 21;
        var state = unchecked((uint)(0x9E3779B9 + caseIndex * 0x6D2B79F5));
        var text = new char[length];
        for (var i = 0; i < text.Length; i++)
        {
            state = unchecked(state * 1664525u + 1013904223u);
            text[i] = CorpusAlphabet[(int)(state % CorpusAlphabet.Length)];
        }

        return new string(text);
    }

    private static void WriteFirstMismatch(
        string outputDirectory,
        int caseIndex,
        string text,
        float pixelsPerEm,
        CpuTextImage ours,
        NativeTextImage native,
        ReferenceBitmap reference)
    {
        var prefix = Path.Combine(outputDirectory, $"first-mismatch-{caseIndex:D5}");
        PngWriter.Write(prefix + "-deltatext.png", ours.Width, ours.Height, ours.Pixels.Span);
        PngWriter.Write(prefix + "-coretext.png", native.Width, native.Height, native.Pixels);
        PngWriter.Write(
            prefix + "-imagesharp.png",
            reference.Width,
            reference.Height,
            ReferenceToRgba(reference));

        var metadata = new
        {
            caseIndex,
            text,
            pixelsPerEm,
            deltaText = new { ours.Width, ours.Height, ours.Bounds },
            coreText = new { native.Width, native.Height },
            imageSharp = new { reference.Width, reference.Height },
        };
        File.WriteAllText(
            prefix + ".json",
            System.Text.Json.JsonSerializer.Serialize(metadata, JsonOptions));
    }

    private static byte[] ReferenceToRgba(ReferenceBitmap reference)
    {
        var pixels = new byte[checked(reference.Width * reference.Height * 4)];
        for (var index = 0; index < reference.Pixels.Length; index++)
        {
            var pixel = reference.Pixels[index];
            var offset = index * 4;
            pixels[offset] = pixel.R;
            pixels[offset + 1] = pixel.G;
            pixels[offset + 2] = pixel.B;
            pixels[offset + 3] = pixel.A;
        }

        return pixels;
    }
}

internal static class PixelComparison
{
    internal static PixelMeasurement MeasureRgba(CpuTextImage ours, NativeTextImage native)
    {
        if (ours.Width != native.Width || ours.Height != native.Height)
        {
            throw new InvalidDataException(
                $"Native bitmap dimensions differ: DeltaText={ours.Width}x{ours.Height}, "
                + $"CoreText={native.Width}x{native.Height}.");
        }

        var histogram = new long[256];
        var oursPixels = ours.Pixels.Span;
        var totalAbsoluteError = 0L;
        var mismatchedPixels = 0L;
        var maximumError = 0;
        for (var pixelIndex = 0; pixelIndex < checked(ours.Width * ours.Height); pixelIndex++)
        {
            var offset = pixelIndex * 4;
            var pixelError = 0;
            for (var channel = 0; channel < 4; channel++)
            {
                pixelError = Math.Max(pixelError, Math.Abs(oursPixels[offset + channel] - native.Pixels[offset + channel]));
            }

            histogram[pixelError]++;
            totalAbsoluteError += pixelError;
            maximumError = Math.Max(maximumError, pixelError);
            mismatchedPixels += pixelError == 0 ? 0 : 1;
        }

        return new PixelMeasurement(
            checked(ours.Width * (long)ours.Height),
            mismatchedPixels,
            totalAbsoluteError,
            maximumError,
            Percentile(histogram, 95),
            0,
            0,
            histogram);
    }

    internal static PixelMeasurement MeasureAlpha(
        ReadOnlySpan<byte> actualPixels,
        int actualWidth,
        int actualHeight,
        ReferenceBitmap expected,
        int alignmentRadius)
    {
        PixelMeasurement best = default;
        var hasBest = false;
        for (var offsetY = -alignmentRadius; offsetY <= alignmentRadius; offsetY++)
        {
            for (var offsetX = -alignmentRadius; offsetX <= alignmentRadius; offsetX++)
            {
                var candidate = MeasureAlphaAtOffset(
                    actualPixels,
                    actualWidth,
                    actualHeight,
                    expected,
                    offsetX,
                    offsetY);
                if (!hasBest
                    || candidate.TotalAbsoluteError < best.TotalAbsoluteError
                    || (candidate.TotalAbsoluteError == best.TotalAbsoluteError
                        && candidate.MismatchedPixels < best.MismatchedPixels))
                {
                    best = candidate;
                    hasBest = true;
                }
            }
        }

        return best;
    }

    private static PixelMeasurement MeasureAlphaAtOffset(
        ReadOnlySpan<byte> actualPixels,
        int actualWidth,
        int actualHeight,
        ReferenceBitmap expected,
        int offsetX,
        int offsetY)
    {
        var histogram = new long[256];
        var width = Math.Max(actualWidth, expected.Width + Math.Abs(offsetX));
        var height = Math.Max(actualHeight, expected.Height + Math.Abs(offsetY));
        var totalAbsoluteError = 0L;
        var mismatchedPixels = 0L;
        var maximumError = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var actualAlpha = GetActualAlpha(actualPixels, actualWidth, actualHeight, x, y);
                var expectedAlpha = GetExpectedAlpha(expected, x - offsetX, y - offsetY);
                var error = Math.Abs(actualAlpha - expectedAlpha);
                histogram[error]++;
                totalAbsoluteError += error;
                maximumError = Math.Max(maximumError, error);
                mismatchedPixels += error == 0 ? 0 : 1;
            }
        }

        return new PixelMeasurement(
            checked(width * (long)height),
            mismatchedPixels,
            totalAbsoluteError,
            maximumError,
            Percentile(histogram, 95),
            offsetX,
            offsetY,
            histogram);
    }

    private static int GetActualAlpha(ReadOnlySpan<byte> pixels, int width, int height, int x, int y)
        => (uint)x < (uint)width && (uint)y < (uint)height
            ? pixels[(y * width + x) * 4 + 3]
            : 0;

    private static int GetExpectedAlpha(ReferenceBitmap image, int x, int y)
        => (uint)x < (uint)image.Width && (uint)y < (uint)image.Height
            ? image.Pixels[y * image.Width + x].A
            : 0;

    private static int Percentile(ReadOnlySpan<long> histogram, int percentile)
    {
        var total = 0L;
        for (var i = 0; i < histogram.Length; i++)
        {
            total += histogram[i];
        }

        var threshold = checked((total * percentile + 99) / 100);
        var count = 0L;
        for (var i = 0; i < histogram.Length; i++)
        {
            count += histogram[i];
            if (count >= threshold)
            {
                return i;
            }
        }

        return 0;
    }
}

internal sealed class PixelAccumulator
{
    private readonly long[] _histogram = new long[256];
    private int _caseCount;
    private int _exactCaseCount;
    private long _comparedPixels;
    private long _mismatchedPixels;
    private long _totalAbsoluteError;
    private int _maximumError;
    private int _firstOffsetX;
    private int _firstOffsetY;

    internal void Add(PixelMeasurement measurement)
    {
        _caseCount++;
        _exactCaseCount += measurement.IsExact ? 1 : 0;
        _comparedPixels += measurement.ComparedPixels;
        _mismatchedPixels += measurement.MismatchedPixels;
        _totalAbsoluteError += measurement.TotalAbsoluteError;
        _maximumError = Math.Max(_maximumError, measurement.MaximumError);
        if (_caseCount == 1)
        {
            _firstOffsetX = measurement.OffsetX;
            _firstOffsetY = measurement.OffsetY;
        }

        for (var i = 0; i < _histogram.Length; i++)
        {
            _histogram[i] += measurement.Histogram[i];
        }
    }

    internal PixelPairSummary CreateSummary()
        => new(
            _caseCount,
            _exactCaseCount,
            _comparedPixels,
            _mismatchedPixels,
            _totalAbsoluteError / (double)Math.Max(1, _comparedPixels),
            PixelComparisonPercentile(_histogram, 95),
            _maximumError,
            _firstOffsetX,
            _firstOffsetY);

    private static int PixelComparisonPercentile(ReadOnlySpan<long> histogram, int percentile)
    {
        var total = 0L;
        for (var i = 0; i < histogram.Length; i++)
        {
            total += histogram[i];
        }

        var threshold = checked((total * percentile + 99) / 100);
        var count = 0L;
        for (var i = 0; i < histogram.Length; i++)
        {
            count += histogram[i];
            if (count >= threshold)
            {
                return i;
            }
        }

        return 0;
    }
}

internal readonly record struct PixelMeasurement(
    long ComparedPixels,
    long MismatchedPixels,
    long TotalAbsoluteError,
    int MaximumError,
    int P95Error,
    int OffsetX,
    int OffsetY,
    long[] Histogram)
{
    internal bool IsExact => MismatchedPixels == 0;
}

internal readonly record struct PixelPairSummary(
    int CaseCount,
    int ExactCaseCount,
    long ComparedPixels,
    long MismatchedPixels,
    double MeanAbsoluteError,
    int P95Error,
    int MaximumError,
    int FirstOffsetX,
    int FirstOffsetY);

internal readonly record struct NativeCorpusSummary(
    bool Supported,
    int RequestedCaseCount,
    int CaseCount,
    int SizeCount,
    string Backend,
    string SkipReason,
    int ImageSharpSampleStride,
    int FirstMismatchCase,
    string FirstMismatchText,
    float FirstMismatchPixelsPerEm,
    PixelPairSummary DeltaTextVsCoreText,
    PixelPairSummary DeltaTextVsImageSharp,
    PixelPairSummary CoreTextVsImageSharp)
{
    internal static NativeCorpusSummary Skipped(int requestedCaseCount, string reason)
        => new(
            false,
            requestedCaseCount,
            0,
            0,
            string.Empty,
            reason,
            0,
            -1,
            string.Empty,
            0,
            default,
            default,
            default);
}
