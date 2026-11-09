using System;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class Errors
{
    public static async Task RunAsync()
    {
        #region readme:errors
        using var client = new PdfConversionApiClient();
        try
        {
            await client.ConvertFileAsync("report.docx", "dwg");
        }
        catch (RateLimitException e) when (e.RetryAfter > TimeSpan.FromMinutes(1))
        {
            // The month's conversions are used up; short waits were already retried.
            Console.WriteLine($"Stopped until later: {e.ApiMessage}");
        }
        catch (ApiException e)
        {
            // Any error status: e.StatusCode, e.ApiMessage, e.RequestId, e.RetryAfter, e.Body.
            Console.WriteLine(e.Message);
        }
        catch (ApiTimeoutException e)
        {
            // Not retried: the conversion may still be running, and counts if it succeeds.
            Console.WriteLine(e.Message);
        }
        catch (ApiConnectionException e)
        {
            // No response after every retry.
            Console.WriteLine(e.Message);
        }
        #endregion
    }
}
