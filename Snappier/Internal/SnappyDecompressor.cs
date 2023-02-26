using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    internal sealed class SnappyDecompressor : IDisposable
    {
        private byte[] _scratch = new byte[Constants.MaximumTagLength];
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
                var readLength = ReadUncompressedLength(ref input);
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

            var i = 0;
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

            return foundEnd ? (int?)result : null;
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

            var i = 0;
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
            // Put Constants.CharTable on the stack to simplify lookups within the loops below.
            // Slicing with length 256 here allows the JIT compiler to recognize the size is greater than
            // the size of the byte we're indexing with and optimize out range checks.
            ReadOnlySpan<ushort> charTable = Constants.CharTable.AsSpan(0, 256);

            unchecked
            {
                ref byte input = ref Unsafe.AsRef(in inputSpan[0]);

                // The reference Snappy implementation uses inputEnd as a pointer one byte past the end of the buffer.
                // However, this is not safe when using ref locals. The ref must point to somewhere within the array
                // so that GC can adjust the ref if the memory is moved.
                ref byte inputEnd = ref Unsafe.Add(ref input, inputSpan.Length - 1);

                // Track the point in the input before which input is guaranteed to have at least Constants.MaxTagLength bytes left
                ref byte inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd, Math.Min(inputSpan.Length, Constants.MaximumTagLength - 1) - 1);

                // We always allocate buffer with at least one extra byte on the end, so bufferEnd doesn't have the same
                // restrictions as inputEnd.
                ref byte buffer = ref _lookbackBuffer.Span[0];
                ref byte bufferEnd = ref Unsafe.Add(ref buffer, _lookbackBuffer.Length);
                ref byte op = ref Unsafe.Add(ref buffer, _lookbackPosition);

                // Get a reference to the first byte in the scratch buffer, we'll reuse this so that we don't repeat range checks every time
                ref byte scratch = ref _scratch[0];

                if (_scratchLength > 0)
                {
                    // Have partial tag remaining from a previous decompress run
                    // Get the combined tag in the scratch buffer, then run through
                    // special case processing that gets the tag from the scratch buffer
                    // and any literal data from the _input buffer

                    // This is not a hot path, so it's more efficient to process this as a separate method
                    // so that the stack size of this method is smaller and JIT can produce better results

                    (uint inputUsed, uint bytesWritten) =
                        DecompressTagFromScratch(ref input, ref inputEnd, ref op, ref buffer, ref bufferEnd, ref scratch);
                    if (inputUsed == 0)
                    {
                        // There was insufficient data to read an entire tag. Some data was moved to scratch
                        // but short circuit for another pass when we have more data.
                        return;
                    }

                    input = ref Unsafe.Add(ref input, inputUsed);
                    op = ref Unsafe.Add(ref op, bytesWritten);
                }

                if (!Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength))
                {
                    uint newScratchLength = RefillTag(ref input, ref inputEnd, ref scratch);
                    if (newScratchLength == uint.MaxValue)
                    {
                        goto exit;
                    }

                    if (newScratchLength > 0)
                    {
                        // Data has been moved to the scratch buffer
                        input = ref scratch;
                        inputEnd = ref Unsafe.Add(ref input, newScratchLength - 1);
                        inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd,
                            Math.Min(newScratchLength, Constants.MaximumTagLength - 1) - 1);
                    }
                }

                uint preload = Helpers.UnsafeReadUInt32(ref input);

                while (true)
                {
                    byte c = (byte) preload;
                    input = ref Unsafe.Add(ref input, 1);

                    if ((c & 0x03) == Constants.Literal)
                    {
                        nint literalLength = unchecked((c >> 2) + 1);

                        if (TryFastAppend(ref op, ref bufferEnd, in input, Unsafe.ByteOffset(ref input, ref inputEnd) + 1, literalLength))
                        {
                            Debug.Assert(literalLength < 61);
                            op = ref Unsafe.Add(ref op, literalLength);
                            input = ref Unsafe.Add(ref input, literalLength);
                            // NOTE: There is no RefillTag here, as TryFastAppend()
                            // will not return true unless there's already at least five spare
                            // bytes in addition to the literal.
                            preload = Helpers.UnsafeReadUInt32(ref input);
                            continue;
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

                        nint inputRemaining = Unsafe.ByteOffset(ref input, ref inputEnd) + 1;
                        if (inputRemaining < literalLength)
                        {
                            Append(ref op, ref bufferEnd, in input, inputRemaining);
                            op = ref Unsafe.Add(ref op, inputRemaining);
                            _remainingLiteral = (int) (literalLength - inputRemaining);
                            goto exit;
                        }
                        else
                        {
                            Append(ref op, ref bufferEnd, in input, literalLength);
                            op = ref Unsafe.Add(ref op, literalLength);
                            input = ref Unsafe.Add(ref input, literalLength);

                            if (!Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength))
                            {
                                uint newScratchLength = RefillTag(ref input, ref inputEnd, ref scratch);
                                if (newScratchLength == uint.MaxValue)
                                {
                                    goto exit;
                                }

                                if (newScratchLength > 0)
                                {
                                    // Data has been moved to the scratch buffer
                                    input = ref scratch;
                                    inputEnd = ref Unsafe.Add(ref input, newScratchLength - 1);
                                    inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd,
                                        Math.Min(newScratchLength, Constants.MaximumTagLength - 1) - 1);

                                }
                            }

                            preload = Helpers.UnsafeReadUInt32(ref input);
                        }
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
                            ushort entry = charTable[c];

                            // We don't use BitConverter to read because we might be reading past the end of the span
                            // But we know that's safe because we'll be doing it in _scratch with extra data on the end.
                            // This reduces this step by several operations
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
                            if (Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength)) continue;
                        }

                        if (!Unsafe.IsAddressLessThan(ref input, ref inputLimitMinMaxTagLength))
                        {
                            uint newScratchLength = RefillTag(ref input, ref inputEnd, ref scratch);
                            if (newScratchLength == uint.MaxValue)
                            {
                                goto exit;
                            }

                            if (newScratchLength > 0)
                            {
                                // Data has been moved to the scratch buffer
                                input = ref scratch;
                                inputEnd = ref Unsafe.Add(ref input, newScratchLength - 1);
                                inputLimitMinMaxTagLength = ref Unsafe.Subtract(ref inputEnd,
                                    Math.Min(newScratchLength, Constants.MaximumTagLength - 1) - 1);
                            }
                        }

                        preload = Helpers.UnsafeReadUInt32(ref input);
                    }
                }

                exit: ; // All input data is processed
                _lookbackPosition = (int)Unsafe.ByteOffset(ref buffer, ref op);
            }
        }

        // Returns the amount of the input used, 0 indicates there was insufficient data.
        // Some of the input may have been used if 0 is returned, but it isn't relevant because
        // DecompressAllTags will short circuit.
        private (uint inputUsed, uint bytesWritten) DecompressTagFromScratch(ref byte input, ref byte inputEnd, ref byte op,
            ref byte buffer, ref byte bufferEnd, ref byte scratch)
        {
            // scratch will be the scratch buffer with only the tag if true is returned
            uint inputUsed = RefillTagFromScratch(ref input, ref inputEnd, ref scratch);
            if (inputUsed == 0)
            {
                return (0, 0);
            }
            input = ref Unsafe.Add(ref input, inputUsed);

            // No more scratch for next cycle, we have a full buffer we're about to use
            _scratchLength = 0;

            byte c = scratch;
            scratch = ref Unsafe.Add(ref scratch, 1);

            if ((c & 0x03) == Constants.Literal)
            {
                uint literalLength = (uint)((c >> 2) + 1);
                if (literalLength >= 61)
                {
                    // Long literal.
                    uint literalLengthLength = literalLength - 60;
                    uint literalLengthTemp = Helpers.UnsafeReadUInt32(ref scratch);

                    literalLength = Helpers.ExtractLowBytes(literalLengthTemp,
                        (int) literalLengthLength) + 1;
                }

                nint inputRemaining = Unsafe.ByteOffset(ref input, ref inputEnd) + 1;
                if (inputRemaining < literalLength)
                {
                    Append(ref op, ref bufferEnd, in input, inputRemaining);
                    _remainingLiteral = (int) (literalLength - inputRemaining);
                    _lookbackPosition += (int)Unsafe.ByteOffset(ref buffer, ref op);

                    // Insufficient data in this case as well, trigger a short circuit
                    return (0, 0);
                }
                else
                {
                    Append(ref op, ref bufferEnd, in input, (nint)literalLength);

                    return (inputUsed + literalLength, literalLength);
                }
            }
            else if ((c & 3) == Constants.Copy4ByteOffset)
            {
                uint copyOffset = Helpers.UnsafeReadUInt32(ref scratch);

                nint length = (c >> 2) + 1;

                AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);

                return (inputUsed, (uint) length);
            }
            else
            {
                ushort entry = Constants.CharTable[c];
                uint data = Helpers.UnsafeReadUInt32(ref scratch);

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
        private uint RefillTagFromScratch(ref byte input, ref byte inputEnd, ref byte scratch)
        {
            Debug.Assert(_scratchLength > 0);

            if (Unsafe.IsAddressGreaterThan(ref input, ref inputEnd))
            {
                return 0;
            }

            // Read the tag character
            uint entry = Constants.CharTable[scratch];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint toCopy = Math.Min((uint)Unsafe.ByteOffset(ref input, ref inputEnd) + 1, needed - _scratchLength);
            Unsafe.CopyBlockUnaligned(ref Unsafe.Add(ref scratch, _scratchLength), ref input, toCopy);

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
        private uint RefillTag(ref byte input, ref byte inputEnd, ref byte scratch)
        {
            if (Unsafe.IsAddressGreaterThan(ref input, ref inputEnd))
            {
                return uint.MaxValue;
            }

            // Read the tag character
            uint entry = Constants.CharTable[input];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint inputLength = (uint)Unsafe.ByteOffset(ref input, ref inputEnd) + 1;
            if (inputLength < needed)
            {
                // Data is insufficient, copy to scratch
                Unsafe.CopyBlockUnaligned(ref scratch, ref input, inputLength);

                _scratchLength = inputLength;
                return uint.MaxValue;
            }

            if (inputLength < Constants.MaximumTagLength)
            {
                // Have enough bytes, but copy to scratch so that we do not
                // read past end of input
                Unsafe.CopyBlockUnaligned(ref scratch, ref input, inputLength);

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

                    // Always pad the lookback buffer with an extra byte that we don't use. This allows a "ref byte" reference past
                    // the end of the perceived buffer that still points within the array. This is a requirement so that GC can recognize
                    // the "ref byte" points within the array and adjust it if the array is moved.
                    _lookbackBufferArray = ArrayPool<byte>.Shared.Rent(value.GetValueOrDefault() + 1);
                    _lookbackBuffer = _lookbackBufferArray.AsMemory(0, _lookbackBufferArray.Length - 1);
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

            var lookbackSpan = _lookbackBuffer.Span;
            ref byte op = ref lookbackSpan[_lookbackPosition];

            Append(ref op, ref Unsafe.Add(ref lookbackSpan[0], lookbackSpan.Length), in inputPtr, input.Length);
            _lookbackPosition += input.Length;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Append(ref byte op, ref byte bufferEnd, in byte input, nint length)
        {
            if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
            {
                ThrowHelper.ThrowInvalidDataException("Data too long");
            }

            Unsafe.CopyBlockUnaligned(ref op, ref Unsafe.AsRef(in input), (uint) length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private bool TryFastAppend(ref byte op, ref byte bufferEnd, in byte input, nint available, nint length)
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
        private void AppendFromSelf(ref byte op, ref byte buffer, ref byte bufferEnd, uint copyOffset, nint length)
        {
            ref byte source = ref Unsafe.Subtract(ref op, copyOffset);
            if (!Unsafe.IsAddressLessThan(ref source, ref op) || Unsafe.IsAddressLessThan(ref source, ref buffer))
            {
                ThrowHelper.ThrowInvalidDataException("Invalid copy offset");
            }

            if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
            {
                ThrowHelper.ThrowInvalidDataException("Data too long");
            }

            CopyHelpers.IncrementalCopy(ref source, ref op,
                ref Unsafe.Add(ref op, length), ref bufferEnd);
        }

        public int Read(Span<byte> destination)
        {
            var unreadBytes = UnreadBytes;
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
            _scratch = newScratch;
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
