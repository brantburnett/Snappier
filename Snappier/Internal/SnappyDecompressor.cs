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
                    throw new InvalidOperationException("Invalid stream length");
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
                    throw new InvalidOperationException("Invalid stream length");
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
                    throw new InvalidDataException("Invalid stream length");
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
                    throw new InvalidDataException("Invalid stream length");
                }
            }

            if (!foundEnd)
            {
                throw new InvalidDataException("Invalid stream length");
            }

            return result;
        }

        internal unsafe void DecompressAllTags(ReadOnlySpan<byte> inputSpan)
        {
            // Put Constants.CharTable on the stack to simplify lookups within the loops below.
            // Slicing with length 256 here allows the JIT compiler to recognize the size is greater than
            // the size of the byte we're indexing with and optimize out range checks.
            ReadOnlySpan<ushort> charTable = Constants.CharTable.AsSpan(0, 256);

            unchecked
            {
                fixed (byte* inputStart = inputSpan)
                {
                    byte* inputEnd = inputStart + inputSpan.Length;
                    byte* input = inputStart;

                    // Track the point in the input before which input is guaranteed to have at least Constants.MaxTagLength bytes left
                    byte* inputLimitMinMaxTagLength = inputEnd - Math.Min(inputEnd - input, Constants.MaximumTagLength - 1);

                    fixed (byte* buffer = _lookbackBuffer.Span)
                    {
                        byte* bufferEnd = buffer + _lookbackBuffer.Length;
                        byte* op = buffer + _lookbackPosition;

                        fixed (byte* scratchStart = _scratch)
                        {
                            byte* scratch = scratchStart;

                            if (_scratchLength > 0)
                            {
                                // Have partial tag remaining from a previous decompress run
                                // Get the combined tag in the scratch buffer, then run through
                                // special case processing that gets the tag from the scratch buffer
                                // and any literal data from the _input buffer

                                // scratch will be the scratch buffer with only the tag if true is returned
                                if (!RefillTagFromScratch(ref input, inputEnd, scratch))
                                {
                                    return;
                                }

                                // No more scratch for next cycle, we have a full buffer we're about to use
                                _scratchLength = 0;

                                byte c = scratch[0];
                                scratch++;

                                if ((c & 0x03) == Constants.Literal)
                                {
                                    nint literalLength = (c >> 2) + 1;
                                    if (literalLength >= 61)
                                    {
                                        // Long literal.
                                        nint literalLengthLength = literalLength - 60;
                                        uint literalLengthTemp = Helpers.UnsafeReadUInt32(scratch);

                                        literalLength = (nint) Helpers.ExtractLowBytes(literalLengthTemp,
                                            (int) literalLengthLength) + 1;
                                    }

                                    nint inputRemaining = (nint)(inputEnd - input);
                                    if (inputRemaining < literalLength)
                                    {
                                        Append(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(bufferEnd), in Unsafe.AsRef<byte>(input), inputRemaining);
                                        op += inputRemaining;
                                        _remainingLiteral = (int) (literalLength - inputRemaining);
                                        _lookbackPosition = (int)(op - buffer);
                                        return;
                                    }
                                    else
                                    {
                                        Append(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(bufferEnd), in Unsafe.AsRef<byte>(input), literalLength);
                                        op += literalLength;
                                        input += literalLength;
                                    }
                                }
                                else if ((c & 3) == Constants.Copy4ByteOffset)
                                {
                                    uint copyOffset = Helpers.UnsafeReadUInt32(scratch);

                                    nint length = (c >> 2) + 1;

                                    AppendFromSelf(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(buffer), ref Unsafe.AsRef<byte>(bufferEnd), copyOffset, length);
                                    op += length;
                                }
                                else
                                {
                                    ushort entry = charTable[c];
                                    uint data = Helpers.UnsafeReadUInt32(scratch);

                                    uint trailer = Helpers.ExtractLowBytes(data, c & 3);
                                    nint length = entry & 0xff;

                                    // copy_offset/256 is encoded in bits 8..10.  By just fetching
                                    // those bits, we get copy_offset (since the bit-field starts at
                                    // bit 8).
                                    uint copyOffset = (entry & 0x700u) + trailer;

                                    AppendFromSelf(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(buffer), ref Unsafe.AsRef<byte>(bufferEnd), copyOffset, length);
                                    op += length;
                                }

                                //  Make sure scratch is reset
                                scratch = scratchStart;
                            }

                            if (input >= inputLimitMinMaxTagLength)
                            {
                                if (!RefillTag(ref input, ref inputEnd, scratch))
                                {
                                    goto exit;
                                }

                                inputLimitMinMaxTagLength = inputEnd - Math.Min(inputEnd - input,
                                   Constants.MaximumTagLength - 1);
                            }

                            uint preload = Helpers.UnsafeReadUInt32(input);

                            while (true)
                            {
                                byte c = (byte) preload;
                                input++;

                                if ((c & 0x03) == Constants.Literal)
                                {
                                    nint literalLength = unchecked((c >> 2) + 1);

                                    if (TryFastAppend(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(bufferEnd), in Unsafe.AsRef<byte>(input), (nint)(inputEnd - input), literalLength))
                                    {
                                        Debug.Assert(literalLength < 61);
                                        op += literalLength;
                                        input += literalLength;
                                        // NOTE: There is no RefillTag here, as TryFastAppend()
                                        // will not return true unless there's already at least five spare
                                        // bytes in addition to the literal.
                                        preload = Helpers.UnsafeReadUInt32(input);
                                        continue;
                                    }

                                    if (literalLength >= 61)
                                    {
                                        // Long literal.
                                        nint literalLengthLength = literalLength - 60;
                                        uint literalLengthTemp = Helpers.UnsafeReadUInt32(input);

                                        literalLength = (nint) Helpers.ExtractLowBytes(literalLengthTemp,
                                            (int) literalLengthLength) + 1;

                                        input += literalLengthLength;
                                    }

                                    nint inputRemaining = (nint)(inputEnd - input);
                                    if (inputRemaining < literalLength)
                                    {
                                        Append(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(bufferEnd), in Unsafe.AsRef<byte>(input), inputRemaining);
                                        op += inputRemaining;
                                        _remainingLiteral = (int) (literalLength - inputRemaining);
                                        goto exit;
                                    }
                                    else
                                    {
                                        Append(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(bufferEnd), in Unsafe.AsRef<byte>(input), literalLength);
                                        op += literalLength;
                                        input += literalLength;

                                        if (input >= inputLimitMinMaxTagLength)
                                        {
                                            if (!RefillTag(ref input, ref inputEnd, scratch))
                                            {
                                                goto exit;
                                            }

                                            inputLimitMinMaxTagLength = inputEnd - Math.Min(inputEnd - input,
                                                Constants.MaximumTagLength - 1);
                                        }

                                        preload = Helpers.UnsafeReadUInt32(input);
                                    }
                                }
                                else
                                {
                                    if ((c & 3) == Constants.Copy4ByteOffset)
                                    {
                                        uint copyOffset = Helpers.UnsafeReadUInt32(input);
                                        input += 4;

                                        nint length = (c >> 2) + 1;
                                        AppendFromSelf(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(buffer), ref Unsafe.AsRef<byte>(bufferEnd), copyOffset, length);
                                        op += length;
                                    }
                                    else
                                    {
                                        ushort entry = charTable[c];

                                        // We don't use BitConverter to read because we might be reading past the end of the span
                                        // But we know that's safe because we'll be doing it in _scratch with extra data on the end.
                                        // This reduces this step by several operations
                                        preload = Helpers.UnsafeReadUInt32(input);

                                        uint trailer = Helpers.ExtractLowBytes(preload, c & 3);
                                        nint length = entry & 0xff;

                                        // copy_offset/256 is encoded in bits 8..10.  By just fetching
                                        // those bits, we get copy_offset (since the bit-field starts at
                                        // bit 8).
                                        uint copyOffset = (entry & 0x700u) + trailer;

                                        AppendFromSelf(ref Unsafe.AsRef<byte>(op), ref Unsafe.AsRef<byte>(buffer), ref Unsafe.AsRef<byte>(bufferEnd), copyOffset, length);
                                        op += length;

                                        input += c & 3;

                                        // By using the result of the previous load we reduce the critical
                                        // dependency chain of ip to 4 cycles.
                                        preload >>= (c & 3) * 8;
                                        if (input < inputLimitMinMaxTagLength) continue;
                                    }

                                    if (input >= inputLimitMinMaxTagLength)
                                    {
                                        if (!RefillTag(ref input, ref inputEnd, scratch))
                                        {
                                            goto exit;
                                        }

                                        inputLimitMinMaxTagLength = inputEnd - Math.Min(inputEnd - input,
                                            Constants.MaximumTagLength - 1);
                                    }

                                    preload = Helpers.UnsafeReadUInt32(input);
                                }
                            }

                            exit: ; // All input data is processed
                            _lookbackPosition = (int)(op - buffer);
                        }
                    }
                }
            }
        }

        private unsafe bool RefillTagFromScratch(ref byte* input, byte* inputEnd, byte* scratch)
        {
            Debug.Assert(_scratchLength > 0);

            if (input >= inputEnd)
            {
                return false;
            }

            // Read the tag character
            byte c = *scratch;
            uint entry = Constants.CharTable[c];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint toCopy = Math.Min(unchecked((uint)(inputEnd - input)), needed - _scratchLength);
            Unsafe.CopyBlockUnaligned(scratch + _scratchLength, input, toCopy);

            _scratchLength += toCopy;
            input += toCopy;

            if (_scratchLength < needed)
            {
                // Still insufficient
                return false;
            }

            return true;
        }

        private unsafe bool RefillTag(ref byte* input, ref byte* inputEnd, byte* scratch)
        {
            if (input >= inputEnd)
            {
                return false;
            }

            // Read the tag character
            byte c = *input;
            uint entry = Constants.CharTable[c];
            uint needed = (entry >> 11) + 1; // +1 byte for 'c'

            uint inputLength = unchecked((uint)(inputEnd - input));
            if (inputLength < needed)
            {
                // Data is insufficient, copy to scratch
                Unsafe.CopyBlockUnaligned(scratch, input, inputLength);

                _scratchLength = inputLength;
                input = inputEnd;
                return false;
            }

            if (inputLength < Constants.MaximumTagLength)
            {
                // Have enough bytes, but copy to scratch so that we do not
                // read past end of input
                Unsafe.CopyBlockUnaligned(scratch, input, inputLength);

                input = scratch;
                inputEnd = input + inputLength;
            }

            return true;
        }

        #region Loopback Writer

        private IMemoryOwner<byte>? _lookbackBufferOwner;
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

                if (_expectedLength.HasValue && _lookbackBuffer.Length < _expectedLength.GetValueOrDefault())
                {
                    _lookbackBufferOwner?.Dispose();

                    // Always pad the lookback buffer with an extra byte that we don't use. This allows a "ref byte" reference past
                    // the end of the perceived buffer that still points within the array. This is a requirement so that GC can recognize
                    // the "ref byte" points within the array and adjust it if the array is moved.
                    _lookbackBufferOwner = MemoryPool<byte>.Shared.Rent(_expectedLength.GetValueOrDefault() + 1);
                    _lookbackBuffer = _lookbackBufferOwner.Memory.Slice(0, _lookbackBufferOwner.Memory.Length - 1);
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
                ThrowInvalidDataException("Data too long");
            }

            Unsafe.CopyBlockUnaligned(ref op, ref Unsafe.AsRef(in input), (uint) length);
        }

        /// <summary>
        /// Throws an <see cref="ThrowInvalidDataException"/>. This is in a separate subroutine to allow the
        /// calling subroutine to be inlined.
        /// </summary>
        /// <param name="message">Exception message.</param>
        private static void ThrowInvalidDataException(string message)
        {
            throw new InvalidDataException(message);
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
                ThrowInvalidDataException("Invalid copy offset");
            }

            if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
            {
                ThrowInvalidDataException("Data too long");
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
            var data = _lookbackBufferOwner!;
            if (!ExpectedLength.HasValue)
            {
                throw new InvalidOperationException("No data present.");
            }
            else if (_lookbackBufferOwner == null)
            {
                // Length was 0, so we've allocated nothing
                return new EmptyMemoryOwner();
            }

            if (!AllDataDecompressed)
            {
                throw new InvalidOperationException("Block is not fully decompressed.");
            }

            if (data.Memory.Length > ExpectedLength.Value)
            {
                data = new SlicedMemoryOwner(data, ExpectedLength.Value);
            }

            // Clear owner so we don't dispose it
            _lookbackBufferOwner = null;
            _lookbackBuffer = Memory<byte>.Empty;

            Reset();

            return data;
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
            _scratch = newScratch ?? throw new ArgumentNullException(nameof(newScratch));
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
            _lookbackBufferOwner?.Dispose();
            _lookbackBufferOwner = null;
        }
    }
}
