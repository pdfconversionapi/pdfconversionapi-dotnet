using System;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Internal;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Error messages and the status-to-exception mapping (SPEC section 9).</summary>
public sealed class ErrorTests
{
    [Theory]
    [InlineData("{\"Code\":400,\"Message\":\"PageRange '3-1' is not valid (example: 1-3,5)\"}", "application/json", "PageRange '3-1' is not valid (example: 1-3,5)")]
    [InlineData("{\"Code\":400,\"Message\":\"  kept as is  \"}", "text/html", "  kept as is  ")]
    [InlineData("", "application/json", "empty response")]
    [InlineData("  \r\n", "text/plain", "empty response")]
    [InlineData("  Invalid `boundary` for `multipart/form-data` request\n", "text/plain", "Invalid `boundary` for `multipart/form-data` request")]
    [InlineData("<html><body>413</body></html>", "text/html", "non-JSON response (text/html)")]
    [InlineData("{\"error\":\"x\"}", "application/json", "non-JSON response (application/json)")]
    [InlineData("{\"Message\":42}", "application/json", "non-JSON response (application/json)")]
    [InlineData("oops", null, "non-JSON response (no content type)")]
    [InlineData("{not json", "application/json", "non-JSON response (application/json)")]
    public void The_message_follows_the_spec(string body, string? mediaType, string expected)
    {
        Assert.Equal(expected, ResponseParser.ErrorMessage(Encoding.UTF8.GetBytes(body), mediaType));
    }

    [Fact]
    public void A_long_plain_text_body_is_not_used_as_the_message()
    {
        Assert.Equal(new string('x', 1000), ResponseParser.ErrorMessage(Encoding.UTF8.GetBytes(new string('x', 1000)), "text/plain"));
        Assert.Equal("non-JSON response (text/plain)", ResponseParser.ErrorMessage(Encoding.UTF8.GetBytes(new string('x', 1001)), "text/plain"));
    }

    [Fact]
    public void The_string_form_adds_the_status_and_request_id()
    {
        var withId = new RateLimitException("too many requests; slow down", "1a2b", TimeSpan.FromSeconds(10));
        Assert.Equal("HTTP 429: too many requests; slow down (request id: 1a2b)", withId.Message);
        Assert.StartsWith("PdfConversionApi.RateLimitException: HTTP 429: too many requests; slow down (request id: 1a2b)", withId.ToString(), StringComparison.Ordinal);

        var withoutId = new PayloadTooLargeException("non-JSON response (text/html)");
        Assert.Equal("HTTP 413: non-JSON response (text/html)", withoutId.Message);
        Assert.Null(withoutId.RequestId);
    }

    [Theory]
    [InlineData(400, typeof(BadRequestException))]
    [InlineData(401, typeof(AuthenticationException))]
    [InlineData(403, typeof(PermissionDeniedException))]
    [InlineData(404, typeof(NotFoundException))]
    [InlineData(413, typeof(PayloadTooLargeException))]
    [InlineData(422, typeof(UnprocessableEntityException))]
    [InlineData(429, typeof(RateLimitException))]
    [InlineData(500, typeof(InternalServerErrorException))]
    [InlineData(503, typeof(ServiceUnavailableException))]
    [InlineData(504, typeof(GatewayTimeoutException))]
    [InlineData(418, typeof(ApiException))]
    [InlineData(502, typeof(ApiException))]
    public async Task Each_status_raises_its_exception(int status, Type expected)
    {
        var handler = new FakeHandler().ThenJsonError(status, "message " + status, ("Retry-After", "100"));
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAnyAsync<ApiException>(() => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1])));

        Assert.Equal(expected, e.GetType());
        Assert.IsAssignableFrom<PdfConversionApiException>(e);
        Assert.Equal(status, e.StatusCode);
        Assert.Equal("message " + status, e.ApiMessage);
        Assert.Equal("req-" + status, e.RequestId);
        Assert.Contains("\"Message\"", e.Body, StringComparison.Ordinal);
        if (status is 429 or 503)
        {
            Assert.Equal(TimeSpan.FromSeconds(100), e.RetryAfter);
        }
        else
        {
            Assert.Null(e.RetryAfter);
        }
    }

    [Fact]
    public async Task An_edge_network_page_has_no_request_id()
    {
        var handler = new FakeHandler().Then(413, "<html><body><h1>413 Request Entity Too Large</h1></body></html>", "text/html; charset=utf-8");
        using var client = Fake.Client(handler);

        var e = await Assert.ThrowsAsync<PayloadTooLargeException>(() => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1])));

        Assert.Equal("non-JSON response (text/html)", e.ApiMessage);
        Assert.Null(e.RequestId);
        Assert.Equal("HTTP 413: non-JSON response (text/html)", e.Message);
        Assert.Contains("<h1>", e.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_exception_derives_from_the_base()
    {
        Assert.IsAssignableFrom<PdfConversionApiException>(new MissingApiKeyException());
        Assert.IsAssignableFrom<PdfConversionApiException>(new StoredFileException("x"));
        Assert.IsAssignableFrom<PdfConversionApiException>(new ApiConnectionException("x", null));
        Assert.IsAssignableFrom<PdfConversionApiException>(new ApiTimeoutException("x", null));
        Assert.IsAssignableFrom<PdfConversionApiException>(new InvalidResponseException("x"));
        Assert.IsAssignableFrom<PdfConversionApiException>(new ApiException(418, "x"));
        Assert.Contains("PDFCONVERSIONAPI_KEY", new MissingApiKeyException().Message, StringComparison.Ordinal);
    }
}
