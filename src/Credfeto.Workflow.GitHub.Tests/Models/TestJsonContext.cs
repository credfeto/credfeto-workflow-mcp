using System.Text.Json.Serialization;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[JsonSerializable(typeof(TestItem))]
[JsonSerializable(typeof(TestItem[]))]
[JsonSerializable(typeof(TestVariables))]
[JsonSerializable(typeof(TestData))]
[JsonSerializable(typeof(TestCount))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class TestJsonContext : JsonSerializerContext;
