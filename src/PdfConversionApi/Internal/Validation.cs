using System;

namespace PdfConversionApi.Internal;

internal static class Validation
{
    /// <summary>A format or tool name in the URL: 1 to 20 letters, digits or hyphens. Returns it in lower case.</summary>
    public static string Format(string? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.Length is < 1 or > 20)
        {
            throw new ArgumentException("A format name must be 1 to 20 letters, digits or hyphens.", paramName);
        }

        foreach (var c in value)
        {
            if (!(IsAsciiLetterOrDigit(c) || c == '-'))
            {
                throw new ArgumentException("A format name must be 1 to 20 letters, digits or hyphens.", paramName);
            }
        }

        return ToLowerAscii(value);
    }

    /// <summary>A stored file's id: 32 hex characters.</summary>
    public static string FileId(string? value, string paramName)
    {
        if (value is null)
        {
            throw new ArgumentNullException(paramName);
        }

        if (value.Length != 32)
        {
            throw new ArgumentException("A stored file id is 32 hex characters.", paramName);
        }

        foreach (var c in value)
        {
            if (!IsHex(c))
            {
                throw new ArgumentException("A stored file id is 32 hex characters.", paramName);
            }
        }

        return value;
    }

    public static TimeSpan Timeout(TimeSpan value, string paramName)
    {
        if (value == System.Threading.Timeout.InfiniteTimeSpan)
        {
            return value;
        }

        if (value <= TimeSpan.Zero || value.TotalMilliseconds > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(paramName, "A timeout must be positive and at most about 24 days, or Timeout.InfiniteTimeSpan.");
        }

        return value;
    }

    public static string ToLowerAscii(string value)
    {
        var chars = value.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (chars[i] is >= 'A' and <= 'Z')
            {
                chars[i] = (char)(chars[i] + 32);
            }
        }

        return new string(chars);
    }

    private static bool IsAsciiLetterOrDigit(char c) => c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9');

    private static bool IsHex(char c) => c is (>= 'a' and <= 'f') or (>= 'A' and <= 'F') or (>= '0' and <= '9');
}
