using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

#if NET8_0_OR_GREATER
using System.Runtime.Intrinsics.Arm;
using System.Runtime.Intrinsics.X86;
#endif

namespace Snappier.Internal;

internal class HashTable : IDisposable
{
    private const int MinHashTableBits = 8;
    private const int MinHashTableSize = 1 << MinHashTableBits;

    private const int MaxHashTableBits = 14;
    private const int MaxHashTableSize = 1 << MaxHashTableBits;

    private ushort[]? _buffer;

    public void EnsureCapacity(long inputSize)
    {
        int maxFragmentSize = (int) Math.Min(inputSize, Constants.BlockSize);
        int tableSize = CalculateTableSize(maxFragmentSize);

        if (_buffer is null || tableSize > _buffer.Length)
        {
            if (_buffer is not null)
            {
                ArrayPool<ushort>.Shared.Return(_buffer);
            }

            _buffer = ArrayPool<ushort>.Shared.Rent(tableSize);
        }
    }

    public Span<ushort> GetHashTable(int fragmentSize)
    {
        if (_buffer is null)
        {
            ThrowHelper.ThrowInvalidOperationException("Buffer not initialized");
        }

        int hashTableSize = CalculateTableSize(fragmentSize);
        if (hashTableSize > _buffer.Length)
        {
            ThrowHelper.ThrowInvalidOperationException("Insufficient buffer size");
        }

        Span<ushort> hashTable = _buffer.AsSpan(0, hashTableSize);
        hashTable.Clear();

        return hashTable;
    }

    private static int CalculateTableSize(int inputSize)
    {
        if (inputSize > MaxHashTableSize)
        {
            return MaxHashTableSize;
        }

        if (inputSize < MinHashTableSize)
        {
            return MinHashTableSize;
        }

        DebugExtensions.Assert(inputSize > 1);
        return 2 << Helpers.Log2Floor((uint)(inputSize - 1));
    }

    public void Dispose()
    {
        if (_buffer is not null)
        {
            ArrayPool<ushort>.Shared.Return(_buffer);
            _buffer = null;
        }
    }

    /// <summary>
    /// Given a table of uint16_t whose size is mask / 2 + 1, return a pointer to the
    /// relevant entry, if any, for the given bytes.  Any hash function will do,
    /// but a good hash function reduces the number of collisions and thus yields
    /// better compression for compressible input.
    ///
    /// REQUIRES: mask is 2 * (table_size - 1), and table_size is a power of two.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ref ushort TableEntry(ref ushort table, uint bytes, uint mask)
    {
        // Our choice is quicker-and-dirtier than the typical hash function;
        // empirically, that seems beneficial.  The upper bits of kMagic * bytes are a
        // higher-quality hash than the lower bits, so when using kMagic * bytes we
        // also shift right to get a higher-quality end result.  There's no similar
        // issue with a CRC because all of the output bits of a CRC are equally good
        // "hashes." So, a CPU instruction for CRC, if available, tends to be a good
        // choice.

        uint hash;

#if NET8_0_OR_GREATER
        // We use mask as the second arg to the CRC function, as it's about to
        // be used anyway; it'd be equally correct to use 0 or some constant.
        // Mathematically, _mm_crc32_u32 (or similar) is a function of the
        // xor of its arguments.

        if (System.Runtime.Intrinsics.X86.Sse42.IsSupported)
        {
            hash = Sse42.Crc32(bytes, mask);

        }
        else if (System.Runtime.Intrinsics.Arm.Crc32.IsSupported)
        {
            hash = Crc32.ComputeCrc32C(bytes, mask);
        }
        else
#endif
        {
            const uint kMagic = 0x1e35a7bd;
            hash = (kMagic * bytes) >> (31 - MaxHashTableBits);
        }

        return ref Unsafe.AddByteOffset(ref table, hash & mask);
    }
}
