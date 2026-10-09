namespace HolidayLights.Core.Bulbs.Writing;

/// <summary>
/// The CRC of a <c>.bul</c> GIF entry (as 5.4 computes it): CRC-32/BZIP2,
/// i.e. MSB-first, polynomial 0x04C11DB7, initial value and final XOR 0xFFFFFFFF, no reflection. 5.4 uses it only to store
/// identical GIFs once.
/// </summary>
internal static class Crc32Bzip2
{
    private const uint Polynomial = 0x04C11DB7;

    private static readonly uint[] Table = BuildTable();

    /// <summary>Computes the CRC of some bytes.</summary>
    /// <param name="data">The bytes.</param>
    /// <returns>The CRC as 5.4 stores it at entry offset +0x08.</returns>
    public static uint Compute(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte b in data)
        {
            crc = crc << 8 ^ Table[(crc >> 24) ^ b];
        }

        return ~crc;
    }

    private static uint[] BuildTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < table.Length; i++)
        {
            uint c = i << 24;
            for (int bit = 0; bit < 8; bit++)
            {
                c = (c & 0x80000000) != 0 ? c << 1 ^ Polynomial : c << 1;
            }

            table[i] = c;
        }

        return table;
    }
}
