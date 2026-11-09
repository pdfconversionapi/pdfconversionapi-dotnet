namespace PdfConversionApi;

/// <summary>One conversion the API offers, from <c>GET /v2/info</c>.</summary>
public sealed class Conversion
{
    internal Conversion(string from, string to, string title, string category, string inputs)
    {
        From = from;
        To = to;
        Title = title;
        Category = category;
        Inputs = inputs;
    }

    /// <summary>The source format, as used in the URL: <c>docx</c>.</summary>
    public string From { get; }

    /// <summary>The target format or tool: <c>pdf</c>, <c>merge</c>.</summary>
    public string To { get; }

    /// <summary>A readable title: <c>DOCX to PDF</c>.</summary>
    public string Title { get; }

    /// <summary>The group it belongs to: <c>Document</c>, <c>PDF Tools</c>.</summary>
    public string Category { get; }

    /// <summary>How many input files it takes: <c>one</c>, <c>many</c> or <c>optional</c>.</summary>
    public string Inputs { get; }

    /// <inheritdoc />
    public override string ToString() => From + "/to/" + To;
}
