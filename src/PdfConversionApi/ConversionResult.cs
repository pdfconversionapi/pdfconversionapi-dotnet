using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfConversionApi.Internal;

namespace PdfConversionApi;

/// <summary>What a conversion returned: its output files, in the API's order.</summary>
public sealed class ConversionResult
{
    internal ConversionResult(IReadOnlyList<OutputFile> files, int conversionCost, double conversionTime, string? requestId)
    {
        Files = files;
        ConversionCost = conversionCost;
        ConversionTime = conversionTime;
        RequestId = requestId;
    }

    /// <summary>Every output file, in the API's order (pages in page order).</summary>
    public IReadOnlyList<OutputFile> Files { get; }

    /// <summary>The first output file, for conversions that return one.</summary>
    /// <exception cref="InvalidOperationException">The response had no files.</exception>
    public OutputFile File => Files.Count > 0 ? Files[0] : throw new InvalidOperationException("The conversion returned no files.");

    /// <summary>Conversions counted for this request: always 1.</summary>
    public int ConversionCost { get; }

    /// <summary>Seconds the conversion took on the server, without the upload, the wait for a slot and the download.</summary>
    public double ConversionTime { get; }

    /// <summary>The response's <c>x-request-id</c>, or null.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// Writes every file into <paramref name="directory"/> under its own name, creating the folder if
    /// needed and replacing files that exist. Stored files are downloaded.
    /// </summary>
    /// <param name="directory">The folder to write into.</param>
    /// <param name="cancellationToken">Cancels the downloads or writes.</param>
    /// <returns>The paths written, in the order of <see cref="Files"/>.</returns>
    public async Task<IReadOnlyList<string>> SaveAllAsync(string directory, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(directory))
        {
            throw new ArgumentException("The folder must not be empty.", nameof(directory));
        }

        Directory.CreateDirectory(directory);
        var paths = new List<string>(Files.Count);
        foreach (var file in Files)
        {
            var path = Path.Combine(directory, FileNames.SafeOutputName(file.Name));
            paths.Add(await file.DownloadToAsync(path, cancellationToken).ConfigureAwait(false));
        }

        return paths;
    }

    /// <inheritdoc />
    public override string ToString() => "ConversionResult(" + Files.Count + " file(s), request id: " + (RequestId ?? "none") + ")";
}
