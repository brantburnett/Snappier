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

        private SnappyDecompressor? _decompressor = new SnappyDecompressor();

        private ReadOnlyMemory<byte> _input;

        private readonly byte[] _scratch = new byte[ScratchBufferSize];
        private int _scratchLength;
        private Constants.ChunkType? _chunkType;
        private int _chunkSize;
        private int _chunkBytesProcessed;
        private uint _expectedChunkCrc;
        private uint _chunkCrc;

        public unsafe int Decompress(Span<byte> buffer)
        {
            Debug.Assert(_decompressor != null);

            var chunkType = _chunkType;
            var chunkSize = _chunkSize;
            var chunkBytesProcessed = _chunkBytesProcessed;

            fixed (byte* bufferStart = buffer)
            {
                var bufferEnd = bufferStart + buffer.Length;
                var bufferPtr = bufferStart;

                fixed (byte* inputStart = _input.Span)
                {
                    var inputEnd = inputStart + _input.Length;
                    var inputPtr = inputStart;

                    while (bufferPtr < bufferEnd && (inputPtr < inputEnd || (chunkType == Constants.ChunkType.CompressedData && _decompressor.AllDataDecompressed)))
                    {
                        // ReSharper disable once SwitchStatementHandlesSomeKnownEnumValuesWithDefault
                        switch (chunkType)
                        {
                            case null:
                                // Not in a chunk, read the chunk type and size

                                var rawChunkHeader =
                                    ReadChunkHeader(ref inputPtr, inputEnd);

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

                                    if (!ReadChunkCrc(ref inputPtr, inputEnd, ref chunkBytesProcessed))
                                    {
                                        // Incomplete CRC
                                        break;
                                    }

                                    if (inputPtr >= inputEnd)
                                    {
                                        // No more data
                                        break;
                                    }
                                }

                                while (bufferPtr < bufferEnd && !_decompressor.EndOfFile)
                                {
                                    if (_decompressor.NeedMoreData)
                                    {
                                        var availableInputBytes = unchecked((int) (inputEnd - inputPtr));
                                        if (availableInputBytes <= 0)
                                        {
                                            // No more data to give
                                            break;
                                        }

                                        var availableChunkBytes = Math.Min(availableInputBytes,
                                            chunkSize - chunkBytesProcessed);
                                        Debug.Assert(availableChunkBytes > 0);


                                        _decompressor.Decompress(new ReadOnlySpan<byte>(inputPtr, availableChunkBytes));

                                        chunkBytesProcessed += availableChunkBytes;
                                        inputPtr += availableChunkBytes;
                                    }

                                    var decompressedBytes = _decompressor.Read(new Span<byte>(bufferPtr,
                                            unchecked((int) (bufferEnd - bufferPtr))));

                                    _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, new ReadOnlySpan<byte>(bufferPtr, decompressedBytes));

                                    bufferPtr += decompressedBytes;
                                }

                                if (_decompressor.EndOfFile)
                                {
                                    // Completed reading the chunk
                                    chunkType = null;

                                    var crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
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
                                    if (!ReadChunkCrc(ref inputPtr, inputEnd, ref chunkBytesProcessed))
                                    {
                                        // Incomplete CRC
                                        break;
                                    }

                                    if (inputPtr >= inputEnd)
                                    {
                                        // No more data
                                        break;
                                    }
                                }

                                var chunkBytes = unchecked(Math.Min(Math.Min((int)(bufferEnd - bufferPtr), (int)(inputEnd - inputPtr)),
                                    chunkSize - chunkBytesProcessed));

                                Unsafe.CopyBlockUnaligned(bufferPtr, inputPtr, unchecked((uint)chunkBytes));

                                _chunkCrc = Crc32CAlgorithm.Append(_chunkCrc, new ReadOnlySpan<byte>(bufferPtr, chunkBytes));

                                bufferPtr += chunkBytes;
                                inputPtr += chunkBytes;
                                chunkBytesProcessed += chunkBytes;

                                if (chunkBytesProcessed >= chunkSize)
                                {
                                    // Completed reading the chunk
                                    chunkType = null;

                                    var crc = Crc32CAlgorithm.ApplyMask(_chunkCrc);
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

                                var chunkBytes = Math.Min(unchecked((int)(inputEnd - inputPtr)), chunkSize - chunkBytesProcessed);

                                inputPtr += chunkBytes;
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

                    _input = _input.Slice(unchecked((int)(inputPtr - inputStart)));
                    return unchecked((int)(bufferPtr - bufferStart));
                }
            }
        }

        public void SetInput(ReadOnlyMemory<byte> input)
        {
            _input = input;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe uint ReadChunkHeader(ref byte* buffer, byte* bufferEnd)
        {
            if (_scratchLength > 0)
            {
                var bytesToCopyToScratch = 4 - _scratchLength;
                fixed (byte* scratch = _scratch)
                {
                    Buffer.MemoryCopy(buffer, scratch + _scratchLength, ScratchBufferSize, bytesToCopyToScratch);

                    buffer += bytesToCopyToScratch;
                    _scratchLength += bytesToCopyToScratch;

                    if (_scratchLength < 4)
                    {
                        // Insufficient data
                        return 0;
                    }

                    _scratchLength = 0;
                    return unchecked((uint) Helpers.UnsafeReadInt32(scratch));
                }
            }

            var bufferLength = unchecked((int) (bufferEnd - buffer));
            if (bufferLength < 4)
            {
                // Insufficient data

                fixed (byte* scratch = _scratch) {
                    Buffer.MemoryCopy(buffer, scratch, ScratchBufferSize, bufferLength);
                }

                buffer += bufferLength;
                _scratchLength = bufferLength;

                return 0;
            }
            else
            {
                var result = unchecked((uint) Helpers.UnsafeReadInt32(buffer));
                buffer += 4;
                return result;
            }
        }

        /// <summary>
        /// Assuming that we're at the beginning of a chunk, reads the CRC. If partially read, stores the value in
        /// _scratch for subsequent reads. Should not be called if chunkByteProcessed >= 4.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe bool ReadChunkCrc(ref byte* inputPtr, byte* inputEnd, ref int chunkBytesProcessed)
        {
            Debug.Assert(chunkBytesProcessed < 4);

            var bytesAvailable = unchecked((int)(inputEnd - inputPtr));

            if (chunkBytesProcessed == 0 && bytesAvailable >= 4)
            {
                // Common fast path

                _expectedChunkCrc = Helpers.UnsafeReadUInt32(inputPtr);
                inputPtr += 4;
                chunkBytesProcessed += 4;
                return true;
            }

            // Copy to scratch
            int crcBytesAvailable = Math.Min(bytesAvailable, 4 - chunkBytesProcessed);
            new ReadOnlySpan<byte>(inputPtr, crcBytesAvailable)
                .CopyTo(_scratch.AsSpan(_scratchLength));
            _scratchLength += crcBytesAvailable;
            inputPtr += crcBytesAvailable;
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
