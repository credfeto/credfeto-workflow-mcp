using System.Text.Json.Serialization;
using Credfeto.Workflow.GitHub.Interfaces.Models;

namespace Credfeto.Workflow.GitHub.Tests.Models;

[JsonSerializable(typeof(TestItem))]
[JsonSerializable(typeof(TestItem[]))]
[JsonSerializable(typeof(TestVariables))]
[JsonSerializable(typeof(TestData))]
[JsonSerializable(typeof(GraphQlResponse<TestData>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public sealed partial class TestJsonContext : JsonSerializerContext { }
