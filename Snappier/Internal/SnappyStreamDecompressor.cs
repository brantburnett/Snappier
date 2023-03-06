using System;
using System.Buffers.Binary;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    /// <summary>
    /// Parses the stream format used for Snappy streams.
    /// </summary>
    internal sealed class SnappyStreamDecompressor : IDisposable
    {
        private const int ScratchBufferSize = 4;

        private SnappyDecompressor? _decompressor = new();

        private ReadOnlyMemory<byte> _input;

        private readonly byte[] _scratch = new byte[ScratchBufferSize];
        private int _scratchLength;
        private Constants.ChunkType? _chunkType;
        private int _chunkSize;
        private int _chunkBytesProcessed;
        private uint _expectedChunkCrc;
        private uint _chunkCrc;

        public int Decompress(Span<byte> buffer)
        {
            Debug.Assert(_decompressor != null);

            ReadOnlySpan<byte> input = _input.Span;

            // Cache this to use later to calculate the total bytes written
            int originalBufferLength = buffer.Length;

            while (buffer.Length > 0
                   && (input.Length > 0 || (_chunkType == Constants.ChunkType.CompressedData && _decompressor.AllDataDecompressed)))
            {
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (_chunkType)
                {
                    case null:
                        // Not in a chunk, read the chunk type and size

                        uint rawChunkHeader = ReadChunkHeader(ref input);

                        if (rawChunkHeader == 0)
                        {
                            // Not enough data, get some more
                            goto exit;
                        }

                        _chunkType = (Constants.ChunkType) (rawChunkHeader & 0xff);
                        _chunkSize = unchecked((int)(rawChunkHeader >> 8));
                        _chunkBytesProcessed = 0;
                        _scratchLength = 0;
                        _chunkCrc = 0;
                        break;

                    case Constants.ChunkType.CompressedData:
                    {
                        if (_chunkBytesProcessed < 4)
                        {
                            _decompressor.Reset();

                            if (!ReadChunkCrc(ref input))
                            {
                                // Incomplete CRC
                                goto exit;
                            }

                            if (input.Length == 0)
                            {
                                // No more data
                                goto exit;
                            }
                        }

                        while (buffer.Length > 0 && !_decompressor.EndOfFile)
                        {
                            if (_decompressor.NeedMoreData)
                            {
                                if (input.Length == 0)
                                {
                                    // No more data to give
                                    goto exit;
                                }

                                int availableChunkBytes = Math.Min(input.Length, _chunkSize - _chunkBytesProcessed);
                                Debug.Assert(availableChunkBytes > 0);

                                _decompressor.Decompress(input.Slice(0, availableChunkBytes));

                                _chunkBytesProcessed += availableChunkBytes;
                                input = input.Slice(availableChunkBytes);
                            }

                            int decompressedBytes = _decompressor.Read(buffer);

                            _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, buffer.Slice(0, decompressedBytes));

                            buffer = buffer.Slice(decompressedBytes);
                        }

                        if (_decompressor.EndOfFile)
                        {
                            // Completed reading the chunk
                            _chunkType = null;

                            uint crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
                            if (_expectedChunkCrc != crc)
                            {
                                ThrowHelper.ThrowInvalidDataException("Chunk CRC mismatch.");
                            }
                        }

                        break;
                    }

                    case Constants.ChunkType.UncompressedData:
                    {
                        if (_chunkBytesProcessed < 4)
                        {
                            if (!ReadChunkCrc(ref input))
                            {
                                // Incomplete CRC
                                goto exit;
                            }

                            if (input.Length == 0)
                            {
                                // No more data
                                goto exit;
                            }
                        }

                        int chunkBytes = unchecked(Math.Min(Math.Min(buffer.Length, input.Length),
                            _chunkSize - _chunkBytesProcessed));

                        input.Slice(0, chunkBytes).CopyTo(buffer);

                        _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, buffer.Slice(0, chunkBytes));

                        buffer = buffer.Slice(chunkBytes);
                        input = input.Slice(chunkBytes);
                        _chunkBytesProcessed += chunkBytes;

                        if (_chunkBytesProcessed >= _chunkSize)
                        {
                            // Completed reading the chunk
                            _chunkType = null;

                            uint crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
                            if (_expectedChunkCrc != crc)
                            {
                                ThrowHelper.ThrowInvalidDataException("Chunk CRC mismatch.");
                            }
                        }

                        break;
                    }

                    default:
                    {
                        if (_chunkType < Constants.ChunkType.SkippableChunk)
                        {
                            ThrowHelper.ThrowInvalidDataException($"Unknown chunk type {(int) _chunkType:x}");
                        }

                        int chunkBytes = Math.Min(input.Length, _chunkSize - _chunkBytesProcessed);

                        input = input.Slice(chunkBytes);
                        _chunkBytesProcessed += chunkBytes;

                        if (_chunkBytesProcessed >= _chunkSize)
                        {
                            // Completed reading the chunk
                            _chunkType = null;
                        }

                        break;
                    }
                }
            }

            // We use a label and goto exit to avoid an unnecessary comparison on the while loop clause before
            // exiting the loop in cases where we know we're done processing data.
            exit:
            _input = _input.Slice(_input.Length - input.Length);
            return originalBufferLength - buffer.Length;
        }

        public void SetInput(ReadOnlyMemory<byte> input)
        {
            _input = input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private uint ReadChunkHeader(ref ReadOnlySpan<byte> buffer)
        {
            if (_scratchLength > 0)
            {
                var bytesToCopyToScratch = 4 - _scratchLength;

                Span<byte> scratch = _scratch.AsSpan();
                buffer.Slice(0, bytesToCopyToScratch).CopyTo(scratch.Slice(_scratchLength));

                buffer = buffer.Slice(bytesToCopyToScratch);
                _scratchLength += bytesToCopyToScratch;

                if (_scratchLength < 4)
                {
                    // Insufficient data
                    return 0;
                }

                _scratchLength = 0;
                return BinaryPrimitives.ReadUInt32LittleEndian(scratch);
            }

            if (buffer.Length < 4)
            {
                // Insufficient data

                buffer.CopyTo(_scratch);

                _scratchLength = buffer.Length;
                buffer = Span<byte>.Empty;

                return 0;
            }
            else
            {
                uint result = BinaryPrimitives.ReadUInt32LittleEndian(buffer);
                buffer = buffer.Slice(4);
                return result;
            }
        }

        /// <summary>
        /// Assuming that we're at the beginning of a chunk, reads the CRC. If partially read, stores the value in
        /// _scratch for subsequent reads. Should not be called if chunkByteProcessed >= 4.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool ReadChunkCrc(ref ReadOnlySpan<byte> input)
        {
            Debug.Assert(_chunkBytesProcessed < 4);

            if (_chunkBytesProcessed == 0 && input.Length >= 4)
            {
                // Common fast path

                _expectedChunkCrc = BinaryPrimitives.ReadUInt32LittleEndian(input);
                input = input.Slice(4);
                _chunkBytesProcessed += 4;
                return true;
            }

            // Copy to scratch
            int crcBytesAvailable = Math.Min(input.Length, 4 - _chunkBytesProcessed);
            input.Slice(0, crcBytesAvailable).CopyTo(_scratch.AsSpan(_scratchLength));
            _scratchLength += crcBytesAvailable;
            input = input.Slice(crcBytesAvailable);
            _chunkBytesProcessed += crcBytesAvailable;

            if (_scratchLength >= 4)
            {
                _expectedChunkCrc = BinaryPrimitives.ReadUInt32LittleEndian(_scratch);
                _scratchLength = 0;
                return true;
            }

            return false;
        }

        public void Dispose()
        {
            _decompressor?.Dispose();
            _decompressor = null;
        }
    }
}
