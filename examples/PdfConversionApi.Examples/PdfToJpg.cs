using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class PdfToJpg
{
    public static async Task RunAsync()
    {
        #region readme:pdf-to-jpg
        using var client = new PdfConversionApiClient();
        var result = await client.ConvertFileAsync(
            "season-guide.pdf",
            "jpg",
            new Dictionary<string, object?> { ["ImageResolution"] = 72 });

        foreach (var page in result.Files)
        {
            Console.WriteLine($"{page.Name}: {page.Size} bytes");
        }

        await result.SaveAllAsync("pages");                    // pages/season-guide-1.jpg and -2.jpg
        #endregion
    }
}
