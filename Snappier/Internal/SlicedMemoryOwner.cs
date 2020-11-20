using System;
using System.Buffers;

namespace Snappier.Internal
{
    /// <summary>
    /// Wraps an inner <see cref="IMemoryOwner{T}"/> to have a shorter length.
    /// </summary>
    internal sealed class SlicedMemoryOwner : IMemoryOwner<byte>
    {
        private IMemoryOwner<byte>? _innerMemoryOwner;
        private readonly int _length;

        /// <inheritdoc />
        public Memory<byte> Memory
        {
            get
            {
                if (_innerMemoryOwner == null)
                {
                    throw new ObjectDisposedException(nameof(SlicedMemoryOwner));
                }

                return _innerMemoryOwner.Memory.Slice(0, _length);
            }
        }

        public SlicedMemoryOwner(IMemoryOwner<byte> innerMemoryOwner, int length)
        {
            _innerMemoryOwner = innerMemoryOwner ?? throw new ArgumentNullException(nameof(innerMemoryOwner));

            if (_length > _innerMemoryOwner.Memory.Length)
            {
                throw new ArgumentException($"{nameof(length)} is greater than the inner length.", nameof(length));
            }

            _length = length;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // Cache to local to ensure order of operations for thread safety
            var innerMemoryOwner = _innerMemoryOwner;
            if (innerMemoryOwner != null)
            {
                _innerMemoryOwner = null;
                innerMemoryOwner.Dispose();
            }
        }
    }
}
