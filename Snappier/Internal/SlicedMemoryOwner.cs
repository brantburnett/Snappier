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
                    ThrowHelper.ThrowObjectDisposedException(nameof(SlicedMemoryOwner));
                }

                return _innerMemoryOwner.Memory.Slice(0, _length);
            }
        }

        public SlicedMemoryOwner(IMemoryOwner<byte> innerMemoryOwner, int length)
        {
            ThrowHelper.ThrowIfNull(innerMemoryOwner);
            if (_length > innerMemoryOwner.Memory.Length)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(length), $"{nameof(length)} is greater than the inner length.");
            }

            _innerMemoryOwner = innerMemoryOwner;
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
