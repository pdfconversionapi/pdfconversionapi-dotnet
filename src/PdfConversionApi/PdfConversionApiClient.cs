using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using PdfConversionApi.Internal;

namespace PdfConversionApi;

/// <summary>
/// The client for the PDF Conversion API. Create one and reuse it: it is safe to share between
/// threads and keeps no per-request state.
/// </summary>
/// <example>
/// <code>
/// var client = new PdfConversionApiClient();   // reads PDFCONVERSIONAPI_KEY
/// var result = await client.ConvertFileAsync("report.docx", "pdf");
/// await result.SaveAllAsync("out");             // writes out/report.pdf
/// </code>
/// </example>
public sealed class PdfConversionApiClient : IDisposable
{
    /// <summary>This library's version.</summary>
    public const string Version = "0.1.0";

    /// <summary>The API's address unless you set another: <c>https://api.pdfconversionapi.com</c>.</summary>
    public const string DefaultBaseUrl = "https://api.pdfconversionapi.com";

    /// <summary>The environment variable the key is read from when none is given: <c>PDFCONVERSIONAPI_KEY</c>.</summary>
    public const string ApiKeyEnvironmentVariable = "PDFCONVERSIONAPI_KEY";

    /// <summary>The environment variable the base URL is read from when none is given: <c>PDFCONVERSIONAPI_BASE_URL</c>.</summary>
    public const string BaseUrlEnvironmentVariable = "PDFCONVERSIONAPI_BASE_URL";

    internal static readonly string UserAgent =
        "pdfconversionapi-dotnet/" + Version + " dotnet/" + Environment.Version.ToString();

    private static readonly TimeSpan OneSecond = TimeSpan.FromSeconds(1);

#if !NET
    private static readonly Random SharedRandom = new();
#endif

    private readonly HttpClient _http;
    private readonly bool _ownsHttp;
    private readonly string? _apiKey;
    private readonly TimeSpan _timeout;
    private readonly int _maxRetries;
    private readonly TimeSpan _maxRetryWait;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;

    /// <summary>A client that reads the key from <c>PDFCONVERSIONAPI_KEY</c>, with the default settings.</summary>
    public PdfConversionApiClient()
        : this(new PdfConversionApiClientOptions(), null)
    {
    }

    /// <summary>A client with this API key and the default settings.</summary>
    /// <param name="apiKey">The API key from https://app.pdfconversionapi.com.</param>
    public PdfConversionApiClient(string apiKey)
        : this(new PdfConversionApiClientOptions { ApiKey = apiKey ?? throw new ArgumentNullException(nameof(apiKey)) }, null)
    {
    }

    /// <summary>A client with these settings and its own <see cref="HttpClient"/>.</summary>
    /// <param name="options">The settings; they are copied.</param>
    public PdfConversionApiClient(PdfConversionApiClientOptions options)
        : this(options ?? throw new ArgumentNullException(nameof(options)), null)
    {
    }

    /// <summary>
    /// A client that sends through your <see cref="HttpClient"/>, such as one from
    /// <c>IHttpClientFactory</c>. The client is not disposed with this one. Its own
    /// <see cref="HttpClient.Timeout"/> (100 seconds by default) also applies: set it to
    /// <see cref="System.Threading.Timeout.InfiniteTimeSpan"/> and use <see cref="PdfConversionApiClientOptions.Timeout"/>.
    /// </summary>
    /// <param name="httpClient">The HTTP client to send with.</param>
    /// <param name="options">The settings, or null for the defaults; they are copied.</param>
    public PdfConversionApiClient(HttpClient httpClient, PdfConversionApiClientOptions? options = null)
        : this(options ?? new PdfConversionApiClientOptions(), httpClient ?? throw new ArgumentNullException(nameof(httpClient)))
    {
    }

    private PdfConversionApiClient(PdfConversionApiClientOptions options, HttpClient? httpClient)
    {
        _apiKey = ResolveApiKey(options.ApiKey);
        BaseUrl = ResolveBaseUrl(options.BaseUrl);
        _timeout = Validation.Timeout(options.Timeout, nameof(options));
        var connectTimeout = Validation.Timeout(options.ConnectTimeout, nameof(options));
        if (options.MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRetries must be 0 or more.");
        }

        if (options.MaxRetryWait < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "MaxRetryWait must not be negative.");
        }

        _maxRetries = options.MaxRetries;
        _maxRetryWait = options.MaxRetryWait;
        _sleep = options.Sleep ?? ((delay, token) => Task.Delay(delay, token));

        if (httpClient is null)
        {
            _http = CreateHttpClient(connectTimeout);
            _ownsHttp = true;
        }
        else
        {
            _http = httpClient;
        }
    }

    /// <summary>The API's address used by this client, without a trailing slash.</summary>
    public string BaseUrl { get; }

    /// <summary>Whether this client has an API key (from the options or the environment).</summary>
    public bool HasApiKey => _apiKey is not null;

    /// <summary>
    /// Converts one file: <c>POST /v2/convert/{from}/to/{to}</c>, with the file in the <c>File</c> field.
    /// </summary>
    /// <param name="from">The source format, such as <c>docx</c> (or <c>file</c>, <c>office</c>, <c>images</c>).</param>
    /// <param name="to">The target format or tool, such as <c>pdf</c>, <c>jpg</c>, <c>compress</c>.</param>
    /// <param name="file">The file to convert.</param>
    /// <param name="parameters">
    /// Conversion parameters by name. Values may be strings, numbers, booleans, null (left out) or an
    /// <see cref="UploadFile"/> (sent as a file field with that name, such as <c>ImageFile</c>).
    /// </param>
    /// <param name="options">Typed options: output name, stored file, timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The output files.</returns>
    public Task<ConversionResult> ConvertAsync(
        string from,
        string to,
        UploadFile file,
        IEnumerable<KeyValuePair<string, object?>>? parameters = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return ConvertCoreAsync(from, to, new[] { file }, parameters, options, cancellationToken);
    }

    /// <summary>
    /// Converts several files, one file or none. One is sent as <c>File</c>, two or more as repeated
    /// <c>Files</c> fields in this order (for <c>pdf/to/merge</c>, <c>images/to/pdf</c>,
    /// <c>images/to/join</c>, <c>file/to/zip</c>). Null or an empty list sends no file field: the API
    /// answers <c>400 no input file was provided</c> unless the conversion takes none.
    /// </summary>
    /// <param name="from">The source format, such as <c>pdf</c> or <c>images</c>.</param>
    /// <param name="to">The target format or tool, such as <c>merge</c>.</param>
    /// <param name="files">The files, in order; null or empty for none.</param>
    /// <param name="parameters">Conversion parameters by name (see the single-file overload).</param>
    /// <param name="options">Typed options: output name, stored file, timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The output files.</returns>
    public Task<ConversionResult> ConvertAsync(
        string from,
        string to,
        IEnumerable<UploadFile>? files,
        IEnumerable<KeyValuePair<string, object?>>? parameters = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var list = files is null ? new List<UploadFile>() : new List<UploadFile>(files);
        if (list.Contains(null!))
        {
            throw new ArgumentException("The list of files contains null.", nameof(files));
        }

        return ConvertCoreAsync(from, to, list, parameters, options, cancellationToken);
    }

    /// <summary>
    /// Runs a conversion that takes no file, such as <c>data/to/qrcode</c> with a <c>Data</c>
    /// parameter. The request has no file field; a conversion that needs one answers
    /// <c>400 no input file was provided</c>.
    /// </summary>
    /// <param name="from">The source, such as <c>data</c>.</param>
    /// <param name="to">The tool, such as <c>qrcode</c> or <c>barcode</c>.</param>
    /// <param name="parameters">Conversion parameters by name, such as <c>Data</c>.</param>
    /// <param name="options">Typed options: output name, stored file, timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The output files.</returns>
    public Task<ConversionResult> ConvertAsync(
        string from,
        string to,
        IEnumerable<KeyValuePair<string, object?>>? parameters = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        return ConvertCoreAsync(from, to, Array.Empty<UploadFile>(), parameters, options, cancellationToken);
    }

    /// <summary>
    /// Converts a file on disk. The source format is the path's extension in lower case:
    /// <c>ConvertFileAsync("report.docx", "pdf")</c> calls <c>docx/to/pdf</c>.
    /// </summary>
    /// <param name="path">The file's path. It must have an extension.</param>
    /// <param name="to">The target format or tool.</param>
    /// <param name="parameters">Conversion parameters by name (see <see cref="ConvertAsync(string, string, UploadFile, IEnumerable{KeyValuePair{string, object}}, ConvertOptions, CancellationToken)"/>).</param>
    /// <param name="options">Typed options: output name, stored file, timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The output files.</returns>
    public Task<ConversionResult> ConvertFileAsync(
        string path,
        string to,
        IEnumerable<KeyValuePair<string, object?>>? parameters = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        var name = FileNames.LastPart(path);
        var dot = name.LastIndexOf('.');
        if (dot < 0 || dot == name.Length - 1)
        {
            throw new ArgumentException(
                "The path has no extension to take the source format from; call ConvertAsync with the format instead.",
                nameof(path));
        }

        return ConvertAsync(name.Substring(dot + 1), to, UploadFile.FromPath(path), parameters, options, cancellationToken);
    }

    /// <summary>Converts a file on disk; the source format is its extension (see the string overload).</summary>
    /// <param name="file">The file. It must have an extension.</param>
    /// <param name="to">The target format or tool.</param>
    /// <param name="parameters">Conversion parameters by name.</param>
    /// <param name="options">Typed options: output name, stored file, timeout.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The output files.</returns>
    public Task<ConversionResult> ConvertFileAsync(
        FileInfo file,
        string to,
        IEnumerable<KeyValuePair<string, object?>>? parameters = null,
        ConvertOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return ConvertFileAsync(file.FullName, to, parameters, options, cancellationToken);
    }

    /// <summary>The account's plan, limits and this month's count: <c>GET /v2/usage</c>. Uses no conversion.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The usage.</returns>
    public Task<Usage> UsageAsync(CancellationToken cancellationToken = default)
    {
        var key = RequireApiKey();
        var url = BaseUrl + "/v2/usage";
        return SendAsync(
            () => NewRequest(HttpMethod.Get, url, key, acceptJson: true),
            status => status == 200,
            async (response, token) => ResponseParser.Usage(await ReadBodyAsync(response, token).ConfigureAwait(false), RequestIdOf(response)),
            _timeout,
            isDownload: false,
            cancellationToken);
    }

    /// <summary>Every conversion the API offers: <c>GET /v2/info</c>. Needs no key.</summary>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>The conversions.</returns>
    public Task<IReadOnlyList<Conversion>> ConversionsAsync(CancellationToken cancellationToken = default)
    {
        var url = BaseUrl + "/v2/info";
        return SendAsync(
            () => NewRequest(HttpMethod.Get, url, null, acceptJson: true),
            status => status == 200,
            async (response, token) => ResponseParser.Conversions(await ReadBodyAsync(response, token).ConfigureAwait(false), RequestIdOf(response)),
            _timeout,
            isDownload: false,
            cancellationToken);
    }

    /// <summary>
    /// Downloads a stored file into memory. Pass the <see cref="OutputFile.Url"/> as returned; no API key
    /// is sent. An expired or deleted file raises <see cref="NotFoundException"/>.
    /// </summary>
    /// <param name="url">The stored file's link.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The file's bytes.</returns>
    public Task<byte[]> DownloadAsync(string url, CancellationToken cancellationToken = default)
    {
        var uri = DownloadUri(url);
        return SendAsync(
            () => NewRequest(HttpMethod.Get, uri.AbsoluteUri, null, acceptJson: false),
            status => status == 200,
            async (response, token) =>
            {
                CheckSameHost(response, uri);
                var body = await ReadBodyAsync(response, token).ConfigureAwait(false);
                return body.ToArray();
            },
            _timeout,
            isDownload: true,
            cancellationToken);
    }

    /// <summary>
    /// Downloads a stored file straight to disk, replacing <paramref name="path"/> if it exists. No API key is sent.
    /// </summary>
    /// <param name="url">The stored file's link, as returned in <see cref="OutputFile.Url"/>.</param>
    /// <param name="path">The file to write.</param>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The path written.</returns>
    public Task<string> DownloadToAsync(string url, string path, CancellationToken cancellationToken = default)
    {
        var uri = DownloadUri(url);
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        return SendAsync(
            () => NewRequest(HttpMethod.Get, uri.AbsoluteUri, null, acceptJson: false),
            status => status == 200,
            async (response, token) =>
            {
                CheckSameHost(response, uri);
                await StreamToFileAsync(response, path, token).ConfigureAwait(false);
                return path;
            },
            _timeout,
            isDownload: true,
            cancellationToken);
    }

    /// <summary>
    /// Deletes a stored file before it expires: <c>DELETE /v2/d/{fileId}</c>.
    /// </summary>
    /// <param name="fileId">The file's <see cref="OutputFile.FileId"/>: 32 hex characters.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>True when it was deleted, false when there was nothing to delete (unknown, expired, already deleted, or another account's).</returns>
    public Task<bool> DeleteStoredFileAsync(string fileId, CancellationToken cancellationToken = default)
    {
        var id = Validation.FileId(fileId, nameof(fileId));
        var key = RequireApiKey();
        var url = BaseUrl + "/v2/d/" + id;
        return SendAsync(
            () => NewRequest(HttpMethod.Delete, url, key, acceptJson: true),
            status => status is (>= 200 and < 300) or 404,
            (response, _) => Task.FromResult(response.StatusCode != HttpStatusCode.NotFound),
            _timeout,
            isDownload: false,
            cancellationToken);
    }

    /// <summary>Disposes the client's own <see cref="HttpClient"/>; one you passed in is left alone.</summary>
    public void Dispose()
    {
        if (_ownsHttp)
        {
            _http.Dispose();
        }
    }

    /// <inheritdoc />
    public override string ToString() => "PdfConversionApiClient(" + BaseUrl + ")";

    private static string? ResolveApiKey(string? fromOptions)
    {
        var key = string.IsNullOrEmpty(fromOptions) ? Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable) : fromOptions;
        key = key?.Trim();
        if (string.IsNullOrEmpty(key))
        {
            return null;
        }

        foreach (var c in key!)
        {
            if (c is < '!' or > '~')
            {
                // The key itself is never part of a message.
                throw new ArgumentException("The API key contains a character that can't be sent in a header.", nameof(fromOptions));
            }
        }

        return key;
    }

    private static string ResolveBaseUrl(string? fromOptions)
    {
        var url = string.IsNullOrEmpty(fromOptions) ? Environment.GetEnvironmentVariable(BaseUrlEnvironmentVariable) : fromOptions;
        url = string.IsNullOrEmpty(url) ? DefaultBaseUrl : url!.Trim().TrimEnd('/');
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0)
        {
            throw new ArgumentException("The base URL must be an absolute http or https URL without a query.", nameof(fromOptions));
        }

        return url;
    }

    private static Uri DownloadUri(string url)
    {
        if (string.IsNullOrEmpty(url))
        {
            throw new ArgumentException("The download link must not be empty.", nameof(url));
        }

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException("The download link must be an absolute http or https URL.", nameof(url));
        }

        return uri;
    }

    private static HttpClient CreateHttpClient(TimeSpan connectTimeout)
    {
#if NET
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.All,
            ConnectTimeout = connectTimeout,
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
#else
        _ = connectTimeout;
        var handler = new HttpClientHandler
        {
            AllowAutoRedirect = false,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
        };
#endif
        return new HttpClient(handler, disposeHandler: true) { Timeout = System.Threading.Timeout.InfiniteTimeSpan };
    }

    private static HttpRequestMessage NewRequest(HttpMethod method, string url, string? apiKey, bool acceptJson)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
        if (acceptJson)
        {
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        if (apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }

        return request;
    }

    private static TimeSpan Jitter(int attempt)
    {
#if NET
        var sample = Random.Shared.NextDouble();
#else
        double sample;
        lock (SharedRandom)
        {
            sample = SharedRandom.NextDouble();
        }
#endif
        return TimeSpan.FromSeconds(sample * Math.Pow(2, attempt));
    }

    private static string? RequestIdOf(HttpResponseMessage response)
    {
        return response.Headers.TryGetValues("x-request-id", out var values) ? FirstNonEmpty(values) : null;
    }

    private static string? MediaTypeOf(HttpResponseMessage response)
    {
        var mediaType = response.Content?.Headers.ContentType?.MediaType;
        if (mediaType is null && response.Content is not null && response.Content.Headers.TryGetValues("Content-Type", out var raw))
        {
            mediaType = FirstNonEmpty(raw)?.Split(';')[0];
        }

        mediaType = mediaType?.Trim();
        return string.IsNullOrEmpty(mediaType) ? null : Validation.ToLowerAscii(mediaType!);
    }

    private static TimeSpan RetryAfterOf(HttpResponseMessage response)
    {
        if (response.Headers.TryGetValues("Retry-After", out var values)
            && FirstNonEmpty(values) is { } text
            && long.TryParse(text.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var seconds)
            && seconds <= int.MaxValue)
        {
            return TimeSpan.FromSeconds(seconds);
        }

        return OneSecond;
    }

    private static string? FirstNonEmpty(IEnumerable<string> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }
        }

        return null;
    }

    private static void CheckSameHost(HttpResponseMessage response, Uri requested)
    {
        var final = response.RequestMessage?.RequestUri;
        if (final is not null && Uri.Compare(final, requested, UriComponents.SchemeAndServer, UriFormat.Unescaped, StringComparison.OrdinalIgnoreCase) != 0)
        {
            throw new PdfConversionApiException("The download was redirected to another host, which this client doesn't follow.");
        }
    }

    private static async Task<Stream> ResponseStreamAsync(HttpResponseMessage response, CancellationToken token)
    {
        try
        {
#if NET
            return await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
#else
            return await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
#endif
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            throw new ResponseBodyException(e);
        }
    }

    private static async Task<ReadOnlyMemory<byte>> ReadBodyAsync(HttpResponseMessage response, CancellationToken token)
    {
        using var stream = await ResponseStreamAsync(response, token).ConfigureAwait(false);
        try
        {
            var declared = response.Content.Headers.ContentLength;
            if (declared is >= 0 and <= 0x7FFFFFC7)
            {
                var buffer = new byte[declared.Value];
                var read = 0;
                while (read < buffer.Length)
                {
                    var n = await stream.ReadAsync(buffer, read, buffer.Length - read, token).ConfigureAwait(false);
                    if (n == 0)
                    {
                        throw new IOException("The response ended before its declared length.");
                    }

                    read += n;
                }

                return buffer;
            }

            using var memory = new MemoryStream();
            await stream.CopyToAsync(memory, 81920, token).ConfigureAwait(false);
            return new ReadOnlyMemory<byte>(memory.GetBuffer(), 0, (int)memory.Length);
        }
        catch (Exception e) when (e is IOException or HttpRequestException)
        {
            throw new ResponseBodyException(e);
        }
    }

    private static async Task StreamToFileAsync(HttpResponseMessage response, string path, CancellationToken token)
    {
        using var source = await ResponseStreamAsync(response, token).ConfigureAwait(false);
        var completed = false;
        var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        try
        {
            var buffer = new byte[81920];
            while (true)
            {
                int read;
                try
                {
                    read = await source.ReadAsync(buffer, 0, buffer.Length, token).ConfigureAwait(false);
                }
                catch (Exception e) when (e is IOException or HttpRequestException)
                {
                    throw new ResponseBodyException(e);
                }

                if (read == 0)
                {
                    break;
                }

                await file.WriteAsync(buffer, 0, read, token).ConfigureAwait(false);
            }

            await file.FlushAsync(token).ConfigureAwait(false);
            completed = true;
        }
        finally
        {
            file.Dispose();
            if (!completed)
            {
                TryDelete(path);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort: the partial file is left behind.
        }
        catch (UnauthorizedAccessException)
        {
            // Best effort: the partial file is left behind.
        }
    }

    private static async Task<ApiException> ReadErrorAsync(HttpResponseMessage response, CancellationToken token)
    {
        ReadOnlyMemory<byte> body;
        try
        {
            body = await ReadBodyAsync(response, token).ConfigureAwait(false);
        }
        catch (ResponseBodyException)
        {
            body = ReadOnlyMemory<byte>.Empty;
        }

        var status = (int)response.StatusCode;
        TimeSpan? retryAfter = status is 429 or 503 ? RetryAfterOf(response) : null;
        return ApiException.Create(
            status,
            ResponseParser.ErrorMessage(body, MediaTypeOf(response)),
            RequestIdOf(response),
            retryAfter,
            ResponseParser.Text(body));
    }

    private static string TimeoutMessage(TimeSpan timeout, bool isDownload)
    {
        var after = timeout == System.Threading.Timeout.InfiniteTimeSpan
            ? "The request timed out"
            : "The request timed out after " + timeout.TotalSeconds.ToString("0.###", CultureInfo.InvariantCulture) + " seconds";
        return isDownload
            ? after + " and was not retried."
            : after + " and was not retried. The conversion may still be running on the server, and it counts toward "
              + "the month if it succeeds. Give the client more time with PdfConversionApiClientOptions.Timeout or ConvertOptions.Timeout.";
    }

    private static string HostOf(HttpRequestMessage request) => request.RequestUri?.Host ?? "the API";

    private string RequireApiKey() => _apiKey ?? throw new MissingApiKeyException();

    private Task<ConversionResult> ConvertCoreAsync(
        string from,
        string to,
        IReadOnlyList<UploadFile> files,
        IEnumerable<KeyValuePair<string, object?>>? parameters,
        ConvertOptions? options,
        CancellationToken cancellationToken)
    {
        var fromFormat = Validation.Format(from, nameof(from));
        var toFormat = Validation.Format(to, nameof(to));
        var fields = FormBuilder.Build(files, parameters, options);
        var timeout = options?.Timeout is { } t ? Validation.Timeout(t, nameof(options)) : _timeout;
        var key = RequireApiKey();
        var url = BaseUrl + "/v2/convert/" + fromFormat + "/to/" + toFormat;
        return SendConversionAsync(url, key, fields, timeout, cancellationToken);
    }

    private async Task<ConversionResult> SendConversionAsync(string url, string key, List<FormField> fields, TimeSpan timeout, CancellationToken cancellationToken)
    {
        foreach (var field in fields)
        {
            if (field is FileField file)
            {
                await file.PrepareAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        return await SendAsync(
            () =>
            {
                var request = NewRequest(HttpMethod.Post, url, key, acceptJson: true);
                request.Content = new MultipartBody(fields);
                return request;
            },
            status => status == 200,
            async (response, token) => ResponseParser.Conversion(await ReadBodyAsync(response, token).ConfigureAwait(false), RequestIdOf(response), this),
            timeout,
            isDownload: false,
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>The retry loop every call goes through (SPEC section 8).</summary>
    private async Task<T> SendAsync<T>(
        Func<HttpRequestMessage> createRequest,
        Func<int, bool> isSuccess,
        Func<HttpResponseMessage, CancellationToken, Task<T>> readSuccess,
        TimeSpan timeout,
        bool isDownload,
        CancellationToken cancellationToken)
    {
        var retried500 = false;
        for (var attempt = 1; ; attempt++)
        {
            var canRetry = attempt <= _maxRetries;
            Exception? connectionError = null;
            TimeSpan wait;

            using (var attemptSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                if (timeout != System.Threading.Timeout.InfiniteTimeSpan)
                {
                    attemptSource.CancelAfter(timeout);
                }

                var token = attemptSource.Token;
                HttpResponseMessage? response = null;
                try
                {
                    using var request = createRequest();
                    try
                    {
                        response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, token).ConfigureAwait(false);
                    }
                    catch (Exception e) when (e is HttpRequestException or IOException)
                    {
                        connectionError = e;
                    }

                    ApiException? error = null;
                    if (response is not null)
                    {
                        if (isSuccess((int)response.StatusCode))
                        {
                            try
                            {
                                return await readSuccess(response, token).ConfigureAwait(false);
                            }
                            catch (ResponseBodyException e)
                            {
                                // The status line arrived, so the request may have done its work
                                // (a conversion may have counted): raised at once, never retried.
                                cancellationToken.ThrowIfCancellationRequested();
                                if (token.IsCancellationRequested)
                                {
                                    throw new ApiTimeoutException(TimeoutMessage(timeout, isDownload), e.InnerException);
                                }

                                throw new ApiConnectionException(
                                    "The connection to " + HostOf(request) + " dropped while the response was being read: "
                                    + e.InnerException!.Message
                                    + (isDownload ? " Not retried." : " Not retried, because the conversion may already have run and counted."),
                                    e.InnerException);
                            }
                        }
                        else
                        {
                            error = await ReadErrorAsync(response, token).ConfigureAwait(false);
                        }
                    }

                    if (connectionError is not null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        if (token.IsCancellationRequested)
                        {
                            throw new ApiTimeoutException(TimeoutMessage(timeout, isDownload), connectionError);
                        }

                        if (!canRetry)
                        {
                            throw new ApiConnectionException(
                                "No response from " + HostOf(request) + " after " + attempt.ToString(CultureInfo.InvariantCulture)
                                + (attempt == 1 ? " attempt: " : " attempts: ") + connectionError.Message,
                                connectionError);
                        }

                        wait = Jitter(attempt);
                    }
                    else
                    {
                        var status = error!.StatusCode;
                        if (canRetry && status is 429 or 503 && error.RetryAfter <= _maxRetryWait)
                        {
                            wait = error.RetryAfter.Value + Jitter(attempt);
                        }
                        else if (canRetry && status == 500 && !retried500)
                        {
                            retried500 = true;
                            wait = OneSecond + Jitter(attempt);
                        }
                        else
                        {
                            throw error;
                        }
                    }
                }
                catch (OperationCanceledException e) when (!cancellationToken.IsCancellationRequested)
                {
                    throw new ApiTimeoutException(TimeoutMessage(timeout, isDownload), e);
                }
                finally
                {
                    response?.Dispose();
                }
            }

            await _sleep(wait, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>A network failure while reading a response body: handled like no response.</summary>
    private sealed class ResponseBodyException : Exception
    {
        public ResponseBodyException(Exception inner)
            : base(inner.Message, inner)
        {
        }
    }
}
