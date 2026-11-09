using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using PdfConversionApi.Tests.Support;
using Xunit;

namespace PdfConversionApi.Tests;

/// <summary>Runs every case of <c>../conformance/cases.json</c> against the mock server (format: conformance/README.md).</summary>
[Collection(MockServerGroup.Name)]
public sealed class ConformanceTests
{
    private static readonly Lazy<JsonObject> Suite = new(() =>
        (JsonObject)JsonNode.Parse(File.ReadAllText(Path.Combine(TestPaths.ConformanceDir, "cases.json")))!);

    /// <summary>Canonical error names (SPEC section 9) and the .NET type each one means.</summary>
    private static readonly Dictionary<string, Type> ErrorTypes = new()
    {
        ["ArgumentError"] = typeof(ArgumentException),
        ["PdfConversionApiError"] = typeof(PdfConversionApiException),
        ["MissingApiKeyError"] = typeof(MissingApiKeyException),
        ["StoredFileError"] = typeof(StoredFileException),
        ["ConnectionError"] = typeof(ApiConnectionException),
        ["TimeoutError"] = typeof(ApiTimeoutException),
        ["InvalidResponseError"] = typeof(InvalidResponseException),
        ["ApiError"] = typeof(ApiException),
        ["BadRequestError"] = typeof(BadRequestException),
        ["AuthenticationError"] = typeof(AuthenticationException),
        ["PermissionDeniedError"] = typeof(PermissionDeniedException),
        ["NotFoundError"] = typeof(NotFoundException),
        ["PayloadTooLargeError"] = typeof(PayloadTooLargeException),
        ["UnprocessableEntityError"] = typeof(UnprocessableEntityException),
        ["RateLimitError"] = typeof(RateLimitException),
        ["InternalServerError"] = typeof(InternalServerErrorException),
        ["ServiceUnavailableError"] = typeof(ServiceUnavailableException),
        ["GatewayTimeoutError"] = typeof(GatewayTimeoutException),
    };

    private readonly MockServer _mock;

    public ConformanceTests(MockServer mock)
    {
        _mock = mock;
    }

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in Suite.Value["cases"]!.AsArray())
        {
            data.Add((string)c!["id"]!);
        }

        return data;
    }

    [Fact]
    public void The_suite_loads_and_its_ids_are_unique()
    {
        var ids = Suite.Value["cases"]!.AsArray().Select(c => (string)c!["id"]!).ToList();
        Assert.NotEmpty(ids);
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.Equal(1, (int)Suite.Value["version"]!);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public async Task Case(string id)
    {
        var c = Suite.Value["cases"]!.AsArray().Select(n => (JsonObject)n!).Single(n => (string)n["id"]! == id);
        var expect = (JsonObject)c["expect"]!;
        var sleeps = new List<double>();
        using var client = NewClient(c["client"] as JsonObject, sleeps);
        var call = (string)c["call"]!;
        string? mockId = null;

        Exception? caught = null;
        object? value = null;
        try
        {
            switch (call)
            {
                case "convert":
                    mockId = id + "-" + Guid.NewGuid().ToString("N");
                    value = await ConvertAsync(client, c, mockId);
                    break;
                case "stored_file_flow":
                    mockId = id + "-" + Guid.NewGuid().ToString("N");
                    value = await ConvertAsync(client, c, mockId);
                    break;
                case "usage":
                    value = await client.UsageAsync();
                    break;
                case "conversions":
                    value = await client.ConversionsAsync();
                    break;
                case "delete_stored_file":
                    value = await client.DeleteStoredFileAsync((string)c["file_id"]!);
                    break;
                default:
                    Assert.Fail("Unknown call: " + call);
                    break;
            }
        }
        catch (Exception e)
        {
            caught = e;
        }

        if (expect["error"] is JsonObject error)
        {
            CheckError(caught, error);
        }
        else if (caught is not null)
        {
            throw new Xunit.Sdk.XunitException("The call raised " + caught.GetType().Name + ": " + caught.Message, caught);
        }

        if (expect["result"] is JsonObject resultExpect)
        {
            CheckResult((ConversionResult)value!, resultExpect);
        }

        if (expect["echo"] is JsonObject echoExpect)
        {
            await CheckEchoAsync((ConversionResult)value!, echoExpect);
        }

        if (expect["save_all"] is JsonObject saveAll)
        {
            await CheckSaveAllAsync((ConversionResult)value!, saveAll);
        }

        if (expect["conversions"] is JsonObject conversionsExpect)
        {
            CheckConversions((IReadOnlyList<Conversion>)value!, conversionsExpect);
        }

        if (expect["usage"] is JsonObject usageExpect)
        {
            CheckUsage((Usage)value!, usageExpect);
        }

        if (expect.ContainsKey("deleted"))
        {
            Assert.Equal((bool)expect["deleted"]!, (bool)value!);
        }

        if (call == "stored_file_flow")
        {
            await CheckStoredFileFlowAsync(client, (ConversionResult)value!, expect);
        }

        if (expect["attempts"] is JsonNode attempts)
        {
            Assert.Equal((int)attempts, await _mock.AttemptsAsync(mockId!));
        }

        if (expect["min_sleeps"] is JsonArray minSleeps)
        {
            Assert.Equal(minSleeps.Count, sleeps.Count);
            for (var i = 0; i < minSleeps.Count; i++)
            {
                Assert.True(sleeps[i] >= (double)minSleeps[i]!, $"wait {i} was {sleeps[i]} s, expected at least {minSleeps[i]} s");
            }
        }
    }

    private PdfConversionApiClient NewClient(JsonObject? spec, List<double> sleeps)
    {
        var options = new PdfConversionApiClientOptions
        {
            ApiKey = (string)Suite.Value["key"]!,
            BaseUrl = _mock.BaseUrl + ((string?)spec?["base_url_suffix"] ?? string.Empty),
            Sleep = (wait, _) =>
            {
                lock (sleeps)
                {
                    sleeps.Add(wait.TotalSeconds);
                }

                return Task.CompletedTask;
            },
        };

        var withoutKey = false;
        if (spec is not null && spec.ContainsKey("api_key"))
        {
            options.ApiKey = (string?)spec["api_key"];
            withoutKey = options.ApiKey is null;
        }

        if (spec?["max_retries"] is JsonNode maxRetries)
        {
            options.MaxRetries = (int)maxRetries;
        }

        if (spec?["timeout_seconds"] is JsonNode timeout)
        {
            options.Timeout = TimeSpan.FromSeconds((double)timeout);
        }

        return withoutKey
            ? EnvironmentScope.WithoutVariable(PdfConversionApiClient.ApiKeyEnvironmentVariable, () => new PdfConversionApiClient(options))
            : new PdfConversionApiClient(options);
    }

    private static Task<ConversionResult> ConvertAsync(PdfConversionApiClient client, JsonObject c, string mockId)
    {
        var inputs = c["files"]?.AsArray().Select(d => Input((JsonObject)d!)).ToList();

        var parameters = new Dictionary<string, object?>();
        if (c["params"] is JsonObject p)
        {
            foreach (var pair in p)
            {
                parameters[pair.Key] = ParameterValue(pair.Value);
            }
        }

        parameters["MockId"] = mockId;

        ConvertOptions? options = null;
        if (c["options"] is JsonObject o)
        {
            options = new ConvertOptions
            {
                FileName = (string?)o["file_name"],
                StoreFile = o["store_file"] is JsonNode store ? (bool)store : null,
            };
        }

        var from = (string)c["from"]!;
        var to = (string)c["to"]!;
        if (inputs is null)
        {
            // No "files" key: call without files at all.
            return client.ConvertAsync(from, to, parameters, options);
        }

        return inputs.Count == 1
            ? client.ConvertAsync(from, to, inputs[0], parameters, options)
            : client.ConvertAsync(from, to, inputs, parameters, options);
    }

    private static UploadFile Input(JsonObject descriptor)
    {
        var name = (string?)descriptor["name"];
        if (descriptor["path"] is JsonNode path)
        {
            return UploadFile.FromPath(TestPaths.Fixture((string)path!));
        }

        if (descriptor["bytes"] is JsonNode bytes)
        {
            return UploadFile.FromBytes(File.ReadAllBytes(TestPaths.Fixture((string)bytes!)), name);
        }

        if (descriptor["stream"] is JsonNode stream)
        {
            // A stream that can't seek: the SDK has to read it into memory once.
            return UploadFile.FromStream(new ForwardOnlyStream(File.ReadAllBytes(TestPaths.Fixture((string)stream!))), name);
        }

        if (descriptor["bytes_utf8"] is JsonNode text)
        {
            return UploadFile.FromBytes(Encoding.UTF8.GetBytes((string)text!), name);
        }

        throw new InvalidOperationException("Unknown input descriptor: " + descriptor.ToJsonString());
    }

    private static object? ParameterValue(JsonNode? node)
    {
        if (node is null)
        {
            return null;
        }

        if (node is JsonObject descriptor)
        {
            return Input(descriptor);
        }

        return node.GetValueKind() switch
        {
            JsonValueKind.String => (string)node!,
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => node.AsValue().TryGetValue<long>(out var whole) ? whole : (double)node,
            _ => throw new InvalidOperationException("Unsupported parameter value: " + node.ToJsonString()),
        };
    }

    private static void CheckError(Exception? caught, JsonObject error)
    {
        var typeName = (string)error["type"]!;
        Assert.True(caught is not null, "expected " + typeName + ", but the call succeeded");
        var type = ErrorTypes[typeName];
        Assert.True(type.IsInstanceOfType(caught), $"expected {typeName} ({type.Name}), got {caught!.GetType().Name}: {caught.Message}");

        var api = caught as ApiException;
        if (error["status"] is JsonNode status)
        {
            Assert.NotNull(api);
            Assert.Equal((int)status, api!.StatusCode);
        }

        if (error["message"] is JsonNode message)
        {
            Assert.Equal((string)message!, api!.ApiMessage);
        }

        if (error["message_contains"] is JsonNode contains)
        {
            Assert.Contains((string)contains!, api!.ApiMessage, StringComparison.Ordinal);
        }

        if (error["retry_after"] is JsonNode retryAfter)
        {
            Assert.Equal((double)retryAfter, api!.RetryAfter!.Value.TotalSeconds);
        }

        if (error["request_id"] is JsonNode requestId)
        {
            if ((bool)requestId)
            {
                Assert.False(string.IsNullOrEmpty(api!.RequestId));
            }
            else
            {
                Assert.Null(api!.RequestId);
            }
        }
    }

    private static void CheckResult(ConversionResult result, JsonObject expect)
    {
        if (expect["conversion_cost"] is JsonNode cost)
        {
            Assert.Equal((int)cost, result.ConversionCost);
        }

        if (expect["request_id"] is JsonNode requestId && (bool)requestId)
        {
            Assert.False(string.IsNullOrEmpty(result.RequestId));
        }

        if (expect["files"] is not JsonArray files)
        {
            return;
        }

        Assert.Equal(files.Count, result.Files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var e = (JsonObject)files[i]!;
            var actual = result.Files[i];
            if (e["name"] is JsonNode name)
            {
                Assert.Equal((string)name!, actual.Name);
            }

            if (e["ext"] is JsonNode ext)
            {
                Assert.Equal((string)ext!, actual.Extension);
            }

            if (e["size"] is JsonNode size)
            {
                Assert.Equal((long)size, actual.Size);
            }

            if (e["data_fixture"] is JsonNode fixture)
            {
                Assert.Equal(File.ReadAllBytes(TestPaths.Fixture((string)fixture!)), actual.GetBytes());
            }

            if (e["data_utf8"] is JsonNode text)
            {
                Assert.Equal(Encoding.UTF8.GetBytes((string)text!), actual.GetBytes());
            }

            if (e["stored"] is JsonNode stored && (bool)stored)
            {
                Assert.True(actual.Stored);
                Assert.False(string.IsNullOrEmpty(actual.Url));
                Assert.False(string.IsNullOrEmpty(actual.FileId));
                Assert.Throws<StoredFileException>(() => actual.GetBytes());
            }
        }
    }

    private static async Task CheckEchoAsync(ConversionResult result, JsonObject expect)
    {
        var first = result.Files[0];
        var data = first.Stored ? await first.DownloadAsync() : first.GetBytes();
        var echo = JsonNode.Parse(data)!.AsObject();
        var headers = echo["headers"]!.AsObject();

        if (expect["method"] is JsonNode method)
        {
            Assert.Equal((string)method!, (string)echo["method"]!);
        }

        if (expect["path"] is JsonNode path)
        {
            Assert.Equal((string)path!, (string)echo["path"]!);
        }

        if (expect["query"] is JsonNode query)
        {
            Assert.Equal((string)query!, (string)echo["query"]!);
        }

        if (expect["authorization"] is JsonNode authorization)
        {
            Assert.Equal((string)authorization!, (string?)headers["authorization"]);
        }

        if (expect["user_agent_prefix"] is JsonNode userAgent)
        {
            Assert.StartsWith((string)userAgent!, (string)headers["user-agent"]!, StringComparison.Ordinal);
        }

        if (expect["content_type_prefix"] is JsonNode contentType)
        {
            Assert.StartsWith((string)contentType!, (string)headers["content-type"]!, StringComparison.Ordinal);
        }

        var fields = echo["fields"]!.AsArray().Select(f => f!.AsObject()).ToList();
        if (expect["fields"] is JsonArray expectedFields)
        {
            var from = 0;
            foreach (var expectedNode in expectedFields)
            {
                var expected = expectedNode!.AsObject();
                var index = fields.FindIndex(from, f => expected.All(pair => JsonNode.DeepEquals(pair.Value, f[pair.Key])));
                Assert.True(index >= 0, "field " + expected.ToJsonString() + " not found in order in " + echo["fields"]!.ToJsonString());
                from = index + 1;
            }
        }

        if (expect["absent_fields"] is JsonArray absent)
        {
            foreach (var name in absent)
            {
                Assert.DoesNotContain(fields, f => (string?)f["name"] == (string)name!);
            }
        }
    }

    private static async Task CheckSaveAllAsync(ConversionResult result, JsonObject expect)
    {
        var dir = Path.Combine(Path.GetTempPath(), "pdfconversionapi-dotnet-" + Guid.NewGuid().ToString("N"));
        try
        {
            var paths = await result.SaveAllAsync(dir);
            Assert.Equal(expect.Count, paths.Count);
            var names = Directory.GetFiles(dir).Select(p => Path.GetFileName(p)!).OrderBy(n => n, StringComparer.Ordinal).ToList();
            Assert.Equal(expect.Select(p => p.Key).OrderBy(n => n, StringComparer.Ordinal).ToList(), names);
            foreach (var pair in expect)
            {
                Assert.Equal(Encoding.UTF8.GetBytes((string)pair.Value!), File.ReadAllBytes(Path.Combine(dir, pair.Key)));
            }
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static void CheckConversions(IReadOnlyList<Conversion> conversions, JsonObject expect)
    {
        Assert.Equal((int)expect["count"]!, conversions.Count);
        if (expect["first"] is JsonObject first)
        {
            var actual = conversions[0];
            Assert.Equal((string)first["from"]!, actual.From);
            Assert.Equal((string)first["to"]!, actual.To);
            Assert.Equal((string)first["title"]!, actual.Title);
            Assert.Equal((string)first["category"]!, actual.Category);
            Assert.Equal((string)first["inputs"]!, actual.Inputs);
        }
    }

    private static void CheckUsage(Usage usage, JsonObject expect)
    {
        Assert.Equal((string)expect["plan"]!, usage.Plan);
        Assert.Equal((long)expect["included"]!, usage.Included);
        Assert.Equal((long)expect["used"]!, usage.Used);
        Assert.Equal((long?)expect["remaining"], usage.Remaining);
        Assert.Equal((bool)expect["overage"]!, usage.Overage);
        Assert.Equal((int)expect["running"]!, usage.Running);
        Assert.Equal((int)expect["concurrent_limit"]!, usage.ConcurrentLimit);
        Assert.Equal((int)expect["max_upload_mb"]!, usage.MaxUploadMB);
        Assert.Equal(DateTimeOffset.Parse((string)expect["period_start"]!, System.Globalization.CultureInfo.InvariantCulture), usage.PeriodStart);
        Assert.Equal(DateTimeOffset.Parse((string)expect["period_end"]!, System.Globalization.CultureInfo.InvariantCulture), usage.PeriodEnd);
        Assert.Equal(TimeSpan.Zero, usage.PeriodStart.Offset);
    }

    private static async Task CheckStoredFileFlowAsync(PdfConversionApiClient client, ConversionResult result, JsonObject expect)
    {
        var file = result.File;
        var dataError = ErrorTypes[(string)expect["data_access_error"]!];
        Assert.IsAssignableFrom(dataError, Record.Exception(() => file.GetBytes()));

        var downloaded = await file.DownloadAsync();
        Assert.Equal(File.ReadAllBytes(TestPaths.Fixture((string)expect["download_fixture"]!)), downloaded);

        Assert.Equal((bool)expect["first_delete"]!, await client.DeleteStoredFileAsync(file.FileId!));
        Assert.Equal((bool)expect["second_delete"]!, await client.DeleteStoredFileAsync(file.FileId!));

        var afterDelete = ErrorTypes[(string)expect["download_after_delete_error"]!];
        Assert.IsAssignableFrom(afterDelete, await Record.ExceptionAsync(() => file.DownloadAsync()));
    }
}
