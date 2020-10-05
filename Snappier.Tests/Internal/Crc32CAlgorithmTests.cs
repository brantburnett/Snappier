using System;
using System.Collections.Generic;
using System.Text;
using Snappier.Internal;
using Xunit;

namespace Snappier.Tests.Internal
{
    public class Crc32CAlgorithmTests
    {
        [Theory]
        [InlineData("123456789", 0xe3069283)]
        public void Compute(string asciiChars, uint expectedResult)
        {
            // Arrange

            var crc = new Crc32CAlgorithm();
            var bytes = Encoding.ASCII.GetBytes(asciiChars);

            // Act

            var result = crc.ComputeHash(bytes);

            // Assert

            Assert.Equal(expectedResult, result);
        }

    }
}
