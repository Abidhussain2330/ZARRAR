using ImageMagick;

namespace ZARRAR.Utils;

/// <summary>
/// Raised when an upload is rejected before any pixel buffer is allocated: unknown or
/// unsupported container, zero-sized canvas, or a canvas beyond the decode budget.
/// Carries no attacker-controlled data — the message is for the operator log only.
/// </summary>
internal sealed class ImageProcessingException(string message) : Exception(message);

/// <summary>
/// The image primitives behind <c>BlobRepository.CreateOrUpdateImage</c>: container probe,
/// proportional resize and WebP encode, on top of Magick.NET.
/// <para>
/// Uploads are attacker-controlled, so the order here is deliberate: sniff the container from
/// its magic bytes and refuse anything not on the allow-list, pin that format on the read
/// settings so ImageMagick never guesses a coder (this is what keeps MSL/MVG/SVG/URL and the
/// other delegate-backed coders unreachable), read the header only, enforce the decode budget,
/// and only then allocate pixels.
/// </para>
/// </summary>
internal static class ImageProcessor
{
    /// <summary>
    /// Largest encoded upload accepted, in bytes. Matches the historical 32 MB guard.
    /// </summary>
    internal const long MaxEncodedBytes = 32 * 1024 * 1024;

    /// <summary>
    /// Largest decoded canvas accepted, in pixels. 40 MP at Q8 RGBA is ~160 MB of pixel
    /// cache — comfortably above any avatar, logo, favicon or event poster (the callers use
    /// target widths of 256, 300 and 640), and far below what a decompression bomb needs.
    /// </summary>
    internal const long MaxDecodedPixels = 40_000_000L;

    /// <summary>
    /// Largest single dimension accepted, in pixels. Bounds pathological strip images that
    /// stay under <see cref="MaxDecodedPixels"/> while still forcing huge row buffers.
    /// </summary>
    internal const uint MaxDimension = 16_384;

    /// <summary>WebP encoder quality. Matches the encoder default the pipeline shipped with.</summary>
    const uint WebpQuality = 75;

    /// <summary>Pixel-cache ceiling. Sized to hold <see cref="MaxDecodedPixels"/> plus resize scratch.</summary>
    const ulong MemoryLimitBytes = 512UL * 1024 * 1024;

    /// <summary>Bytes sniffed for the container probe.</summary>
    const int SignatureLength = 12;

    /// <summary>
    /// Frames a multi-frame container (TIFF, ICO) may declare. The pipeline only ever keeps the
    /// first frame, so this exists purely to bound a sequence bomb.
    /// </summary>
    const ulong MaxSequenceLength = 64;

    static ImageProcessor()
    {
        // Process-wide native limits. Disk is pinned to zero so ImageMagick can never page the
        // pixel cache out to a temporary file — there is no temp path to constrain, and a bomb
        // that outgrows the memory limit fails loudly instead of filling the disk.
        ResourceLimits.Disk = 0;
        ResourceLimits.Memory = MemoryLimitBytes;
        ResourceLimits.Area = (ulong)MaxDecodedPixels;
        ResourceLimits.Width = MaxDimension;
        ResourceLimits.Height = MaxDimension;
        ResourceLimits.ListLength = MaxSequenceLength;
        ResourceLimits.Thread = 1;

        OpenCL.IsEnabled = false;
    }

    /// <summary>
    /// Whether <paramref name="length"/> is at or above the upload ceiling.
    /// </summary>
    internal static bool ExceedsUploadLimit(long length) => length >= MaxEncodedBytes;

    /// <summary>
    /// Whether the stream carries a GIF, decided from the six-byte signature alone.
    /// GIFs are stored byte-for-byte, so they are never handed to a decoder.
    /// </summary>
    /// <param name="source">A seekable stream positioned anywhere; left at position 0.</param>
    internal static bool IsGif(Stream source) => DetectFormat(source) == MagickFormat.Gif;

    /// <summary>
    /// Decodes <paramref name="source"/>, optionally scales it to <paramref name="resize"/>
    /// pixels wide preserving the aspect ratio, and writes WebP to <paramref name="destination"/>.
    /// </summary>
    /// <param name="source">Encoded image bytes. Rewound before use; buffered first if not seekable.</param>
    /// <param name="destination">Receives the WebP bytes. Left at the end of what was written.</param>
    /// <param name="resize">Target width in pixels; zero or less leaves the size untouched.</param>
    /// <param name="token">Cancellation token, honoured between every stage.</param>
    /// <exception cref="ImageProcessingException">Unsupported container, or beyond the decode budget.</exception>
    /// <exception cref="MagickException">Malformed or truncated payload.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    internal static async Task ConvertToWebpAsync(Stream source, Stream destination, int resize,
        CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (!source.CanSeek)
        {
            await using var buffered = BufferHelper.GetTempStream(null, "image");
            await CopyBounded(source, buffered, token);
            await ConvertToWebpAsync(buffered, destination, resize, token);
            return;
        }

        if (DetectFormat(source) is not { } format || format == MagickFormat.Gif)
            throw new ImageProcessingException("Unsupported image container.");

        var settings = new MagickReadSettings { Format = format };

        using var image = new MagickImage();

        // Header-only pass. Ping parses the container metadata without inflating pixel data,
        // so a declared-but-never-delivered 30000x30000 canvas is refused for free.
        image.Ping(source, settings);
        EnsureWithinDecodeBudget(image.Width, image.Height);

        token.ThrowIfCancellationRequested();

        source.Position = 0;
        await image.ReadAsync(source, settings, token);

        EnsureWithinDecodeBudget(image.Width, image.Height);
        token.ThrowIfCancellationRequested();

        if (resize > 0)
        {
            var width = (uint)resize;
            var height = ProportionalHeight(image.Width, image.Height, width);

            // IgnoreAspectRatio pins the width to exactly the requested value; the height is
            // already the proportional one, so the ratio is preserved without fit-inside rounding.
            image.Resize(new MagickGeometry(width, height) { IgnoreAspectRatio = true });
        }

        token.ThrowIfCancellationRequested();

        image.Quality = WebpQuality;
        await image.WriteAsync(destination, MagickFormat.WebP, token);
    }

    /// <summary>
    /// Identifies the container from its magic bytes, or null when it is not on the allow-list.
    /// The stream is left at position 0.
    /// </summary>
    static MagickFormat? DetectFormat(Stream source)
    {
        if (!source.CanSeek)
            throw new ArgumentException("Format detection requires a seekable stream.", nameof(source));

        Span<byte> signature = stackalloc byte[SignatureLength];

        source.Position = 0;
        var read = source.ReadAtLeast(signature, SignatureLength, false);
        source.Position = 0;

        signature = signature[..read];

        return signature switch
        {
            [0x47, 0x49, 0x46, 0x38, 0x37 or 0x39, 0x61, ..] => MagickFormat.Gif,
            [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, ..] => MagickFormat.Png,
            [0xFF, 0xD8, 0xFF, ..] => MagickFormat.Jpeg,
            [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => MagickFormat.WebP,
            [0x42, 0x4D, ..] => MagickFormat.Bmp,
            [0x49, 0x49, 0x2A, 0x00, ..] or [0x4D, 0x4D, 0x00, 0x2A, ..] => MagickFormat.Tiff,
            [0x00, 0x00, 0x01, 0x00, ..] => MagickFormat.Ico,
            _ => null
        };
    }

    static void EnsureWithinDecodeBudget(uint width, uint height)
    {
        if (width == 0 || height == 0)
            throw new ImageProcessingException("Image reports an empty canvas.");

        if (width > MaxDimension || height > MaxDimension)
            throw new ImageProcessingException("Image dimension exceeds the decode budget.");

        if ((long)width * height > MaxDecodedPixels)
            throw new ImageProcessingException("Image area exceeds the decode budget.");
    }

    static uint ProportionalHeight(uint width, uint height, uint targetWidth) =>
        (uint)Math.Max(1L, (long)Math.Round((double)height * targetWidth / width));

    /// <summary>
    /// Copies at most <see cref="MaxEncodedBytes"/>, so a non-seekable upload cannot outrun
    /// the ceiling that <see cref="ExceedsUploadLimit"/> applies to declared lengths.
    /// </summary>
    static async Task CopyBounded(Stream source, Stream destination, CancellationToken token)
    {
        var buffer = new byte[81920];
        long total = 0;

        while (await source.ReadAsync(buffer, token) is var count and > 0)
        {
            total += count;

            if (ExceedsUploadLimit(total))
                throw new ImageProcessingException("Image exceeds the upload ceiling.");

            await destination.WriteAsync(buffer.AsMemory(0, count), token);
        }

        destination.Position = 0;
    }
}
