using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class Parameters
{
    public static async Task RunAsync()
    {
        #region readme:parameters
        using var client = new PdfConversionApiClient();
        var parameters = new Dictionary<string, object?>
        {
            ["PageRange"] = "2",               // strings are sent as they are
            ["ImageResolution"] = 100,         // numbers in plain decimal
            ["ScaleProportions"] = true,       // booleans as true or false
            ["Password"] = null,               // null is left out
        };

        var result = await client.ConvertFileAsync(
            "season-guide.pdf",
            "jpg",
            parameters,
            new ConvertOptions { FileName = "cover" });    // typed options: FileName, StoreFile, Timeout

        Console.WriteLine(result.File.Name);
        #endregion
    }
}
