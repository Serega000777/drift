using System.Text;

namespace DriftCareer.Core
{
    /// <summary>CRC-32 (IEEE 802.3) used to detect truncated or corrupted save files.</summary>
    public static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        private static uint[] BuildTable()
        {
            var t = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                t[i] = c;
            }
            return t;
        }

        public static uint Compute(byte[] data)
        {
            uint crc = 0xFFFFFFFFu;
            for (int i = 0; i < data.Length; i++) crc = Table[(crc ^ data[i]) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        public static uint Compute(string text) => Compute(Encoding.UTF8.GetBytes(text ?? string.Empty));

        public static string ToHex(uint crc) => crc.ToString("x8");
    }
}
