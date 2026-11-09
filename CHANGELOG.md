# Changelog

## 0.1.0 (unreleased)

First release of the official .NET client for the PDF Conversion API.

- `PdfConversionApiClient` with `ConvertAsync`, `ConvertFileAsync`, `UsageAsync`,
  `ConversionsAsync`, `DownloadAsync`, `DownloadToAsync` and `DeleteStoredFileAsync`, all async
  with a `CancellationToken`.
- Uploads from a path, bytes or a stream (`UploadFile`), several inputs, file parameters such as
  `ImageFile`, and conversions without a file. Non-ASCII file names are sent as raw UTF-8, which
  is what the API reads.
- Typed results (`ConversionResult`, `OutputFile`, `Usage`, `Conversion`), stored files with
  streamed downloads, and safe file names on disk.
- Typed exceptions for every error status, the API's retry rules (connection failures, 429 and
  503 with `Retry-After`, one retry of a 500) and a 240-second per-attempt timeout.
- Works with your own `HttpClient` or `IHttpClientFactory`.
- Targets .NET 8 and .NET Standard 2.0.
