namespace Snappier.Internal;

internal static partial class VarIntEncoding
{
    private static int WriteSlow(Span<byte> output, uint length)
    {
        const int b = 0b1000_0000;

        unchecked
        {
            if (length < (1 << 7))
            {
                output[0] = (byte) length;
                return 1;
            }
            else if (length < (1 << 14))
            {
                output[0] = (byte) (length | b);
                output[1] = (byte) (length >> 7);
                return 2;
            }
            else if (length < (1 << 21))
            {
                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) (length >> 14);
                return 3;
            }
            else if (length < (1 << 28))
            {
                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) ((length >> 14) | b);
                output[3] = (byte) (length >> 21);
                return 4;
            }
            else
            {
                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) ((length >> 14) | b);
                output[3] = (byte) ((length >> 21) | b);
                output[4] = (byte) (length >> 28);
                return 5;
            }
        }
    }
}
