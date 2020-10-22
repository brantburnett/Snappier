# Snappier

![.NET Core](https://github.com/brantburnett/Snappier/workflows/.NET%20Core/badge.svg)

## Introduction

Snappier is a pure C# port of Google's [Snappy](https://github.com/google/snappy) compression algorithm. It is designed with speed as the primary goal, rather than compression ratio, and is ideal for compressing network traffic. Please see [the Snappy README file](https://github.com/google/snappy/blob/master/README.md) for more details on Snappy.

## Project Goals

The Snappier project aims to meet the following needs of the .NET community.

- Cross-platform C# implementation for Linux and Windows, without P/Invoke
- Compatible with .NET 4.6.1 and later and .NET Core 2.0 and later
- Use .NET paradigms, including asynchronous stream support
- Full compatibility with both block and stream formats
- Near C++ level performance
  - Note: This is only possible on .NET Core 3.0 and later with the aid of [Span&lt;T&gt;](https://docs.microsoft.com/en-us/dotnet/api/system.span-1?view=netcore-3.1) and [https://fiigii.com/2019/03/03/Hardware-intrinsic-in-NET-Core-3-0-Introduction/](System.Runtime.Intrinsics).
- Keep allocations and garbage-collection to a minimum using buffer pools

## Using Snappier

### Installing

Simply add a NuGet package reference to the latest version of Snappier.

```xml
<PackageReference Include="Snappier" Version="1.0.0" />
```

### Block compression/decompression

Compressing or decompressing a block is done via static methods on the `Snappy` class.

```cs
using Snappier;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // First, compression
        var buffer = new byte[Snappy.GetMaxCompressedLength(Data)];
        var compressedLength = Snappy.Compress(Data, buffer);
        var compressed = buffer.AsSpan(0, compressedLength);

        // Option 1: Decompress to a memory pool buffer
        var outputMemory = Snappy.DecompressToMemory(compressed);
        outputMemory.Dispose(); // Be SURE to dispose to avoid memory leaks

        // Option 2: Decompress to a heap allocated byte[]
        // This is a bit less efficient, but if you need a byte[]...
        var outputBytes = Snappy.DecompressToArray(compressed);

        // Option 3: Decompress to a buffer you already own
        var outputBuffer = new byte[Snappy.GetUncompressedLength(compressed)];
        var decompressedLength = Snappy.Decompress(compressed, outputBuffer);
    }
}
```

### Stream compression/decompression

Compressing or decompressing a stream follows the same paradigm as other compression streams in .NET. `SnappyStream` wraps an inner stream. If decompressing you read from the `SnappyStream`, if compressing you write to the `SnappyStream`

This approach reads or writes the [Snappy framing format](https://github.com/google/snappy/blob/master/framing_format.txt) designed for streaming. The input/output is not the same as the block method above. It includes additional headers and CRC checks.

```cs
using System.IO;
using System.IO.Compression;
using Snappier;

public class Program
{
    private static byte[] Data = {0, 1, 2}; // Wherever you get the data from

    public static void Main()
    {
        // First, compression
        using var compressed = new MemoryStream();

        using (var compressor = new SnappyStream(compressed, CompressionMode.Compress, false)) {
            compressor.Write(Data, 0, Data.Length);
            
            // Disposing the compressor also flushes the buffers to the inner stream
            // We pass false to the constructor above so that it doesn't dispose the inner stream
            // Alternatively, we could call compressor.Flush()
        }

        // Then, decompression

        compressed.Position = 0; // Reset to beginning of the stream so we can read
        using var decompressor = new SnappyStream(compressed, CompressionMode.Decompress);

        var buffer = new byte[65536];
        while (decompressor.Read(buffer, 0, 65536) > 0)
        {
            // Do something with the data
        }
    }
}
```

## Other Projects

There are other projects available for C#/.NET which implement Snappy compression.

- [Snappy.NET](https://snappy.machinezoo.com/) - Uses P/Invoke to C++ for great performance. However, it only works on Windows, and is a bit heap allocation heavy in some cases. It also hasn't been updated since 2014 (as of 10/2020).
- [IronSnappy](https://github.com/aloneguid/IronSnappy) - Another pure C# port, based on the Golang implemenation instead of the C++ implementation.
