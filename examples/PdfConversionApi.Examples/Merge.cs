using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class Merge
{
    public static async Task RunAsync()
    {
        #region readme:merge
        using var client = new PdfConversionApiClient();
        var inputs = new List<UploadFile>
        {
            UploadFile.FromPath("invoice.pdf"),
            UploadFile.FromPath("terms.pdf"),
            UploadFile.FromPath("timesheet.pdf"),
        };

        var result = await client.ConvertAsync("pdf", "merge", inputs, options: new ConvertOptions { FileName = "october-billing" });

        await result.File.SaveAsync(".");                      // writes ./october-billing.pdf
        Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
        #endregion
    }
}
