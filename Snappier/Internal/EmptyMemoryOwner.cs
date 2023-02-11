using System;
using System.Buffers;

namespace Snappier.Internal
{
    /// <summary>
    /// A fake owner wrapping an empty <see cref="Memory{T}"/>.
    /// </summary>
    internal sealed class EmptyMemoryOwner : IMemoryOwner<byte>
    {
        private bool _disposed;

        /// <inheritdoc />
        public void Dispose() => _disposed = true;

        /// <inheritdoc />
        public Memory<byte> Memory
        {
            get
            {
                if (_disposed)
                {
                    ThrowHelper.ThrowObjectDisposedException(nameof(EmptyMemoryOwner));
                }

                return Memory<byte>.Empty;
            }
        }
    }
}
