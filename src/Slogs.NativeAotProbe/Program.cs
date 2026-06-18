using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

var startedAt = DateTimeOffset.UtcNow;
var builder = WebApplication.CreateSlimBuilder(args);

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, ProbeJsonSerializerContext.Default);
});

var app = builder.Build();

app.MapGet("/health", () => new NativeAotHealthResponse(
    "ok",
    RuntimeInformation.FrameworkDescription,
    RuntimeInformation.RuntimeIdentifier,
    Environment.ProcessId,
    startedAt));

app.MapGet("/routes", () => new[] { "/health", "/routes" });

app.Run();

internal sealed record NativeAotHealthResponse(
    string Status,
    string Framework,
    string RuntimeIdentifier,
    int ProcessId,
    DateTimeOffset StartedAt);

[JsonSerializable(typeof(NativeAotHealthResponse))]
[JsonSerializable(typeof(string[]))]
internal partial class ProbeJsonSerializerContext : JsonSerializerContext;
