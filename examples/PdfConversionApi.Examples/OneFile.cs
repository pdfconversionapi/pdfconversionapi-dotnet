using System;
using System.IO;
using System.Threading.Tasks;

namespace PdfConversionApi.Examples;

internal static class OneFile
{
    public static async Task RunAsync()
    {
        #region readme:one-file
        using var client = new PdfConversionApiClient();

        // Bytes or a stream: pass the name to send, since the output is named after it.
        var bytes = await File.ReadAllBytesAsync("report.docx");
        var fromBytes = await client.ConvertAsync("docx", "pdf", UploadFile.FromBytes(bytes, "report.docx"));

        await using var stream = File.OpenRead("report.docx");
        var fromStream = await client.ConvertAsync("docx", "pdf", UploadFile.FromStream(stream, "report.docx"));

        var path = await fromStream.File.SaveAsync("report.pdf");
        Console.WriteLine($"{fromBytes.File.Name}: {fromBytes.File.Size} bytes; saved {path}");
        #endregion
    }
}
