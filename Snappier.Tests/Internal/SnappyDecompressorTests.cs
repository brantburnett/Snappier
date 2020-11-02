using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests.Internal
{
    public class SnappyDecompressorTests
    {
        #region DecompressAllTags

        [Fact]
        public void DecompressAllTags_ShortInputBufferWhichCopiesToScratch_DoesNotReadPastEndOfScratch()
        {
            // Arrange

            var decompressor = new SnappyDecompressor();
            decompressor.SetExpectedLengthForTest(1024);

            decompressor.WriteToBufferForTest(Enumerable.Range(0, 255).Select(p => (byte) p).ToArray());

            // if in error, decompressor will read the 222, 0, 0 as the next tag and throw a copy offset exception
            decompressor.LoadScratchForTest(new byte[] { 222, 222, 222, 222, 0, 0 }, 0);

            // Act

            decompressor.DecompressAllTags(new byte[] { 150, 255, 0 });
        }

        #endregion
    }
}
