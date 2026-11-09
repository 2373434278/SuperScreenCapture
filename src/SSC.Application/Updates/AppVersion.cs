namespace SSC.Application.Updates;

/// <summary>
/// 宽容的 SemVer 解析：支持 v 前缀、缺省补零与预发布标识，忽略构建元数据。
/// </summary>
public sealed class AppVersion : IComparable<AppVersion>, IEquatable<AppVersion>
{
    public AppVersion(int major, int minor, int patch, string? prerelease = null)
    {
        Major = major;
        Minor = minor;
        Patch = patch;
        Prerelease = string.IsNullOrWhiteSpace(prerelease) ? null : prerelease.Trim();
    }

    public int Major { get; }

    public int Minor { get; }

    public int Patch { get; }

    public string? Prerelease { get; }

    public bool IsPrerelease => Prerelease is not null;

    public string Display => IsPrerelease
        ? $"{Major}.{Minor}.{Patch}-{Prerelease}"
        : $"{Major}.{Minor}.{Patch}";

    public static bool TryParse(string? text, out AppVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim();
        if (value.Length > 1 && (value[0] == 'v' || value[0] == 'V'))
        {
            value = value[1..];
        }

        var buildIndex = value.IndexOf('+', StringComparison.Ordinal);
        if (buildIndex >= 0)
        {
            value = value[..buildIndex];
        }

        string? prerelease = null;
        var prereleaseIndex = value.IndexOf('-', StringComparison.Ordinal);
        if (prereleaseIndex >= 0)
        {
            prerelease = value[(prereleaseIndex + 1)..];
            value = value[..prereleaseIndex];
            if (prerelease.Length == 0)
            {
                return false;
            }
        }

        var parts = value.Split('.');
        if (parts.Length is 0 or > 3)
        {
            return false;
        }

        var numbers = new int[3];
        for (var index = 0; index < parts.Length; index++)
        {
            if (!int.TryParse(parts[index], out var number) || number < 0)
            {
                return false;
            }

            numbers[index] = number;
        }

        version = new AppVersion(numbers[0], numbers[1], numbers[2], prerelease);
        return true;
    }

    public static AppVersion Parse(string text) =>
        TryParse(text, out var version) && version is not null
            ? version
            : throw new FormatException($"'{text}' is not a valid version.");

    public int CompareTo(AppVersion? other)
    {
        if (other is null)
        {
            return 1;
        }

        var result = Major.CompareTo(other.Major);
        if (result != 0)
        {
            return result;
        }

        result = Minor.CompareTo(other.Minor);
        if (result != 0)
        {
            return result;
        }

        result = Patch.CompareTo(other.Patch);
        if (result != 0)
        {
            return result;
        }

        if (Prerelease is null && other.Prerelease is null)
        {
            return 0;
        }

        if (Prerelease is null)
        {
            return 1;
        }

        if (other.Prerelease is null)
        {
            return -1;
        }

        return ComparePrerelease(Prerelease, other.Prerelease);
    }

    public bool Equals(AppVersion? other) => other is not null && CompareTo(other) == 0;

    public override bool Equals(object? obj) => obj is AppVersion other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Major, Minor, Patch, Prerelease);

    public override string ToString() => Display;

    public static bool operator >(AppVersion left, AppVersion right) => left.CompareTo(right) > 0;

    public static bool operator <(AppVersion left, AppVersion right) => left.CompareTo(right) < 0;

    public static bool operator >=(AppVersion left, AppVersion right) => left.CompareTo(right) >= 0;

    public static bool operator <=(AppVersion left, AppVersion right) => left.CompareTo(right) <= 0;

    private static int ComparePrerelease(string left, string right)
    {
        var leftParts = left.Split('.');
        var rightParts = right.Split('.');
        var length = Math.Min(leftParts.Length, rightParts.Length);

        for (var index = 0; index < length; index++)
        {
            var leftPart = leftParts[index];
            var rightPart = rightParts[index];
            var leftNumeric = int.TryParse(leftPart, out var leftNumber);
            var rightNumeric = int.TryParse(rightPart, out var rightNumber);

            if (leftNumeric && rightNumeric)
            {
                var numeric = leftNumber.CompareTo(rightNumber);
                if (numeric != 0)
                {
                    return numeric;
                }
            }
            else if (leftNumeric != rightNumeric)
            {
                return leftNumeric ? -1 : 1;
            }
            else
            {
                var text = string.CompareOrdinal(leftPart, rightPart);
                if (text != 0)
                {
                    return text;
                }
            }
        }

        return leftParts.Length.CompareTo(rightParts.Length);
    }
}
