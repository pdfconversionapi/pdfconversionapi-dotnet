using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using PdfConversionApi.Internal;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>How a conversion request body is written.</summary>
public sealed class EncodingTests
{
    [Theory]
    [InlineData("plain.pdf", "plain.pdf")]
    [InlineData("say \"hi\".pdf", "say _hi_.pdf")]
    [InlineData("a\rb\nc.pdf", "a_b_c.pdf")]
    [InlineData("100% done %22.pdf", "100% done %22.pdf")]
    [InlineData("Résumé final (1).txt", "Résumé final (1).txt")]
    public void File_names_turn_quotes_and_line_breaks_into_underscores(string name, string expected)
    {
        Assert.Equal(expected, MultipartBody.CleanFileName(name));
    }

    [Theory]
    [InlineData("PageRange", "PageRange")]
    [InlineData("a\"b", "a%22b")]
    [InlineData("a\r\nb", "a%0D%0Ab")]
    public void Field_names_keep_the_html_form_escapes_so_the_api_can_refuse_them(string name, string expected)
    {
        Assert.Equal(expected, MultipartBody.EscapeFieldName(name));
    }

    [Fact]
    public void No_files_means_no_file_field()
    {
        var fields = FormBuilder.Build(Array.Empty<UploadFile>(), new Dictionary<string, object?> { ["Data"] = "hello" }, null);
        Assert.Equal(new[] { "Data" }, fields.Select(f => f.Name));
    }

    [Fact]
    public async Task A_non_ascii_name_is_raw_utf8_in_filename_without_filename_star()
    {
        var body = await BodyAsync(new[] { UploadFile.FromBytes(new byte[] { 1, 2, 3 }, "Résumé final (1).txt") });
        var bytes = body.Bytes;
        var expected = Encoding.UTF8.GetBytes("Content-Disposition: form-data; name=\"File\"; filename=\"Résumé final (1).txt\"\r\n");
        Assert.True(IndexOf(bytes, expected) >= 0, "raw UTF-8 filename not found in: " + body.Text);
        Assert.DoesNotContain("filename*", body.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("=?utf-8?", body.Text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("R%C3%A9sum", body.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_body_is_well_formed_and_its_length_is_exact()
    {
        var content = new MultipartBody(
            FormBuilder.Build(new[] { UploadFile.FromBytes(Encoding.ASCII.GetBytes("abc"), "a.txt") }, new Dictionary<string, object?> { ["PageRange"] = "1-3" }, null),
            "BOUNDARY");
        var bytes = await content.ReadAsByteArrayAsync();
        Assert.Equal(bytes.Length, content.Headers.ContentLength);
        Assert.Equal("multipart/form-data; boundary=BOUNDARY", content.Headers.ContentType!.ToString());
        Assert.Equal(
            "--BOUNDARY\r\n"
            + "Content-Disposition: form-data; name=\"File\"; filename=\"a.txt\"\r\n"
            + "Content-Type: application/octet-stream\r\n\r\n"
            + "abc\r\n"
            + "--BOUNDARY\r\n"
            + "Content-Disposition: form-data; name=\"PageRange\"\r\n\r\n"
            + "1-3\r\n"
            + "--BOUNDARY--\r\n",
            Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void One_file_is_File_and_several_are_repeated_Files_in_order()
    {
        var one = FormBuilder.Build(new[] { UploadFile.FromBytes(new byte[1], "a.pdf") }, null, null);
        Assert.Equal(new[] { "File" }, one.Select(f => f.Name));

        var three = FormBuilder.Build(
            new[] { UploadFile.FromBytes(new byte[1], "a.pdf"), UploadFile.FromBytes(new byte[1], "b.pdf"), UploadFile.FromBytes(new byte[1], "c.pdf") },
            null,
            null);
        Assert.Equal(new[] { "Files", "Files", "Files" }, three.Select(f => f.Name));
        Assert.Equal(new[] { "a.pdf", "b.pdf", "c.pdf" }, three.Cast<FileField>().Select(f => f.Upload.Name));
    }

    [Fact]
    public void Parameters_keep_their_order_named_files_become_file_fields_and_null_is_left_out()
    {
        var logo = UploadFile.FromBytes(new byte[] { 1 }, "logo.png");
        var fields = FormBuilder.Build(
            new[] { UploadFile.FromBytes(new byte[1], "in.pdf") },
            new Dictionary<string, object?> { ["ImageFile"] = logo, ["Opacity"] = 30, ["Empty"] = null, ["Grayscale"] = true },
            null);

        Assert.Equal(new[] { "File", "ImageFile", "Opacity", "Grayscale" }, fields.Select(f => f.Name));
        Assert.Same(logo, ((FileField)fields[1]).Upload);
        Assert.Equal("30", ((TextField)fields[2]).Value);
        Assert.Equal("true", ((TextField)fields[3]).Value);
    }

    [Fact]
    public void Typed_options_win_over_parameters_of_the_same_name_in_any_case()
    {
        var fields = FormBuilder.Build(
            new[] { UploadFile.FromBytes(new byte[1]) },
            new Dictionary<string, object?> { ["filename"] = "from-params", ["STOREFILE"] = "false", ["Other"] = "x" },
            new ConvertOptions { FileName = "typed", StoreFile = true });

        var texts = fields.OfType<TextField>().Select(f => f.Name + "=" + f.Value).ToList();
        Assert.Equal(new[] { "Other=x", "FileName=typed", "StoreFile=true" }, texts);
    }

    [Fact]
    public void StoreFile_false_removes_a_StoreFile_parameter_and_sends_nothing()
    {
        var fields = FormBuilder.Build(
            new[] { UploadFile.FromBytes(new byte[1]) },
            new Dictionary<string, object?> { ["StoreFile"] = true },
            new ConvertOptions { StoreFile = false });

        Assert.DoesNotContain(fields, f => f.Name == "StoreFile");
    }

    [Fact]
    public void Without_typed_options_the_parameters_are_sent_as_given()
    {
        var fields = FormBuilder.Build(
            new[] { UploadFile.FromBytes(new byte[1]) },
            new Dictionary<string, object?> { ["FileName"] = "p", ["StoreFile"] = true },
            new ConvertOptions());

        Assert.Equal(new[] { "FileName=p", "StoreFile=true" }, fields.OfType<TextField>().Select(f => f.Name + "=" + f.Value));
    }

    public static TheoryData<object, string> Numbers() => new()
    {
        { 72, "72" },
        { -3L, "-3" },
        { (byte)7, "7" },
        { (ushort)8, "8" },
        { 18446744073709551615UL, "18446744073709551615" },
        { 1.5, "1.5" },
        { 0.1, "0.1" },
        { 1e-7, "0.0000001" },
        { -2.5e-7, "-0.00000025" },
        { 1e21, "1000000000000000000000" },
        { 123456789012.5, "123456789012.5" },
        { 0.0, "0" },
        { -0.0, "0" },
        { 0.1f, "0.1" },
        { 3e-5f, "0.00003" },
        { 2.50m, "2.50" },
        { true, "true" },
        { false, "false" },
        { "1-3,5", "1-3,5" },
        { string.Empty, string.Empty },
    };

    [Theory]
    [MemberData(nameof(Numbers))]
    public void Values_are_sent_as_plain_text(object value, string expected)
    {
        Assert.Equal(expected, ParameterValues.ToText(value, "P"));
    }

    [Fact]
    public void Numbers_ignore_the_current_culture()
    {
        var saved = System.Globalization.CultureInfo.CurrentCulture;
        try
        {
            System.Globalization.CultureInfo.CurrentCulture = new System.Globalization.CultureInfo("de-DE");
            Assert.Equal("1.5", ParameterValues.ToText(1.5, "P"));
            Assert.Equal("2.25", ParameterValues.ToText(2.25m, "P"));
        }
        finally
        {
            System.Globalization.CultureInfo.CurrentCulture = saved;
        }
    }

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    [InlineData(double.NegativeInfinity)]
    public void Non_finite_numbers_are_refused(double value)
    {
        Assert.Throws<ArgumentException>(() => ParameterValues.ToText(value, "P"));
    }

    [Fact]
    public void Unsupported_value_types_are_refused_with_the_parameter_name()
    {
        var e = Assert.Throws<ArgumentException>(() => ParameterValues.ToText(new DateTime(2026, 1, 1), "When"));
        Assert.Contains("When", e.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("report.docx", "report.docx")]
    [InlineData("/tmp/in/report.docx", "report.docx")]
    [InlineData("C:\\Users\\me\\report.docx", "report.docx")]
    [InlineData("../../My Photo (1).png", "My Photo (1).png")]
    public void A_path_sends_only_its_last_part(string path, string expected)
    {
        Assert.Equal(expected, UploadFile.FromPath(path).Name);
    }

    [Theory]
    [InlineData(null, "file")]
    [InlineData("", "file")]
    [InlineData("notes.txt", "notes.txt")]
    [InlineData("folder/notes.txt", "notes.txt")]
    [InlineData("folder/", "file")]
    public void Bytes_and_streams_are_named_file_unless_given_a_name(string? name, string expected)
    {
        Assert.Equal(expected, UploadFile.FromBytes(new byte[1], name).Name);
        Assert.Equal(expected, UploadFile.FromStream(new MemoryStream(new byte[1]), name).Name);
    }

    [Fact]
    public void Bad_inputs_are_argument_errors()
    {
        Assert.ThrowsAny<ArgumentException>(() => UploadFile.FromPath(string.Empty));
        Assert.ThrowsAny<ArgumentException>(() => UploadFile.FromPath("folder/"));
        Assert.ThrowsAny<ArgumentException>(() => UploadFile.FromPath((string)null!));
        Assert.ThrowsAny<ArgumentException>(() => UploadFile.FromBytes(null!));
        Assert.ThrowsAny<ArgumentException>(() => UploadFile.FromStream(null!));
    }

    private static async Task<(byte[] Bytes, string Text)> BodyAsync(IReadOnlyList<UploadFile> files)
    {
        var content = new MultipartBody(FormBuilder.Build(files, null, null));
        var bytes = await content.ReadAsByteArrayAsync();
        return (bytes, Encoding.UTF8.GetString(bytes));
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            if (haystack.AsSpan(i, needle.Length).SequenceEqual(needle))
            {
                return i;
            }
        }

        return -1;
    }
}
