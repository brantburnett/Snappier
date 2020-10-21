using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace Snappier.Internal
{
    internal sealed class SnappyDecompressor : IDisposable
    {
        private readonly byte[] _scratch = new byte[Constants.MaximumTagLength];
        private int _scratchLength = 0;

        private int _remainingLiteral;

        private int _uncompressedLengthShift;
        private int _uncompressedLength;

        public bool NeedMoreData => !AllDataDecompressed && UnreadBytes == 0;

        public void Decompress(ReadOnlySpan<byte> input)
        {
            if (ExpectedLength == 0)
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
                    var toWrite = Math.Min(_remainingLiteral, input.Length);

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
            ExpectedLength = 0;
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
            while (input.Length > 0)
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

        private unsafe void DecompressAllTags(ReadOnlySpan<byte> inputSpan)
        {
            // Put Constants.CharTable on the stack to simplify lookups within the loops below
            ReadOnlySpan<ushort> charTable = Constants.CharTable.AsSpan();

            unchecked
            {
                fixed (byte* inputStart = inputSpan)
                {
                    var inputEnd = inputStart + inputSpan.Length;
                    var input = inputStart;

                    // Track the point in the input before which input is guaranteed to have at least Constants.MaxTagLength bytes left
                    var inputLimitMinMaxTagLength = inputEnd - Math.Min(inputEnd - input, Constants.MaximumTagLength - 1);

                    fixed (byte* buffer = _lookbackBuffer.Span)
                    {
                        fixed (byte* scratchStart = _scratch)
                        {
                            fixed (sbyte* pshufbFillPatterns = CopyHelpers.PshufbFillPatterns)
                            {
                                var scratch = scratchStart;

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
                                        long literalLength = (c >> 2) + 1;
                                        if (literalLength >= 61)
                                        {
                                            // Long literal.
                                            long literalLengthLength = literalLength - 60;
                                            var literalLengthTemp = Helpers.UnsafeReadInt32(scratch);

                                            literalLength = (int) Helpers.ExtractLowBytes((uint) literalLengthTemp,
                                                (int) literalLengthLength) + 1;
                                        }

                                        var inputRemaining = inputEnd - input;
                                        if (inputRemaining < literalLength)
                                        {
                                            Append(buffer, input, inputRemaining);
                                            _remainingLiteral = (int) (literalLength - inputRemaining);
                                            return;
                                        }
                                        else
                                        {
                                            Append(buffer, input, literalLength);
                                            input += literalLength;
                                        }
                                    }
                                    else if ((c & 3) == Constants.Copy4ByteOffset)
                                    {
                                        uint copyOffset = Helpers.UnsafeReadUInt32(scratch);

                                        long length = (c >> 2) + 1;

                                        AppendFromSelf(buffer, copyOffset, length, pshufbFillPatterns);
                                    }
                                    else
                                    {
                                        var entry = charTable[c];
                                        int data = Helpers.UnsafeReadInt32(scratch);

                                        var trailer = Helpers.ExtractLowBytes((uint) data, c & 3);
                                        long length = entry & 0xff;

                                        // copy_offset/256 is encoded in bits 8..10.  By just fetching
                                        // those bits, we get copy_offset (since the bit-field starts at
                                        // bit 8).
                                        var copyOffset = (uint)(entry & 0x700) + trailer;

                                        AppendFromSelf(buffer, copyOffset, length, pshufbFillPatterns);
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
                                }

                                uint preload = Helpers.UnsafeReadUInt32(input);

                                while (true)
                                {
                                    var c = (byte) preload;
                                    input++;

                                    if ((c & 0x03) == Constants.Literal)
                                    {
                                        long literalLength = (c >> 2) + 1;

                                        if (TryFastAppend(buffer, input, inputEnd - input, literalLength))
                                        {
                                            Debug.Assert(literalLength < 61);
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
                                            long literalLengthLength = literalLength - 60;
                                            var literalLengthTemp = Helpers.UnsafeReadInt32(input);

                                            literalLength = Helpers.ExtractLowBytes((uint) literalLengthTemp,
                                                (int) literalLengthLength) + 1;

                                            input += literalLengthLength;
                                        }

                                        var inputRemaining = inputEnd - input;
                                        if (inputRemaining < literalLength)
                                        {
                                            Append(buffer, input, inputRemaining);
                                            _remainingLiteral = (int) (literalLength - inputRemaining);
                                            input = inputEnd;
                                            goto exit;
                                        }
                                        else
                                        {
                                            Append(buffer, input, literalLength);
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

                                            var length = (c >> 2) + 1;
                                            AppendFromSelf(buffer, copyOffset, length, pshufbFillPatterns);
                                        }
                                        else
                                        {
                                            var entry = charTable[c];

                                            // We don't use BitConverter to read because we might be reading past the end of the span
                                            // But we know that's safe because we'll be doing it in _scratch with extra data on the end.
                                            // This reduces this step by several operations
                                            preload = Helpers.UnsafeReadUInt32(input);

                                            var trailer = Helpers.ExtractLowBytes(preload, c & 3);
                                            long length = entry & 0xff;

                                            // copy_offset/256 is encoded in bits 8..10.  By just fetching
                                            // those bits, we get copy_offset (since the bit-field starts at
                                            // bit 8).
                                            var copyOffset = (uint)(entry & 0x700) + trailer;

                                            AppendFromSelf(buffer, copyOffset, length, pshufbFillPatterns);

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
                            }
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
            int needed = (int)(entry >> 11) + 1; // +1 byte for 'c'

            var toCopy = Math.Min(unchecked((int)(inputEnd - input)), needed - _scratchLength);
            Unsafe.CopyBlockUnaligned(scratch + _scratchLength, input, unchecked((uint)toCopy));

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
            int needed = (int)(entry >> 11) + 1; // +1 byte for 'c'

            var inputLength = unchecked((int)(inputEnd - input));
            if (inputLength < needed)
            {
                // Data is insufficient, copy to scratch
                Unsafe.CopyBlockUnaligned(scratch, input, unchecked((uint)inputLength));

                _scratchLength = inputLength;
                input = inputEnd;
                return false;
            }

            if (inputLength < Constants.MaximumTagLength)
            {
                // Have enough bytes, but copy to scratch so that we do not
                // read past end of input
                Unsafe.CopyBlockUnaligned(scratch, input, unchecked((uint)inputLength));

                input = scratch;
                inputEnd = input + inputLength;
            }

            return true;
        }

        #region Loopback Writer

        private IMemoryOwner<byte>? _lookbackBufferOwner;
        private Memory<byte> _lookbackBuffer;
        private uint _lookbackPosition = 0;
        private int _readPosition = 0;

        private int _expectedLength;
        public int ExpectedLength
        {
            get => _expectedLength;
            set
            {
                _expectedLength = value;

                if (_lookbackBuffer.Length < _expectedLength)
                {
                    _lookbackBufferOwner?.Dispose();
                    _lookbackBufferOwner = MemoryPool<byte>.Shared.Rent(_expectedLength);
                    _lookbackBuffer = _lookbackBufferOwner.Memory;
                }
            }
        }

        public int WrittenLength => (int)_lookbackPosition;

        public int UnreadBytes
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => (int)_lookbackPosition - _readPosition;
        }

        public bool EndOfFile
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ExpectedLength > 0 && _readPosition >= ExpectedLength;
        }

        public bool AllDataDecompressed
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => ExpectedLength > 0 && _lookbackPosition >= ExpectedLength;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void Append(ReadOnlySpan<byte> input)
        {
            fixed (byte* inputPtr = input)
            {
                fixed (byte* buffer = _lookbackBuffer.Span)
                {
                    Append(buffer, inputPtr, input.Length);
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void Append(byte* buffer, byte* input, long length)
        {
            if (length > _lookbackBuffer.Length - _lookbackPosition)
            {
                ThrowInvalidDataException("Data too long");
            }

            Unsafe.CopyBlockUnaligned(buffer + _lookbackPosition, input, unchecked((uint) length));

            _lookbackPosition += unchecked((uint) length);
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
        private unsafe bool TryFastAppend(byte* buffer, byte* input, long available, long length)
        {
            // Save to the local stack (which effectively saves to a register)
            var lookbackPosition = _lookbackPosition;

            if (length <= 16 && available >= 16 + Constants.MaximumTagLength &&
                _lookbackBuffer.Length - lookbackPosition >= 16)
            {
                CopyHelpers.UnalignedCopy128(input, buffer + lookbackPosition);
                _lookbackPosition = lookbackPosition + unchecked((uint) length);
                return true;
            }

            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private unsafe void AppendFromSelf(byte* buffer, uint copyOffset, long length, sbyte* pshufbFillPatterns)
        {
            // copyOffset - 1u will cause it to wrap around to a very large number if copyOffset == 0
            // This allows us to combine two comparisons into one
            if (unchecked(copyOffset - 1u) >= _lookbackPosition)
            {
                ThrowInvalidDataException("Invalid copy offset");
            }
            if (length > _lookbackBuffer.Length - _lookbackPosition)
            {
                ThrowInvalidDataException("Data too long");
            }

            var op = buffer + _lookbackPosition;
            CopyHelpers.IncrementalCopy(op - copyOffset, op, op + length, buffer + _lookbackBuffer.Length, pshufbFillPatterns);

            _lookbackPosition += unchecked((uint) length);
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

        #endregion

        public void Dispose()
        {
            _lookbackBufferOwner?.Dispose();
            _lookbackBufferOwner = null;
        }
    }
}
