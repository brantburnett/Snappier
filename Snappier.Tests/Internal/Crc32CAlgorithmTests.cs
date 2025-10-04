using System.Text;

namespace Snappier.Tests.Internal;

public class Crc32CAlgorithmTests
{
    [Theory]
    [InlineData("123456789", 0xe3069283)]
    [InlineData("1234567890123456", 0x9aa4287f)]
    [InlineData("123456789012345612345678901234", 0xecc74934)]
    [InlineData("12345678901234561234567890123456", 0xcd486b4b)]
    public void Compute(string asciiChars, uint expectedResult)
    {
        // Arrange

        byte[] bytes = Encoding.ASCII.GetBytes(asciiChars);

        // Act

        uint result = Crc32CAlgorithm.Compute(bytes);

        // Assert

        Assert.Equal(expectedResult, result);
    }

}
