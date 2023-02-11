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

            Constants.ChunkType? chunkType = _chunkType;
            int chunkSize = _chunkSize;
            int chunkBytesProcessed = _chunkBytesProcessed;

            ReadOnlySpan<byte> input = _input.Span;

            // Cache this to use later to calculate the total bytes written
            int originalBufferLength = buffer.Length;

            while (buffer.Length > 0
                   && (input.Length > 0 || (chunkType == Constants.ChunkType.CompressedData && _decompressor.AllDataDecompressed)))
            {
                // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                switch (chunkType)
                {
                    case null:
                        // Not in a chunk, read the chunk type and size

                        uint rawChunkHeader = ReadChunkHeader(ref input);

                        if (rawChunkHeader == 0)
                        {
                            // Not enough data, get some more
                            break;
                        }

                        chunkType = (Constants.ChunkType) (rawChunkHeader & 0xff);
                        chunkSize = unchecked((int)(rawChunkHeader >> 8));
                        chunkBytesProcessed = 0;
                        _scratchLength = 0;
                        _chunkCrc = 0;
                        break;

                    case Constants.ChunkType.CompressedData:
                    {
                        if (chunkBytesProcessed < 4)
                        {
                            _decompressor.Reset();

                            if (!ReadChunkCrc(ref input, ref chunkBytesProcessed))
                            {
                                // Incomplete CRC
                                break;
                            }

                            if (input.Length == 0)
                            {
                                // No more data
                                break;
                            }
                        }

                        while (buffer.Length > 0 && !_decompressor.EndOfFile)
                        {
                            if (_decompressor.NeedMoreData)
                            {
                                if (input.Length == 0)
                                {
                                    // No more data to give
                                    break;
                                }

                                int availableChunkBytes = Math.Min(input.Length, chunkSize - chunkBytesProcessed);
                                Debug.Assert(availableChunkBytes > 0);

                                _decompressor.Decompress(input.Slice(0, availableChunkBytes));

                                chunkBytesProcessed += availableChunkBytes;
                                input = input.Slice(availableChunkBytes);
                            }

                            int decompressedBytes = _decompressor.Read(buffer);

                            _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, buffer.Slice(0, decompressedBytes));

                            buffer = buffer.Slice(decompressedBytes);
                        }

                        if (_decompressor.EndOfFile)
                        {
                            // Completed reading the chunk
                            chunkType = null;

                            uint crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
                            if (_expectedChunkCrc != crc)
                            {
                                throw new InvalidDataException("Chunk CRC mismatch.");
                            }
                        }

                        break;
                    }

                    case Constants.ChunkType.UncompressedData:
                    {
                        if (chunkBytesProcessed < 4)
                        {
                            if (!ReadChunkCrc(ref input, ref chunkBytesProcessed))
                            {
                                // Incomplete CRC
                                break;
                            }

                            if (input.Length == 0)
                            {
                                // No more data
                                break;
                            }
                        }

                        int chunkBytes = unchecked(Math.Min(Math.Min(buffer.Length, input.Length),
                            chunkSize - chunkBytesProcessed));

                        input.Slice(0, chunkBytes).CopyTo(buffer);

                        _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, buffer.Slice(0, chunkBytes));

                        buffer = buffer.Slice(chunkBytes);
                        input = input.Slice(chunkBytes);
                        chunkBytesProcessed += chunkBytes;

                        if (chunkBytesProcessed >= chunkSize)
                        {
                            // Completed reading the chunk
                            chunkType = null;

                            uint crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
                            if (_expectedChunkCrc != crc)
                            {
                                throw new InvalidDataException("Chunk CRC mismatch.");
                            }
                        }

                        break;
                    }

                    default:
                    {
                        if (chunkType < Constants.ChunkType.SkippableChunk)
                        {
                            throw new InvalidDataException($"Unknown chunk type {(int) chunkType:x}");
                        }

                        int chunkBytes = Math.Min(input.Length, chunkSize - chunkBytesProcessed);

                        input = input.Slice(chunkBytes);
                        chunkBytesProcessed += chunkBytes;

                        if (chunkBytesProcessed >= chunkSize)
                        {
                            // Completed reading the chunk
                            chunkType = null;
                        }

                        break;
                    }
                }
            }

            _chunkType = chunkType;
            _chunkSize = chunkSize;
            _chunkBytesProcessed = chunkBytesProcessed;

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
        private bool ReadChunkCrc(ref ReadOnlySpan<byte> input, ref int chunkBytesProcessed)
        {
            Debug.Assert(chunkBytesProcessed < 4);

            if (chunkBytesProcessed == 0 && input.Length >= 4)
            {
                // Common fast path

                _expectedChunkCrc = BinaryPrimitives.ReadUInt32LittleEndian(input);
                input = input.Slice(4);
                chunkBytesProcessed += 4;
                return true;
            }

            // Copy to scratch
            int crcBytesAvailable = Math.Min(input.Length, 4 - chunkBytesProcessed);
            input.Slice(0, crcBytesAvailable).CopyTo(_scratch.AsSpan(_scratchLength));
            _scratchLength += crcBytesAvailable;
            input = input.Slice(crcBytesAvailable);
            chunkBytesProcessed += crcBytesAvailable;

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
