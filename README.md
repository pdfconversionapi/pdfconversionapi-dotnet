# PDF Conversion API for .NET

The official .NET client for the [PDF Conversion API](https://pdfconversionapi.com): convert
documents, spreadsheets, presentations, images and PDFs with one async call, and get typed
results and typed errors back. Retries and timeouts follow the API's documented rules.

- Documentation: https://pdfconversionapi.com/docs
- API keys: https://app.pdfconversionapi.com
- Pricing: https://pdfconversionapi.com/pricing

## Install

```bash
dotnet add package PdfConversionApi
```

It runs on .NET 8 and later, and on anything that supports .NET Standard 2.0 (such as .NET
Framework 4.6.2 and later). On .NET Standard 2.0 it depends on `System.Text.Json`; on .NET 8 and
later it has no dependencies.

## API key

Create a key in the [dashboard](https://app.pdfconversionapi.com) and put it in the
`PDFCONVERSIONAPI_KEY` environment variable, or pass it as `ApiKey` (see
[Configuration](#configuration)). Keep it on your server: the key is secret, and the API sends no
CORS headers, so this library is for servers, scripts and command-line tools, not browsers.

## Quick start

```csharp
using PdfConversionApi;

var client = new PdfConversionApiClient();             // reads PDFCONVERSIONAPI_KEY
var result = await client.ConvertFileAsync("report.docx", "pdf");
await result.SaveAllAsync("out");                      // writes out/report.pdf
```

Every method is async and takes an optional `CancellationToken`. Create one client and reuse it:
it is safe to share between threads.

## Files to upload

An `UploadFile` is a file's bytes and the name sent with them. The API names the output after
that name (`report.docx` comes back as `report.pdf`).

| Factory | Sends |
|---|---|
| `UploadFile.FromPath("report.docx")` | The file on disk, read again on each retry. The name sent is the path's last part, never its folders. |
| `UploadFile.FromBytes(bytes, "report.docx")` | Bytes in memory. Without a name, the file is sent as `file`. |
| `UploadFile.FromStream(stream, "report.docx")` | A stream, which is not disposed. A seekable stream is read from its current position on each attempt; any other stream is read into memory once, so retries send the same bytes. |

Wherever this library takes a `string` for a file, such as `ConvertFileAsync("report.docx", ...)`,
the string is a **path**, never the file's content.

## Convert one file

`ConvertFileAsync` takes the source format from the file's extension, as in the quick start.
`ConvertAsync` takes it explicitly, with any kind of upload:

```csharp
using var client = new PdfConversionApiClient();

// Bytes or a stream: pass the name to send, since the output is named after it.
var bytes = await File.ReadAllBytesAsync("report.docx");
var fromBytes = await client.ConvertAsync("docx", "pdf", UploadFile.FromBytes(bytes, "report.docx"));

await using var stream = File.OpenRead("report.docx");
var fromStream = await client.ConvertAsync("docx", "pdf", UploadFile.FromStream(stream, "report.docx"));

var path = await fromStream.File.SaveAsync("report.pdf");
Console.WriteLine($"{fromBytes.File.Name}: {fromBytes.File.Size} bytes; saved {path}");
```

```text
report.pdf: 82830 bytes; saved report.pdf
```

## Several outputs: PDF to JPG

Page images come back as one file per page, in page order:

```csharp
using var client = new PdfConversionApiClient();
var result = await client.ConvertFileAsync(
    "season-guide.pdf",
    "jpg",
    new Dictionary<string, object?> { ["ImageResolution"] = 72 });

foreach (var page in result.Files)
{
    Console.WriteLine($"{page.Name}: {page.Size} bytes");
}

await result.SaveAllAsync("pages");                    // pages/season-guide-1.jpg and -2.jpg
```

```text
season-guide-1.jpg: 65272 bytes
season-guide-2.jpg: 66627 bytes
```

`SaveAllAsync` creates the folder if needed, writes every file under its own name (replacing
files that exist) and returns the paths written.

## Several inputs: merge

Pass a list: one file is sent as `File`, two or more as repeated `Files` fields in the order
given (for `pdf/to/merge`, `images/to/pdf`, `images/to/join` and `file/to/zip`):

```csharp
using var client = new PdfConversionApiClient();
var inputs = new List<UploadFile>
{
    UploadFile.FromPath("invoice.pdf"),
    UploadFile.FromPath("terms.pdf"),
    UploadFile.FromPath("timesheet.pdf"),
};

var result = await client.ConvertAsync("pdf", "merge", inputs, options: new ConvertOptions { FileName = "october-billing" });

await result.File.SaveAsync(".");                      // writes ./october-billing.pdf
Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
```

```text
october-billing.pdf: 107308 bytes
```

## A file parameter: image watermark

A parameter whose value is an `UploadFile` is sent as a file field with that name, such as
`ImageFile` for `pdf/to/image-watermark` or `OverlayFile` for `pdf/to/pdf-watermark`:

```csharp
using var client = new PdfConversionApiClient();
var result = await client.ConvertAsync(
    "pdf",
    "image-watermark",
    UploadFile.FromPath("proposal.pdf"),
    new Dictionary<string, object?>
    {
        ["ImageFile"] = UploadFile.FromPath("logo.png"),   // a file parameter: sent as its own file field
        ["Opacity"] = 30,
    });

await result.File.SaveAsync("proposal-stamped.pdf");
Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
```

## Parameters

Parameters are a dictionary of name to value. Strings are sent as they are, numbers in plain
decimal (`72`, `1.5`) whatever the current culture, booleans as `true` or `false`, and `null`
is left out. Names and values are checked by the API, which answers 400 with a clear message.
Each conversion's parameters are listed on its page, for example
[PDF to JPG](https://pdfconversionapi.com/convert/pdf-to-jpg).

```csharp
using var client = new PdfConversionApiClient();
var parameters = new Dictionary<string, object?>
{
    ["PageRange"] = "2",               // strings are sent as they are
    ["ImageResolution"] = 100,         // numbers in plain decimal
    ["ScaleProportions"] = true,       // booleans as true or false
    ["Password"] = null,               // null is left out
};

var result = await client.ConvertFileAsync(
    "season-guide.pdf",
    "jpg",
    parameters,
    new ConvertOptions { FileName = "cover" });    // typed options: FileName, StoreFile, Timeout

Console.WriteLine(result.File.Name);
```

```text
cover-2.jpg
```

`ConvertOptions.FileName` and `ConvertOptions.StoreFile` win over parameters of the same name.

## Conversions without a file

A few conversions take no file, such as `data/to/qrcode` and `data/to/barcode` with a `Data`
parameter. Call `ConvertAsync` without files (passing `null` or an empty list does the same):

```csharp
using var client = new PdfConversionApiClient();
var result = await client.ConvertAsync(
    "data",
    "qrcode",
    new Dictionary<string, object?> { ["Data"] = "https://pdfconversionapi.com" });

await result.File.SaveAsync(".");                      // writes ./qrcode.svg
Console.WriteLine($"{result.File.Name}: {result.File.Size} bytes");
```

```text
qrcode.svg: 9912 bytes
```

## Results

`ConversionResult` has `Files` (in the API's order), `File` (the first one), `ConversionCost`,
`ConversionTime` (seconds on the server), `RequestId` and `SaveAllAsync(folder)`.

Each `OutputFile` has:

| Member | What it is |
|---|---|
| `Name`, `Extension`, `Size` | `report.pdf`, `pdf`, the size in bytes. |
| `GetBytes()` | The data. A stored file raises `StoredFileException`: download it instead. |
| `SaveAsync(path)` | Writes the file. When `path` is an existing folder, writes inside it under `Name`. |
| `DownloadAsync()`, `DownloadToAsync(path)` | The bytes, or the file on disk; a stored file is downloaded (streamed to disk by `DownloadToAsync` and `SaveAsync`). |
| `Stored`, `Url`, `FileId` | For stored files only (below). |

Files are written under their own name only: anything up to the last `/` or `\` is dropped,
and the names `.` and `..` are refused.

**Memory:** the whole response is held in memory while it is decoded, and base64 is a third
larger than the files it carries (a 100 MB result is about 133 MB of JSON). For large outputs,
use stored files and `DownloadToAsync`, which streams to disk.

## Stored files: download and delete

`StoreFile = true` returns a download link for each output instead of its data. A link works for
3 hours, needs no API key (so treat it like a password), and can be deleted early:

```csharp
using var client = new PdfConversionApiClient();
var result = await client.ConvertFileAsync(
    "season-guide.pdf",
    "jpg",
    new Dictionary<string, object?> { ["ImageResolution"] = 72 },
    new ConvertOptions { StoreFile = true });

foreach (var file in result.Files)
{
    // file.Url needs no key and works for 3 hours: treat it like a password.
    var path = await file.SaveAsync(".");                          // streams the download to disk
    var deleted = await client.DeleteStoredFileAsync(file.FileId!);   // false if it was already gone
    Console.WriteLine($"{path}: {file.Size} bytes, stored copy deleted: {deleted}");
}
```

```text
./season-guide-1.jpg: 65272 bytes, stored copy deleted: True
./season-guide-2.jpg: 66627 bytes, stored copy deleted: True
```

`DownloadAsync(url)` and `DownloadToAsync(url, path)` on the client take a link as returned in
`Url`; they never send the API key and don't follow redirects to other hosts. A download after the
link expired or was deleted raises `NotFoundException`.

## Usage and the list of conversions

```csharp
using var client = new PdfConversionApiClient();

var usage = await client.UsageAsync();                 // uses no conversion
Console.WriteLine($"{usage.Plan}: {usage.Used} of {usage.Included} used, until {usage.PeriodEnd:yyyy-MM-dd}");

var conversions = await client.ConversionsAsync();     // needs no key
Console.WriteLine($"{conversions.Count} conversions, such as {conversions[0].Title}");
```

```text
Free: 12 of 250 used, until 2026-12-01
5 conversions, such as DOCX to PDF
```

## Errors

Everything this library raises derives from `PdfConversionApiException`, apart from argument
errors (`ArgumentException`, raised before anything is sent) and cancellation
(`OperationCanceledException`, when your `CancellationToken` is cancelled).

| Exception | When |
|---|---|
| `MissingApiKeyException` | A call that needs a key, with no key set. Nothing was sent. |
| `StoredFileException` | `GetBytes()` on a stored file. |
| `ApiConnectionException` | No response after every retry, or the connection dropped while the response was arriving. |
| `ApiTimeoutException` | The client's own timeout ran out (see [Timeouts](#timeouts)). |
| `InvalidResponseException` | A 200 response that can't be used (not JSON, no `Files`, a size that doesn't match). |
| `ApiException` | Any error status, with `StatusCode`, `ApiMessage`, `RequestId`, `RetryAfter` and `Body`. |
| ↳ `BadRequestException` | 400: a parameter, page range or number of files the conversion can't take. |
| ↳ `AuthenticationException` | 401: the key is missing, unknown or replaced. |
| ↳ `PermissionDeniedException` | 403: the key's plan isn't set up. |
| ↳ `NotFoundException` | 404: an unsupported conversion, or a stored file that expired or was deleted. |
| ↳ `PayloadTooLargeException` | 413: the request is larger than the plan allows. |
| ↳ `UnprocessableEntityException` | 422: the file can't be read or converted as asked. |
| ↳ `RateLimitException` | 429: the rate limit, conversions at once, or the month's conversions. |
| ↳ `InternalServerErrorException` | 500. |
| ↳ `ServiceUnavailableException` | 503: the servers are busy, or conversions on the plan are paused. |
| ↳ `GatewayTimeoutException` | 504: the conversion ran out of time on the server. |

An `ApiException`'s `Message` reads `HTTP <status>: <message> (request id: <id>)`. Include the
request id when you ask for help.

```csharp
using var client = new PdfConversionApiClient();
try
{
    await client.ConvertFileAsync("report.docx", "dwg");
}
catch (RateLimitException e) when (e.RetryAfter > TimeSpan.FromMinutes(1))
{
    // The month's conversions are used up; short waits were already retried.
    Console.WriteLine($"Stopped until later: {e.ApiMessage}");
}
catch (ApiException e)
{
    // Any error status: e.StatusCode, e.ApiMessage, e.RequestId, e.RetryAfter, e.Body.
    Console.WriteLine(e.Message);
}
catch (ApiTimeoutException e)
{
    // Not retried: the conversion may still be running, and counts if it succeeds.
    Console.WriteLine(e.Message);
}
catch (ApiConnectionException e)
{
    // No response after every retry.
    Console.WriteLine(e.Message);
}
```

```text
HTTP 404: conversion from docx to dwg is not supported (request id: 74974fbdba2a45c2)
```

## Retries

Every call, downloads included, is retried for you, up to `MaxRetries` (4) times:

- **No response** (refused or reset connection, DNS failure, connect timeout): retried after a
  random wait of up to 2, 4, 8 and 16 seconds.
- **429 and 503:** retried after the `Retry-After` seconds plus that random wait, when
  `Retry-After` is at most `MaxRetryWait` (60 seconds). A longer one, such as a used-up month,
  is raised at once with `RetryAfter` set.
- **500:** retried once.
- Everything else is raised at once: 400, 401, 403, 404, 413, 422 and 504 would fail the same
  way again. So is a connection that drops after the response started, because the conversion
  may already have counted.

Retrying is safe: a conversion changes nothing on the server, and only successful attempts count
toward the month.

## Timeouts

Each attempt may take 240 seconds by default, from sending the request to the end of the
response. A timeout raises `ApiTimeoutException` and is **not** retried: the conversion may still
be running on the server, and it counts toward the month if it succeeds. Raise it for the client
with `PdfConversionApiClientOptions.Timeout`, or for one call with `ConvertOptions.Timeout`
(see the next example).

## Configuration

```csharp
using var client = new PdfConversionApiClient(new PdfConversionApiClientOptions
{
    ApiKey = Environment.GetEnvironmentVariable("PDFCONVERSIONAPI_KEY"), // the default when left out
    BaseUrl = null,                             // PDFCONVERSIONAPI_BASE_URL, else https://api.pdfconversionapi.com
    Timeout = TimeSpan.FromMinutes(5),          // per attempt, for the whole response (default 240 s)
    ConnectTimeout = TimeSpan.FromSeconds(10),  // the default
    MaxRetries = 2,                             // default 4, so 5 attempts; 0 turns retries off
    MaxRetryWait = TimeSpan.FromSeconds(30),    // a longer Retry-After is raised at once (default 60 s)
});

// One call can have its own timeout.
var result = await client.ConvertFileAsync(
    "report.docx",
    "pdf",
    options: new ConvertOptions { Timeout = TimeSpan.FromMinutes(10) });
Console.WriteLine($"{result.File.Name}, request id {result.RequestId}");
```

| Option | Default |
|---|---|
| `ApiKey` | The `PDFCONVERSIONAPI_KEY` environment variable. Without a key, `ConversionsAsync` and downloads still work. |
| `BaseUrl` | The `PDFCONVERSIONAPI_BASE_URL` environment variable, else `https://api.pdfconversionapi.com`. |
| `Timeout` | 240 seconds per attempt. `Timeout.InfiniteTimeSpan` turns it off. |
| `ConnectTimeout` | 10 seconds (the client's own `HttpClient`, on .NET 8 and later). |
| `MaxRetries` | 4, so 5 attempts. 0 turns retries off. |
| `MaxRetryWait` | 60 seconds. |

## Your own HttpClient and IHttpClientFactory

Pass an `HttpClient` to `new PdfConversionApiClient(httpClient, options)`, or register the
client as a typed client. The `HttpClient`'s own `Timeout` (100 seconds by default) also
applies, so turn it off and let `PdfConversionApiClientOptions.Timeout` decide:

```csharp
var services = new ServiceCollection();
services.AddHttpClient<PdfConversionApiClient>(http => http.Timeout = Timeout.InfiniteTimeSpan);
// Optional: services.AddSingleton(new PdfConversionApiClientOptions { ... });
using var provider = services.BuildServiceProvider();

var client = provider.GetRequiredService<PdfConversionApiClient>();
var result = await client.ConvertFileAsync("report.docx", "pdf");
Console.WriteLine(result.File.Name);
```

A client you pass in is not disposed with `PdfConversionApiClient`. A client created without one
owns its `HttpClient`; dispose it when you are done.

## License

MIT. Copyright (c) 2026 Zeel Shekhaliya.
