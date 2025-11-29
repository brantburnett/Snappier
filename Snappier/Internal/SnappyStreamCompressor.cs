using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

#if !NET8_0_OR_GREATER
using System.Runtime.InteropServices;
#endif

namespace Snappier.Internal;

/// <summary>
/// Emits the stream format used for Snappy streams.
/// </summary>
internal class SnappyStreamCompressor : IDisposable
{
    private static ReadOnlySpan<byte> SnappyHeader =>
    [
        0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59
    ];

    private SnappyCompressor? _compressor = new();

    private byte[]? _inputBuffer;
    private int _inputBufferSize;

    private byte[]? _outputBuffer;
    private int _outputBufferSize;

    private bool _streamHeaderWritten;

    /// <summary>
    /// Processes some input, potentially returning compressed data. Flush must be called when input is complete
    /// to get any remaining compressed data.
    /// </summary>
    /// <param name="input">Uncompressed data to emit.</param>
    /// <param name="stream">Output stream.</param>
    /// <returns>A block of memory with compressed data (if any). Must be used before any subsequent call to Write.</returns>
    public void Write(ReadOnlySpan<byte> input, Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_compressor is null, this);

        EnsureBuffer();
        EnsureStreamHeaderWritten();

        while (input.Length > 0)
        {
            int bytesRead = CompressInput(input);
            input = input.Slice(bytesRead);

            WriteOutputBuffer(stream);
        }
    }

    /// <summary>
    /// Processes some input, potentially returning compressed data. Flush must be called when input is complete
    /// to get any remaining compressed data.
    /// </summary>
    /// <param name="input">Uncompressed data to emit.</param>
    /// <param name="stream">Output stream.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A block of memory with compressed data (if any). Must be used before any subsequent call to Write.</returns>
    public async Task WriteAsync(ReadOnlyMemory<byte> input, Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_compressor is null, this);

        EnsureBuffer();
        EnsureStreamHeaderWritten();

        while (input.Length > 0)
        {
            int bytesRead = CompressInput(input.Span);
            input = input.Slice(bytesRead);

            await WriteOutputBufferAsync(stream, cancellationToken).ConfigureAwait(false);
        }
    }

    public void Flush(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_compressor is null, this);

        EnsureBuffer();
        EnsureStreamHeaderWritten();

        if (_inputBufferSize > 0)
        {
            CompressBlock(_inputBuffer.AsSpan(0, _inputBufferSize));
            _inputBufferSize = 0;
        }

        WriteOutputBuffer(stream);
    }

    public async Task FlushAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        ObjectDisposedException.ThrowIf(_compressor is null, this);

        EnsureBuffer();
        EnsureStreamHeaderWritten();

        if (_inputBufferSize > 0)
        {
            CompressBlock(_inputBuffer.AsSpan(0, _inputBufferSize));
            _inputBufferSize = 0;
        }

        await WriteOutputBufferAsync(stream, cancellationToken).ConfigureAwait(false);
    }

    private void WriteOutputBuffer(Stream stream)
    {
        DebugExtensions.Assert(_outputBuffer is not null);

        if (_outputBufferSize <= 0)
        {
            return;
        }

        stream.Write(_outputBuffer, 0, _outputBufferSize);

        _outputBufferSize = 0;
    }

    private async Task WriteOutputBufferAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        DebugExtensions.Assert(_outputBuffer is not null);

        if (_outputBufferSize <= 0)
        {
            return;
        }

#if NET8_0_OR_GREATER
        await stream.WriteAsync(_outputBuffer.AsMemory(0, _outputBufferSize), cancellationToken).ConfigureAwait(false);
#else
        await stream.WriteAsync(_outputBuffer, 0, _outputBufferSize, cancellationToken).ConfigureAwait(false);
#endif

        _outputBufferSize = 0;
    }

    private void EnsureStreamHeaderWritten()
    {
        if (!_streamHeaderWritten)
        {
            SnappyHeader.CopyTo(_outputBuffer.AsSpan());
            _outputBufferSize += SnappyHeader.Length;

            _streamHeaderWritten = true;
        }
    }

    /// <summary>
    /// Processes up to one entire block from the input, potentially combining with previous input blocks.
    /// Fills the compressed data to the output buffer. Will not process more than one output block at a time
    /// to avoid overflowing the output buffer.
    /// </summary>
    /// <param name="input">Input to compress.</param>
    /// <returns>Number of bytes consumed.</returns>
    private int CompressInput(ReadOnlySpan<byte> input)
    {
        DebugExtensions.Assert(input.Length > 0);

        if (_inputBufferSize == 0 && input.Length >= Constants.BlockSize)
        {
            // Optimize to avoid copying

            input = input.Slice(0, (int) Constants.BlockSize);
            CompressBlock(input);
            return input.Length;
        }

        // Append what we can to the input buffer

        int appendLength = Math.Min(input.Length, (int) Constants.BlockSize - _inputBufferSize);
        input.Slice(0, appendLength).CopyTo(_inputBuffer.AsSpan(_inputBufferSize));
        _inputBufferSize += appendLength;

        if (_inputBufferSize >= Constants.BlockSize)
        {
            CompressBlock(_inputBuffer.AsSpan(0, _inputBufferSize));
            _inputBufferSize = 0;
        }

        return appendLength;
    }

    private void CompressBlock(ReadOnlySpan<byte> input)
    {
        DebugExtensions.Assert(_compressor != null);
        DebugExtensions.Assert(input.Length <= Constants.BlockSize);

        const int headerSize = 8; // 1 byte for chunk type, 3 bytes for block size, 4 bytes for CRC

        Span<byte> output = _outputBuffer.AsSpan(_outputBufferSize);

        // Make room for the header and CRC
        Span<byte> blockBody = output.Slice(headerSize);

        if (!_compressor.TryCompress(input, blockBody, out int bytesWritten))
        {
            // Should be unreachable since we're allocating a buffer of the correct size.
            ThrowHelper.ThrowInvalidOperationException();
        }

        if (bytesWritten < input.Length)
        {
            // Compression resulted in a smaller size, write the header

            WriteCompressedBlockHeader(input, output, bytesWritten);

            _outputBufferSize += bytesWritten + headerSize;
        }
        else
        {
            // Compression resulted in growth, switch to an uncompressed block,
            // overwriting the compressed data in the output buffer

            WriteUncompressedBlockHeader(input, output);
            input.CopyTo(blockBody);

            _outputBufferSize += input.Length + headerSize;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void WriteCompressedBlockHeader(ReadOnlySpan<byte> input, Span<byte> output, int compressedSize)
    {
        // Write block size to bytes 1-3
        int blockSize = compressedSize + 4; // CRC
        BinaryPrimitives.WriteInt32LittleEndian(output, blockSize << 8);

        // Overwrite byte 1 with the chunk type
        output[0] = (byte) Constants.ChunkType.CompressedData;

        // Write masked CRC to bytes 5-8
        uint crc = Crc32CAlgorithm.Compute(input);
        crc = Crc32CAlgorithm.ApplyMask(crc);
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(4), crc);
    }

    private static void WriteUncompressedBlockHeader(ReadOnlySpan<byte> input, Span<byte> output)
    {
        // Write block size to bytes 1-3
        int blockSize = input.Length + 4; // CRC
        BinaryPrimitives.WriteInt32LittleEndian(output, blockSize << 8);

        // Overwrite byte 1 with the chunk type
        output[0] = (byte)Constants.ChunkType.UncompressedData;

        // Write masked CRC to bytes 5-8
        uint crc = Crc32CAlgorithm.Compute(input);
        crc = Crc32CAlgorithm.ApplyMask(crc);
        BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(4), crc);
    }

    [MemberNotNull(nameof(_outputBuffer), nameof(_inputBuffer))]
    private void EnsureBuffer()
    {
        // Allocate enough room for the stream header and block headers
        _outputBuffer ??=
            ArrayPool<byte>.Shared.Rent(Helpers.MaxBlockCompressedLength + 8 + SnappyHeader.Length);

        // Allocate enough room for the stream header and block headers
        _inputBuffer ??= ArrayPool<byte>.Shared.Rent((int) Constants.BlockSize);
    }

    public void Dispose()
    {
        _compressor?.Dispose();
        _compressor = null;

        if (_outputBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_outputBuffer, clearArray: true);
            _outputBuffer = null;
        }
        if (_inputBuffer is not null)
        {
            ArrayPool<byte>.Shared.Return(_inputBuffer, clearArray: true);
            _inputBuffer = null;
        }
    }
}
