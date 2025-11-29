#if NET8_0_OR_GREATER
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
#endif

/*
 * This file is ported from https://github.com/couchbase/couchbase-net-client/blob/c10fe9ef09beadb8512f696d764b7a770429e641/src/Couchbase/Core/Utils/Leb128.cs
 * and therefore retains a Couchbase copyright.
 **/

namespace Snappier.Internal;

internal static partial class VarIntEncoding
{
    /// <summary>
    /// Maximum length, in bytes, when encoding a 32-bit integer.
    /// </summary>
    public const int MaxLength = 5;

    /// <summary>
    /// Encodes a value onto a buffer using little-ending varint encoding.
    /// </summary>
    /// <param name="buffer">Buffer to receive the value.</param>
    /// <param name="value">Value to encode.</param>
    /// <param name="bytesWritten">Number of bytes written to the buffer.</param>
    /// <returns><see langword="true"/> if the value was written successfully. <see langword="false"/> if the buffer is too small.</returns>
    public static bool TryWrite(Span<byte> buffer, uint value, out int bytesWritten)
    {
        // Note: This method is likely to be inlined into the caller, potentially
        // eliding the size check if JIT knows the size of the buffer. BitConverter.IsLittleEndian
        // will always be elided based on CPU architecture.

#if NET8_0_OR_GREATER
        if (BitConverter.IsLittleEndian && buffer.Length >= sizeof(ulong))
        {
            // Only use the fast path on little-endian CPUs and when there's enough padding in the
            // buffer to write an ulong. At most there will be 5 real bytes written, but for speed
            // up to 8 bytes are being copied to the buffer from a register. This guard prevents a
            // potential buffer overrun.

            bytesWritten = WriteFast(ref MemoryMarshal.GetReference(buffer), value);
            return true;
        }
#endif

        return TryWriteSlow(buffer, value, out bytesWritten);
    }

#if NET8_0_OR_GREATER

    private static int WriteFast(ref byte buffer, uint value)
    {
        // The use of unsafe writes below is made safe because this method is never
        // called without at least 8 bytes available in the buffer.

        if (value < 128)
        {
            // We need to special case 0 to ensure we write one byte, so go ahead and
            // special case 0-127, which all write only one byte with the continuation bit unset.

            buffer = (byte)value;
            return 1;
        }

        // First get the value spread onto an ulong with 7 bit groups

        ulong result = Spread7BitGroupsIntoBytes(value);

        // Next, calculate the size of the output in bytes

        int unusedBytes = BitOperations.LeadingZeroCount(result) >>> 3; // right shift is the equivalent of divide by 8

        // Build a mask to set the continuation bits

        const ulong allContinuationBits = 0x8080808080808080UL;
        ulong mask = allContinuationBits >>> ((unusedBytes + 1) << 3); // left shift is the equivalent of multiply by 8

        // Finally, write the result to the buffer

        Unsafe.WriteUnaligned(ref buffer, result | mask);

        return sizeof(ulong) - unusedBytes;
    }

    // This spreads the 4 bytes of an uint into the lower 5 bytes of an 8 byte ulong
    // as 7 bit blocks, with the high bit of each byte set to 0. This is the basis
    // of LEB128 encoding, but without the continuation bit set.
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ulong Spread7BitGroupsIntoBytes(uint value)
    {
        // Only one of the three branches below will be included in the JIT output
        // based on CPU support at runtime

        if (Bmi2.X64.IsSupported)
        {
            return Bmi2.X64.ParallelBitDeposit(value, 0xf7f7f7f7fUL);
        }

        if (Bmi2.IsSupported)
        {
            // Intel x86 branch, using 32-bit BMI2 instruction

            return Bmi2.ParallelBitDeposit(value, 0x7f7f7f7fU) |
                   ((value & 0xf0000000UL) << 4);
        }

        // Fallback for unsupported CPUs (i.e. ARM)
        return value  & 0x0000007fUL
            | ((value & 0x00003f80UL) << 1)
            | ((value & 0x001fc000UL) << 2)
            | ((value & 0x0fe00000UL) << 3)
            | ((value & 0xf0000000UL) << 4);
    }

#endif
}

/* ************************************************************
*
*    @author Couchbase <info@couchbase.com>
*    @copyright 2021 Couchbase, Inc.
*
*    Licensed under the Apache License, Version 2.0 (the "License");
*    you may not use this file except in compliance with the License.
*    You may obtain a copy of the License at
*
*        http://www.apache.org/licenses/LICENSE-2.0
*
*    Unless required by applicable law or agreed to in writing, software
*    distributed under the License is distributed on an "AS IS" BASIS,
*    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
*    See the License for the specific language governing permissions and
*    limitations under the License.
*
* ************************************************************/
