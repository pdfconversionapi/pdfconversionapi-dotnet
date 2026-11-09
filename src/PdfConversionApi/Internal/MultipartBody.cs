using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PdfConversionApi.Internal;

/// <summary>
/// A <c>multipart/form-data</c> body written by hand. .NET's <c>MultipartFormDataContent</c>
/// MIME-encodes or <c>filename*</c>-encodes non-ASCII file names, and the API reads only a plain
/// <c>filename="..."</c>; here the name is written as raw UTF-8, with <c>"</c>, CR and LF replaced by
/// <c>_</c> (the API turns a quote into <c>_</c> itself, and would read <c>%22</c> literally).
/// File contents are streamed, not buffered.
/// </summary>
internal sealed class MultipartBody : HttpContent
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);
    private static readonly byte[] Crlf = { (byte)'\r', (byte)'\n' };

    private readonly List<Segment> _segments = new();
    private readonly long _length;

    public MultipartBody(IReadOnlyList<FormField> fields)
        : this(fields, "pdfconversionapi-" + Guid.NewGuid().ToString("N"))
    {
    }

    public MultipartBody(IReadOnlyList<FormField> fields, string boundary)
    {
        Boundary = boundary;
        long length = 0;
        foreach (var field in fields)
        {
            var header = new StringBuilder();
            header.Append("--").Append(boundary).Append("\r\n");
            header.Append("Content-Disposition: form-data; name=\"").Append(EscapeFieldName(field.Name)).Append('"');
            if (field is FileField file)
            {
                header.Append("; filename=\"").Append(CleanFileName(file.Upload.Name)).Append("\"\r\n");
                header.Append("Content-Type: application/octet-stream\r\n\r\n");
                var headerBytes = Utf8.GetBytes(header.ToString());
                var fileLength = file.Length();
                _segments.Add(new Segment(headerBytes, null, 0));
                _segments.Add(new Segment(null, file, fileLength));
                _segments.Add(new Segment(Crlf, null, 0));
                length += headerBytes.Length + fileLength + Crlf.Length;
            }
            else
            {
                header.Append("\r\n\r\n").Append(((TextField)field).Value).Append("\r\n");
                var bytes = Utf8.GetBytes(header.ToString());
                _segments.Add(new Segment(bytes, null, 0));
                length += bytes.Length;
            }
        }

        var closing = Utf8.GetBytes("--" + boundary + "--\r\n");
        _segments.Add(new Segment(closing, null, 0));
        length += closing.Length;
        _length = length;

        var contentType = new MediaTypeHeaderValue("multipart/form-data");
        contentType.Parameters.Add(new NameValueHeaderValue("boundary", boundary));
        Headers.ContentType = contentType;
    }

    public string Boundary { get; }

    /// <summary>A file name for <c>filename="..."</c>: raw UTF-8, with <c>"</c>, CR and LF as <c>_</c> (SPEC section 2).</summary>
    internal static string CleanFileName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(c is '"' or '\r' or '\n' ? '_' : c);
        }

        return builder.ToString();
    }

    /// <summary>
    /// A field name for <c>name="..."</c>, with the HTML form escapes (<c>%22</c>, <c>%0D</c>, <c>%0A</c>).
    /// Parameter names aren't validated here: one with these characters reaches the API, which
    /// answers 400, instead of being quietly renamed.
    /// </summary>
    internal static string EscapeFieldName(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            switch (c)
            {
                case '"':
                    builder.Append("%22");
                    break;
                case '\r':
                    builder.Append("%0D");
                    break;
                case '\n':
                    builder.Append("%0A");
                    break;
                default:
                    builder.Append(c);
                    break;
            }
        }

        return builder.ToString();
    }

    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        => WriteAsync(stream, CancellationToken.None);

#if NET
    protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken)
        => WriteAsync(stream, cancellationToken);
#endif

    protected override bool TryComputeLength(out long length)
    {
        length = _length;
        return true;
    }

    private async Task WriteAsync(Stream stream, CancellationToken cancellationToken)
    {
        foreach (var segment in _segments)
        {
            if (segment.Bytes is { } bytes)
            {
                await stream.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            }
            else
            {
                await segment.File!.WriteToAsync(stream, segment.FileLength, cancellationToken).ConfigureAwait(false);
            }
        }
    }

    private sealed class Segment
    {
        public Segment(byte[]? bytes, FileField? file, long fileLength)
        {
            Bytes = bytes;
            File = file;
            FileLength = fileLength;
        }

        public byte[]? Bytes { get; }

        public FileField? File { get; }

        public long FileLength { get; }
    }
}
