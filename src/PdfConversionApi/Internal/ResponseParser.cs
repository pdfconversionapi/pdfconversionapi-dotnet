using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace PdfConversionApi.Internal;

/// <summary>Reads the API's JSON without reflection (trimming- and AOT-safe).</summary>
internal static class ResponseParser
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: false);

    public static ConversionResult Conversion(ReadOnlyMemory<byte> body, string? requestId, PdfConversionApiClient client)
    {
        using var document = Parse(body, requestId);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("Files", out var filesElement)
            || filesElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidResponseException("The response has no Files list.", requestId);
        }

        var cost = root.TryGetProperty("ConversionCost", out var costElement) && costElement.ValueKind == JsonValueKind.Number && costElement.TryGetInt32(out var c) ? c : 0;
        var time = root.TryGetProperty("ConversionTime", out var timeElement) && timeElement.ValueKind == JsonValueKind.Number ? timeElement.GetDouble() : 0;

        var files = new List<OutputFile>(filesElement.GetArrayLength());
        var index = 0;
        foreach (var entry in filesElement.EnumerateArray())
        {
            files.Add(OutputFile(entry, index++, requestId, client));
        }

        return new ConversionResult(files, cost, time, requestId);
    }

    public static Usage Usage(ReadOnlyMemory<byte> body, string? requestId)
    {
        using var document = Parse(body, requestId);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidResponseException("The usage response is not a JSON object.", requestId);
        }

        long? remaining = null;
        if (root.TryGetProperty("Remaining", out var remainingElement) && remainingElement.ValueKind != JsonValueKind.Null)
        {
            remaining = Int64(root, "Remaining", requestId);
        }

        return new Usage(
            String(root, "Plan", requestId),
            Int64(root, "Included", requestId),
            Int64(root, "Used", requestId),
            remaining,
            Boolean(root, "Overage", requestId),
            Int32(root, "Running", requestId),
            Int32(root, "ConcurrentLimit", requestId),
            Int32(root, "MaxUploadMB", requestId),
            DateTime(root, "PeriodStart", requestId),
            DateTime(root, "PeriodEnd", requestId));
    }

    public static IReadOnlyList<Conversion> Conversions(ReadOnlyMemory<byte> body, string? requestId)
    {
        using var document = Parse(body, requestId);
        var root = document.RootElement;
        if (root.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidResponseException("The list of conversions is not a JSON array.", requestId);
        }

        var list = new List<Conversion>(root.GetArrayLength());
        foreach (var entry in root.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidResponseException("An entry in the list of conversions is not an object.", requestId);
            }

            list.Add(new Conversion(
                String(entry, "From", requestId),
                String(entry, "To", requestId),
                OptionalString(entry, "Title"),
                OptionalString(entry, "Category"),
                OptionalString(entry, "Inputs")));
        }

        return list;
    }

    /// <summary>The <c>message</c> of an error response (SPEC section 9).</summary>
    public static string ErrorMessage(ReadOnlyMemory<byte> body, string? mediaType)
    {
        var text = Text(body);
        if (text.Trim().Length == 0)
        {
            return "empty response";
        }

        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("Message", out var message)
                && message.ValueKind == JsonValueKind.String)
            {
                return message.GetString()!;
            }
        }
        catch (JsonException)
        {
            // Not JSON: described below.
        }

        if (mediaType == "text/plain" && text.Length <= 1000)
        {
            return text.Trim();
        }

        return mediaType is null ? "non-JSON response (no content type)" : "non-JSON response (" + mediaType + ")";
    }

    public static string Text(ReadOnlyMemory<byte> body)
    {
        if (body.IsEmpty)
        {
            return string.Empty;
        }

        var segment = System.Runtime.InteropServices.MemoryMarshal.TryGetArray(body, out var array) ? array : new ArraySegment<byte>(body.ToArray());
        return Utf8.GetString(segment.Array!, segment.Offset, segment.Count);
    }

    private static OutputFile OutputFile(JsonElement entry, int index, string? requestId, PdfConversionApiClient client)
    {
        if (entry.ValueKind != JsonValueKind.Object)
        {
            throw new InvalidResponseException("Files[" + index + "] is not an object.", requestId);
        }

        var name = String(entry, "FileName", requestId);
        var size = Int64(entry, "FileSize", requestId);
        var extension = entry.TryGetProperty("FileExt", out var extElement) && extElement.ValueKind == JsonValueKind.String
            ? extElement.GetString()!
            : ExtensionOf(name);

        if (entry.TryGetProperty("FileData", out var dataElement) && dataElement.ValueKind == JsonValueKind.String)
        {
            if (!dataElement.TryGetBytesFromBase64(out var data) || data is null)
            {
                throw new InvalidResponseException("The data of '" + name + "' is not valid base64.", requestId);
            }

            if (data.LongLength != size)
            {
                throw new InvalidResponseException(
                    "The data of '" + name + "' is " + data.LongLength.ToString(CultureInfo.InvariantCulture)
                    + " bytes, but FileSize says " + size.ToString(CultureInfo.InvariantCulture) + ".",
                    requestId);
            }

            return new OutputFile(client, name, extension, size, data, null, null);
        }

        if (entry.TryGetProperty("Url", out var urlElement) && urlElement.ValueKind == JsonValueKind.String
            && entry.TryGetProperty("FileId", out var idElement) && idElement.ValueKind == JsonValueKind.String)
        {
            return new OutputFile(client, name, extension, size, null, urlElement.GetString(), idElement.GetString());
        }

        throw new InvalidResponseException("'" + name + "' has neither FileData nor Url and FileId.", requestId);
    }

    private static JsonDocument Parse(ReadOnlyMemory<byte> body, string? requestId)
    {
        try
        {
            return JsonDocument.Parse(body);
        }
        catch (JsonException e)
        {
            throw new InvalidResponseException("The response is not valid JSON.", requestId, e);
        }
    }

    private static JsonElement Property(JsonElement element, string name, string? requestId)
    {
        if (!element.TryGetProperty(name, out var value))
        {
            throw new InvalidResponseException("The response has no " + name + ".", requestId);
        }

        return value;
    }

    private static string String(JsonElement element, string name, string? requestId)
    {
        var value = Property(element, name, requestId);
        if (value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidResponseException(name + " in the response is not a string.", requestId);
        }

        return value.GetString()!;
    }

    private static string OptionalString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString()! : string.Empty;

    private static long Int64(JsonElement element, string name, string? requestId)
    {
        var value = Property(element, name, requestId);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt64(out var number))
        {
            throw new InvalidResponseException(name + " in the response is not a whole number.", requestId);
        }

        return number;
    }

    private static int Int32(JsonElement element, string name, string? requestId)
    {
        var value = Property(element, name, requestId);
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var number))
        {
            throw new InvalidResponseException(name + " in the response is not a whole number.", requestId);
        }

        return number;
    }

    private static bool Boolean(JsonElement element, string name, string? requestId)
    {
        var value = Property(element, name, requestId);
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => throw new InvalidResponseException(name + " in the response is not true or false.", requestId),
        };
    }

    private static DateTimeOffset DateTime(JsonElement element, string name, string? requestId)
    {
        var text = String(element, name, requestId);
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var value))
        {
            throw new InvalidResponseException(name + " in the response is not a date and time.", requestId);
        }

        return value.ToUniversalTime();
    }

    private static string ExtensionOf(string name)
    {
        var dot = name.LastIndexOf('.');
        return dot < 0 ? string.Empty : Validation.ToLowerAscii(name.Substring(dot + 1));
    }
}
