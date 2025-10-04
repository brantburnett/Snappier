namespace Snappier.Tests.Internal;


public class SnappyStreamCompressorTests
{
    [Theory]
    [InlineData("alice29.txt")]
    [InlineData("asyoulik.txt")]
    [InlineData("fireworks.jpeg")]
    [InlineData("geo.protodata")]
    [InlineData("html")]
    [InlineData("html_x_4")]
    [InlineData("kppkn.gtb")]
    [InlineData("lcet10.txt")]
    [InlineData("paper-100k.pdf")]
    [InlineData("plrabn12.txt")]
    [InlineData("urls.10K")]
    public void Write(string resourceName)
    {
        using Stream resource =
            typeof(SnappyStreamCompressorTests).Assembly.GetManifestResourceStream("Snappier.Tests.TestData." + resourceName);
        Assert.NotNull(resource);

        using var memStream = new MemoryStream();
        resource.CopyTo(memStream);

        Span<byte> input = memStream.GetBuffer().AsSpan(0, (int) memStream.Length);

        using var output = new MemoryStream();

        using var compressor = new SnappyStreamCompressor();
        compressor.Write(input, output);
        compressor.Flush(output);

        using var decompressor = new SnappyStreamDecompressor();
        decompressor.SetInput(output.GetBuffer().AsMemory(0, (int) output.Length));

        byte[] decompressed = new byte[memStream.Length + 1]; // Add 1 to make sure decompress ends correctly
        int bytesDecompressed = decompressor.Decompress(decompressed);

        Assert.Equal(input.Length, bytesDecompressed);
        for (int i = 0; i < bytesDecompressed; i++)
        {
            Assert.Equal(input[i], decompressed[i]);
        }
    }
}
