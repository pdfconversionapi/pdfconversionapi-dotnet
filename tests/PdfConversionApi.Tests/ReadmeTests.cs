using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>
/// The README's code is the code the examples run: each <c>#region readme:name</c> in
/// examples/ must appear in README.md word for word.
/// </summary>
public sealed class ReadmeTests
{
    private static readonly string Readme = File.ReadAllText(Path.Combine(TestPaths.SdkRoot, "README.md")).Replace("\r\n", "\n", StringComparison.Ordinal);

    public static TheoryData<string> RegionNames()
    {
        var data = new TheoryData<string>();
        foreach (var name in Regions().Keys)
        {
            data.Add(name);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RegionNames))]
    public void The_readme_shows_the_example_as_it_ran(string name)
    {
        Assert.Contains(Regions()[name], Readme, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_csharp_block_in_the_readme_comes_from_an_example()
    {
        var regions = Regions().Values.ToList();
        var blocks = Regex.Matches(Readme, "```csharp\n(.*?)```", RegexOptions.Singleline).Select(m => m.Groups[1].Value).ToList();
        Assert.NotEmpty(blocks);
        Assert.All(blocks, block => Assert.Contains(regions, region => block.Contains(region, StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("https://pdfconversionapi.com/docs")]
    [InlineData("https://app.pdfconversionapi.com")]
    [InlineData("https://pdfconversionapi.com/pricing")]
    public void The_readme_links_the_docs_dashboard_and_pricing(string link)
    {
        Assert.Contains(link, Readme, StringComparison.Ordinal);
    }

    private static Dictionary<string, string> Regions()
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in Directory.GetFiles(Path.Combine(TestPaths.SdkRoot, "examples", "PdfConversionApi.Examples"), "*.cs"))
        {
            string? name = null;
            var lines = new List<string>();
            foreach (var line in File.ReadAllLines(file))
            {
                var start = Regex.Match(line, @"^\s*#region readme:(\S+)");
                if (start.Success)
                {
                    name = start.Groups[1].Value;
                    lines.Clear();
                }
                else if (name is not null && Regex.IsMatch(line, @"^\s*#endregion"))
                {
                    regions[name] = Dedent(lines);
                    name = null;
                }
                else if (name is not null)
                {
                    lines.Add(line);
                }
            }
        }

        return regions;
    }

    private static string Dedent(List<string> lines)
    {
        var indent = lines.Where(l => l.Trim().Length > 0).Min(l => l.Length - l.TrimStart().Length);
        return string.Join("\n", lines.Select(l => l.Length >= indent ? l.Substring(indent) : l.TrimStart())).Trim('\n');
    }
}
