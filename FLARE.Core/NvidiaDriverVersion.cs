namespace FLARE.Core;

// Windows driver "31.0.15.8129" → marketing "581.29": last digit of third + all of fourth, split 3.2.
// Shape pinned (fourth exactly 4 digits) so a future scheme widening returns raw winVer rather
// than silently emitting a wrong marketing number.
internal static class NvidiaDriverVersion
{
    public static string ToNvidiaVersion(string winVer)
    {
        var parts = winVer.Split('.');
        if (parts.Length < 4) return winVer;
        var third = parts[2];
        var fourth = parts[3];
        if (third.Length == 0 || !IsAllDigits(third)) return winVer;
        if (fourth.Length != 4 || !IsAllDigits(fourth)) return winVer;

        var combined = third[^1] + fourth;
        return $"{combined[..3]}.{combined[3..]}";
    }

    private static bool IsAllDigits(string s)
    {
        foreach (var c in s)
            if (c is < '0' or > '9') return false;
        return true;
    }
}
