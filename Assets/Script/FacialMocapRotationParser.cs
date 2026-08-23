using System.Globalization;
using UnityEngine;

public static class FacialMocapRotationParser
{
    public static bool TryParseEuler(string value, out Vector3 euler)
    {
        euler = Vector3.zero;
        if (string.IsNullOrWhiteSpace(value)) return false;

        string[] axes = value.Split(',');
        if (axes.Length < 3) return false;

        const NumberStyles Style = NumberStyles.Float;
        if (!float.TryParse(axes[0], Style, CultureInfo.InvariantCulture, out float x) ||
            !float.TryParse(axes[1], Style, CultureInfo.InvariantCulture, out float y) ||
            !float.TryParse(axes[2], Style, CultureInfo.InvariantCulture, out float z))
        {
            return false;
        }

        euler = new Vector3(x, y, z);
        return true;
    }
}
