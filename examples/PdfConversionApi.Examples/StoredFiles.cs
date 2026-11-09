using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class StoredFiles
{
    public static async Task RunAsync()
    {
        #region readme:stored-files
        using var client = new PdfConversionApiClient();
        var result = await client.ConvertFileAsync(
            "season-guide.pdf",
            "jpg",
            new Dictionary<string, object?> { ["ImageResolution"] = 72 },
            new ConvertOptions { StoreFile = true });

        foreach (var file in result.Files)
        {
            // file.Url needs no key and works for 3 hours: treat it like a password.
            var path = await file.SaveAsync(".");                          // streams the download to disk
            var deleted = await client.DeleteStoredFileAsync(file.FileId!);   // false if it was already gone
            Console.WriteLine($"{path}: {file.Size} bytes, stored copy deleted: {deleted}");
        }
        #endregion
    }
}
