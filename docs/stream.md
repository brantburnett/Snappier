# Stream Compression

Stream compression reads or writes the [Snappy framing format](https://github.com/google/snappy/blob/master/framing_format.txt) designed for streaming. 
It is ideal for data being sent over a network stream, and includes additional framing data and CRC validation.
It also recognizes when an individual block in the stream compresses poorly and will include it in uncompressed form.

## Stream compression/decompression

Compressing or decompressing a stream follows the same paradigm as other compression streams in .NET. `SnappyStream` wraps an inner stream. If decompressing you read from the `SnappyStream`, if compressing you write to the `SnappyStream`

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