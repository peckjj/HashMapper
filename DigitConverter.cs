using System.Numerics;
using System.Text;

namespace HashMapper
{
    internal class DigitConverter
    {
        public static string convertFromBase10(BigInteger value, char[] charset)
        {
            int nBase = charset.Length;

            if (nBase == 0)
            {
                throw new ArgumentException("Must have at least one character in charset.");
            }
            if (nBase == 1)
            {
                // Thought you could get me with this one?
                StringBuilder sb = new StringBuilder();
                while (value != 0)
                {
                    sb.Append(charset[0]);
                    value--;
                }

                return sb.ToString();
            }
            if (value == BigInteger.Zero)
            {
                return "" + charset[0];
            }

            string result = "";

            while (value > 0)
            {
                result = charset[((int)(value % nBase))] + result;
                value = value / nBase;
            }

            return result;
        }
    }
}
