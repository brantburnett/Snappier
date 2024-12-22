# Block Compression

Block compression is ideal for data up to 64KB, though it may be used for data of any size. It does not include any stream
framing or CRC validation. It also doesn't automatically revert to uncompressed data in the event of data size growth.

## Block compression/decompression using a buffer you already own

```cs
using Snappier;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // This option assumes that you are managing buffers yourself in an efficient way.
        // In this example, we're using heap allocated byte arrays, however in most cases
        // you would get these buffers from a buffer pool like ArrayPool<byte> or MemoryPool<byte>.

        // If the output buffer is too small, an ArgumentException is thrown. This will not
        // occur in this example because a sufficient buffer is always allocated via 
        // Snappy.GetMaxCompressedLength or Snappy.GetUncompressedLength. There are TryCompress
        // and TryDecompress overloads that return false if the output buffer is too small
        // rather than throwing an exception.

        // Compression
        byte[] buffer = new byte[Snappy.GetMaxCompressedLength(Data)];
        int compressedLength = Snappy.Compress(Data, buffer);
        Span<byte> compressed = buffer.AsSpan(0, compressedLength);

        // Decompression
        byte[] outputBuffer = new byte[Snappy.GetUncompressedLength(compressed)];
        int decompressedLength = Snappy.Decompress(compressed, outputBuffer);

        for (var i = 0; i < decompressedLength; i++)
        {
            // Do something with the data
        }
    }
}
```

## Block compression/decompression using a memory pool buffer

```cs
using Snappier;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // This option uses `MemoryPool<byte>.Shared`. However, if you fail to
        // dispose of the returned buffers correctly it can result in inefficient garbage collection.
        // It is important to either call .Dispose() or use a using statement.

        // Compression
        using (IMemoryOwner<byte> compressed = Snappy.CompressToMemory(Data))
        {
            // Decompression
            using (IMemoryOwner<byte> decompressed = Snappy.DecompressToMemory(compressed.Memory.Span))
            {
                // Do something with the data
            }
        }
    }
}
```

## Block compression/decompression using a buffer writter

```cs
using Snappier;
using System.Buffers;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // This option uses `IBufferWriter<byte>`. In .NET 6 you can get a simple
        // implementation such as `ArrayBufferWriter<byte>` but it may also be a `PipeWriter<byte>`
        // or any other more advanced implementation of `IBufferWriter<byte>`.
        
        // These overloads also accept a `ReadOnlySequence<byte>` which allows the source data
        // to be made up of buffer segments rather than one large buffer. However, segment size
        // may be a factor in performance. For compression, segments that are some multiple of
        // 64KB are recommended. For decompression, simply avoid small segments.

        // Compression
        var compressedBufferWriter = new ArrayBufferWriter<byte>();
        Snappy.Compress(new ReadOnlySequence<byte>(Data), compressedBufferWriter);
        var compressedData = compressedBufferWriter.WrittenMemory;

        // Decompression
        var decompressedBufferWriter = new ArrayBufferWriter<byte>();
        Snappy.Decompress(new ReadOnlySequence<byte>(compressedData), decompressedBufferWriter);
        var decompressedData = decompressedBufferWriter.WrittenMemory;

        // Do something with the data
    }
}
```

## Block compression/decompression using heap allocated byte[]

```cs
using Snappier;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // This is generally the least efficient option,
        // but in some cases may be the simplest to implement.

        // Compression
        byte[] compressed = Snappy.CompressToArray(Data);

        // Decompression
        byte[] decompressed = Snappy.DecompressToArray(compressed);
    }
}
```