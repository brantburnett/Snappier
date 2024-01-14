using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Nethereum.Hex.HexConvertors.Extensions;
using Xunit;

namespace Snappier.Tests
{
    public class FailingSequenceTest
    {
        [Fact]
        public void FailingSequence()
        {
            var bodybytes =  ("0x" + File.ReadAllText("TestData/failsequence.txt")).HexToByteArray();
            using var compressed = new MemoryStream();

            using SnappyStream compressor = new(compressed, System.IO.Compression.CompressionMode.Compress);

            compressor.Write(bodybytes, 0, bodybytes.Length);
            compressor.Flush();

            compressed.Position = 0;

            using SnappyStream decompressor = new(compressed, System.IO.Compression.CompressionMode.Decompress);
            using var decompressed = new MemoryStream();
            decompressor.CopyTo(decompressed);

        }
    }
}
