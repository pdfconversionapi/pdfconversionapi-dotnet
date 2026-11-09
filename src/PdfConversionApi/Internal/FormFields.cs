using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace PdfConversionApi.Internal;

/// <summary>One field of the multipart form, in the order it is sent.</summary>
internal abstract class FormField
{
    protected FormField(string name)
    {
        Name = name;
    }

    public string Name { get; }
}

internal sealed class TextField : FormField
{
    public TextField(string name, string value)
        : base(name)
    {
        Value = value;
    }

    public string Value { get; }
}

internal sealed class FileField : FormField
{
    private byte[]? _buffered;

    public FileField(string name, UploadFile upload)
        : base(name)
    {
        Upload = upload;
    }

    public UploadFile Upload { get; }

    /// <summary>
    /// Reads what can't be read again (a stream that can't seek) into memory, once, and checks that
    /// a path exists. Called once per call, before the first attempt.
    /// </summary>
    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (Upload.Path is { } path)
        {
            if (!File.Exists(path))
            {
                throw new FileNotFoundException("The file to upload was not found.", path);
            }
        }
        else if (Upload.Stream is { CanSeek: false } stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
            _buffered = buffer.ToArray();
        }
    }

    /// <summary>The number of bytes this attempt will send.</summary>
    public long Length()
    {
        if (Upload.Bytes is { } bytes)
        {
            return bytes.LongLength;
        }

        if (_buffered is { } buffered)
        {
            return buffered.LongLength;
        }

        if (Upload.Path is { } path)
        {
            return new FileInfo(path).Length;
        }

        var stream = Upload.Stream!;
        return Math.Max(0, stream.Length - Upload.StreamStart);
    }

    /// <summary>Writes exactly <paramref name="length"/> bytes of the file.</summary>
    public async Task WriteToAsync(Stream destination, long length, CancellationToken cancellationToken)
    {
        if (Upload.Bytes is { } bytes)
        {
            await destination.WriteAsync(bytes, 0, bytes.Length, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (_buffered is { } buffered)
        {
            await destination.WriteAsync(buffered, 0, buffered.Length, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (Upload.Path is { } path)
        {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await CopyExactlyAsync(file, destination, length, cancellationToken).ConfigureAwait(false);
            return;
        }

        var stream = Upload.Stream!;
        stream.Position = Upload.StreamStart;
        await CopyExactlyAsync(stream, destination, length, cancellationToken).ConfigureAwait(false);
    }

    private static async Task CopyExactlyAsync(Stream source, Stream destination, long length, CancellationToken cancellationToken)
    {
        var buffer = new byte[(int)Math.Min(81920, Math.Max(1, length))];
        var left = length;
        while (left > 0)
        {
            var read = await source.ReadAsync(buffer, 0, (int)Math.Min(buffer.Length, left), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                throw new IOException("The file to upload got shorter while it was being sent.");
            }

            await destination.WriteAsync(buffer, 0, read, cancellationToken).ConfigureAwait(false);
            left -= read;
        }
    }
}

/// <summary>Builds the ordered field list for a conversion.</summary>
internal static class FormBuilder
{
    /// <summary>
    /// One file as <c>File</c>, several as repeated <c>Files</c>, none as no file field at all (the API
    /// decides: <c>data/to/qrcode</c> takes none, the rest answer 400), then the parameters in order,
    /// then the typed options.
    /// </summary>
    public static List<FormField> Build(IReadOnlyList<UploadFile> files, IEnumerable<KeyValuePair<string, object?>>? parameters, ConvertOptions? options)
    {
        var fields = new List<FormField>();
        var fileField = files.Count == 1 ? "File" : "Files";
        foreach (var file in files)
        {
            fields.Add(new FileField(fileField, file));
        }

        var dropFileName = options?.FileName is not null;
        var dropStoreFile = options?.StoreFile is not null;

        if (parameters is not null)
        {
            foreach (var pair in parameters)
            {
                var name = pair.Key;
                if (string.IsNullOrEmpty(name))
                {
                    throw new ArgumentException("A parameter name must not be empty.", nameof(parameters));
                }

                if ((dropFileName && string.Equals(name, "FileName", StringComparison.OrdinalIgnoreCase))
                    || (dropStoreFile && string.Equals(name, "StoreFile", StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                if (pair.Value is UploadFile upload)
                {
                    fields.Add(new FileField(name, upload));
                    continue;
                }

                var text = ParameterValues.ToText(pair.Value, name);
                if (text is not null)
                {
                    fields.Add(new TextField(name, text));
                }
            }
        }

        if (options?.FileName is { } fileName)
        {
            fields.Add(new TextField("FileName", fileName));
        }

        if (options?.StoreFile == true)
        {
            fields.Add(new TextField("StoreFile", "true"));
        }

        return fields;
    }
}
