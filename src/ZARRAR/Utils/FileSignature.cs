namespace ZARRAR.Utils;

/// <summary>
/// Content-based file type checks.
/// </summary>
/// <remarks>
/// Uploads were accepted on the strength of the browser-supplied <c>Content-Type</c> header and the
/// filename extension alone (finding F-014). Both are chosen by the client, so any payload renamed
/// to <c>.pdf</c> was stored and later served back to other participants. Reading the file's own
/// leading bytes is the only check the uploader cannot forge without actually supplying that format.
/// </remarks>
public static class FileSignature
{
    static ReadOnlySpan<byte> PdfMagic => "%PDF-"u8;

    /// <summary>
    /// Whether the stream begins with the PDF file signature.
    /// </summary>
    /// <remarks>
    /// The stream position is restored, so the caller can go on to store the file. A stream that is
    /// shorter than the signature cannot be a PDF and is rejected.
    /// </remarks>
    public static async Task<bool> IsPdfAsync(Stream stream, CancellationToken token = default)
    {
        if (!stream.CanSeek)
            throw new ArgumentException("A seekable stream is required to inspect the file signature", nameof(stream));

        var origin = stream.Position;

        try
        {
            var header = new byte[PdfMagic.Length];
            stream.Position = 0;

            return await stream.ReadAtLeastAsync(header, header.Length, false, token) == header.Length
                   && header.AsSpan().SequenceEqual(PdfMagic);
        }
        finally
        {
            stream.Position = origin;
        }
    }
}
