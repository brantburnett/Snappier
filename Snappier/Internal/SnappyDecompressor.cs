using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Snappier.Internal;

internal sealed class SnappyDecompressor : IDisposable
{
#if NETSTANDARD2_0
    private readonly byte[] _scratch = new byte[Constants.MaximumTagLength];

    private Span<byte> Scratch => _scratch.AsSpan();
#else
    [InlineArray(Constants.MaximumTagLength)]
    private struct ScratchBuffer
    {
        private byte _element0;
    }

    private ScratchBuffer _scratch;

    private Span<byte> Scratch => _scratch;
#endif

    private int _scratchLength = 0;

    private int _remainingLiteral;

    public bool NeedMoreData => !AllDataDecompressed && UnreadBytes == 0;

    /// <summary>
    /// Decompress a portion of the input.
    /// </summary>
    /// <param name="input">Input to process.</param>
    /// <remarks>
    /// The first call to this method after construction or after a call to <see cref="Reset"/> start at the
    /// beginning of a new Snappy block, leading with the encoded block size. It may be called multiple times
    /// as more data becomes available. <see cref="AllDataDecompressed"/> will be true once the entire block
    /// has been processed.
    /// </remarks>
    public void Decompress(ReadOnlySpan<byte> input)
    {
        if (AllDataDecompressed)
        {
            ThrowHelper.ThrowInvalidOperationException("All data has been decompressed");
        }

        if (!ExpectedLength.HasValue)
        {
            OperationStatus status = TryReadUncompressedLength(input, out int bytesConsumed);
            if (status == OperationStatus.InvalidData)
            {
                ThrowHelper.ThrowInvalidOperationException("Invalid stream length");
            }
            else if (status != OperationStatus.Done)
            {
                return;
            }

            input = input.Slice(bytesConsumed);
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

        if (BufferWriter is not null && AllDataDecompressed)
        {
            // Advance the buffer writer to the end of the data
            BufferWriter.Advance(_lookbackPosition);

            // Release the lookback buffer
            _lookbackBuffer = default;
        }
    }

    public void Reset()
    {
        _scratchLength = 0;
        _remainingLiteral = 0;

        _lookbackPosition = 0;
        _readPosition = 0;
        ExpectedLength = null;

        if (BufferWriter is not null)
        {
            // Don't reuse the lookback buffer when it came from a BufferWriter
            _lookbackBuffer = default;
        }
    }

    private OperationStatus TryReadUncompressedLength(ReadOnlySpan<byte> input, out int bytesConsumed)
    {
        OperationStatus status;

        if (_scratchLength > 0)
        {
            // We have a partial length in the scratch buffer, so we need to finish reading that first
            // The maximum tag length of 5 bytes is also the maximum varint length, so we can reuse _scratch

            // Copy the remaining bytes from the input to the scratch buffer
            Span<byte> scratch = Scratch;
            int toCopy = Math.Min(input.Length, scratch.Length - _scratchLength);
            input.Slice(0, toCopy).CopyTo(scratch.Slice(_scratchLength));

            status = VarIntEncoding.TryRead(scratch.Slice(0, _scratchLength + toCopy), out uint length, out int scratchBytesConsumed);

            switch (status)
            {
                case OperationStatus.Done:
                    ExpectedLength = (int)length;

                    // The number of bytes consumed from the input is the number of bytes used by VarIntEncoding.TryRead
                    // less the number of bytes previously found in the scratch buffer
                    bytesConsumed = scratchBytesConsumed - _scratchLength;

                    // Reset scratch buffer
                    _scratchLength = 0;
                    break;

                case OperationStatus.NeedMoreData:
                    // We consumed all the input, but still need more data to finish reading the length
                    bytesConsumed = toCopy;
                    _scratchLength += toCopy;

                    Debug.Assert(_scratchLength < scratch.Length);
                    break;

                default:
                    bytesConsumed = 0;
                    break;
            }
        }
        else
        {
            // No data in the scratch buffer, try to read directly from the input
            status = VarIntEncoding.TryRead(input, out uint length, out bytesConsumed);

            switch (status)
            {
                case OperationStatus.Done:
                    ExpectedLength = (int)length;
                    break;

                case OperationStatus.NeedMoreData:
                    // Copy all of the input to the scratch buffer
                    input.CopyTo(Scratch);
                    _scratchLength = input.Length;
                    bytesConsumed = input.Length;
                    break;
            }
        }

        return status;
    }

    /// <summary>
    /// Read the uncompressed length stored at the start of the compressed data.
    /// </summary>
    /// <param name="input">Input data, which should begin with the varint encoded uncompressed length.</param>
    /// <returns>The length of the uncompressed data.</returns>
    /// <exception cref="InvalidDataException">Invalid stream length</exception>
    public static int ReadUncompressedLength(ReadOnlySpan<byte> input) =>
        (int) VarIntEncoding.Read(input, out _);

    internal void DecompressAllTags(ReadOnlySpan<byte> inputSpan)
    {
        // We only index into this array with a byte, and the list is 256 long, so it's safe to skip range checks.
        // JIT doesn't seem to recognize this currently, so we'll use a ref and Unsafe.Add to avoid the checks.
        Debug.Assert(Constants.CharTable.Length >= 256);
        ref readonly ushort charTable = ref MemoryMarshal.GetReference(Constants.CharTable);

        unchecked
        {
            ref readonly byte input = ref Unsafe.AsRef(in inputSpan[0]);
            ref readonly byte inputEnd = ref Unsafe.Add(in input, inputSpan.Length);

            // Track the point in the input before which input is guaranteed to have at least Constants.MaxTagLength bytes left
            ref readonly byte inputLimitMinMaxTagLength = ref Unsafe.Subtract(in inputEnd, Math.Min(inputSpan.Length, Constants.MaximumTagLength - 1));

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
                    DecompressTagFromScratch(in input, in inputEnd, ref op, ref buffer, ref bufferEnd);
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

                input = ref Unsafe.Add(in input, inputUsed);
            }

            while (true)
            {
                if (!Unsafe.IsAddressLessThan(in input, in inputLimitMinMaxTagLength))
                {
                    uint newScratchLength = RefillTag(in input, in inputEnd);
                    if (newScratchLength == uint.MaxValue)
                    {
                        break;
                    }

                    if (newScratchLength > 0)
                    {
                        // Data has been moved to the scratch buffer
                        input = ref _scratch[0];
                        inputEnd = ref Unsafe.Add(in input, newScratchLength);
                        inputLimitMinMaxTagLength = ref Unsafe.Subtract(in inputEnd,
                            Math.Min(newScratchLength, Constants.MaximumTagLength - 1));
                    }
                }

                uint preload = Helpers.UnsafeReadUInt32(in input);

                // Some branches refill preload in a more optimal manner, they jump here to avoid the code above
                skip_preload:

                byte c = (byte) preload;
                input = ref Unsafe.Add(in input, 1);

                if ((c & 0x03) == Constants.Literal)
                {
                    nint literalLength = unchecked((c >> 2) + 1);

                    if (TryFastAppend(ref op, ref bufferEnd, in input, Unsafe.ByteOffset(in input, in inputEnd), literalLength))
                    {
                        Debug.Assert(literalLength < 61);
                        op = ref Unsafe.Add(ref op, literalLength);
                        input = ref Unsafe.Add(in input, literalLength);
                        // NOTE: There is no RefillTag here, as TryFastAppend()
                        // will not return true unless there's already at least five spare
                        // bytes in addition to the literal.
                        preload = Helpers.UnsafeReadUInt32(in input);
                        goto skip_preload;
                    }

                    if (literalLength >= 61)
                    {
                        // Long literal.
                        nint literalLengthLength = literalLength - 60;
                        uint literalLengthTemp = Helpers.UnsafeReadUInt32(in input);

                        literalLength = (nint) Helpers.ExtractLowBytes(literalLengthTemp,
                            (int) literalLengthLength) + 1;

                        input = ref Unsafe.Add(in input, literalLengthLength);
                    }

                    nint inputRemaining = Unsafe.ByteOffset(in input, in inputEnd);
                    if (inputRemaining < literalLength)
                    {
                        Append(ref op, ref bufferEnd, in input, inputRemaining);
                        op = ref Unsafe.Add(ref op, inputRemaining);
                        _remainingLiteral = (int) (literalLength - inputRemaining);
                        break;
                    }

                    Append(ref op, ref bufferEnd, in input, literalLength);
                    op = ref Unsafe.Add(ref op, literalLength);
                    input = ref Unsafe.Add(in input, literalLength);
                }
                else
                {
                    if ((c & 3) == Constants.Copy4ByteOffset)
                    {
                        uint copyOffset = Helpers.UnsafeReadUInt32(in input);
                        input = ref Unsafe.Add(in input, 4);

                        nint length = (c >> 2) + 1;
                        AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);
                        op = ref Unsafe.Add(ref op, length);
                    }
                    else
                    {
                        ushort entry = Unsafe.Add(in charTable, c);
                        preload = Helpers.UnsafeReadUInt32(in input);

                        uint trailer = Helpers.ExtractLowBytes(preload, c & 3);
                        nint length = entry & 0xff;

                        // copy_offset/256 is encoded in bits 8..10.  By just fetching
                        // those bits, we get copy_offset (since the bit-field starts at
                        // bit 8).
                        uint copyOffset = (entry & 0x700u) + trailer;

                        AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);
                        op = ref Unsafe.Add(ref op, length);

                        input = ref Unsafe.Add(in input, c & 3);

                        // By using the result of the previous load we reduce the critical
                        // dependency chain of ip to 4 cycles.
                        preload >>= (c & 3) * 8;
                        if (Unsafe.IsAddressLessThan(in input, in inputLimitMinMaxTagLength))
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
    private (uint inputUsed, uint bytesWritten) DecompressTagFromScratch(ref readonly byte input, ref readonly byte inputEnd,
        ref byte op, ref byte buffer, ref byte bufferEnd)
    {
        // scratch will be the scratch buffer with only the tag if true is returned
        uint inputUsed = RefillTagFromScratch(in input, in inputEnd);
        if (inputUsed == 0)
        {
            return (0, 0);
        }
        input = ref Unsafe.Add(in input, inputUsed);

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
                uint literalLengthTemp = Helpers.UnsafeReadUInt32(in _scratch[1]);

                literalLength = Helpers.ExtractLowBytes(literalLengthTemp,
                    (int) literalLengthLength) + 1;
            }

            nint inputRemaining = Unsafe.ByteOffset(in input, in inputEnd);
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
            uint copyOffset = Helpers.UnsafeReadUInt32(in _scratch[1]);

            nint length = (c >> 2) + 1;

            AppendFromSelf(ref op, ref buffer, ref bufferEnd, copyOffset, length);

            return (inputUsed, (uint) length);
        }
        else
        {
            ushort entry = Constants.CharTable[c];
            uint data = Helpers.UnsafeReadUInt32(in _scratch[1]);

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
    private uint RefillTagFromScratch(ref readonly byte input, ref readonly byte inputEnd)
    {
        Debug.Assert(_scratchLength > 0);

        if (!Unsafe.IsAddressLessThan(in input, in inputEnd))
        {
            return 0;
        }

        // Read the tag character
        uint entry = Constants.CharTable[_scratch[0]];
        uint needed = (entry >> 11) + 1; // +1 byte for 'c'

        uint toCopy = Math.Min((uint)Unsafe.ByteOffset(in input, in inputEnd), needed - (uint) _scratchLength);
        Unsafe.CopyBlockUnaligned(ref _scratch[(int)_scratchLength], in input, toCopy);

        _scratchLength += (int) toCopy;

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
    private uint RefillTag(ref readonly byte input, ref readonly byte inputEnd)
    {
        if (!Unsafe.IsAddressLessThan(in input, in inputEnd))
        {
            return uint.MaxValue;
        }

        // Read the tag character
        uint entry = Constants.CharTable[input];
        uint needed = (entry >> 11) + 1; // +1 byte for 'c'

        uint inputLength = (uint)Unsafe.ByteOffset(in input, in inputEnd);
        if (inputLength < needed)
        {
            // Data is insufficient, copy to scratch
            Unsafe.CopyBlockUnaligned(ref _scratch[0], in input, inputLength);

            _scratchLength = (int) inputLength;
            return uint.MaxValue;
        }

        if (inputLength < Constants.MaximumTagLength)
        {
            // Have enough bytes, but copy to scratch so that we do not
            // read past end of input
            Unsafe.CopyBlockUnaligned(ref _scratch[0], in input, inputLength);

            return inputLength;
        }

        return 0;
    }

    #region Loopback Writer

    /// <summary>
    /// Buffer writer for the output data. Incompatible with <see cref="ExtractData"/> and <see cref="Read"/>.
    /// </summary>
    public IBufferWriter<byte>? BufferWriter { get; init; }

    private byte[]? _lookbackBufferArray;
    private Memory<byte> _lookbackBuffer;
    private int _lookbackPosition = 0;
    private int _readPosition = 0;

    private int? ExpectedLength
    {
        get;
        set
        {
            field = value;

            if (value.HasValue && _lookbackBuffer.Length < value.GetValueOrDefault())
            {
                if (_lookbackBufferArray is not null)
                {
                    // Clear the used portion of the lookback buffer before returning
                    Helpers.ClearAndReturn(_lookbackBufferArray, _lookbackPosition);
                }

                if (BufferWriter is not null)
                {
                    _lookbackBuffer = BufferWriter.GetMemory(value.GetValueOrDefault());
                }
                else
                {
                    _lookbackBufferArray = ArrayPool<byte>.Shared.Rent(value.GetValueOrDefault());
                    _lookbackBuffer = _lookbackBufferArray.AsMemory(0, _lookbackBufferArray.Length);
                }
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
    private static void Append(ref byte op, ref byte bufferEnd, ref readonly byte input, nint length)
    {
        if (length > Unsafe.ByteOffset(ref op, ref bufferEnd))
        {
            ThrowHelper.ThrowInvalidDataException("Data too long");
        }

        Unsafe.CopyBlockUnaligned(ref op, in input, (uint) length);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool TryFastAppend(ref byte op, ref byte bufferEnd, ref readonly byte input, nint available, nint length)
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

        ref readonly byte source = ref Unsafe.Subtract(ref op, copyOffset);
        CopyHelpers.IncrementalCopy(in source, ref op,
            ref Unsafe.Add(ref op, length), ref bufferEnd);
    }

    public int Read(Span<byte> destination)
    {
        if (BufferWriter is not null)
        {
            ThrowCannotUseWithBufferWriter(nameof(Read));
        }

        int bytesToRead = Math.Min(destination.Length, UnreadBytes);
        if (bytesToRead <= 0)
        {
            return 0;
        }

        _lookbackBuffer.Span.Slice(_readPosition, bytesToRead).CopyTo(destination);
        _readPosition += bytesToRead;
        return bytesToRead;
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
        if (BufferWriter is not null)
        {
            ThrowCannotUseWithBufferWriter(nameof(ExtractData));
        }

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

    [DoesNotReturn]
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void ThrowCannotUseWithBufferWriter(string method)
    {
        // This is intentionally not inlined to keep the size of Read and ExtractData smaller,
        // making it more likely they may be inlined.
        ThrowHelper.ThrowNotSupportedException($"Cannot use {method} when using a BufferWriter.");
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
    internal void LoadScratchForTest(byte[] newScratch, int newScratchLength)
    {
        ArgumentNullException.ThrowIfNull(newScratch);
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
            // Clear the used portion of the lookback buffer before returning
            Helpers.ClearAndReturn(_lookbackBufferArray, _lookbackPosition);

            _lookbackBufferArray = null;
            _lookbackBuffer = default;
        }
    }
}
