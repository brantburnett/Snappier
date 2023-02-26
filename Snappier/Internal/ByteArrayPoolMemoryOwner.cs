using System;
using System.Buffers;

namespace Snappier.Internal
{
    /// <summary>
    /// Wraps an inner byte array from <see cref="ArrayPool{T}.Shared"/>"/> with a limited length.
    /// </summary>
    /// <remarks>
    /// We use this instead of the built-in <see cref="MemoryPool{T}"/> because we want to slice the array without
    /// allocating another wrapping class on the heap.
    /// </remarks>
    internal sealed class ByteArrayPoolMemoryOwner : IMemoryOwner<byte>
    {
        private byte[]? _innerArray;

        /// <inheritdoc />
        public Memory<byte> Memory { get; private set; }

        /// <summary>
        /// Create an empty ByteArrayPoolMemoryOwner.
        /// </summary>
        public ByteArrayPoolMemoryOwner()
        {
            // _innerArray will be null and Memory will be a default empty Memory<byte>
        }

        /// <summary>
        /// Given a byte array from <see cref="ArrayPool{T}.Shared"/>, create a ByteArrayPoolMemoryOwner
        /// which wraps it until disposed and slices it to <paramref name="length"/>.
        /// </summary>
        /// <param name="innerArray">An array from the <see cref="ArrayPool{T}.Shared"/>.</param>
        /// <param name="length">The length of the array to return from <see cref="Memory"/>.</param>
        public ByteArrayPoolMemoryOwner(byte[] innerArray, int length)
        {
            ThrowHelper.ThrowIfNull(innerArray);

            _innerArray = innerArray;
            Memory = innerArray.AsMemory(0, length); // Also validates length
        }

        /// <inheritdoc />
        public void Dispose()
        {
            byte[]? innerArray = _innerArray;
            if (innerArray is not null)
            {
                _innerArray = null;
                Memory = default;
                ArrayPool<byte>.Shared.Return(innerArray);
            }
        }
    }
}
