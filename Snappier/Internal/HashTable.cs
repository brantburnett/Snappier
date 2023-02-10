using System;
using System.Buffers;

namespace Snappier.Internal
{
    internal class HashTable : IDisposable
    {
        private const int MinHashTableBits = 8;
        private const int MinHashTableSize = 1 << MinHashTableBits;

        private const int MaxHashTableBits = 14;
        private const int MaxHashTableSize = 1 << MaxHashTableBits;

        private ushort[]? _buffer;

        public void EnsureCapacity(int inputSize)
        {
            var maxFragmentSize = Math.Min(inputSize, (int) Constants.BlockSize);
            var tableSize = CalculateTableSize(maxFragmentSize);

            if (_buffer is null || tableSize < _buffer.Length)
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

            var hashTable = _buffer.AsSpan(0, hashTableSize);
            hashTable.Fill(0);

            return hashTable;
        }

        private int CalculateTableSize(int inputSize)
        {
            if (inputSize > MaxHashTableSize)
            {
                return MaxHashTableSize;
            }

            if (inputSize < MinHashTableSize)
            {
                return MinHashTableSize;
            }

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
    }
}
