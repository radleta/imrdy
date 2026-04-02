using System.Text.Json.Serialization;
using Imrdy.Core.Hooks;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Workspace;

namespace Imrdy.Core;

[JsonSerializable(typeof(StateFileModel))]
[JsonSerializable(typeof(HookEventModel))]
[JsonSerializable(typeof(PackJson))]
[JsonSerializable(typeof(SoundConfig))]
[JsonSerializable(typeof(WorkspaceConfig))]
[JsonSerializable(typeof(WorkspaceEntry))]
internal partial class ImrdyJsonContext : JsonSerializerContext
{
}
