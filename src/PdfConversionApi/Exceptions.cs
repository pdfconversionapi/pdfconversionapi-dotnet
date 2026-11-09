using System;
using System.Globalization;

namespace PdfConversionApi;

/// <summary>The base of every exception this library raises, apart from argument errors.</summary>
/// <remarks>
/// Bad arguments raise <see cref="ArgumentException"/> (or a subclass of it) before anything is sent,
/// and cancelling through a <see cref="System.Threading.CancellationToken"/> raises
/// <see cref="OperationCanceledException"/> as usual in .NET.
/// </remarks>
public class PdfConversionApiException : Exception
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public PdfConversionApiException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a message and the exception that caused it.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cause.</param>
    public PdfConversionApiException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>A call that needs an API key was made without one. Nothing was sent.</summary>
public sealed class MissingApiKeyException : PdfConversionApiException
{
    internal const string DefaultMessage =
        "No API key: set PdfConversionApiClientOptions.ApiKey or the PDFCONVERSIONAPI_KEY environment variable. "
        + "Get a key at https://app.pdfconversionapi.com";

    /// <summary>Creates the exception with the standard message.</summary>
    public MissingApiKeyException()
        : base(DefaultMessage)
    {
    }

    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public MissingApiKeyException(string message)
        : base(message)
    {
    }
}

/// <summary>
/// The data of a stored file (one converted with <c>StoreFile</c>) was read. A stored file has a
/// download link instead of data: call <see cref="OutputFile.DownloadAsync"/>.
/// </summary>
public sealed class StoredFileException : PdfConversionApiException
{
    /// <summary>Creates the exception with a message.</summary>
    /// <param name="message">What went wrong.</param>
    public StoredFileException(string message)
        : base(message)
    {
    }
}

/// <summary>No response from the API after every attempt (DNS failure, refused, reset or closed connection).</summary>
public sealed class ApiConnectionException : PdfConversionApiException
{
    /// <summary>Creates the exception with a message and the last network error.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The network error of the last attempt.</param>
    public ApiConnectionException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The client's own timeout ran out before the whole response arrived. It is never retried: the
/// conversion may still be running on the server, and it counts toward the month if it succeeds.
/// </summary>
public sealed class ApiTimeoutException : PdfConversionApiException
{
    /// <summary>Creates the exception with a message and the cancellation it came from.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="innerException">The cancellation raised by the HTTP stack.</param>
    public ApiTimeoutException(string message, Exception? innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>The API answered 200 with a body this library can't use (not JSON, no <c>Files</c>, a size that doesn't match).</summary>
public sealed class InvalidResponseException : PdfConversionApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What was wrong with the response.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="innerException">The parsing error, if any.</param>
    public InvalidResponseException(string message, string? requestId = null, Exception? innerException = null)
        : base(requestId is null ? message : message + " (request id: " + requestId + ")", innerException)
    {
        RequestId = requestId;
    }

    /// <summary>The response's <c>x-request-id</c> header, or null when it had none.</summary>
    public string? RequestId { get; }
}

/// <summary>
/// The API answered with an error status. Subclasses name the common ones; other statuses raise
/// this class itself. <see cref="Exception.Message"/> reads like
/// <c>HTTP 429: too many requests; slow down (request id: 1a2b...)</c>.
/// </summary>
public class ApiException : PdfConversionApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="statusCode">The HTTP status.</param>
    /// <param name="apiMessage">The error message (see <see cref="ApiMessage"/>).</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">The wait the API asked for (429 and 503 only).</param>
    /// <param name="body">The raw response body.</param>
    public ApiException(int statusCode, string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(Describe(statusCode, apiMessage, requestId))
    {
        StatusCode = statusCode;
        ApiMessage = apiMessage ?? string.Empty;
        RequestId = requestId;
        RetryAfter = retryAfter;
        Body = body ?? string.Empty;
    }

    /// <summary>The HTTP status, such as 400 or 429.</summary>
    public int StatusCode { get; }

    /// <summary>
    /// The error message: the API's <c>Message</c> text unchanged; <c>empty response</c> for an
    /// empty body; a short <c>text/plain</c> body, trimmed; otherwise
    /// <c>non-JSON response (&lt;content type&gt;)</c>.
    /// </summary>
    public string ApiMessage { get; }

    /// <summary>The response's <c>x-request-id</c> header, or null when it had none (an edge network page). Include it when you contact support.</summary>
    public string? RequestId { get; }

    /// <summary>
    /// On 429 and 503: the <c>Retry-After</c> wait (1 second when the header was missing or unreadable).
    /// Null for other statuses.
    /// </summary>
    public TimeSpan? RetryAfter { get; }

    /// <summary>The raw response body, for debugging.</summary>
    public string Body { get; }

    internal static string Describe(int statusCode, string apiMessage, string? requestId)
    {
        var text = "HTTP " + statusCode.ToString(CultureInfo.InvariantCulture) + ": " + apiMessage;
        return requestId is null ? text : text + " (request id: " + requestId + ")";
    }

    internal static ApiException Create(int statusCode, string apiMessage, string? requestId, TimeSpan? retryAfter, string body)
    {
        return statusCode switch
        {
            400 => new BadRequestException(apiMessage, requestId, retryAfter, body),
            401 => new AuthenticationException(apiMessage, requestId, retryAfter, body),
            403 => new PermissionDeniedException(apiMessage, requestId, retryAfter, body),
            404 => new NotFoundException(apiMessage, requestId, retryAfter, body),
            413 => new PayloadTooLargeException(apiMessage, requestId, retryAfter, body),
            422 => new UnprocessableEntityException(apiMessage, requestId, retryAfter, body),
            429 => new RateLimitException(apiMessage, requestId, retryAfter, body),
            500 => new InternalServerErrorException(apiMessage, requestId, retryAfter, body),
            503 => new ServiceUnavailableException(apiMessage, requestId, retryAfter, body),
            504 => new GatewayTimeoutException(apiMessage, requestId, retryAfter, body),
            _ => new ApiException(statusCode, apiMessage, requestId, retryAfter, body),
        };
    }
}

/// <summary>400: the request is not valid (a parameter, a page range, the number of files). Not retried.</summary>
public sealed class BadRequestException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public BadRequestException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(400, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>401: the API key is missing, unknown or no longer valid. Not retried.</summary>
public sealed class AuthenticationException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public AuthenticationException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(401, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>403: the key's plan is not set up. Not retried.</summary>
public sealed class PermissionDeniedException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public PermissionDeniedException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(403, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>404: an unsupported conversion, or a stored file that expired or was deleted. Not retried.</summary>
public sealed class NotFoundException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public NotFoundException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(404, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>413: the request is larger than the plan allows. Not retried.</summary>
public sealed class PayloadTooLargeException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public PayloadTooLargeException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(413, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>422: the file can't be read or converted as asked (damaged, password-protected...). Not retried.</summary>
public sealed class UnprocessableEntityException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public UnprocessableEntityException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(422, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>429: the rate limit, conversions at once, or the month's conversions. Retried when <see cref="ApiException.RetryAfter"/> is short.</summary>
public sealed class RateLimitException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">The wait the API asked for.</param>
    /// <param name="body">The raw response body.</param>
    public RateLimitException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(429, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>500: an internal error. Retried once.</summary>
public sealed class InternalServerErrorException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public InternalServerErrorException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(500, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>503: the servers are busy, or conversions on the plan are paused. Retried when <see cref="ApiException.RetryAfter"/> is short.</summary>
public sealed class ServiceUnavailableException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">The wait the API asked for.</param>
    /// <param name="body">The raw response body.</param>
    public ServiceUnavailableException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(503, apiMessage, requestId, retryAfter, body)
    {
    }
}

/// <summary>504: the conversion ran out of time on the server. Not retried: the same file would most likely time out again.</summary>
public sealed class GatewayTimeoutException : ApiException
{
    /// <summary>Creates the exception.</summary>
    /// <param name="apiMessage">The error message.</param>
    /// <param name="requestId">The response's <c>x-request-id</c>, if any.</param>
    /// <param name="retryAfter">Not used for this status.</param>
    /// <param name="body">The raw response body.</param>
    public GatewayTimeoutException(string apiMessage, string? requestId = null, TimeSpan? retryAfter = null, string? body = null)
        : base(504, apiMessage, requestId, retryAfter, body)
    {
    }
}
