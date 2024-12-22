# Snappier

## Introduction

Snappier is a pure C# port of Google's [Snappy](https://github.com/google/snappy) compression algorithm. It is designed with speed as the primary goal, rather than compression ratio, and is ideal for compressing network traffic. Please see [the Snappy README file](https://github.com/google/snappy/blob/master/README.md) for more details on Snappy.

## Project Goals

The Snappier project aims to meet the following needs of the .NET community.

- Cross-platform C# implementation for Linux and Windows, without P/Invoke or special OS installation requirements
- Compatible with .NET 4.6.1 and later and .NET 6 and later
- Use .NET paradigms, including asynchronous stream support
- Full compatibility with both block and stream formats
- Near C++ level performance
  - Note: This is only possible on .NET 6 and later with the aid of [Span&lt;T&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.span-1?view=netcore-3.1) and [System.Runtime.Intrinsics](https://fiigii.com/2019/03/03/Hardware-intrinsic-in-NET-Core-3-0-Introduction/).
  - .NET 4.6.1 is the slowest
- Keep allocations and garbage collection to a minimum using buffer pools

## Installing

Simply add a NuGet package reference to the latest version of Snappier.

```xml
<PackageReference Include="Snappier" Version="1.1.6" />
```

or

```sh
dotnet add package Snappier
```

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

## Stream compression/decompression

Compressing or decompressing a stream follows the same paradigm as other compression streams in .NET. `SnappyStream` wraps an inner stream. If decompressing you read from the `SnappyStream`, if compressing you write to the `SnappyStream`

This approach reads or writes the [Snappy framing format](https://github.com/google/snappy/blob/master/framing_format.txt) designed for streaming. The input/output is not the same as the block method above. It includes additional headers and CRC32C checks.

```cs
using System.IO;
using System.IO.Compression;
using Snappier;

public class Program
{
    public static async Task Main()
    {
        using var fileStream = File.OpenRead("somefile.txt");

        // First, compression
        using var compressed = new MemoryStream();

        using (var compressor = new SnappyStream(compressed, CompressionMode.Compress, leaveOpen: true))
        {
            await fileStream.CopyToAsync(compressor);

            // Disposing the compressor also flushes the buffers to the inner stream
            // We pass true to the constructor above so that it doesn't close/dispose the inner stream
            // Alternatively, we could call compressor.Flush()
        }

        // Then, decompression

        compressed.Position = 0; // Reset to beginning of the stream so we can read
        using var decompressor = new SnappyStream(compressed, CompressionMode.Decompress);

        var buffer = new byte[65536];
        var bytesRead = decompressor.Read(buffer, 0, buffer.Length);
        while (bytesRead > 0)
        {
            // Do something with the data

            bytesRead = decompressor.Read(buffer, 0, buffer.Length)
        }
    }
}
```

## Other Projects

There are other projects available for C#/.NET which implement Snappy compression.

- [Snappy.NET](https://snappy.machinezoo.com/) - Uses P/Invoke to C++ for great performance. However, it only works on Windows, and is a bit heap allocation heavy in some cases. It also hasn't been updated since 2014 (as of 10/2020). This project may still be the best choice if your project is on the legacy .NET Framework on Windows, where Snappier is much less performant.
- [IronSnappy](https://github.com/aloneguid/IronSnappy) - Another pure C# port, based on the Golang implementation instead of the C++ implementation.
