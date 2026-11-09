using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace PdfConversionApi.Examples;

internal static class HttpClientFactory
{
    public static async Task RunAsync()
    {
        #region readme:http-client-factory
        var services = new ServiceCollection();
        services.AddHttpClient<PdfConversionApiClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
        // Optional: services.AddSingleton(new PdfConversionApiClientOptions { ... });
        using var provider = services.BuildServiceProvider();

        var client = provider.GetRequiredService<PdfConversionApiClient>();
        var result = await client.ConvertFileAsync("report.docx", "pdf");
        Console.WriteLine(result.File.Name);
        #endregion
    }
}
