using System;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class UsageAndConversions
{
    public static async Task RunAsync()
    {
        #region readme:usage
        using var client = new PdfConversionApiClient();

        var usage = await client.UsageAsync();                 // uses no conversion
        Console.WriteLine($"{usage.Plan}: {usage.Used} of {usage.Included} used, until {usage.PeriodEnd:yyyy-MM-dd}");

        var conversions = await client.ConversionsAsync();     // needs no key
        Console.WriteLine($"{conversions.Count} conversions, such as {conversions[0].Title}");
        #endregion
    }
}
