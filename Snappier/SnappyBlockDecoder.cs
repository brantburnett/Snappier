using System.Buffers;
using Snappier.Internal;

namespace Snappier;

/// <summary>
/// Provides reusable, incremental decompression for raw Snappy blocks.
/// </summary>
/// <remarks>
/// Call <see cref="Reset()"/> between blocks when using incremental decompression. The sequence-based
/// overload resets automatically. Instances are not thread-safe and must not be used concurrently.
/// </remarks>
public sealed class SnappyBlockDecoder : IDisposable
{
    private SnappyDecompressor? _decompressor = new();

    /// <summary>
    /// Decompresses as much source and buffered output as possible.
    /// </summary>
    /// <param name="source">Compressed block data.</param>
    /// <param name="destination">Destination for decompressed data.</param>
    /// <param name="bytesConsumed">Number of bytes consumed from <paramref name="source"/>.</param>
    /// <param name="bytesWritten">Number of bytes written to <paramref name="destination"/>.</param>
    /// <returns>
    /// <see cref="OperationStatus.Done"/> when the block and its output are complete;
    /// <see cref="OperationStatus.NeedMoreData"/> when more compressed source is required;
    /// <see cref="OperationStatus.DestinationTooSmall"/> when buffered output remains; or
    /// <see cref="OperationStatus.InvalidData"/> when the block is corrupt.
    /// </returns>
    /// <remarks>
    /// After <see cref="OperationStatus.DestinationTooSmall"/>, call again with an empty source to drain
    /// buffered output. After <see cref="OperationStatus.InvalidData"/> or <see cref="OperationStatus.Done"/>,
    /// call <see cref="Reset()"/> before beginning another block.
    /// </remarks>
    public OperationStatus Decompress(ReadOnlySpan<byte> source, Span<byte> destination,
        out int bytesConsumed, out int bytesWritten)
    {
        SnappyDecompressor decompressor = Decompressor;
        OperationStatus status = decompressor.Decompress(source, out bytesConsumed);

        if (status == OperationStatus.InvalidData)
        {
            bytesWritten = 0;
            return status;
        }

        bytesWritten = decompressor.Read(destination);
        return decompressor.UnreadBytes > 0 ? OperationStatus.DestinationTooSmall : status;
    }

    /// <summary>
    /// Resets the decoder for a new block while retaining reusable working memory.
    /// </summary>
    public void Reset() => Decompressor.Reset();

    /// <summary>
    /// Resets the decoder and decompresses an entire raw Snappy block into a buffer writer.
    /// </summary>
    /// <param name="source">Compressed block data.</param>
    /// <param name="destination">Destination for decompressed data.</param>
    /// <exception cref="InvalidDataException">The compressed block is invalid or incomplete.</exception>
    public void Decompress(ReadOnlySequence<byte> source, IBufferWriter<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(destination);
        Reset();

        OperationStatus status = OperationStatus.NeedMoreData;
        foreach (ReadOnlyMemory<byte> segment in source)
        {
            status = DecompressToWriter(segment.Span, destination);
        }

        if (status == OperationStatus.InvalidData)
        {
            ThrowHelper.ThrowInvalidDataException("Invalid Snappy block.");
        }
        else if (status != OperationStatus.Done)
        {
            ThrowHelper.ThrowInvalidDataExceptionIncompleteSnappyBlock();
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _decompressor?.Dispose();
        _decompressor = null;
    }

    private OperationStatus DecompressToWriter(ReadOnlySpan<byte> source, IBufferWriter<byte> destination)
    {
        OperationStatus status;
        do
        {
            Span<byte> output = destination.GetSpan(1);
            status = Decompress(source, output, out int bytesConsumed, out int bytesWritten);
            destination.Advance(bytesWritten);

            if (bytesConsumed != source.Length)
            {
                return OperationStatus.InvalidData;
            }

            source = default;
        }
        while (status == OperationStatus.DestinationTooSmall);

        return status;
    }

    private SnappyDecompressor Decompressor
    {
        get
        {
            ObjectDisposedException.ThrowIf(_decompressor is null, this);
            return _decompressor;
        }
    }
}
