using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Xunit;

namespace PdfConversionApi.Tests.Support;

/// <summary>
/// Runs <c>node ../conformance/mock-server.mjs --port 0</c> for the test run and stops it afterwards.
/// <c>NODE</c> picks another node binary.
/// </summary>
public sealed class MockServer : IAsyncLifetime
{
    private Process? _process;

    public string BaseUrl { get; private set; } = string.Empty;

    public async ValueTask InitializeAsync()
    {
        var node = Environment.GetEnvironmentVariable("NODE");
        var start = new ProcessStartInfo(string.IsNullOrEmpty(node) ? "node" : node)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(TestPaths.ConformanceDir, "mock-server.mjs"));
        start.ArgumentList.Add("--port");
        start.ArgumentList.Add("0");

        _process = Process.Start(start) ?? throw new InvalidOperationException("Could not start node.");
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginErrorReadLine();

        var firstLine = _process.StandardOutput.ReadLineAsync();
        if (await Task.WhenAny(firstLine, Task.Delay(TimeSpan.FromSeconds(30))) != firstLine)
        {
            throw new InvalidOperationException("The mock server did not start within 30 seconds.");
        }

        var line = await firstLine;
        const string Prefix = "LISTENING ";
        if (line is null || !line.StartsWith(Prefix, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Unexpected first line from the mock server: " + line);
        }

        BaseUrl = line.Substring(Prefix.Length).Trim();
        _ = _process.StandardOutput.ReadToEndAsync();
    }

    /// <summary>How many requests carried this <c>MockId</c>.</summary>
    public async Task<int> AttemptsAsync(string mockId)
    {
        using var http = new HttpClient();
        var json = await http.GetStringAsync(BaseUrl + "/__mock/attempts/" + Uri.EscapeDataString(mockId));
        return (int)JsonNode.Parse(json)!["attempts"]!;
    }

    public ValueTask DisposeAsync()
    {
        if (_process is not null)
        {
            if (!_process.HasExited)
            {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(5000);
            }

            _process.Dispose();
        }

        return ValueTask.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class MockServerGroup : ICollectionFixture<MockServer>
{
    public const string Name = "mock server";
}
