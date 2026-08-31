using System.Buffers;
using Snappier.Internal;

namespace Snappier;

/// <summary>
/// Provides reusable compression for raw Snappy blocks.
/// </summary>
/// <remarks>
/// An encoder retains its working memory between calls. Instances are not thread-safe and must not
/// be used concurrently.
/// </remarks>
public sealed class SnappyBlockEncoder : IDisposable
{
    private SnappyCompressor? _compressor = new();

    /// <summary>
    /// Compresses a raw Snappy block into a caller-provided buffer.
    /// </summary>
    /// <param name="source">Data to compress.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <returns>Number of bytes written to <paramref name="destination"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="destination"/> is too small.</exception>
    public int Compress(ReadOnlySpan<byte> source, Span<byte> destination)
    {
        if (!TryCompress(source, destination, out int bytesWritten))
        {
            ThrowHelper.ThrowArgumentExceptionInsufficientOutputBuffer(nameof(destination));
        }

        return bytesWritten;
    }

    /// <summary>
    /// Attempts to compress a raw Snappy block into a caller-provided buffer.
    /// </summary>
    /// <param name="source">Data to compress.</param>
    /// <param name="destination">Destination buffer.</param>
    /// <param name="bytesWritten">Number of bytes written to <paramref name="destination"/>.</param>
    /// <returns><see langword="true"/> if compression succeeds; otherwise, <see langword="false"/>.</returns>
    public bool TryCompress(ReadOnlySpan<byte> source, Span<byte> destination, out int bytesWritten) =>
        Compressor.TryCompress(source, destination, out bytesWritten);

    /// <summary>
    /// Compresses a raw Snappy block into a buffer writer.
    /// </summary>
    /// <param name="source">Data to compress.</param>
    /// <param name="destination">Destination for compressed data.</param>
    public void Compress(ReadOnlySequence<byte> source, IBufferWriter<byte> destination) =>
        Compressor.Compress(source, destination);

    /// <inheritdoc />
    public void Dispose()
    {
        _compressor?.Dispose();
        _compressor = null;
    }

    private SnappyCompressor Compressor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_compressor is null, this);
            return _compressor;
        }
    }
}
