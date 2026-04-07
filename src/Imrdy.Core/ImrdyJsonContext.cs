using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Imrdy.Core.Hooks;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Workspace;

namespace Imrdy.Core;

[JsonSerializable(typeof(StateFileModel))]
[JsonSerializable(typeof(HookEventModel))]
[JsonSerializable(typeof(PackJson))]
[JsonSerializable(typeof(ImrdyConfig))]
[JsonSerializable(typeof(WorkspaceConfig))]
[JsonSerializable(typeof(WorkspaceEntry))]
[JsonSerializable(typeof(JsonDocument))]
[JsonSerializable(typeof(JsonElement))]
[JsonSerializable(typeof(JsonNode))]
[JsonSerializable(typeof(JsonObject))]
[JsonSerializable(typeof(JsonArray))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal partial class ImrdyJsonContext : JsonSerializerContext
{
    /// <summary>
    /// Indented serializer options using the source-generated context (trim-safe).
    /// </summary>
    private static JsonSerializerOptions? _indented;
    internal static JsonSerializerOptions Indented => _indented ??= new(Default.Options) { WriteIndented = true };
}
