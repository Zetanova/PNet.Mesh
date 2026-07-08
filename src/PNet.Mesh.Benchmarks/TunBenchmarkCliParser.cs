using System.Globalization;

namespace PNet.Mesh.Benchmarks;

internal static class TunBenchmarkCliParser
{
    internal static bool TryReadValue(string[] args, ref int index, out string value)
    {
        value = string.Empty;
        if (++index >= args.Length)
            return false;

        value = args[index];
        return !string.IsNullOrWhiteSpace(value);
    }

    internal static bool TryReadIntValue(string[] args, ref int index, out int value)
    {
        value = 0;
        return TryReadValue(args, ref index, out var text)
               && int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadInt64Value(string[] args, ref int index, out long value)
    {
        value = 0;
        return TryReadValue(args, ref index, out var text)
               && long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    internal static bool TryReadDurationValue(string[] args, ref int index, out TimeSpan value)
    {
        value = default;
        return TryReadValue(args, ref index, out var text) && TryReadDuration(text, out value);
    }

    internal static bool IsHelp(string value)
    {
        return string.Equals(value, "--help", StringComparison.OrdinalIgnoreCase)
               || string.Equals(value, "-h", StringComparison.OrdinalIgnoreCase);
    }

    internal static bool IsSupportedPayloadMode(string value)
    {
        return string.Equals(value, "control", StringComparison.Ordinal)
               || string.Equals(value, "mtu", StringComparison.Ordinal);
    }

    internal static string FormatDuration(TimeSpan value)
    {
        return value.TotalMilliseconds % 1000 == 0
            ? value.TotalSeconds.ToString(CultureInfo.InvariantCulture) + "s"
            : value.TotalMilliseconds.ToString(CultureInfo.InvariantCulture) + "ms";
    }

    static bool TryReadDuration(string text, out TimeSpan value)
    {
        value = default;
        if (text.EndsWith("ms", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var milliseconds))
        {
            value = TimeSpan.FromMilliseconds(milliseconds);
            return true;
        }

        if (text.EndsWith("s", StringComparison.OrdinalIgnoreCase)
            && double.TryParse(text[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            value = TimeSpan.FromSeconds(seconds);
            return true;
        }

        return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
    }
}
