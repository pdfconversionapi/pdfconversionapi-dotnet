using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;

namespace PdfConversionApi;

/// <summary>Settings for a <see cref="PdfConversionApiClient"/>. The client copies them when it is created.</summary>
public sealed class PdfConversionApiClientOptions
{
    /// <summary>The default per-attempt timeout: 240 seconds.</summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(240);

    /// <summary>The default connect timeout: 10 seconds.</summary>
    public static readonly TimeSpan DefaultConnectTimeout = TimeSpan.FromSeconds(10);

    /// <summary>The default longest <c>Retry-After</c> worth waiting for: 60 seconds.</summary>
    public static readonly TimeSpan DefaultMaxRetryWait = TimeSpan.FromSeconds(60);

    /// <summary>The default number of retries: 4, so 5 attempts in all.</summary>
    public const int DefaultMaxRetries = 4;

    /// <summary>
    /// The API key. When null or empty, the client reads the <c>PDFCONVERSIONAPI_KEY</c> environment
    /// variable. A client without a key can still list conversions and download stored files; a call
    /// that needs a key raises <see cref="MissingApiKeyException"/> before sending anything.
    /// </summary>
    public string? ApiKey { get; set; }

    /// <summary>
    /// The API's address. When null or empty, the client reads the <c>PDFCONVERSIONAPI_BASE_URL</c>
    /// environment variable, else uses <c>https://api.pdfconversionapi.com</c>. A trailing slash is ignored.
    /// </summary>
    public string? BaseUrl { get; set; }

    /// <summary>
    /// How long one attempt may take, from sending the request to the end of the response body.
    /// 240 seconds by default. A timeout is never retried. <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> turns it off.
    /// </summary>
    public TimeSpan Timeout { get; set; } = DefaultTimeout;

    /// <summary>
    /// How long to wait for a connection, 10 seconds by default. Used by the client's own
    /// <see cref="System.Net.Http.HttpClient"/> on .NET 8 and later; ignored when you pass your own
    /// <see cref="System.Net.Http.HttpClient"/> and on .NET Standard 2.0.
    /// </summary>
    public TimeSpan ConnectTimeout { get; set; } = DefaultConnectTimeout;

    /// <summary>How many times a failed attempt may be retried, 4 by default (5 attempts). 0 turns retries off.</summary>
    public int MaxRetries { get; set; } = DefaultMaxRetries;

    /// <summary>
    /// The longest <c>Retry-After</c> the client waits for on a 429 or 503, 60 seconds by default.
    /// A longer one (such as a month's conversions used up) is raised at once.
    /// </summary>
    public TimeSpan MaxRetryWait { get; set; } = DefaultMaxRetryWait;

    /// <summary>
    /// For tests: replaces the wait between retries. It receives each wait the client would make.
    /// Leave it null to really wait (<see cref="Task.Delay(TimeSpan, CancellationToken)"/>).
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Advanced)]
    public Func<TimeSpan, CancellationToken, Task>? Sleep { get; set; }
}
