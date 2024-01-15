using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snappier.Internal
{
    internal sealed class SnappyDecompressor : IDisposable
    {
#if NET8_0_OR_GREATER
#pragma warning disable IDE0051
#pragma warning disable IDE0044
#pragma warning disable CS0649 // Field is never assigned to, and will always have its default value
        [InlineArray(Constants.MaximumTagLength)]
        private struct ScratchBuffer
        {
            private byte _element0;
        }

        private ScratchBuffer _scratch;
#pragma warning restore CS0649 // Field is never assigned to, and will always have its default value
#pragma warning restore IDE0044
#pragma warning restore IDE0051
#else
        private readonly byte[] _scratch = new byte[Constants.MaximumTagLength];
#endif

        private uint _scratchLength = 0;

        private int _remainingLiteral;

        private int _uncompressedLengthShift;
        private int _uncompressedLength;

        public bool NeedMoreData => !AllDataDecompressed && UnreadBytes == 0;

        /// <summary>
        /// Decompress a portion of the input.
        /// </summary>
        /// <param name="input">Input to process.</param>
        /// <returns>Number of bytes processed from the input.</returns>
        /// <remarks>
        /// The first call to this method after construction or after a call to <see cref="Reset"/> start at the
        /// beginning of a new Snappy block, leading with the encoded block size. It may be called multiple times
        /// as more data becomes available. <see cref="AllDataDecompressed"/> will be true once the entire block
        /// has been processed.
        /// </remarks>
        public void Decompress(ReadOnlySpan<byte> input)
        {
            if (!ExpectedLength.HasValue)
            {
                int? readLength = ReadUncompressedLength(ref input);
                if (readLength.HasValue)
                {
                    ExpectedLength = readLength.GetValueOrDefault();
                }
                else
                {
                    // Not enough data yet to process the length
                    return;
                }
            }

            // Process any input into the write buffer

            if (input.Length > 0)
            {
                if (_remainingLiteral > 0)
                {
                    int toWrite = Math.Min(_remainingLiteral, input.Length);

                    Append(input.Slice(0, toWrite));
                    input = input.Slice(toWrite);
                    _remainingLiteral -= toWrite;
                }

                if (!AllDataDecompressed && input.Length > 0)
                {
                    DecompressAllTags(input);
                }
            }
        }

        public void Reset()
        {
            _scratchLength = 0;
            _remainingLiteral = 0;

            _uncompressedLength = 0;
            _uncompressedLengthShift = 0;

            _lookbackPosition = 0;
            _readPosition = 0;
            ExpectedLength = null;
        }

        /// <summary>
        /// Read the uncompressed length stored at the start of the compressed data.
        /// </summary>
        /// <param name="input">Input data, which should begin with the varint encoded uncompressed length.</param>
        /// <returns>The length of the compressed data, or null if the length is not yet complete.</returns>
        /// <remarks>
        /// This variant is used when reading a stream, and will pause if there aren't enough bytes available
        /// in the input. Subsequent calls with more data will resume processing.
        /// </remarks>
        private int? ReadUncompressedLength(ref ReadOnlySpan<byte> input)
        {
            int result = _uncompressedLength;
            int shift = _uncompressedLengthShift;
            bool foundEnd = false;

            int i = 0;
            while (input.Length > i)
            {
                byte c = input[i];
                i += 1;

                int val = c & 0x7f;
                if (Helpers.LeftShiftOverflows((byte) val, shift))
                {
                    ThrowHelper.ThrowInvalidOperationException("Invalid stream length");
                }

                result |= val << shift;

                if (c < 128)
                {
                    foundEnd = true;
                    break;
                }

                shift += 7;

                if (shift >= 32)
                {
                    ThrowHelper.ThrowInvalidOperationException("Invalid stream length");
                }
            }

            input = input.Slice(i);
            _uncompressedLength = result;
            _uncompressedLengthShift = shift;

            return foundEnd ? result : null;
        }

        /// <summary>
        /// Read the uncompressed length stored at the start of the compressed data.
        /// </summary>
        /// <param name="input">Input data, which should begin with the varint encoded uncompressed length.</param>
        /// <returns>The length of the uncompressed data.</returns>
        /// <exception cref="InvalidDataException">Invalid stream length</exception>
        public static int ReadUncompressedLength(ReadOnlySpan<byte> input)
        {
            int result = 0;
            int shift = 0;
            bool foundEnd = false;

            int i = 0;
            while (input.Length > 0)
            {
                byte c = input[i];
                i += 1;

                int val = c & 0x7f;
                if (Helpers.LeftShiftOverflows((byte) val, shift))
                {
                    ThrowHelper.ThrowInvalidDataException("Invalid stream length");
                }

                result |= val << shift;

                if (c < 128)
                {
                    foundEnd = true;
                    break;
                }

                shift += 7;

                if (shift >= 32)
                {
                    ThrowHelper.ThrowInvalidDataException("Invalid stream length");
                }
            }

            if (!foundEnd)
            {
                ThrowHelper.ThrowInvalidDataException("Invalid stream length");
            }

            return result;
        }

        internal void DecompressAllTags(ReadOnlySpan<byte> inputSpan)
        {
            // We only index into this array with a byte, and the list is 256 long, so it's safe to skip range checks.
            // JIT doesn't seem to recognize this currently, so we'll use a ref and Unsafe.Add to avoid the checks.
            Debug.Assert(Constants.CharTable.Length >= 256);
            ref ushort charTable = ref MemoryMarshal.GetReference(Constants.CharTable);

            unchecked
            {
                ref byte input = ref Unsafe.AsRef(in inputSpan[0]);
                ref byte inputEnd = ref Unsafe.Add(ref input, inputSpan.Length);

                // Track the point in the input before which input is guaranteed to have at least Constants.MaxTagLength bytes left
                ref byte inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd, Math.Min(inputSpan.Length, Constants.MaximumTagLength - 1));

                ref byte buffer = ref _lookbackBuffer.Span[0];
                ref byte bufferEnd = ref Unsafe.Add(ref buffer, _lookbackBuffer.Length);
                ref byte op = ref Unsafe.Add(ref buffer, _lookbackPosition);

                if (_scratchLength > 0)
                {
                    // Have partial tag remaining from a previous decompress run
                    // Get the combined tag in the scratch buffer, then run through
                    // special case processing that gets the tag from the scratch buffer
                    // and any literal data from the _input buffer

                    // This is not a hot path, so it's more efficient to process this as a separate method
                    // so that the stack size of this method is smaller and JIT can produce better results

                    (uint inputUsed, uint bytesWritten) =
                        DecompressTagFromScratch(ref input, ref inputEnd, ref op, ref buffer, ref bufferEnd);
                    op = ref Unsafe.Add(ref op, bytesWritten);

                    if (inputUsed == 0)
                    {
                        // There was insufficient data to read an entire tag. Some data was moved to scratch
                        // but short circuit for another pass when we have more data.
                        return;
                    }

                    if (_remainingLiteral > 0)
                    {
                        // The last tag was fully read by there is still literal content remaining that is
                        // not yet available. Make sure we update _lookbackPosition on exit.
                        goto exit;
                    }

                    input = ref Unsafe.Add(ref input, inputUsed);
                }

                while (true)
                {
                    if (!Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength))
                    {
                        uint newScratchLength = RefillTag(ref input, ref inputEnd);
                        if (newScratchLength == uint.MaxValue)
                        {
                            break;
                        }

                        if (newScratchLength > 0)
                        {
                            // Data has been moved to the scratch buffer
                            input = ref _scratch[0];
                            inputEnd = ref Unsafe.Add(ref input, newScratchLength);
                            inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd,
                                Math.Min(newScratchLength, Constants.MaximumTagLength - 1));
                        }
                    }

                    uint preload = Helpers.UnsafeReadUInt32(ref input);

                    // Some branches refill preload in a more optimal manner, they jump here to avoid the code above
                    skip_preload:

                    byte c = (byte) preload;
                    input = ref Unsafe.Add(ref input, 1);

                    if ((c & 0x03) == Constants.Literal)
                    {
                        nint literalLength = unchecked((c >> 2) + 1);

                        if (TryFastAppend(ref op, ref bufferEnd, in input, Unsafe.ByteOffset(ref input, ref inputEnd), literalLength))
                        {
                            Debug.Assert(literalLength < 61);
                            op = ref Unsafe.Add(ref op, literalLength);
                            input = ref Unsafe.Add(ref input, literalLength);
                            // NOTE: There is no RefillTag here, as TryFastAppend()
                            // will not return true unless there's already at least five spare
                            // bytes in addition to the literal.
                            preload = Helpers.UnsafeReadUInt32(ref input);
                            goto skip_preload;
                        }

                        if (literalLength >= 61)
                        {
                            // Long literal.
                            nint literalLengthLength = literalLength - 60;
                            uint literalLengthTemp = Helpers.UnsafeReadUInt32(ref input);

                            literalLength = (nint) Helpers.ExtractLowBytes(literalLengthTemp,
                                (int) literalLengthLength) + 1;

                            input = ref Unsafe.Add(ref input, literalLengthLength);
                        }

                        nint inputRemaining = Unsafe.ByteOffset(ref input, ref inputEnd);
                        if (inputRemaining < literalLength)
                        {
                            Append(ref op, ref bufferEnd, in input, inputRemaining);
                            op = ref Unsafe.Add(ref op, inputRemaining);
                            _remainingLiteral = (int) (literalLength - inputRemaining);
                            break;
                        }

                        Append(ref op, ref bufferEnd, in input, literalLength);
                        op = ref Unsafe.Add(ref op, literalLength);
                        input = ref Unsafe.Add(ref input, literalLength);
                    }
                    else
                    {
                        if ((c & 3) == Constants.Copy4ByteOffset)
                        {
                            uint copyOffset = Helpers.UnsafeReadUInt32(ref input);
                            input = ref Unsafe.Add(ref input, 4);

                            nint length = (c >> 2) + 1;
                            AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);
                            op = ref Unsafe.Add(ref op, length);
                        }
                        else
                        {
                            ushort entry = Unsafe.Add(ref charTable, c);
                            preload = Helpers.UnsafeReadUInt32(ref input);

                            uint trailer = Helpers.ExtractLowBytes(preload, c & 3);
                            nint length = entry & 0xff;

                            // copy_offset/256 is encoded in bits 8..10.  By just fetching
                            // those bits, we get copy_offset (since the bit-field starts at
                            // bit 8).
                            uint copyOffset = (entry & 0x700u) + trailer;

                            AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);
                            op = ref Unsafe.Add(ref op, length);

                            input = ref Unsafe.Add(ref input, c & 3);

                            // By using the result of the previous load we reduce the critical
                            // dependency chain of ip to 4 cycles.
                            preload >>= (c & 3) * 8;
                            if (Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength))
                            {
                                goto skip_preload;
                            }
                        }
                    }
                }

                exit:
                // All input data is processed
                _lookbackPosition = (int)Unsafe.ByteOffset(ref buffer, ref op);
            }
        }

        // Returns the amount of the input used, 0 indicates there was insufficient data.
        // Some of the input may have been used if 0 is returned, but it isn't relevant because
        // DecompressAllTags will short circuit.
        private (uint inputUsed, uint bytesWritten) DecompressTagFromScratch(ref byte input, ref byte inputEnd, ref byte op,
            ref byte buffer, ref byte bufferEnd)
        {
            // scratch will be the scratch buffer with only the tag if true is returned
            uint inputUsed = RefillTagFromScratch(ref input, ref inputEnd);
            if (inputUsed == 0)
            {
                return (0, 0);
            }
            input = ref Unsafe.Add(ref input, inputUsed);

            // No more scratch for next cycle, we have a full buffer we're about to use
            _scratchLength = 0;

            byte c = _scratch[0];

            if ((c & 0x03) == Constants.Literal)
            {
                uint literalLength = (uint)((c >> 2) + 1);
                if (literalLength >= 61)
                {
                    // Long literal.
                    uint literalLengthLength = literalLength - 60;
                    uint literalLengthTemp = Helpers.UnsafeReadUInt32(ref _scratch[1]);

                    literalLength = Helpers.ExtractLowBytes(literalLengthTemp,
                        (int) literalLengthLength) + 1;
                }

                nint inputRemaining = Unsafe.ByteOffset(ref input, ref inputEnd);
                if (inputRemaining < literalLength)
                {
                    Append(ref op, ref bufferEnd, in input, inputRemaining);
                    _remainingLiteral = (int) (literalLength - inputRemaining);

                    return (inputUsed + (uint) inputRemaining, (uint) inputRemaining);
                }
                else
                {
                    Append(ref op, ref bufferEnd, in input, (nint)literalLength);

                    return (inputUsed + literalLength, literalLength);
                }
            }
            else if ((c & 3) == Constants.Copy4ByteOffset)
            {
                uint copyOffset = Helpers.UnsafeReadUInt32(ref _scratch[1]);

                nint length = (c >> 2) + 1;

                AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);

                return (inputUsed, (uint) length);
            }
            else
            {
                ushort entry = Constants.CharTable[c];
                uint data = Helpers.UnsafeReadUInt32(ref _scratch[1]);

                uint trailer = Helpers.ExtractLowBytes(data, c & 3);
                nint length = entry & 0xff;

                // copy_offset/256 is encoded in bits 8..10.  By just fetching
                // those bits, we get copy_offset (since the bit-field starts at
                // bit 8).
                uint copyOffset = (entry & 0x700u) + trailer;

                AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);

                return (inputUsed, (uint) length);
            }
        }

        // Returns the amount of the input used, 0 indicates there was insufficient data.
        // Some of the input may have been used if 0 is returned, but it isn't relevant because
        // DecompressAllTags will short circuit.
        private uint RefillTagFromScratch(ref byte input, ref byte inputEnd)
        {
            Debug.Assert(_scratchLength > 0);

            if (!Unsafe.IsAddressLessThan(ref input, ref inputEnd))
            {
                return 0;
            }

            // Read the tag character
            uint entry = Constants.CharTable[_scratch[0]];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint toCopy = Math.Min((uint)Unsafe.ByteOffset(ref input, ref inputEnd), needed - _scratchLength);
            Unsafe.CopyBlockUnaligned(ref _scratch[(int)_scratchLength], ref input, toCopy);

            _scratchLength += toCopy;

            if (_scratchLength < needed)
            {
                // Still insufficient
                return 0;
            }

            return toCopy;
        }

        // Returns 0 if there is sufficient data available in the input buffer for the next tag AND enough extra padding to
        // safely read preload without overrunning the buffer.
        //
        // Returns uint.MaxValue if there is insufficient data and the decompression should stop until more data is available.
        // In this case any dangling unused bytes will be moved to scratch and _scratchLength for the next iteration.
        //
        // Returns a small number if we have enough data for this tag but not enough to safely load preload without a buffer
        // overrun. In this case, further reads should be from scratch with a length up to the returned number. Scratch will
        // always have some extra bytes on the end so we don't risk buffer overruns.
        private uint RefillTag(ref byte input, ref byte inputEnd)
        {
            if (!Unsafe.IsAddressLessThan(ref input, ref inputEnd))
            {
                return uint.MaxValue;
            }

            // Read the tag character
            uint entry = Constants.CharTable[input];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint inputLength = (uint)Unsafe.ByteOffset(ref input, ref inputEnd);
            if (inputLength < needed)
            {
                // Data is insufficient, copy to scratch
                Unsafe.CopyBlockUnaligned(ref _scratch[0], ref input, inputLength);

                _scratchLength = inputLength;
                return uint.MaxValue;
            }

            if (inputLength < Constants.MaximumTagLength)
            {
                // Have enough bytes, but copy to scratch so that we do not
                // read past end of input
                Unsafe.CopyBlockUnaligned(ref _scratch[0], ref input, inputLength);

                return inputLength;
            }

            return 0;
        }

        #region Loopback Writer

        private byte[]? _lookbackBufferArray;
        private Memory<byte> _lookbackBuffer;
        private int _lookbackPosition = 0;
        private int _readPosition = 0;

        private int? _expectedLength;
        private int? ExpectedLength
        {
            get => _expectedLength;
            set
            {
                _expectedLength = value;

                if (value.HasValue && _lookbackBuffer.Length < value.GetValueOrDefault())
                {
                    if (_lookbackBufferArray is not null)
                    {
                        ArrayPool<byte>.Shared.Return(_lookbackBufferArray);
                    }

                    _lookbackBufferArray = ArrayPool<byte>.Shared.Rent(value.GetValueOrDefault());
                    _lookbackBuffer = _lookbackBufferArray.AsMemory(0, _lookbackBufferArray.Length);
                }
            }
        }

        public int UnreadBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)_lookbackPosition - _readPosition;
        }

        public bool EndOfFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ExpectedLength.HasValue && _readPosition >= ExpectedLength.GetValueOrDefault();
        }

        public bool AllDataDecompressed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ExpectedLength.HasValue && _lookbackPosition >= ExpectedLength.GetValueOrDefault();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Append(ReadOnlySpan<byte> input)
        {
            ref readonly byte inputPtr = ref input[0];

            Span<byte> lookbackSpan = _lookbackBuffer.Span;
            ref byte op = ref lookbackSpan[_lookbackPosition];

            Append(ref op, ref Unsafe.Add(ref lookbackSpan[0], lookbackSpan.Length), in inputPtr, input.Length);
            _lookbackPosition += input.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void Append(ref byte op, ref byte bufferEnd, in byte input, nint length)
        {
            if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
            {
                ThrowHelper.ThrowInvalidDataException("Data too long");
            }

            Unsafe.CopyBlockUnaligned(ref op, ref Unsafe.AsRef(in input), (uint) length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryFastAppend(ref byte op, ref byte bufferEnd, in byte input, nint available, nint length)
        {
            if (length <= 16 && available >= 16 + Constants.MaximumTagLength &&
                Unsafe.ByteOffset(ref op, ref bufferEnd) >= (nint) 16)
            {
                CopyHelpers.UnalignedCopy128(in input, ref op);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void AppendFromSelf(ref byte op, ref byte buffer, ref byte bufferEnd, uint copyOffset, nint length)
        {
            // ToInt64() ensures that this logic works correctly on x86 (with a slight perf hit on x86, though). This is because
            // nint is only 32-bit on x86, so casting uint copyOffset to an nint for the comparison can result in a negative number with some
            // forms of illegal data. This would then bypass the exception and cause unsafe memory access. Performing the comparison
            // as a long ensures we have enough bits to not lose data. On 64-bit platforms this is effectively a no-op.
            if (copyOffset == 0 || Unsafe.ByteOffset(ref buffer, ref op).ToInt64() < copyOffset)
            {
                ThrowHelper.ThrowInvalidDataException("Invalid copy offset");
            }

            if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
            {
                ThrowHelper.ThrowInvalidDataException("Data too long");
            }

            ref byte source = ref Unsafe.Subtract(ref op, copyOffset);
            CopyHelpers.IncrementalCopy(ref source, ref op,
                ref Unsafe.Add(ref op, length), ref bufferEnd);
        }

        public int Read(Span<byte> destination)
        {
            int unreadBytes = UnreadBytes;
            if (unreadBytes == 0)
            {
                return 0;
            }

            if (unreadBytes >= destination.Length)
            {
                _lookbackBuffer.Span.Slice(_readPosition, destination.Length).CopyTo(destination);
                _readPosition += destination.Length;
                return destination.Length;
            }
            else
            {
                _lookbackBuffer.Span.Slice(_readPosition, unreadBytes).CopyTo(destination);
                _readPosition += unreadBytes;
                return unreadBytes;
            }
        }

        /// <summary>
        /// Extracts the data from from the block, returning a block of memory and resetting the block.
        /// </summary>
        /// <returns>An block of memory. Caller is responsible for disposing.</returns>
        /// <remarks>
        /// This provides a more efficient way to decompress an entire block in scenarios where the caller
        /// wants an owned block of memory and isn't going to reuse the SnappyDecompressor. It avoids the
        /// need to copy a block of memory calling <see cref="Read"/>.
        /// </remarks>
        public IMemoryOwner<byte> ExtractData()
        {
            byte[]? data = _lookbackBufferArray;
            if (!ExpectedLength.HasValue)
            {
                ThrowHelper.ThrowInvalidOperationException("No data present.");
            }
            else if (data is null || ExpectedLength.GetValueOrDefault() == 0)
            {
                // Length was 0, so we've allocated nothing
                return new ByteArrayPoolMemoryOwner();
            }

            if (!AllDataDecompressed)
            {
                ThrowHelper.ThrowInvalidOperationException("Block is not fully decompressed.");
            }

            // Build the return before we reset and clear ExpectedLength
            var returnBuffer = new ByteArrayPoolMemoryOwner(data, ExpectedLength.GetValueOrDefault());

            // Clear the buffer so we don't return it
            _lookbackBufferArray = null;
            _lookbackBuffer = default;

            Reset();

            return returnBuffer;
        }

        #endregion

        #region Test Helpers

        /// <summary>
        /// Load some data into the output buffer, only used for testing.
        /// </summary>
        /// <param name="toWrite"></param>
        internal void WriteToBufferForTest(ReadOnlySpan<byte> toWrite)
        {
            Append(toWrite);
        }

        /// <summary>
        /// Load a byte array into _scratch, only used for testing.
        /// </summary>
        internal void LoadScratchForTest(byte[] newScratch, uint newScratchLength)
        {
            ThrowHelper.ThrowIfNull(newScratch);
            if (newScratchLength > ((ReadOnlySpan<byte>)_scratch).Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(newScratchLength), "Scratch length exceeds limit");
            }

            newScratch.AsSpan(0, (int) newScratchLength).CopyTo(_scratch);
            _scratchLength = newScratchLength;
        }

        /// <summary>
        /// Only used for testing.
        /// </summary>
        internal void SetExpectedLengthForTest(int expectedLength)
        {
            ExpectedLength = expectedLength;
        }

        #endregion

        public void Dispose()
        {
            if (_lookbackBufferArray is not null)
            {
                ArrayPool<byte>.Shared.Return(_lookbackBufferArray);
                _lookbackBufferArray = null;
                _lookbackBuffer = default;
            }
        }
    }
}
