using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Behaviour over real HTTP, against the mock server, beyond the shared cases.</summary>
[Collection(MockServerGroup.Name)]
public sealed class MockBackedTests
{
    private readonly MockServer _mock;

    public MockBackedTests(MockServer mock)
    {
        _mock = mock;
    }

    [Fact]
    public async Task A_stream_that_cannot_seek_is_sent_again_unchanged_after_a_503()
    {
        var input = Encoding.UTF8.GetBytes("bytes from a network stream\n");
        var sleeps = new List<double>();
        using var client = Client(sleeps);
        var id = "stream-retry-" + Guid.NewGuid().ToString("N");

        var result = await client.ConvertAsync(
            "txt",
            "pdf",
            UploadFile.FromStream(new ForwardOnlyStream(input), "net.txt"),
            new Dictionary<string, object?> { ["MockScenario"] = "503-busy", ["MockFailures"] = 1, ["MockId"] = id });

        Assert.Equal(input, result.File.GetBytes());
        Assert.Equal("net.pdf", result.File.Name);
        Assert.Equal(2, await _mock.AttemptsAsync(id));
        Assert.Single(sleeps);
    }

    [Fact]
    public async Task A_dropped_connection_is_retried_with_the_file_reopened()
    {
        using var client = Client(new List<double>());
        var id = "drop-retry-" + Guid.NewGuid().ToString("N");

        var result = await client.ConvertAsync(
            "txt",
            "pdf",
            UploadFile.FromPath(TestPaths.Fixture("hello.txt")),
            new Dictionary<string, object?> { ["MockScenario"] = "drop", ["MockFailures"] = 2, ["MockId"] = id });

        Assert.Equal(await File.ReadAllBytesAsync(TestPaths.Fixture("hello.txt")), result.File.GetBytes());
        Assert.Equal(3, await _mock.AttemptsAsync(id));
    }

    [Fact]
    public async Task Quotes_and_line_breaks_in_file_names_become_underscores()
    {
        using var client = Client(new List<double>());

        var result = await client.ConvertAsync(
            "txt",
            "pdf",
            UploadFile.FromBytes(Encoding.UTF8.GetBytes("x"), "say \"hi\"\r\n.txt"),
            new Dictionary<string, object?> { ["MockScenario"] = "echo", ["Note"] = "é \"quoted\" value" });

        var echo = JsonNode.Parse(result.File.GetBytes())!;
        Assert.Equal("say _hi___.txt", (string)echo["fields"]![0]!["filename"]!);
        Assert.Equal("é \"quoted\" value", (string)echo["fields"]![2]!["value"]!);
        Assert.Equal("application/octet-stream", (string)echo["fields"]![0]!["contentType"]!);
        Assert.Equal("application/json", (string)echo["headers"]!["accept"]!);
    }

    [Fact]
    public async Task Stored_outputs_stream_to_disk_and_save_into_a_folder()
    {
        using var client = Client(new List<double>());
        var dir = Path.Combine(Path.GetTempPath(), "pdfconversionapi-mock-" + Guid.NewGuid().ToString("N"));
        try
        {
            var result = await client.ConvertAsync(
                "txt",
                "pdf",
                UploadFile.FromPath(TestPaths.Fixture("hello.txt")),
                new Dictionary<string, object?> { ["MockScenario"] = "multi" },
                new ConvertOptions { StoreFile = true, FileName = "pages" });

            Assert.All(result.Files, f => Assert.True(f.Stored));
            var paths = await result.SaveAllAsync(dir);

            Assert.Equal(new[] { "pages-1.pdf", "pages-2.pdf", "pages-3.pdf" }, paths.Select(Path.GetFileName));
            Assert.Equal("page 2\n", await File.ReadAllTextAsync(paths[1]));

            var single = await result.Files[2].SaveAsync(dir);
            Assert.Equal(Path.Combine(dir, "pages-3.pdf"), single);

            foreach (var file in result.Files)
            {
                Assert.True(await client.DeleteStoredFileAsync(file.FileId!));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public async Task A_client_built_on_your_own_HttpClient_works()
    {
        using var http = new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
        using var client = new PdfConversionApiClient(http, new PdfConversionApiClientOptions { ApiKey = "test-key", BaseUrl = _mock.BaseUrl });

        var usage = await client.UsageAsync();
        var result = await client.ConvertFileAsync(TestPaths.Fixture("hello.txt"), "pdf");

        Assert.Equal("Free", usage.Plan);
        Assert.Equal("hello.pdf", result.File.Name);
    }

    [Fact]
    public async Task Several_calls_at_once_share_one_client()
    {
        using var client = Client(new List<double>());
        var inputs = new[] { "a.txt", "b.txt", "c.txt", "hello.txt" };

        var results = await Task.WhenAll(inputs.Select(name => client.ConvertAsync("txt", "pdf", UploadFile.FromPath(TestPaths.Fixture(name)))));

        for (var i = 0; i < inputs.Length; i++)
        {
            Assert.Equal(await File.ReadAllBytesAsync(TestPaths.Fixture(inputs[i])), results[i].File.GetBytes());
        }
    }

    private PdfConversionApiClient Client(List<double> sleeps) => new(new PdfConversionApiClientOptions
    {
        ApiKey = "test-key",
        BaseUrl = _mock.BaseUrl,
        Sleep = (wait, _) =>
        {
            sleeps.Add(wait.TotalSeconds);
            return Task.CompletedTask;
        },
    });
}
