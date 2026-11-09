using System;
using System.IO;

namespace PdfConversionApi.Tests.Support;

/// <summary>Finds the SDK folder, the shared conformance suite and the repository's sample files.</summary>
internal static class TestPaths
{
    private static readonly Lazy<string> SdkRootLazy = new(FindSdkRoot);
    private static readonly Lazy<string> ConformanceLazy = new(FindConformance);

    /// <summary>The <c>sdks/dotnet</c> folder (the one holding <c>PdfConversionApi.sln</c>).</summary>
    public static string SdkRoot => SdkRootLazy.Value;

    /// <summary>
    /// The conformance suite: <c>PDFCONVERSIONAPI_CONFORMANCE_DIR</c> when set, else <c>../conformance</c>
    /// next to the SDK folder (the monorepo layout), else <c>conformance/</c> inside it (a vendored copy).
    /// </summary>
    public static string ConformanceDir => ConformanceLazy.Value;

    public static string Fixtures => Path.Combine(ConformanceDir, "fixtures");

    public static string Fixture(string name) => Path.Combine(Fixtures, name);

    /// <summary>
    /// The live tests' input files: <c>PDFCONVERSIONAPI_SAMPLES_DIR</c> when set, else the suite's <c>samples/</c>.
    /// </summary>
    public static string Samples
    {
        get
        {
            var fromEnvironment = Environment.GetEnvironmentVariable("PDFCONVERSIONAPI_SAMPLES_DIR");
            return Path.GetFullPath(string.IsNullOrEmpty(fromEnvironment) ? Path.Combine(ConformanceDir, "samples") : fromEnvironment);
        }
    }

    private static string FindSdkRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "PdfConversionApi.sln")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("Can't find the SDK folder (PdfConversionApi.sln) above " + AppContext.BaseDirectory);
    }

    private static string FindConformance()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable("PDFCONVERSIONAPI_CONFORMANCE_DIR");
        if (!string.IsNullOrEmpty(fromEnvironment))
        {
            return Path.GetFullPath(fromEnvironment);
        }

        foreach (var candidate in new[] { Path.Combine(SdkRoot, "..", "conformance"), Path.Combine(SdkRoot, "conformance") })
        {
            if (File.Exists(Path.Combine(candidate, "cases.json")))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new InvalidOperationException(
            "Can't find the conformance suite (cases.json). Set PDFCONVERSIONAPI_CONFORMANCE_DIR, or keep it at ../conformance.");
    }
}
