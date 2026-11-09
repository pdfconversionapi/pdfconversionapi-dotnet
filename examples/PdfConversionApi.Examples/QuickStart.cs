using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class QuickStart
{
    public static async Task RunAsync()
    {
        #region readme:quickstart
        var client = new PdfConversionApiClient();             // reads PDFCONVERSIONAPI_KEY
        var result = await client.ConvertFileAsync("report.docx", "pdf");
        await result.SaveAllAsync("out");                      // writes out/report.pdf
        #endregion
        System.Console.WriteLine("wrote out/" + result.File.Name);
        client.Dispose();
    }
}
