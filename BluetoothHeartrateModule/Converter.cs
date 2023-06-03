using System.Text;

namespace BluetoothHeartrateModule
{
    internal class Converter
    {
        internal static string FormatAsMac(ulong deviceMac)
        {
            return BitConverter.ToString(BitConverter.GetBytes(deviceMac)).Replace('-', ':');
        }

        internal static int[] ToDigitArray(int num, int totalWidth)
        {
            return num.ToString().PadLeft(totalWidth, '0').Select(digit => int.Parse(digit.ToString())).ToArray();
        }

        internal static byte[] GetAsciiStringInt(int number)
        {
            return Encoding.ASCII.GetBytes(number.ToString());
        }
    }
}
