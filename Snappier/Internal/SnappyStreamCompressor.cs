using System;
using System.Buffers;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
#if NETSTANDARD2_0
using System.Runtime.InteropServices;
#endif
using System.Threading;
using System.Threading.Tasks;

namespace Snappier.Internal
{
    /// <summary>
    /// Emits the stream format used for Snappy streams.
    /// </summary>
    internal class SnappyStreamCompressor : IDisposable
    {
        private static readonly byte[] SnappyHeader =
        {
            0xff, 0x06, 0x00, 0x00, 0x73, 0x4e, 0x61, 0x50, 0x70, 0x59
        };

        private SnappyCompressor? _compressor = new SnappyCompressor();

        private IMemoryOwner<byte>? _inputBufferOwner;
        private Memory<byte> _inputBuffer;
        private int _inputBufferSize;

        private IMemoryOwner<byte>? _outputBufferOwner;
        private Memory<byte> _outputBuffer;
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
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (_compressor == null)
            {
                throw new ObjectDisposedException(nameof(SnappyStreamCompressor));
            }

            EnsureBuffer();
            EnsureStreamHeaderWritten();

            while (input.Length > 0)
            {
                var bytesRead = CompressInput(input);
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
        public async ValueTask WriteAsync(ReadOnlyMemory<byte> input, Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (_compressor == null)
            {
                throw new ObjectDisposedException(nameof(SnappyStreamCompressor));
            }

            EnsureBuffer();
            EnsureStreamHeaderWritten();

            while (input.Length > 0)
            {
                var bytesRead = CompressInput(input.Span);
                input = input.Slice(bytesRead);

                await WriteOutputBufferAsync(stream, cancellationToken).ConfigureAwait(false);
            }
        }

        public void Flush(Stream stream)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (_compressor == null)
            {
                throw new ObjectDisposedException(nameof(SnappyStreamCompressor));
            }

            EnsureBuffer();
            EnsureStreamHeaderWritten();

            if (_inputBufferSize > 0)
            {
                CompressBlock(_inputBuffer.Span.Slice(0, _inputBufferSize));
                _inputBufferSize = 0;
            }

            WriteOutputBuffer(stream);
        }

        public async ValueTask FlushAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (stream == null)
            {
                throw new ArgumentNullException(nameof(stream));
            }
            if (_compressor == null)
            {
                throw new ObjectDisposedException(nameof(SnappyStreamCompressor));
            }

            EnsureBuffer();
            EnsureStreamHeaderWritten();

            if (_inputBufferSize > 0)
            {
                CompressBlock(_inputBuffer.Span.Slice(0, _inputBufferSize));
                _inputBufferSize = 0;
            }

            await WriteOutputBufferAsync(stream, cancellationToken).ConfigureAwait(false);
        }

        private void WriteOutputBuffer(Stream stream)
        {
            if (_outputBufferSize <= 0)
            {
                return;
            }

            ReadOnlyMemory<byte> output = _outputBuffer.Slice(0, _outputBufferSize);

#if NETCOREAPP3_0 || NETSTANDARD2_1
            stream.Write(output.Span);
#else
            if (MemoryMarshal.TryGetArray(output, out var arraySegment))
            {
                stream.Write(arraySegment.Array!, arraySegment.Offset, arraySegment.Count);
            }
            else
            {
                var temp = ArrayPool<byte>.Shared.Rent(_outputBufferSize);
                try
                {
                    output.CopyTo(temp);
                    stream.Write(temp, 0, temp.Length);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }
#endif

            _outputBufferSize = 0;
        }

        private async ValueTask WriteOutputBufferAsync(Stream stream, CancellationToken cancellationToken = default)
        {
            if (_outputBufferSize <= 0)
            {
                return;
            }

            ReadOnlyMemory<byte> output = _outputBuffer.Slice(0, _outputBufferSize);

#if NETCOREAPP3_0 || NETSTANDARD2_1
            await stream.WriteAsync(output, cancellationToken).ConfigureAwait(false);
#else
            if (MemoryMarshal.TryGetArray(output, out var arraySegment))
            {
                await stream.WriteAsync(arraySegment.Array!, arraySegment.Offset, arraySegment.Count, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                var temp = ArrayPool<byte>.Shared.Rent(_outputBufferSize);
                try
                {
                    output.CopyTo(temp);
                    await stream.WriteAsync(temp, 0, temp.Length, cancellationToken).ConfigureAwait(false);
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }
#endif

            _outputBufferSize = 0;
        }

        private void EnsureStreamHeaderWritten()
        {
            if (!_streamHeaderWritten)
            {
                SnappyHeader.AsSpan().CopyTo(_outputBuffer.Span);
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
            Debug.Assert(input.Length > 0);

            if (_inputBufferSize == 0 && input.Length >= Constants.BlockSize)
            {
                // Optimize to avoid copying

                input = input.Slice(0, (int) Constants.BlockSize);
                CompressBlock(input);
                return input.Length;
            }

            // Append what we can to the input buffer

            var appendLength = Math.Min(input.Length, (int) Constants.BlockSize - _inputBufferSize);
            input.Slice(0, appendLength).CopyTo(_inputBuffer.Span.Slice(_inputBufferSize));
            _inputBufferSize += appendLength;

            if (_inputBufferSize >= Constants.BlockSize)
            {
                CompressBlock(_inputBuffer.Span.Slice(0, _inputBufferSize));
                _inputBufferSize = 0;
            }

            return appendLength;
        }

        private void CompressBlock(ReadOnlySpan<byte> input)
        {
            Debug.Assert(_compressor != null);
            Debug.Assert(input.Length <= Constants.BlockSize);

            var output = _outputBuffer.Span.Slice(_outputBufferSize);

            // Make room for the header and CRC
            var compressionOutput = output.Slice(8);

            var bytesWritten = _compressor.Compress(input, compressionOutput);

            // Write the header

            WriteCompressedBlockHeader(input, output, bytesWritten);

            _outputBufferSize += bytesWritten + 8;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void WriteCompressedBlockHeader(ReadOnlySpan<byte> input, Span<byte> output, int compressedSize)
        {
            var blockSize = compressedSize + 4; // CRC

            BinaryPrimitives.WriteInt32LittleEndian(output.Slice(1), blockSize);
            output[0] = (byte) Constants.ChunkType.CompressedData;

            var crc = Crc32CAlgorithm.Compute(input);
            crc = Crc32CAlgorithm.ApplyMask(crc);
            BinaryPrimitives.WriteUInt32LittleEndian(output.Slice(4), crc);
        }

        private void EnsureBuffer()
        {
            if (_outputBufferOwner == null)
            {
                // Allocate enough room for the stream header and block headers
                _outputBufferOwner =
                    MemoryPool<byte>.Shared.Rent(Helpers.MaxCompressedLength((int) Constants.BlockSize) + 8 + SnappyHeader.Length);
                _outputBuffer = _outputBufferOwner.Memory;
            }

            if (_inputBufferOwner == null)
            {
                // Allocate enough room for the stream header and block headers
                _inputBufferOwner = MemoryPool<byte>.Shared.Rent((int) Constants.BlockSize);
                _inputBuffer = _inputBufferOwner.Memory;
            }
        }

        public void Dispose()
        {
            try
            {
                _compressor?.Dispose();
            }
            finally
            {
                _compressor = null;

                try
                {
                    _outputBufferOwner?.Dispose();
                }
                finally
                {
                    _outputBufferOwner = null;
                    _outputBuffer = default;

                    try
                    {
                        _inputBufferOwner?.Dispose();
                    }
                    finally
                    {
                        _inputBufferOwner = null;
                        _inputBuffer = default;
                    }
                }
            }
        }
    }
}
