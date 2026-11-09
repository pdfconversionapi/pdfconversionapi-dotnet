using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>The retry loop of SPEC section 8.</summary>
public sealed class RetryTests
{
    private static readonly byte[] Output = Encoding.UTF8.GetBytes("%PDF");

    private static string Ok => Fake.ConversionJson(("out.pdf", Output));

    [Theory]
    [InlineData(429)]
    [InlineData(503)]
    public async Task Waits_Retry_After_plus_jitter_then_retries(int status)
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenJsonError(status, "busy", ("Retry-After", "3")).Then(200, Ok);
        using var client = Fake.Client(handler, sleeps);

        var result = await Convert(client);

        Assert.Equal("out.pdf", result.File.Name);
        Assert.Equal(2, handler.Requests.Count);
        var wait = Assert.Single(sleeps);
        Assert.InRange(wait, 3, 3 + 2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("soon")]
    [InlineData("-5")]
    [InlineData("Wed, 21 Oct 2026 07:28:00 GMT")]
    public async Task A_missing_or_unreadable_Retry_After_means_one_second(string? header)
    {
        var sleeps = new List<double>();
        var headers = header is null ? Array.Empty<(string, string)>() : new[] { ("Retry-After", header) };
        var handler = new FakeHandler().ThenJsonError(503, "busy", headers).ThenJsonError(503, "busy", headers);
        using var client = Fake.Client(handler, sleeps, o => o.MaxRetries = 1);

        var e = await Assert.ThrowsAsync<ServiceUnavailableException>(() => Convert(client));

        Assert.Equal(TimeSpan.FromSeconds(1), e.RetryAfter);
        Assert.InRange(Assert.Single(sleeps), 1, 1 + 2);
    }

    [Fact]
    public async Task A_Retry_After_longer_than_MaxRetryWait_is_raised_at_once()
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenJsonError(429, "used up", ("Retry-After", "61"));
        using var client = Fake.Client(handler, sleeps);

        var e = await Assert.ThrowsAsync<RateLimitException>(() => Convert(client));

        Assert.Equal(TimeSpan.FromSeconds(61), e.RetryAfter);
        Assert.Single(handler.Requests);
        Assert.Empty(sleeps);
    }

    [Fact]
    public async Task MaxRetryWait_can_be_raised()
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenJsonError(503, "paused", ("Retry-After", "600")).Then(200, Ok);
        using var client = Fake.Client(handler, sleeps, o => o.MaxRetryWait = TimeSpan.FromMinutes(15));

        await Convert(client);

        Assert.InRange(Assert.Single(sleeps), 600, 602);
    }

    [Fact]
    public async Task A_500_is_retried_once_per_call()
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenJsonError(500, "internal error").ThenJsonError(503, "busy", ("Retry-After", "1")).ThenJsonError(500, "internal error");
        using var client = Fake.Client(handler, sleeps);

        var e = await Assert.ThrowsAsync<InternalServerErrorException>(() => Convert(client));

        Assert.Equal("internal error", e.ApiMessage);
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, sleeps.Count);
        Assert.InRange(sleeps[0], 1, 1 + 2);
    }

    [Theory]
    [InlineData(400)]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(413)]
    [InlineData(422)]
    [InlineData(504)]
    [InlineData(502)]
    public async Task Other_errors_are_not_retried(int status)
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenJsonError(status, "no", ("Retry-After", "1"));
        using var client = Fake.Client(handler, sleeps);

        var e = await Assert.ThrowsAnyAsync<ApiException>(() => Convert(client));

        Assert.Equal(status, e.StatusCode);
        Assert.Null(e.RetryAfter);
        Assert.Single(handler.Requests);
        Assert.Empty(sleeps);
    }

    [Fact]
    public async Task No_response_is_retried_with_growing_jitter_then_raised()
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenThrow(new HttpRequestException("Connection refused"));
        using var client = Fake.Client(handler, sleeps);

        var e = await Assert.ThrowsAsync<ApiConnectionException>(() => Convert(client));

        Assert.Equal(5, handler.Requests.Count);
        Assert.Equal(4, sleeps.Count);
        for (var i = 0; i < sleeps.Count; i++)
        {
            Assert.InRange(sleeps[i], 0, Math.Pow(2, i + 1));
        }

        Assert.IsType<HttpRequestException>(e.InnerException);
        Assert.Contains("5 attempts", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Jitter_stays_below_two_to_the_attempt()
    {
        var sleeps = new List<double>();
        for (var run = 0; run < 25; run++)
        {
            var handler = new FakeHandler().ThenThrow(new HttpRequestException("reset"));
            using var client = Fake.Client(handler, sleeps);
            await Assert.ThrowsAsync<ApiConnectionException>(() => Convert(client));
        }

        var byAttempt = sleeps.Select((wait, i) => (wait, attempt: (i % 4) + 1)).ToList();
        Assert.All(byAttempt, s => Assert.InRange(s.wait, 0, Math.Pow(2, s.attempt) - 1e-9));
        Assert.True(byAttempt.Where(s => s.attempt == 4).Max(s => s.wait) > 2, "jitter for attempt 4 should reach past 2 seconds sometimes");
    }

    [Fact]
    public async Task MaxRetries_zero_sends_once()
    {
        var handler = new FakeHandler().ThenThrow(new HttpRequestException("reset"));
        using var client = Fake.Client(handler, configure: o => o.MaxRetries = 0);

        var e = await Assert.ThrowsAsync<ApiConnectionException>(() => Convert(client));

        Assert.Single(handler.Requests);
        Assert.Contains("1 attempt:", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_body_cut_off_midway_is_a_connection_error_and_not_retried()
    {
        var handler = new FakeHandler()
            .Then((request, _) =>
            {
                var response = FakeHandler.Response(request, 200, Array.Empty<byte>(), "application/json");
                response.Content = new StreamContent(new FailingStream(Encoding.UTF8.GetBytes("{\"Files\":[")));
                return Task.FromResult(response);
            })
            .Then(200, Ok);
        var sleeps = new List<double>();
        using var client = Fake.Client(handler, sleeps);

        var e = await Assert.ThrowsAsync<ApiConnectionException>(() => Convert(client));

        Assert.Single(handler.Requests);
        Assert.Empty(sleeps);
        Assert.IsType<IOException>(e.InnerException);
        Assert.Contains("may already have run and counted", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_client_timeout_raises_ApiTimeout_and_is_not_retried()
    {
        var handler = new FakeHandler().Then(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHandler.Response(request, 200, Array.Empty<byte>(), null);
        });
        using var client = Fake.Client(handler, configure: o => o.Timeout = TimeSpan.FromMilliseconds(200));

        var e = await Assert.ThrowsAsync<ApiTimeoutException>(() => Convert(client));

        Assert.Single(handler.Requests);
        Assert.Contains("may still be running", e.Message, StringComparison.Ordinal);
        Assert.Contains("0.2 seconds", e.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_per_call_timeout_overrides_the_client_timeout()
    {
        var handler = new FakeHandler().Then(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHandler.Response(request, 200, Array.Empty<byte>(), null);
        });
        using var client = Fake.Client(handler);

        var started = DateTime.UtcNow;
        await Assert.ThrowsAsync<ApiTimeoutException>(() =>
            client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]), options: new ConvertOptions { Timeout = TimeSpan.FromMilliseconds(150) }));
        Assert.True(DateTime.UtcNow - started < TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task An_HttpClient_timeout_is_also_a_timeout()
    {
        var handler = new FakeHandler().Then(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHandler.Response(request, 200, Array.Empty<byte>(), null);
        });
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromMilliseconds(150) };
        using var client = new PdfConversionApiClient(http, new PdfConversionApiClientOptions { ApiKey = Fake.Key, BaseUrl = Fake.BaseUrl });

        await Assert.ThrowsAsync<ApiTimeoutException>(() => Convert(client));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Cancelling_raises_OperationCanceled_not_a_timeout()
    {
        var handler = new FakeHandler().Then(async (request, token) =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return FakeHandler.Response(request, 200, Array.Empty<byte>(), null);
        });
        using var client = Fake.Client(handler);
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]), cancellationToken: cancel.Token));
    }

    [Fact]
    public async Task Cancelling_stops_the_wait_between_retries()
    {
        var handler = new FakeHandler().ThenJsonError(503, "busy", ("Retry-After", "30"));
        using var cancel = new CancellationTokenSource();
        using var client = Fake.Client(handler, configure: o => o.Sleep = (wait, token) =>
        {
            cancel.Cancel();
            return Task.Delay(wait, token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(new byte[1]), cancellationToken: cancel.Token));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task Retries_resend_the_same_bytes_from_a_stream_that_cannot_seek()
    {
        var input = Encoding.UTF8.GetBytes("stream content");
        var handler = new FakeHandler().ThenJsonError(503, "busy", ("Retry-After", "1")).Then(200, Ok);
        using var client = Fake.Client(handler);

        await client.ConvertAsync("txt", "pdf", UploadFile.FromStream(new ForwardOnlyStream(input), "in.txt"));

        Assert.Equal(2, handler.Requests.Count);
        Assert.All(handler.Requests, r => Assert.Contains("stream content", r.BodyText, StringComparison.Ordinal));
        Assert.Equal(handler.Requests[0].Body!.Length, handler.Requests[1].Body!.Length);
    }

    [Fact]
    public async Task A_seekable_stream_is_read_again_from_its_starting_position()
    {
        var stream = new MemoryStream(Encoding.UTF8.GetBytes("skip|keep this"));
        stream.Position = 5;
        var handler = new FakeHandler().ThenThrow(new HttpRequestException("reset")).Then(200, Ok);
        using var client = Fake.Client(handler);

        await client.ConvertAsync("txt", "pdf", UploadFile.FromStream(stream, "in.txt"));

        Assert.All(handler.Requests, r =>
        {
            Assert.Contains("\r\n\r\nkeep this\r\n", r.BodyText, StringComparison.Ordinal);
            Assert.DoesNotContain("skip", r.BodyText, StringComparison.Ordinal);
        });
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task A_path_is_opened_again_for_each_attempt()
    {
        var path = Path.Combine(Path.GetTempPath(), "pdfconversionapi-retry-" + Guid.NewGuid().ToString("N") + ".txt");
        await File.WriteAllTextAsync(path, "first");
        try
        {
            var handler = new FakeHandler()
                .Then(async (request, _) =>
                {
                    await File.WriteAllTextAsync(path, "second version", CancellationToken.None);
                    return FakeHandler.Response(request, 503, Encoding.UTF8.GetBytes("{\"Code\":503,\"Message\":\"busy\"}"), "application/json", ("Retry-After", "1"));
                })
                .Then(200, Ok);
            using var client = Fake.Client(handler);

            await client.ConvertFileAsync(path, "pdf");

            Assert.Contains("\r\n\r\nfirst\r\n", handler.Requests[0].BodyText, StringComparison.Ordinal);
            Assert.Contains("\r\n\r\nsecond version\r\n", handler.Requests[1].BodyText, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task Downloads_go_through_the_same_retries()
    {
        var sleeps = new List<double>();
        var handler = new FakeHandler().ThenThrow(new HttpRequestException("reset")).ThenJsonError(503, "busy", ("Retry-After", "2")).Then(200, "data", "application/pdf");
        using var client = Fake.Client(handler, sleeps);

        var bytes = await client.DownloadAsync(Fake.BaseUrl + "/v2/d/" + new string('b', 32) + "/a.pdf");

        Assert.Equal("data", Encoding.UTF8.GetString(bytes));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Equal(2, sleeps.Count);
        Assert.True(sleeps[1] >= 2);
    }

    private static Task<ConversionResult> Convert(PdfConversionApiClient client)
        => client.ConvertAsync("txt", "pdf", UploadFile.FromBytes(Encoding.UTF8.GetBytes("hello"), "in.txt"));

    /// <summary>Returns some bytes, then fails like a reset connection.</summary>
    private sealed class FailingStream : Stream
    {
        private readonly byte[] _prefix;
        private int _position;

        public FailingStream(byte[] prefix)
        {
            _prefix = prefix;
        }

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position < _prefix.Length)
            {
                var n = Math.Min(count, _prefix.Length - _position);
                Array.Copy(_prefix, _position, buffer, offset, n);
                _position += n;
                return n;
            }

            throw new IOException("Connection reset by peer");
        }

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
