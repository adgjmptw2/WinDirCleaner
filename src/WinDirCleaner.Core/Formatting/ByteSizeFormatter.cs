using System.Globalization;

namespace WinDirCleaner.Core.Formatting;

public static class ByteSizeFormatter
{
    public static string Format(long bytes)
    {
        if (bytes < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytes), bytes, "Byte count cannot be negative.");
        }

        if (bytes == 0)
        {
            return "0 B";
        }

        if (bytes < 1024)
        {
            return bytes.ToString(CultureInfo.InvariantCulture) + " B";
        }

        var exponent = (int)Math.Floor(Math.Log(bytes, 1024));
        exponent = Math.Clamp(exponent, 1, 4);

        var divisor = Math.Pow(1024, exponent);
        var value = bytes / divisor;
        value = Math.Round(value, 1, MidpointRounding.AwayFromZero);

        var suffix = exponent switch
        {
            1 => "KB",
            2 => "MB",
            3 => "GB",
            _ => "TB",
        };

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + suffix;
    }
}
