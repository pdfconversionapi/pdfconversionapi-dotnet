using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class NoFile
{
    public static async Task RunAsync()
    {
        #region readme:no-file
        using var client = new PdfConversionApiClient();
        var result = await client.ConvertAsync(
            "data",
            "qrcode",
            new Dictionary<string, object?> { ["Data"] = "https://pdfconversionapi.com" });

        await result.File.SaveAsync(".");                      // writes ./qrcode.svg
        Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
        #endregion
    }
}
