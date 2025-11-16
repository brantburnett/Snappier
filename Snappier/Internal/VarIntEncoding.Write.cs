namespace Snappier.Internal;

internal static partial class VarIntEncoding
{
    private static bool TryWriteSlow(Span<byte> output, uint length, out int bytesWritten)
    {
        const int b = 0b1000_0000;

        unchecked
        {
            if (length < (1 << 7))
            {
                if (output.Length < 1)
                {
                    bytesWritten = 0;
                    return false;
                }

                output[0] = (byte) length;
                bytesWritten = 1;
            }
            else if (length < (1 << 14))
            {
                if (output.Length < 2)
                {
                    bytesWritten = 0;
                    return false;
                }

                output[0] = (byte) (length | b);
                output[1] = (byte) (length >> 7);
                bytesWritten = 2;
            }
            else if (length < (1 << 21))
            {
                if (output.Length < 3)
                {
                    bytesWritten = 0;
                    return false;
                }

                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) (length >> 14);
                bytesWritten = 3;
            }
            else if (length < (1 << 28))
            {
                if (output.Length < 4)
                {
                    bytesWritten = 0;
                    return false;
                }

                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) ((length >> 14) | b);
                output[3] = (byte) (length >> 21);
                bytesWritten = 4;
            }
            else
            {
                if (output.Length < 5)
                {
                    bytesWritten = 0;
                    return false;
                }

                output[0] = (byte) (length | b);
                output[1] = (byte) ((length >> 7) | b);
                output[2] = (byte) ((length >> 14) | b);
                output[3] = (byte) ((length >> 21) | b);
                output[4] = (byte) (length >> 28);
                bytesWritten = 5;
            }
        }

        return true;
    }
}
