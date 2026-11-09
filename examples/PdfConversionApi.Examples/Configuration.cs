using System;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class Configuration
{
    public static async Task RunAsync()
    {
        #region readme:configuration
        using var client = new PdfConversionApiClient(new PdfConversionApiClientOptions
        {
            ApiKey = Environment.GetEnvironmentVariable("PDFCONVERSIONAPI_KEY"), // the default when left out
            BaseUrl = null,                             // PDFCONVERSIONAPI_BASE_URL, else https://api.pdfconversionapi.com
            Timeout = TimeSpan.FromMinutes(5),          // per attempt, for the whole response (default 240 s)
            ConnectTimeout = TimeSpan.FromSeconds(10),  // the default
            MaxRetries = 2,                             // default 4, so 5 attempts; 0 turns retries off
            MaxRetryWait = TimeSpan.FromSeconds(30),    // a longer Retry-After is raised at once (default 60 s)
        });

        // One call can have its own timeout.
        var result = await client.ConvertFileAsync(
            "report.docx",
            "pdf",
            options: new ConvertOptions { Timeout = TimeSpan.FromMinutes(10) });
        Console.WriteLine($"{result.File.Name}, request id {result.RequestId}");
        #endregion
    }
}
