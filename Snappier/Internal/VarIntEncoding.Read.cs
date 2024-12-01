using System;
using System.Buffers;

#if NET7_0_OR_GREATER
using System.Buffers.Binary;
using System.Diagnostics;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
#endif

namespace Snappier.Internal
{
    internal static partial class VarIntEncoding
    {
        public static uint Read(ReadOnlySpan<byte> input, out int bytesRead)
        {
            if (TryRead(input, out var result, out bytesRead) != OperationStatus.Done)
            {
                ThrowHelper.ThrowInvalidDataException("Invalid stream length");
            }

            return result;
        }

        public static OperationStatus TryRead(ReadOnlySpan<byte> input, out uint result, out int bytesRead)
        {
#if NET7_0_OR_GREATER
            if (Sse2.IsSupported && Bmi2.IsSupported && BitConverter.IsLittleEndian && input.Length >= Vector128<byte>.Count)
            {
                return ReadFast(input, out result, out bytesRead);
            }
#endif

            return TryReadSlow(input, out result, out bytesRead);
        }

        private static OperationStatus TryReadSlow(ReadOnlySpan<byte> input, out uint result, out int bytesRead)
        {
            result = 0;
            int shift = 0;
            bool foundEnd = false;

            bytesRead = 0;
            while (input.Length > bytesRead)
            {
                byte c = input[bytesRead++];

                int val = c & 0x7f;
                if (Helpers.LeftShiftOverflows((byte) val, shift))
                {
                    return OperationStatus.InvalidData;
                }

                result |= (uint)(val << shift);
                shift += 7;

                if (c < 128)
                {
                    foundEnd = true;
                    break;
                }

                if (shift >= 32)
                {
                    return OperationStatus.InvalidData;
                }
            }

            if (!foundEnd)
            {
                bytesRead = 0;
                return OperationStatus.NeedMoreData;
            }

            return OperationStatus.Done;
        }

#if NET7_0_OR_GREATER

        private static ReadOnlySpan<uint> ReadMasks =>
        [
            0x00000000, // Not used, present for padding
            0x0000007f,
            0x00003fff,
            0x001fffff,
            0x0fffffff,
            0xffffffff
        ];

        private static OperationStatus ReadFast(ReadOnlySpan<byte> input, out uint result, out int bytesRead)
        {
            Debug.Assert(Sse2.IsSupported);
            Debug.Assert(Bmi2.IsSupported);
            Debug.Assert(input.Length >= Vector128<byte>.Count);
            Debug.Assert(BitConverter.IsLittleEndian);

            var mask = ~Sse2.MoveMask(Vector128.LoadUnsafe(ref MemoryMarshal.GetReference(input)));
            bytesRead = BitOperations.TrailingZeroCount(mask) + 1;

            uint shuffledBits = Bmi2.X64.IsSupported
                ? unchecked((uint)Bmi2.X64.ParallelBitExtract(BinaryPrimitives.ReadUInt64LittleEndian(input), 0x7F7F7F7F7Fu))
                : Bmi2.ParallelBitExtract(BinaryPrimitives.ReadUInt32LittleEndian(input), 0x7F7F7F7Fu) |
                    ((BinaryPrimitives.ReadUInt32LittleEndian(input.Slice(4)) & 0xf) << 28);

            if (bytesRead < ReadMasks.Length)
            {
                result = shuffledBits & ReadMasks[bytesRead];
            }
            else
            {
                // Currently, JIT doesn't optimize the bounds check away in the branch above,
                // but we'll leave it written this way in case JIT improves in the future to avoid
                // checking the bounds twice.

                result = 0;
                return OperationStatus.InvalidData;
            }

            return OperationStatus.Done;
        }

#endif
    }
}
