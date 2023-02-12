using System;
using BenchmarkDotNet.Attributes;

namespace Snappier.Benchmarks
{
    public class HashTable
    {
        private Snappier.Internal.HashTable _hashTable = new();

        [GlobalSetup]
        public void GlobalSetup()
        {
            _hashTable = new Snappier.Internal.HashTable();
            _hashTable.EnsureCapacity(65536);
        }

        [Benchmark]
        public Span<ushort> GetHashTable()
        {
            return _hashTable.GetHashTable(65536);
        }
    }
}
