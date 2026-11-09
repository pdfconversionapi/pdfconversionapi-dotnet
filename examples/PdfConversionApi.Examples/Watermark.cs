using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class Watermark
{
    public static async Task RunAsync()
    {
        #region readme:watermark
        using var client = new PdfConversionApiClient();
        var result = await client.ConvertAsync(
            "pdf",
            "image-watermark",
            UploadFile.FromPath("proposal.pdf"),
            new Dictionary<string, object?>
            {
                ["ImageFile"] = UploadFile.FromPath("logo.png"),   // a file parameter: sent as its own file field
                ["Opacity"] = 30,
            });

        await result.File.SaveAsync("proposal-stamped.pdf");
        Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
        #endregion
    }
}
