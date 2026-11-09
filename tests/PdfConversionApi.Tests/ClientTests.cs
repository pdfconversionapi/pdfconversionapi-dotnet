using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Requests the client sends, its settings, and arguments it refuses before sending.</summary>
public sealed class ClientTests
{
    [Fact]
    public async Task A_conversion_sends_the_expected_request()
    {
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("report.pdf", new byte[] { 37, 80 })), "application/json", Fake.RequestId("r1"));
        using var client = Fake.Client(handler);

        var result = await client.ConvertAsync("DOCX", "Pdf", UploadFile.FromBytes(new byte[] { 1, 2 }, "report.docx"));

        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal(Fake.BaseUrl + "/v2/convert/docx/to/pdf", request.Uri.AbsoluteUri);
        Assert.Equal(string.Empty, request.Uri.Query);
        Assert.Equal("Bearer " + Fake.Key, request.Header("Authorization"));
        Assert.Equal("application/json", request.Header("Accept"));
        Assert.StartsWith("multipart/form-data; boundary=", request.ContentType, StringComparison.Ordinal);
        Assert.Matches(@"^pdfconversionapi-dotnet/0\.1\.0 dotnet/\d+\.\d+\.\d+", request.Header("User-Agent"));
        Assert.Equal("report.pdf", result.File.Name);
        Assert.Equal("r1", result.RequestId);
    }

    [Theory]
    [InlineData("../etc")]
    [InlineData("")]
    [InlineData("pdf/to")]
    [InlineData("pdf?x=1")]
    [InlineData("abcdefghijklmnopqrstu")]
    [InlineData("pdf ")]
    [InlineData("pdé")]
    public async Task Invalid_format_names_are_refused_before_sending(string format)
    {
        var handler = new FakeHandler();
        using var client = Fake.Client(handler);
        var file = UploadFile.FromBytes(new byte[1]);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ConvertAsync(format, "pdf", file));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ConvertAsync("pdf", format, file));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Twenty_characters_with_hyphens_are_a_valid_format()
    {
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("a.pdf", new byte[1])));
        using var client = Fake.Client(handler);

        await client.ConvertAsync("pdf", "Image-Watermark-1234", UploadFile.FromBytes(new byte[1]));

        Assert.EndsWith("/v2/convert/pdf/to/image-watermark-1234", handler.Requests[0].Uri.AbsoluteUri, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_null_file_is_refused_before_sending()
    {
        var handler = new FakeHandler();
        using var client = Fake.Client(handler);

        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ConvertAsync("pdf", "merge", new UploadFile[] { null! }));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ConvertAsync("pdf", "pdf", (UploadFile)null!));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task No_files_null_files_and_an_empty_list_all_send_no_file_field()
    {
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("qrcode.svg", new byte[1])));
        using var client = Fake.Client(handler);
        var data = new Dictionary<string, object?> { ["Data"] = "hello" };

        await client.ConvertAsync("data", "qrcode", data);
        await client.ConvertAsync("data", "qrcode", parameters: data);
        await client.ConvertAsync("data", "qrcode", (IEnumerable<UploadFile>?)null, data);
        await client.ConvertAsync("data", "qrcode", Array.Empty<UploadFile>(), data);

        Assert.Equal(4, handler.Requests.Count);
        Assert.All(handler.Requests, r =>
        {
            Assert.EndsWith("/v2/convert/data/to/qrcode", r.Uri.AbsoluteUri, StringComparison.Ordinal);
            Assert.DoesNotContain("filename=", r.BodyText, StringComparison.Ordinal);
            Assert.Contains("name=\"Data\"\r\n\r\nhello\r\n", r.BodyText, StringComparison.Ordinal);
        });
    }

    [Fact]
    public async Task ConvertFile_takes_the_source_format_from_the_extension()
    {
        var path = Path.Combine(Path.GetTempPath(), "pdfconversionapi-test-" + Guid.NewGuid().ToString("N") + ".DOCX");
        await File.WriteAllBytesAsync(path, new byte[] { 9, 9 });
        try
        {
            var handler = new FakeHandler().Then(200, Fake.ConversionJson(("x.pdf", new byte[1])));
            using var client = Fake.Client(handler);

            await client.ConvertFileAsync(path, "pdf");
            await client.ConvertFileAsync(new FileInfo(path), "pdf");

            Assert.All(handler.Requests, r => Assert.EndsWith("/v2/convert/docx/to/pdf", r.Uri.AbsoluteUri, StringComparison.Ordinal));
            Assert.Contains("filename=\"" + Path.GetFileName(path) + "\"", handler.Requests[0].BodyText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("report")]
    [InlineData("report.")]
    [InlineData("folder.v2/report")]
    [InlineData("")]
    public async Task ConvertFile_without_an_extension_is_an_argument_error(string path)
    {
        using var client = Fake.Client(new FakeHandler());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.ConvertFileAsync(path, "pdf"));
    }

    [Fact]
    public async Task A_missing_input_file_is_reported_before_sending()
    {
        var handler = new FakeHandler();
        using var client = Fake.Client(handler);
        await Assert.ThrowsAsync<FileNotFoundException>(() => client.ConvertFileAsync("/no/such/file.docx", "pdf"));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Calls_that_need_a_key_raise_MissingApiKey_before_sending()
    {
        var handler = new FakeHandler();
        using var client = EnvironmentScope.WithoutVariable(
            PdfConversionApiClient.ApiKeyEnvironmentVariable,
            () => new PdfConversionApiClient(new HttpClient(handler), new PdfConversionApiClientOptions { BaseUrl = Fake.BaseUrl }));

        Assert.False(client.HasApiKey);
        await Assert.ThrowsAsync<MissingApiKeyException>(() => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1])));
        await Assert.ThrowsAsync<MissingApiKeyException>(() => client.UsageAsync());
        await Assert.ThrowsAsync<MissingApiKeyException>(() => client.DeleteStoredFileAsync(new string('a', 32)));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Conversions_and_downloads_work_without_a_key_and_never_send_one()
    {
        var handler = new FakeHandler()
            .Then(200, "[{\"From\":\"docx\",\"To\":\"pdf\",\"Title\":\"DOCX to PDF\",\"Category\":\"Document\",\"Inputs\":\"one\"}]")
            .Then(200, "file bytes", "application/pdf");
        using var client = Fake.Client(handler);

        var conversions = await client.ConversionsAsync();
        var bytes = await client.DownloadAsync(Fake.BaseUrl + "/v2/d/" + new string('a', 32) + "/report.pdf");

        Assert.Equal("docx", conversions[0].From);
        Assert.Equal("file bytes", Encoding.UTF8.GetString(bytes));
        Assert.All(handler.Requests, r => Assert.Null(r.Header("Authorization")));
        Assert.Equal(Fake.BaseUrl + "/v2/info", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Null(handler.Requests[1].Header("Accept"));
    }

    [Fact]
    public void The_key_and_base_url_come_from_the_environment_when_not_given()
    {
        var client = EnvironmentScope.WithVariable(
            PdfConversionApiClient.ApiKeyEnvironmentVariable,
            "env-key-0123456789abcdef",
            () => EnvironmentScope.WithVariable(
                PdfConversionApiClient.BaseUrlEnvironmentVariable,
                "http://127.0.0.1:9/prefix/",
                () => new PdfConversionApiClient()));
        using (client)
        {
            Assert.True(client.HasApiKey);
            Assert.Equal("http://127.0.0.1:9/prefix", client.BaseUrl);
        }
    }

    [Fact]
    public void The_default_base_url_is_the_public_api()
    {
        using var client = EnvironmentScope.WithoutVariable(PdfConversionApiClient.BaseUrlEnvironmentVariable, () => new PdfConversionApiClient("k-0123456789abcdef"));
        Assert.Equal("https://api.pdfconversionapi.com", client.BaseUrl);
    }

    [Fact]
    public async Task A_trailing_slash_in_the_base_url_is_ignored()
    {
        var handler = new FakeHandler().Then(200, "[]");
        using var client = Fake.Client(handler, configure: o => o.BaseUrl = Fake.BaseUrl + "///");
        await client.ConversionsAsync();
        Assert.Equal(Fake.BaseUrl + "/v2/info", handler.Requests[0].Uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("not a url")]
    [InlineData("/relative")]
    [InlineData("https://example.com/?q=1")]
    public void A_bad_base_url_is_an_argument_error(string url)
    {
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { ApiKey = "k", BaseUrl = url }));
    }

    [Fact]
    public void Bad_settings_are_argument_errors()
    {
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { MaxRetries = -1 }));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { Timeout = TimeSpan.Zero }));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { ConnectTimeout = TimeSpan.FromSeconds(-1) }));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { MaxRetryWait = TimeSpan.FromSeconds(-1) }));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { ApiKey = "bad\nkey" }));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient((PdfConversionApiClientOptions)null!));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient((HttpClient)null!));
        Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient((string)null!));
    }

    [Fact]
    public async Task A_bad_key_character_is_reported_without_the_key()
    {
        var e = Assert.ThrowsAny<ArgumentException>(() => new PdfConversionApiClient(new PdfConversionApiClientOptions { ApiKey = "secret-part\u0001" }));
        Assert.DoesNotContain("secret-part", e.ToString(), StringComparison.Ordinal);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task The_key_never_appears_in_strings_or_errors()
    {
        var handler = new FakeHandler()
            .ThenJsonError(401, "a valid API key is required (Authorization: Bearer <key>)")
            .ThenThrow(new HttpRequestException("connection refused"));
        using var client = Fake.Client(handler, configure: o => o.MaxRetries = 0);

        var auth = await Assert.ThrowsAsync<AuthenticationException>(() => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1])));
        var connection = await Assert.ThrowsAsync<ApiConnectionException>(() => client.UsageAsync());

        foreach (var text in new[] { auth.ToString(), auth.Body, connection.ToString(), client.ToString(), new PdfConversionApiClientOptions { ApiKey = Fake.Key }.ToString() ?? string.Empty })
        {
            Assert.DoesNotContain(Fake.Key, text, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("0123456789abcdef0123456789abcde")]
    [InlineData("0123456789abcdef0123456789abcdef0")]
    [InlineData("0123456789abcdef0123456789abcdeg")]
    [InlineData("../../v2/usage/aaaaaaaaaaaaaaaaaa")]
    public async Task Delete_refuses_an_id_that_is_not_32_hex_characters(string id)
    {
        var handler = new FakeHandler();
        using var client = Fake.Client(handler);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DeleteStoredFileAsync(id));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Delete_returns_true_on_204_and_false_on_404()
    {
        var id = "0123456789ABCDEF0123456789abcdef";
        var handler = new FakeHandler().Then(204, string.Empty, null).ThenJsonError(404, "not found");
        using var client = Fake.Client(handler);

        Assert.True(await client.DeleteStoredFileAsync(id));
        Assert.False(await client.DeleteStoredFileAsync(id));

        Assert.All(handler.Requests, r =>
        {
            Assert.Equal(HttpMethod.Delete, r.Method);
            Assert.Equal(Fake.BaseUrl + "/v2/d/" + id, r.Uri.AbsoluteUri);
            Assert.Equal("Bearer " + Fake.Key, r.Header("Authorization"));
        });
    }

    [Fact]
    public async Task Delete_raises_other_errors()
    {
        var handler = new FakeHandler().ThenJsonError(401, "a valid API key is required (Authorization: Bearer <key>)");
        using var client = Fake.Client(handler);
        await Assert.ThrowsAsync<AuthenticationException>(() => client.DeleteStoredFileAsync(new string('f', 32)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("v2/d/x/y.pdf")]
    [InlineData("file:///etc/passwd")]
    public async Task Downloads_need_an_absolute_http_url(string url)
    {
        using var client = Fake.Client(new FakeHandler());
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DownloadAsync(url));
        await Assert.ThrowsAnyAsync<ArgumentException>(() => client.DownloadToAsync(url, "x"));
    }

    [Fact]
    public async Task A_download_redirected_to_another_host_is_refused()
    {
        var handler = new FakeHandler().Then((request, _) =>
        {
            var moved = new HttpRequestMessage(HttpMethod.Get, "https://elsewhere.example/file.pdf");
            return Task.FromResult(FakeHandler.Response(moved, 200, new byte[] { 1 }, "application/pdf"));
        });
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAsync<PdfConversionApiException>(() => client.DownloadAsync(Fake.BaseUrl + "/v2/d/" + new string('a', 32) + "/x.pdf"));
        Assert.Contains("another host", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_redirect_status_is_not_followed()
    {
        var handler = new FakeHandler().Then(302, string.Empty, null, ("Location", "https://elsewhere.example/x"));
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAsync<ApiException>(() => client.DownloadAsync(Fake.BaseUrl + "/v2/d/" + new string('a', 32) + "/x.pdf"));
        Assert.Equal(302, e.StatusCode);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public void The_version_constant_matches_the_assembly()
    {
        var informational = typeof(PdfConversionApiClient).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
        Assert.Equal(PdfConversionApiClient.Version, informational.Split('+')[0]);
    }

    [Fact]
    public void The_tests_run_against_the_intended_library_build()
    {
        var framework = typeof(PdfConversionApiClient).Assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName;
#if LIBRARY_NETSTANDARD
        Assert.Equal(".NETStandard,Version=v2.0", framework);
#else
        Assert.Equal(".NETCoreApp,Version=v8.0", framework);
#endif
    }

    [Fact]
    public async Task The_client_can_be_shared_between_tasks()
    {
        var handler = new FakeHandler().Then((request, _) =>
        {
            var json = Fake.ConversionJson(("out.pdf", Encoding.UTF8.GetBytes(request.RequestUri!.AbsolutePath)));
            return Task.FromResult(FakeHandler.Response(request, 200, Encoding.UTF8.GetBytes(json), "application/json"));
        });
        using var client = Fake.Client(handler);

        var formats = Enumerable.Range(0, 20).Select(i => "f" + i).ToList();
        var results = await Task.WhenAll(formats.Select(f => client.ConvertAsync(f, "pdf", UploadFile.FromBytes(new byte[1]))));

        for (var i = 0; i < formats.Count; i++)
        {
            Assert.Equal("/v2/convert/" + formats[i] + "/to/pdf", Encoding.UTF8.GetString(results[i].File.GetBytes()));
        }
    }
}
