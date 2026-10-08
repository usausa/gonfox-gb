namespace GonFox.GameBoy.Platform;

using System.Globalization;
using System.Text;

public static class MemoryView
{
    // Parses a hex start (0x optional) and a decimal length of 1 to 256 bytes within 0000-FFFF.
    public static bool TryParseRange(string address, string length, out ushort start, out int count)
    {
        address = address.Trim();
        if (address.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            address = address[2..];
        }

        count = 0;
        return ushort.TryParse(address, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out start) &&
            int.TryParse(length.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out count) &&
            count is >= 1 and <= 256 && start + count <= 0x10000;
    }

    public static string Format(MemorySnapshot snapshot)
    {
        var text = new StringBuilder();
        for (var row = 0; row < snapshot.Bytes.Length; row += 16)
        {
            text.Append((snapshot.Address + row).ToString("X4", CultureInfo.InvariantCulture)).Append(':');
            for (var i = row; i < Math.Min(row + 16, snapshot.Bytes.Length); i++)
            {
                text.Append(' ').Append(snapshot.Bytes[i].ToString("X2", CultureInfo.InvariantCulture));
            }

            text.AppendLine();
        }
        return text.ToString();
    }
}
