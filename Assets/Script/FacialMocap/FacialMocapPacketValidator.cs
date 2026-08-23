using System;
using System.Globalization;

public static class FacialMocapPacketValidator
{
    public static bool IsValid(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains("|"))
            return false;

        string[] values = text.Split('|');
        foreach (string value in values)
        {
            if (string.IsNullOrWhiteSpace(value)) continue;

            int headIndex = value.IndexOf("head#", StringComparison.Ordinal);
            if (headIndex >= 0 && headIndex + 5 < value.Length)
                return true;

            int separatorIndex = value.LastIndexOf('-');
            if (separatorIndex <= 0 || separatorIndex >= value.Length - 1)
                continue;

            if (float.TryParse(
                    value.Substring(separatorIndex + 1),
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out _))
            {
                return true;
            }
        }

        return false;
    }
}
