using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>
/// Against the real API (SPEC section 12). Reads PDFCONVERSIONAPI_LOCAL_BASE_URL and
/// PDFCONVERSIONAPI_LOCAL_KEY from ../conformance/.local-api.env (the API running locally), else
/// PDFCONVERSIONAPI_LIVE_BASE_URL and PDFCONVERSIONAPI_KEY from the environment (the release
/// workflow's run against the production API), and is skipped without either. The key alone
/// doesn't start them, and it is never printed. The input files are in ../conformance/samples.
/// 6 conversions per run and target framework: pick one with -f net10.0.
/// </summary>
[Trait("Category", "Live")]
public sealed class LiveApiTests
{
    private static readonly Lazy<LiveConfig?> Config = new(LiveConfig.Load);

    [Fact]
    public async Task Docx_to_pdf()
    {
        using var client = NewClient();

        var result = await client.ConvertFileAsync(Sample("docx-to-pdf", "input-quarterly-report.docx"), "pdf");

        var file = Assert.Single(result.Files);
        Assert.Equal("input-quarterly-report.pdf", file.Name);
        Assert.Equal("pdf", file.Extension);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(file.GetBytes(), 0, 5), StringComparison.Ordinal);
        Assert.Equal(file.Size, file.GetBytes().LongLength);
        Assert.Equal(1, result.ConversionCost);
        Assert.False(string.IsNullOrEmpty(result.RequestId));
    }

    [Fact]
    public async Task Pdf_to_jpg_returns_two_pages()
    {
        using var client = NewClient();

        var result = await client.ConvertAsync(
            "pdf",
            "jpg",
            UploadFile.FromPath(Sample("pdf-to-jpg", "input-season-guide.pdf")),
            new Dictionary<string, object?> { ["ImageResolution"] = 72 });

        Assert.Equal(new[] { "input-season-guide-1.jpg", "input-season-guide-2.jpg" }, result.Files.Select(f => f.Name));
        Assert.All(result.Files, f => Assert.Equal(new byte[] { 0xFF, 0xD8 }, f.GetBytes().Take(2)));

        var dir = TempDir();
        try
        {
            var paths = await result.SaveAllAsync(dir);
            Assert.Equal(result.Files.Select(f => f.Size), paths.Select(p => new FileInfo(p).Length));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Merge_three_pdfs()
    {
        using var client = NewClient();
        var inputs = new[] { "input-invoice.pdf", "input-terms.pdf", "input-timesheet.pdf" }.Select(n => UploadFile.FromPath(Sample("merge", n))).ToList();

        var result = await client.ConvertAsync("pdf", "merge", inputs, options: new ConvertOptions { FileName = "october-billing" });

        var file = Assert.Single(result.Files);
        Assert.Equal("october-billing.pdf", file.Name);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(file.GetBytes(), 0, 5), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unicode_file_name_names_the_output()
    {
        using var client = NewClient();

        var result = await client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(Encoding.UTF8.GetBytes("unicode\n"), "Résumé final.txt"));

        Assert.Equal("Résumé final.pdf", result.File.Name);
    }

    [Fact]
    public async Task Quote_in_file_name_comes_back_as_underscore()
    {
        using var client = NewClient();

        var result = await client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(Encoding.UTF8.GetBytes("q\n"), "say \"hi\".txt"));

        Assert.Equal("say _hi_.pdf", result.File.Name);
    }

    [Fact]
    public async Task Stored_file_download_and_delete()
    {
        var config = RequireConfig();
        using var client = NewClient();

        var result = await client.ConvertFileAsync(Sample("docx-to-pdf", "input-quarterly-report.docx"), "pdf", options: new ConvertOptions { StoreFile = true });
        var file = result.File;

        Assert.True(file.Stored);
        Assert.Matches("^[0-9a-f]{32}$", file.FileId);
        Assert.Throws<StoredFileException>(() => file.GetBytes());

        // Never fetch a link that points anywhere but the local instance.
        Assert.StartsWith(config.BaseUrl + "/v2/d/" + file.FileId + "/", file.Url, StringComparison.Ordinal);

        var bytes = await file.DownloadAsync();
        Assert.Equal(file.Size, bytes.LongLength);
        Assert.StartsWith("%PDF-", Encoding.ASCII.GetString(bytes, 0, 5), StringComparison.Ordinal);

        var dir = TempDir();
        try
        {
            var path = await file.SaveAsync(dir);
            Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        Assert.True(await client.DeleteStoredFileAsync(file.FileId!));
        Assert.False(await client.DeleteStoredFileAsync(file.FileId!));
        var gone = await Assert.ThrowsAsync<NotFoundException>(() => file.DownloadAsync());
        Assert.Equal(404, gone.StatusCode);
    }

    [Fact]
    public async Task Unsupported_pair_is_404()
    {
        using var client = NewClient();

        var e = await Assert.ThrowsAsync<NotFoundException>(() => client.ConvertFileAsync(Sample("docx-to-pdf", "input-quarterly-report.docx"), "dwg"));

        Assert.Equal("conversion from docx to dwg is not supported", e.ApiMessage);
        Assert.False(string.IsNullOrEmpty(e.RequestId));
    }

    [Fact]
    public async Task Wrong_key_is_401()
    {
        var config = RequireConfig();
        using var client = new PdfConversionApiClient(new PdfConversionApiClientOptions { ApiKey = "wrong-key-0123456789abcdef", BaseUrl = config.BaseUrl });

        var e = await Assert.ThrowsAsync<AuthenticationException>(() => client.ConvertFileAsync(Sample("docx-to-pdf", "input-quarterly-report.docx"), "pdf"));

        Assert.Equal(401, e.StatusCode);
        Assert.Contains("valid API key", e.ApiMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Bad_page_range_is_400()
    {
        using var client = NewClient();

        var e = await Assert.ThrowsAsync<BadRequestException>(() => client.ConvertAsync(
            "pdf",
            "jpg",
            UploadFile.FromPath(Sample("pdf-to-jpg", "input-season-guide.pdf")),
            new Dictionary<string, object?> { ["PageRange"] = "3-1" }));

        Assert.Equal("PageRange '3-1' is not valid (example: 1-3,5)", e.ApiMessage);
    }

    [Fact]
    public async Task Conversions_list_needs_no_key()
    {
        var config = RequireConfig();
        using var client = EnvironmentScope.WithoutVariable(
            PdfConversionApiClient.ApiKeyEnvironmentVariable,
            () => new PdfConversionApiClient(new PdfConversionApiClientOptions { BaseUrl = config.BaseUrl }));

        var list = await client.ConversionsAsync();

        Assert.Contains(list, c => c.From == "docx" && c.To == "pdf");
        Assert.Contains(list, c => c.From == "pdf" && c.To == "merge" && c.Inputs == "many");
    }

    private static LiveConfig RequireConfig()
    {
        var config = Config.Value;
        Assert.SkipWhen(
            config is null,
            "No ../conformance/.local-api.env, and PDFCONVERSIONAPI_LIVE_BASE_URL or PDFCONVERSIONAPI_KEY is not set: live tests skipped.");
        Assert.True(Directory.Exists(TestPaths.Samples), "The live tests' sample files are missing: " + TestPaths.Samples);
        return config!;
    }

    private static PdfConversionApiClient NewClient()
    {
        var config = RequireConfig();
        return new PdfConversionApiClient(new PdfConversionApiClientOptions { ApiKey = config.Key, BaseUrl = config.BaseUrl });
    }

    private static string Sample(string folder, string name) => Path.Combine(TestPaths.Samples, folder, name);

    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "pdfconversionapi-live-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    private sealed class LiveConfig
    {
        private LiveConfig(string baseUrl, string key)
        {
            BaseUrl = baseUrl;
            Key = key;
        }

        public string BaseUrl { get; }

        public string Key { get; }

        public override string ToString() => "LiveConfig(" + BaseUrl + ")";

        public static LiveConfig? Load()
        {
            string path;
            try
            {
                path = Path.Combine(TestPaths.ConformanceDir, ".local-api.env");
            }
            catch (InvalidOperationException)
            {
                return null;
            }

            if (!File.Exists(path))
            {
                return FromEnvironment();
            }

            var values = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var raw in File.ReadAllLines(path))
            {
                var line = raw.Trim();
                if (line.Length == 0 || line.StartsWith('#'))
                {
                    continue;
                }

                if (line.StartsWith("export ", StringComparison.Ordinal))
                {
                    line = line.Substring(7).Trim();
                }

                var eq = line.IndexOf('=', StringComparison.Ordinal);
                if (eq <= 0)
                {
                    continue;
                }

                var value = line.Substring(eq + 1).Trim();
                if (value.Length >= 2 && (value[0] == '"' || value[0] == '\'') && value[^1] == value[0])
                {
                    value = value.Substring(1, value.Length - 2);
                }

                values[line.Substring(0, eq).Trim()] = value;
            }

            return values.TryGetValue("PDFCONVERSIONAPI_LOCAL_BASE_URL", out var baseUrl) && values.TryGetValue("PDFCONVERSIONAPI_LOCAL_KEY", out var key)
                   && baseUrl.Length > 0 && key.Length > 0
                ? new LiveConfig(baseUrl.TrimEnd('/'), key)
                : null;
        }

        private static LiveConfig? FromEnvironment()
        {
            var baseUrl = Environment.GetEnvironmentVariable("PDFCONVERSIONAPI_LIVE_BASE_URL");
            var key = Environment.GetEnvironmentVariable(PdfConversionApiClient.ApiKeyEnvironmentVariable);
            return string.IsNullOrEmpty(baseUrl) || string.IsNullOrEmpty(key) ? null : new LiveConfig(baseUrl.TrimEnd('/'), key);
        }
    }
}
