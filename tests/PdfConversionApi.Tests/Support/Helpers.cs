using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PdfConversionApi.Tests.Support;

/// <summary>Runs code with an environment variable unset (tests run one at a time, so this is safe).</summary>
internal static class EnvironmentScope
{
    public static T WithoutVariable<T>(string name, Func<T> action) => WithVariable(name, null, action);

    public static T WithVariable<T>(string name, string? value, Func<T> action)
    {
        var saved = Environment.GetEnvironmentVariable(name);
        Environment.SetEnvironmentVariable(name, value);
        try
        {
            return action();
        }
        finally
        {
            Environment.SetEnvironmentVariable(name, saved);
        }
    }
}

/// <summary>A stream that can only be read forward once, like a network stream.</summary>
internal sealed class ForwardOnlyStream : Stream
{
    private readonly MemoryStream _inner;

    public ForwardOnlyStream(byte[] data)
    {
        _inner = new MemoryStream(data, writable: false);
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

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _inner.Dispose();
        }

        base.Dispose(disposing);
    }
}

/// <summary>A request as the fake handler saw it.</summary>
internal sealed record CapturedRequest(HttpMethod Method, Uri Uri, HttpRequestHeaders Headers, string? ContentType, byte[]? Body)
{
    public string? Header(string name)
    {
        if (string.Equals(name, "User-Agent", StringComparison.OrdinalIgnoreCase))
        {
            return Headers.UserAgent.Count == 0 ? null : Headers.UserAgent.ToString();
        }

        return Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;
    }

    public string BodyText => Body is null ? string.Empty : Encoding.UTF8.GetString(Body);
}

/// <summary>An in-memory HTTP handler: answers with queued responses (the last one repeats) and records requests.</summary>
internal sealed class FakeHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _answers = new();
    private Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? _last;

    public List<CapturedRequest> Requests { get; } = new();

    public FakeHandler Then(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> answer)
    {
        _answers.Enqueue(answer);
        return this;
    }

    public FakeHandler Then(int status, string body = "", string? contentType = "application/json", params (string Name, string Value)[] headers)
    {
        return Then((request, _) => Task.FromResult(Response(request, status, Encoding.UTF8.GetBytes(body), contentType, headers)));
    }

    public FakeHandler ThenJsonError(int status, string message, params (string Name, string Value)[] headers)
    {
        var json = "{\"Code\":" + status + ",\"Message\":" + System.Text.Json.JsonSerializer.Serialize(message) + "}";
        return Then(status, json, "application/json", headers.Append(("x-request-id", "req-" + status)).ToArray());
    }

    public FakeHandler ThenThrow(Exception exception)
    {
        return Then((_, _) => Task.FromException<HttpResponseMessage>(exception));
    }

    public static HttpResponseMessage Response(HttpRequestMessage request, int status, byte[] body, string? contentType, params (string Name, string Value)[] headers)
    {
        var content = new ByteArrayContent(body);
        if (contentType is not null)
        {
            content.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        var response = new HttpResponseMessage((HttpStatusCode)status) { Content = content, RequestMessage = request };
        foreach (var (name, value) in headers)
        {
            if (!response.Headers.TryAddWithoutValidation(name, value))
            {
                content.Headers.TryAddWithoutValidation(name, value);
            }
        }

        return response;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        byte[]? body = null;
        if (request.Content is not null)
        {
            body = await request.Content.ReadAsByteArrayAsync(cancellationToken);
        }

        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? answer;
        lock (Requests)
        {
            Requests.Add(new CapturedRequest(request.Method, request.RequestUri!, request.Headers, request.Content?.Headers.ContentType?.ToString(), body));
            if (_answers.Count > 0)
            {
                _last = _answers.Dequeue();
            }

            answer = _last;
        }

        if (answer is null)
        {
            throw new InvalidOperationException("No answer queued.");
        }

        return await answer(request, cancellationToken);
    }
}

/// <summary>Builds clients around a <see cref="FakeHandler"/>.</summary>
internal static class Fake
{
    public const string Key = "unit-test-key-0123456789abcdef";
    public const string BaseUrl = "https://api.example.test";

    public static PdfConversionApiClient Client(FakeHandler handler, List<double>? sleeps = null, Action<PdfConversionApiClientOptions>? configure = null)
    {
        var options = new PdfConversionApiClientOptions
        {
            ApiKey = Key,
            BaseUrl = BaseUrl,
            Sleep = (wait, _) =>
            {
                sleeps?.Add(wait.TotalSeconds);
                return Task.CompletedTask;
            },
        };
        configure?.Invoke(options);
        return new PdfConversionApiClient(new HttpClient(handler), options);
    }

    public static string ConversionJson(params (string Name, byte[] Data)[] files)
    {
        var entries = files.Select(f =>
            "{\"FileName\":" + System.Text.Json.JsonSerializer.Serialize(f.Name)
            + ",\"FileExt\":\"" + Path.GetExtension(f.Name).TrimStart('.') + "\""
            + ",\"FileSize\":" + f.Data.Length
            + ",\"FileData\":\"" + Convert.ToBase64String(f.Data) + "\"}");
        return "{\"ConversionCost\":1,\"ConversionTime\":0.25,\"Files\":[" + string.Join(",", entries) + "]}";
    }

    public static (string Name, string Value)[] RequestId(string id) => new[] { ("x-request-id", id) };
}
