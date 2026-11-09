using System;
using System.Globalization;

namespace PdfConversionApi.Internal;

/// <summary>Turns a parameter value into the text sent in its form field.</summary>
internal static class ParameterValues
{
    private static readonly char[] ExponentMarks = { 'E', 'e' };

    /// <summary>The text for a value, or null when the value is null (the field is left out).</summary>
    public static string? ToText(object? value, string name)
    {
        switch (value)
        {
            case null:
                return null;
            case string s:
                return s;
            case bool b:
                return b ? "true" : "false";
            case int or long or short or sbyte or byte or ushort or uint or ulong:
                return Convert.ToString(value, CultureInfo.InvariantCulture)!;
            case decimal m:
                return m.ToString(CultureInfo.InvariantCulture);
            case double d:
                return FromFloatingPoint(d, d.ToString("R", CultureInfo.InvariantCulture), name);
            case float f:
                return FromFloatingPoint(f, f.ToString("R", CultureInfo.InvariantCulture), name);
            default:
                throw new ArgumentException(
                    "Parameter '" + name + "' has a value of type " + value.GetType().Name
                    + "; use a string, a number, a bool, null or an UploadFile.",
                    nameof(value));
        }
    }

    private static string FromFloatingPoint(double value, string roundTrip, string name)
    {
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            throw new ArgumentException("Parameter '" + name + "' must be a finite number.", nameof(value));
        }

        return value == 0 ? "0" : PlainDecimal(roundTrip);
    }

    /// <summary>Rewrites a number such as <c>1.5E-07</c> as plain decimal: <c>0.00000015</c>.</summary>
    internal static string PlainDecimal(string number)
    {
        var e = number.IndexOfAny(ExponentMarks);
        if (e < 0)
        {
            return number;
        }

        var negative = number[0] == '-';
        var mantissa = number.Substring(negative ? 1 : 0, e - (negative ? 1 : 0));
        var exponent = int.Parse(number.Substring(e + 1), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture);

        var dot = mantissa.IndexOf('.');
        var digits = dot < 0 ? mantissa : mantissa.Remove(dot, 1);
        var point = (dot < 0 ? mantissa.Length : dot) + exponent;

        string plain;
        if (point <= 0)
        {
            plain = "0." + new string('0', -point) + digits;
        }
        else if (point >= digits.Length)
        {
            plain = digits + new string('0', point - digits.Length);
        }
        else
        {
            plain = digits.Substring(0, point) + "." + digits.Substring(point);
        }

        return negative ? "-" + plain : plain;
    }
}
