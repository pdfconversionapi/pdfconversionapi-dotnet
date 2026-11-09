using System;
using System.IO;
using PdfConversionApi.Internal;

namespace PdfConversionApi;

/// <summary>
/// A file to upload: its bytes and the name sent with them. The API names the output after it
/// (<c>report.docx</c> comes back as <c>report.pdf</c>), so give in-memory data a name with the right extension.
/// </summary>
/// <remarks>
/// Only the file's own name is sent, never its folders. A path is opened again for every attempt.
/// A stream is not disposed: a seekable stream is read from the position it had when the
/// <see cref="UploadFile"/> was made, again for each attempt; any other stream is read into memory
/// once, before the first attempt. Don't use one stream in two calls at the same time.
/// </remarks>
public sealed class UploadFile
{
    internal const string DefaultName = "file";

    private UploadFile(string name, string? path, byte[]? bytes, Stream? stream, long streamStart)
    {
        Name = name;
        Path = path;
        Bytes = bytes;
        Stream = stream;
        StreamStart = streamStart;
    }

    /// <summary>The name sent with the file (its own name, without folders).</summary>
    public string Name { get; }

    internal string? Path { get; }

    internal byte[]? Bytes { get; }

    internal Stream? Stream { get; }

    internal long StreamStart { get; }

    /// <summary>A file on disk. The name sent is the path's last part. The file is read when the call is made.</summary>
    /// <param name="path">The file's path.</param>
    /// <returns>The upload.</returns>
    /// <exception cref="ArgumentException">The path is null, empty or ends in a folder separator.</exception>
    public static UploadFile FromPath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            throw new ArgumentException("The path must not be empty.", nameof(path));
        }

        var name = FileNames.LastPart(path);
        if (name.Length == 0)
        {
            throw new ArgumentException("The path must name a file, not a folder.", nameof(path));
        }

        return new UploadFile(name, path, null, null, 0);
    }

    /// <summary>A file on disk. The name sent is the file's name.</summary>
    /// <param name="file">The file.</param>
    /// <returns>The upload.</returns>
    public static UploadFile FromPath(FileInfo file)
    {
        if (file is null)
        {
            throw new ArgumentNullException(nameof(file));
        }

        return FromPath(file.FullName);
    }

    /// <summary>Bytes in memory. The array is not copied: don't change it until the call ends.</summary>
    /// <param name="data">The file's bytes.</param>
    /// <param name="name">The name to send, such as <c>report.docx</c>; <c>file</c> when null or empty.</param>
    /// <returns>The upload.</returns>
    public static UploadFile FromBytes(byte[] data, string? name = null)
    {
        if (data is null)
        {
            throw new ArgumentNullException(nameof(data));
        }

        return new UploadFile(CleanName(name), null, data, null, 0);
    }

    /// <summary>
    /// A stream. A seekable stream is read from its current position, again for each attempt; any
    /// other stream is read into memory once before the first attempt. The stream is not disposed.
    /// </summary>
    /// <param name="stream">A readable stream.</param>
    /// <param name="name">The name to send, such as <c>report.docx</c>; <c>file</c> when null or empty.</param>
    /// <returns>The upload.</returns>
    public static UploadFile FromStream(Stream stream, string? name = null)
    {
        if (stream is null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable.", nameof(stream));
        }

        return new UploadFile(CleanName(name), null, null, stream, stream.CanSeek ? stream.Position : 0);
    }

    /// <inheritdoc />
    public override string ToString() => "UploadFile(" + Name + ")";

    private static string CleanName(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return DefaultName;
        }

        var last = FileNames.LastPart(name!);
        return last.Length == 0 ? DefaultName : last;
    }
}
