using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using PdfConversionApi.Examples;

// One program per README section. Each reads PDFCONVERSIONAPI_KEY (and PDFCONVERSIONAPI_BASE_URL
// when set) and reads and writes files in the current folder. Run one by name:
//
//   dotnet run --project examples/PdfConversionApi.Examples -- quickstart
//
// examples/run-local.sh runs all of them against the local test API with the sample files.
var examples = new Dictionary<string, Func<Task>>(StringComparer.Ordinal)
{
    ["quickstart"] = QuickStart.RunAsync,
    ["one-file"] = OneFile.RunAsync,
    ["pdf-to-jpg"] = PdfToJpg.RunAsync,
    ["merge"] = Merge.RunAsync,
    ["watermark"] = Watermark.RunAsync,
    ["parameters"] = Parameters.RunAsync,
    ["no-file"] = NoFile.RunAsync,
    ["stored-files"] = StoredFiles.RunAsync,
    ["usage"] = UsageAndConversions.RunAsync,
    ["errors"] = Errors.RunAsync,
    ["configuration"] = Configuration.RunAsync,
    ["http-client-factory"] = HttpClientFactory.RunAsync,
};

if (args.Length != 1 || !examples.TryGetValue(args[0], out var run))
{
    Console.Error.WriteLine("usage: PdfConversionApi.Examples <" + string.Join("|", examples.Keys.OrderBy(k => k, StringComparer.Ordinal)) + ">");
    return 2;
}

await run();
return 0;
