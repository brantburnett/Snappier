namespace Snappier.Tests.Internal;

public class VarIntEncodingWriteTests
{
    public static TheoryData<uint, byte[]> TestData() =>
        new() {
            { 0x00, [ 0x00 ] },
            { 0x01, [ 0x01 ] },
            { 0x7F, [ 0x7F ] },
            { 0x80, [ 0x80, 0x01 ] },
            { 0x555, [ 0xD5, 0x0A ] },
            { 0x7FFF, [ 0xFF, 0xFF, 0x01 ] },
            { 0xBFFF, [ 0xFF, 0xFF, 0x02 ] },
            { 0xFFFF, [ 0XFF, 0xFF, 0x03 ] },
            { 0x8000, [ 0x80, 0x80, 0x02 ] },
            { 0x5555, [ 0xD5, 0xAA, 0x01 ] },
            { 0xCAFEF00, [ 0x80, 0xDE, 0xBF, 0x65 ] },
            { 0xCAFEF00D, [ 0x8D, 0xE0, 0xFB, 0xD7, 0x0C ] },
            { 0xFFFFFFFF, [ 0xFF, 0xFF, 0xFF, 0xFF, 0x0F ] },
        };

    [Theory]
    [MemberData(nameof(TestData))]
    public void Test_Write(uint value, byte[] expected)
    {
        byte[] bytes = new byte[5];

        int length = VarIntEncoding.Write(bytes, value);
        Assert.Equal(expected, bytes.Take(length));
    }

    [Theory]
    [MemberData(nameof(TestData))]
    public void Test_WriteWithPadding(uint value, byte[] expected)
    {
        // Test of the fast path where there are at least 8 bytes in the buffer

        byte[] bytes = new byte[sizeof(ulong)];

        int length = VarIntEncoding.Write(bytes, value);
        Assert.Equal(expected, bytes.Take(length));
    }
}

/* ************************************************************
*
*    @author Couchbase <info@couchbase.com>
*    @copyright 2021 Couchbase, Inc.
*
*    Licensed under the Apache License, Version 2.0 (the "License");
*    you may not use this file except in compliance with the License.
*    You may obtain a copy of the License at
*
*        http://www.apache.org/licenses/LICENSE-2.0
*
*    Unless required by applicable law or agreed to in writing, software
*    distributed under the License is distributed on an "AS IS" BASIS,
*    WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
*    See the License for the specific language governing permissions and
*    limitations under the License.
*
* ************************************************************/
