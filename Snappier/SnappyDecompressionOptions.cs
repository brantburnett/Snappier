using Snappier.Internal;

namespace Snappier;

/// <summary>
/// Options for decompressing a Snappy block.
/// </summary>
/// <remarks>
/// The default value applies no options, matching the overloads that do not accept options.
/// </remarks>
public readonly struct SnappyDecompressionOptions
{
    // Zero means no maximum, so that the default value applies no maximum
    private readonly int _maxUncompressedLength;

    /// <summary>
    /// The largest uncompressed length to accept, in bytes, or <c>null</c> for no maximum.
    /// </summary>
    /// <remarks>
    /// A block declaring a larger uncompressed length is rejected with <see cref="InvalidDataException"/> before any
    /// buffer is allocated for it. Set this when decompressing data from an untrusted source. Regardless of this
    /// setting, a block declaring more data than it could decompress to is always rejected.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public int? MaxUncompressedLength
    {
        get => _maxUncompressedLength > 0 ? _maxUncompressedLength : null;
        init
        {
            if (value <= 0)
            {
                ThrowHelper.ThrowArgumentOutOfRangeException(nameof(MaxUncompressedLength), "Must be greater than zero.");
            }

            _maxUncompressedLength = value.GetValueOrDefault();
        }
    }
}
