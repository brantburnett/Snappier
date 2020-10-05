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

        private IMemoryOwner<ushort>? _buffer;

        public void EnsureCapacity(long inputSize)
        {
            var maxFragmentSize = (int)Math.Min(inputSize, Constants.BlockSize);
            var tableSize = CalculateTableSize(maxFragmentSize);

            if (_buffer == null || tableSize < _buffer.Memory.Length)
            {
                _buffer?.Dispose();
                _buffer = MemoryPool<ushort>.Shared.Rent(tableSize);
            }
        }

        public Span<ushort> GetHashTable(int fragmentSize)
        {
            if (_buffer == null)
            {
                throw new InvalidOperationException("Buffer not initialized");
            }

            int hashTableSize = CalculateTableSize(fragmentSize);
            if (hashTableSize > _buffer.Memory.Length)
            {
                throw new InvalidOperationException("Insufficient buffer size");
            }

            var hashTable = _buffer.Memory.Span.Slice(0, hashTableSize);
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

        private void Dispose(bool disposing)
        {
            _buffer?.Dispose();
            _buffer = null;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        ~HashTable()
        {
            Dispose(false);
        }
    }
}
