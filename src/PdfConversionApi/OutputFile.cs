using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using PdfConversionApi.Internal;

namespace PdfConversionApi;

/// <summary>
/// One output file of a conversion: its data, or for a stored file (<c>StoreFile</c>) a download
/// link that works for 3 hours.
/// </summary>
public sealed class OutputFile
{
    private readonly byte[]? _data;
    private readonly PdfConversionApiClient _client;

    internal OutputFile(PdfConversionApiClient client, string name, string extension, long size, byte[]? data, string? url, string? fileId)
    {
        _client = client;
        Name = name;
        Extension = extension;
        Size = size;
        _data = data;
        Url = url;
        FileId = fileId;
    }

    /// <summary>The file's name with its extension, such as <c>report.pdf</c> or <c>season-guide-1.jpg</c>.</summary>
    public string Name { get; }

    /// <summary>The extension in lower case, without the dot: <c>pdf</c>, <c>jpg</c>.</summary>
    public string Extension { get; }

    /// <summary>The file's size in bytes.</summary>
    public long Size { get; }

    /// <summary>True when the file was stored (<c>StoreFile</c>): it has <see cref="Url"/> and <see cref="FileId"/> instead of data.</summary>
    public bool Stored => _data is null;

    /// <summary>
    /// For a stored file, the download link (it needs no key; treat it like a password). Null otherwise.
    /// </summary>
    public string? Url { get; }

    /// <summary>For a stored file, its id (32 hex characters), for <see cref="PdfConversionApiClient.DeleteStoredFileAsync"/>. Null otherwise.</summary>
    public string? FileId { get; }

    /// <summary>The file's bytes. The array is not copied, so don't change it if you read it again later.</summary>
    /// <returns>The decoded data.</returns>
    /// <exception cref="StoredFileException">The file is stored: call <see cref="DownloadAsync"/> instead.</exception>
    public byte[] GetBytes()
    {
        if (_data is null)
        {
            throw new StoredFileException(
                "'" + Name + "' is a stored file: it has a download link instead of data. Call DownloadAsync() to fetch it.");
        }

        return _data;
    }

    /// <summary>The file's bytes: downloaded (without the API key) for a stored file, else the data.</summary>
    /// <param name="cancellationToken">Cancels the download.</param>
    /// <returns>The bytes.</returns>
    public Task<byte[]> DownloadAsync(CancellationToken cancellationToken = default)
    {
        return _data is null ? _client.DownloadAsync(Url!, cancellationToken) : Task.FromResult(_data);
    }

    /// <summary>Writes the file to <paramref name="path"/>, replacing it if it exists. A stored file is streamed to disk.</summary>
    /// <param name="path">The file to write.</param>
    /// <param name="cancellationToken">Cancels the download or the write.</param>
    /// <returns>The path written.</returns>
    public async Task<string> DownloadToAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        if (_data is null)
        {
            return await _client.DownloadToAsync(Url!, path, cancellationToken).ConfigureAwait(false);
        }

        using (var file = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true))
        {
            await file.WriteAsync(_data, 0, _data.Length, cancellationToken).ConfigureAwait(false);
        }

        return path;
    }

    /// <summary>
    /// Writes the file. When <paramref name="path"/> is an existing folder, the file is written inside it
    /// under <see cref="Name"/>; otherwise <paramref name="path"/> is the file to write. A stored file is
    /// downloaded first.
    /// </summary>
    /// <param name="path">A folder that exists, or the file to write.</param>
    /// <param name="cancellationToken">Cancels the download or the write.</param>
    /// <returns>The path written.</returns>
    public Task<string> SaveAsync(string path, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        var target = Directory.Exists(path) ? System.IO.Path.Combine(path, FileNames.SafeOutputName(Name)) : path;
        return DownloadToAsync(target, cancellationToken);
    }

    /// <inheritdoc />
    public override string ToString() => "OutputFile(" + Name + ", " + Size + " bytes" + (Stored ? ", stored)" : ")");
}
