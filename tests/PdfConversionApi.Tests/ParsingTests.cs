using System;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Reading 200 responses: conversions, stored files, usage and the list of conversions.</summary>
public sealed class ParsingTests
{
    [Fact]
    public async Task Inline_files_are_decoded_in_order_with_their_fields()
    {
        var handler = new FakeHandler().Then(
            200,
            "{\"ConversionCost\":1,\"ConversionTime\":0.036,\"Files\":["
            + "{\"FileName\":\"season-guide-1.jpg\",\"FileExt\":\"jpg\",\"FileSize\":3,\"FileData\":\"AQID\"},"
            + "{\"FileName\":\"season-guide-2.jpg\",\"FileExt\":\"jpg\",\"FileSize\":2,\"FileData\":\"BAU=\"}]}",
            "application/json",
            Fake.RequestId("abc123"));
        using var client = Fake.Client(handler);

        var result = await client.ConvertAsync("pdf", "jpg", UploadFile.FromBytes(new byte[1], "season-guide.pdf"));

        Assert.Equal(1, result.ConversionCost);
        Assert.Equal(0.036, result.ConversionTime);
        Assert.Equal("abc123", result.RequestId);
        Assert.Equal(2, result.Files.Count);
        Assert.Same(result.Files[0], result.File);
        Assert.Equal("season-guide-1.jpg", result.Files[0].Name);
        Assert.Equal("jpg", result.Files[0].Extension);
        Assert.Equal(3, result.Files[0].Size);
        Assert.False(result.Files[0].Stored);
        Assert.Null(result.Files[0].Url);
        Assert.Null(result.Files[0].FileId);
        Assert.Equal(new byte[] { 1, 2, 3 }, result.Files[0].GetBytes());
        Assert.Equal(new byte[] { 4, 5 }, await result.Files[1].DownloadAsync());
        Assert.Equal("OutputFile(season-guide-1.jpg, 3 bytes)", result.Files[0].ToString());
    }

    [Theory]
    [InlineData("{not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"Files\":{}}")]
    [InlineData("{\"Files\":[1]}")]
    [InlineData("{\"Files\":[{\"FileName\":\"a.pdf\",\"FileSize\":3,\"FileData\":\"not base64!\"}]}")]
    [InlineData("{\"Files\":[{\"FileName\":\"a.pdf\",\"FileSize\":4,\"FileData\":\"AQID\"}]}")]
    [InlineData("{\"Files\":[{\"FileName\":\"a.pdf\",\"FileData\":\"AQID\"}]}")]
    [InlineData("{\"Files\":[{\"FileSize\":3,\"FileData\":\"AQID\"}]}")]
    [InlineData("{\"Files\":[{\"FileName\":\"a.pdf\",\"FileSize\":3}]}")]
    [InlineData("{\"Files\":[{\"FileName\":\"a.pdf\",\"FileSize\":3,\"Url\":\"https://x/y\"}]}")]
    public async Task A_body_that_cannot_be_used_is_InvalidResponse(string body)
    {
        var handler = new FakeHandler().Then(200, body, "application/json", Fake.RequestId("rid-9"));
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAsync<InvalidResponseException>(() => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1])));

        Assert.Equal("rid-9", e.RequestId);
        Assert.Contains("(request id: rid-9)", e.Message, StringComparison.Ordinal);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_missing_FileExt_is_taken_from_the_name()
    {
        var handler = new FakeHandler().Then(200, "{\"Files\":[{\"FileName\":\"Report.PDF\",\"FileSize\":1,\"FileData\":\"AA==\"}]}");
        using var client = Fake.Client(handler);

        var result = await client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]));

        Assert.Equal("pdf", result.File.Extension);
        Assert.Equal(0, result.ConversionCost);
    }

    [Fact]
    public async Task Stored_files_have_a_link_and_an_id_instead_of_data()
    {
        var id = "32001ff088d5c35e10e4bc6f6ddf09a9";
        var url = Fake.BaseUrl + "/v2/d/" + id + "/season%20guide-1.jpg";
        var handler = new FakeHandler()
            .Then(200, "{\"ConversionCost\":1,\"ConversionTime\":0.04,\"Files\":[{\"FileName\":\"season guide-1.jpg\",\"FileExt\":\"jpg\",\"FileSize\":5,\"Url\":\"" + url + "\",\"FileId\":\"" + id + "\"}]}")
            .Then(200, "JPEG!", "image/jpeg");
        using var client = Fake.Client(handler);

        var result = await client.ConvertAsync("pdf", "jpg", UploadFile.FromBytes(new byte[1]), options: new ConvertOptions { StoreFile = true });
        var file = result.File;

        Assert.True(file.Stored);
        Assert.Equal(url, file.Url);
        Assert.Equal(id, file.FileId);
        var e = Assert.Throws<StoredFileException>(() => file.GetBytes());
        Assert.Contains("DownloadAsync", e.Message, StringComparison.Ordinal);
        Assert.Equal("OutputFile(season guide-1.jpg, 5 bytes, stored)", file.ToString());
        Assert.DoesNotContain(id, file.ToString(), StringComparison.Ordinal);

        Assert.Equal("JPEG!", Encoding.UTF8.GetString(await file.DownloadAsync()));
        var download = handler.Requests[1];
        Assert.Equal(url, download.Uri.AbsoluteUri);
        Assert.Null(download.Header("Authorization"));
        Assert.Contains("StoreFile", handler.Requests[0].BodyText, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Usage_is_read_with_dates_in_utc()
    {
        var handler = new FakeHandler().Then(
            200,
            "{\"Plan\":\"Pro\",\"Included\":5000,\"Used\":12,\"Remaining\":null,\"Overage\":false,\"Running\":1,"
            + "\"ConcurrentLimit\":4,\"MaxUploadMB\":100,\"PeriodStart\":\"2026-11-01T00:00:00.000Z\",\"PeriodEnd\":\"2026-12-01T05:30:00+05:30\"}");
        using var client = Fake.Client(handler);

        var usage = await client.UsageAsync();

        Assert.Equal("Pro", usage.Plan);
        Assert.Equal(5000, usage.Included);
        Assert.Equal(12, usage.Used);
        Assert.Null(usage.Remaining);
        Assert.False(usage.Overage);
        Assert.Equal(1, usage.Running);
        Assert.Equal(4, usage.ConcurrentLimit);
        Assert.Equal(100, usage.MaxUploadMB);
        Assert.Equal(new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), usage.PeriodStart);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 0, 0, 0, TimeSpan.Zero), usage.PeriodEnd);
        Assert.Equal(TimeSpan.Zero, usage.PeriodEnd.Offset);
        Assert.Equal(Fake.BaseUrl + "/v2/usage", handler.Requests[0].Uri.AbsoluteUri);
        Assert.Equal("Bearer " + Fake.Key, handler.Requests[0].Header("Authorization"));
    }

    [Theory]
    [InlineData("{\"Plan\":\"Free\"}")]
    [InlineData("[]")]
    [InlineData("{\"Plan\":\"Free\",\"Included\":\"250\",\"Used\":1,\"Overage\":false,\"Running\":0,\"ConcurrentLimit\":1,\"MaxUploadMB\":10,\"PeriodStart\":\"2026-11-01T00:00:00Z\",\"PeriodEnd\":\"2026-12-01T00:00:00Z\"}")]
    [InlineData("{\"Plan\":\"Free\",\"Included\":250,\"Used\":1,\"Overage\":false,\"Running\":0,\"ConcurrentLimit\":1,\"MaxUploadMB\":10,\"PeriodStart\":\"soon\",\"PeriodEnd\":\"2026-12-01T00:00:00Z\"}")]
    public async Task A_usage_body_that_cannot_be_used_is_InvalidResponse(string body)
    {
        using var client = Fake.Client(new FakeHandler().Then(200, body));
        await Assert.ThrowsAsync<InvalidResponseException>(() => client.UsageAsync());
    }

    [Fact]
    public async Task Conversions_are_listed()
    {
        var handler = new FakeHandler().Then(
            200,
            "[{\"From\":\"pdf\",\"To\":\"merge\",\"Title\":\"PDF to MERGE\",\"Category\":\"PDF Tools\",\"Inputs\":\"many\"},"
            + "{\"From\":\"data\",\"To\":\"barcode\",\"Title\":\"DATA to BARCODE\",\"Category\":\"Barcode\",\"Inputs\":\"optional\"}]");
        using var client = Fake.Client(handler);

        var list = await client.ConversionsAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal("pdf", list[0].From);
        Assert.Equal("merge", list[0].To);
        Assert.Equal("PDF to MERGE", list[0].Title);
        Assert.Equal("PDF Tools", list[0].Category);
        Assert.Equal("many", list[0].Inputs);
        Assert.Equal("data/to/barcode", list[1].ToString());
    }

    [Fact]
    public async Task Large_outputs_are_decoded()
    {
        var data = new byte[12 * 1024 * 1024];
        new Random(7).NextBytes(data);
        var handler = new FakeHandler().Then(200, Fake.ConversionJson(("big.pdf", data)));
        using var client = Fake.Client(handler);

        var result = await client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]));

        Assert.Equal(data, result.File.GetBytes());
    }
}
