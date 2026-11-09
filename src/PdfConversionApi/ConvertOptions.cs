using System;

namespace PdfConversionApi;

/// <summary>Typed options for one conversion. They win over the same names in the parameters.</summary>
public sealed class ConvertOptions
{
    /// <summary>
    /// The output's name without the extension, sent as <c>FileName</c>. The API cleans it and adds the
    /// extension; with several outputs it replaces the part before the page number.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// <c>true</c> stores the outputs for 3 hours and returns download links instead of data
    /// (sent as <c>StoreFile=true</c>). <c>false</c> removes any <c>StoreFile</c> parameter. Null leaves
    /// the parameters as they are.
    /// </summary>
    public bool? StoreFile { get; set; }

    /// <summary>A timeout for this call's attempts instead of the client's <see cref="PdfConversionApiClientOptions.Timeout"/>.</summary>
    public TimeSpan? Timeout { get; set; }
}
