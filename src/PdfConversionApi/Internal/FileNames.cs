using System;
using System.IO;

namespace PdfConversionApi.Internal;

internal static class FileNames
{
    private static readonly char[] Separators = { '/', '\\' };

    /// <summary>Everything after the last <c>/</c> or <c>\</c>.</summary>
    public static string LastPart(string name)
    {
        var index = name.LastIndexOfAny(Separators);
        return index < 0 ? name : name.Substring(index + 1);
    }

    /// <summary>
    /// The name an output file is written under inside a folder: its own name only. Refuses names
    /// that would leave the folder or can't be a file name. The API never sends such names, but
    /// the response is not trusted with the file system.
    /// </summary>
    public static string SafeOutputName(string name)
    {
        var last = LastPart(name ?? string.Empty);
        if (last.Length == 0 || last == "." || last == ".." || last.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new InvalidResponseException("Refusing to write an output file named '" + Printable(name ?? string.Empty) + "'.");
        }

        return last;
    }

    private static string Printable(string name)
    {
        var chars = name.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
        {
            if (char.IsControl(chars[i]))
            {
                chars[i] = '?';
            }
        }

        return new string(chars);
    }
}
