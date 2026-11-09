using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Internal;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Names on disk, saving and downloading to files.</summary>
public sealed class FileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "pdfconversionapi-files-" + Guid.NewGuid().ToString("N"));

    public FileTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        Directory.Delete(_dir, recursive: true);
    }

    [Theory]
    [InlineData("report.pdf", "report.pdf")]
    [InlineData("../../evil.pdf", "evil.pdf")]
    [InlineData("/etc/passwd", "passwd")]
    [InlineData("a\\b\\c.pdf", "c.pdf")]
    [InlineData("Résumé final.pdf", "Résumé final.pdf")]
    [InlineData("..pdf", "..pdf")]
    public void Output_names_keep_only_their_own_name(string name, string expected)
    {
        Assert.Equal(expected, FileNames.SafeOutputName(name));
    }

    [Theory]
    [InlineData("")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("folder/")]
    [InlineData("a/..")]
    [InlineData("nul\0.pdf")]
    public void Names_that_are_not_a_file_are_refused(string name)
    {
        Assert.Throws<InvalidResponseException>(() => FileNames.SafeOutputName(name));
    }

    [Fact]
    public async Task SaveAll_creates_the_folder_writes_every_file_and_overwrites()
    {
        var target = Path.Combine(_dir, "out", "nested");
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("p-1.jpg", Bytes("one")), ("../p-2.jpg", Bytes("two"))));
        using var client = Fake.Client(handler);
        var result = await client.ConvertAsync("pdf", "jpg", UploadFile.FromBytes(new byte[1]));

        Directory.CreateDirectory(target);
        await File.WriteAllTextAsync(Path.Combine(target, "p-1.jpg"), "old content that is longer");
        var paths = await result.SaveAllAsync(target);

        Assert.Equal(new[] { Path.Combine(target, "p-1.jpg"), Path.Combine(target, "p-2.jpg") }, paths);
        Assert.Equal("one", await File.ReadAllTextAsync(paths[0]));
        Assert.Equal("two", await File.ReadAllTextAsync(paths[1]));
        Assert.Equal(new[] { "p-1.jpg", "p-2.jpg" }, Directory.GetFiles(target).Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal));
        Assert.False(File.Exists(Path.Combine(_dir, "out", "p-2.jpg")));
    }

    [Fact]
    public async Task Save_writes_inside_an_existing_folder_or_to_the_path_given()
    {
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("report.pdf", Bytes("pdf"))));
        using var client = Fake.Client(handler);
        var file = (await client.ConvertAsync("docx", "pdf", UploadFile.FromBytes(new byte[1]))).File;

        var intoFolder = await file.SaveAsync(_dir);
        var asFile = await file.SaveAsync(Path.Combine(_dir, "renamed.pdf"));

        Assert.Equal(Path.Combine(_dir, "report.pdf"), intoFolder);
        Assert.Equal(Path.Combine(_dir, "renamed.pdf"), asFile);
        Assert.Equal("pdf", await File.ReadAllTextAsync(intoFolder));
        Assert.Equal("pdf", await File.ReadAllTextAsync(asFile));
    }

    [Fact]
    public async Task Stored_files_are_streamed_to_disk_without_the_key()
    {
        var id = new string('c', 32);
        var handler = new FakeHandler()
            .Then(200, "{\"ConversionCost\":1,\"ConversionTime\":0.1,\"Files\":[{\"FileName\":\"big.pdf\",\"FileExt\":\"pdf\",\"FileSize\":7,\"Url\":\"" + Fake.BaseUrl + "/v2/d/" + id + "/big.pdf\",\"FileId\":\"" + id + "\"}]}")
            .Then(200, "content", "application/pdf");
        using var client = Fake.Client(handler);
        var result = await client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]), options: new ConvertOptions { StoreFile = true });

        var paths = await result.SaveAllAsync(_dir);
        var again = await result.File.DownloadToAsync(Path.Combine(_dir, "copy.pdf"));

        Assert.Equal("content", await File.ReadAllTextAsync(paths[0]));
        Assert.Equal("content", await File.ReadAllTextAsync(again));
        Assert.All(handler.Requests.Skip(1), r => Assert.Null(r.Header("Authorization")));
    }

    [Fact]
    public async Task A_failed_download_leaves_no_partial_file()
    {
        var path = Path.Combine(_dir, "partial.pdf");
        var handler = new FakeHandler().Then((request, _) =>
        {
            var response = FakeHandler.Response(request, 200, Array.Empty<byte>(), "application/pdf");
            response.Content = new StreamContent(new BrokenAfter(Bytes("half")));
            return Task.FromResult(response);
        });
        using var client = Fake.Client(handler, configure: o => o.MaxRetries = 1);

        await Assert.ThrowsAsync<ApiConnectionException>(() => client.DownloadToAsync(Fake.BaseUrl + "/v2/d/" + new string('d', 32) + "/x.pdf", path));

        Assert.False(File.Exists(path));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_download_error_status_is_raised_and_writes_nothing()
    {
        var path = Path.Combine(_dir, "missing.pdf");
        var handler = new FakeHandler().ThenJsonError(404, "not found");
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAsync<NotFoundException>(() => client.DownloadToAsync(Fake.BaseUrl + "/v2/d/" + new string('d', 32) + "/x.pdf", path));

        Assert.Equal("not found", e.ApiMessage);
        Assert.False(File.Exists(path));
    }

    private static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    private sealed class BrokenAfter : MemoryStream
    {
        public BrokenAfter(byte[] data)
            : base(data)
        {
        }

        public override bool CanSeek => false;

        public override int Read(byte[] buffer, int offset, int count)
        {
            var n = base.Read(buffer, offset, count);
            return n > 0 ? n : throw new IOException("Connection reset by peer");
        }

        public override System.Threading.Tasks.Task<int> ReadAsync(byte[] buffer, int offset, int count, System.Threading.CancellationToken cancellationToken)
            => System.Threading.Tasks.Task.FromResult(Read(buffer, offset, count));

        public override System.Threading.Tasks.ValueTask<int> ReadAsync(Memory<byte> buffer, System.Threading.CancellationToken cancellationToken = default)
        {
            var array = new byte[buffer.Length];
            var n = Read(array, 0, array.Length);
            array.AsSpan(0, n).CopyTo(buffer.Span);
            return new System.Threading.Tasks.ValueTask<int>(n);
        }
    }
}
