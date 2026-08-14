using System.Globalization;

namespace LocalWhale.Core.Updates;

public readonly record struct SemVersion(int Major, int Minor, int Patch, string? PreRelease) : IComparable<SemVersion>
{
    public static SemVersion Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var versionAndBuild = value.Split('+', 2, StringSplitOptions.TrimEntries);
        var versionAndPreRelease = versionAndBuild[0].Split('-', 2, StringSplitOptions.TrimEntries);
        var core = versionAndPreRelease[0].Split('.', StringSplitOptions.TrimEntries);
        if (core.Length != 3 ||
            !int.TryParse(core[0], NumberStyles.None, CultureInfo.InvariantCulture, out var major) ||
            !int.TryParse(core[1], NumberStyles.None, CultureInfo.InvariantCulture, out var minor) ||
            !int.TryParse(core[2], NumberStyles.None, CultureInfo.InvariantCulture, out var patch))
        {
            throw new FormatException($"Invalid semantic version: {value}");
        }

        var preRelease = versionAndPreRelease.Length == 2 ? versionAndPreRelease[1] : null;
        if (preRelease is { Length: 0 }) throw new FormatException($"Invalid semantic version: {value}");
        return new SemVersion(major, minor, patch, preRelease);
    }

    public int CompareTo(SemVersion other)
    {
        var core = Major.CompareTo(other.Major);
        if (core == 0) core = Minor.CompareTo(other.Minor);
        if (core == 0) core = Patch.CompareTo(other.Patch);
        if (core != 0) return core;
        if (PreRelease is null) return other.PreRelease is null ? 0 : 1;
        if (other.PreRelease is null) return -1;

        var left = PreRelease.Split('.');
        var right = other.PreRelease.Split('.');
        for (var index = 0; index < Math.Max(left.Length, right.Length); index++)
        {
            if (index >= left.Length) return -1;
            if (index >= right.Length) return 1;
            var comparison = CompareIdentifier(left[index], right[index]);
            if (comparison != 0) return comparison;
        }

        return 0;
    }

    public static bool operator >(SemVersion left, SemVersion right) => left.CompareTo(right) > 0;
    public static bool operator <(SemVersion left, SemVersion right) => left.CompareTo(right) < 0;
    public static bool operator >=(SemVersion left, SemVersion right) => left.CompareTo(right) >= 0;
    public static bool operator <=(SemVersion left, SemVersion right) => left.CompareTo(right) <= 0;

    public override string ToString() => $"{Major}.{Minor}.{Patch}{(PreRelease is null ? string.Empty : $"-{PreRelease}")}";

    private static int CompareIdentifier(string left, string right)
    {
        var leftNumeric = int.TryParse(left, NumberStyles.None, CultureInfo.InvariantCulture, out var leftValue);
        var rightNumeric = int.TryParse(right, NumberStyles.None, CultureInfo.InvariantCulture, out var rightValue);
        if (leftNumeric && rightNumeric) return leftValue.CompareTo(rightValue);
        if (leftNumeric) return -1;
        if (rightNumeric) return 1;
        return string.CompareOrdinal(left, right);
    }
}
