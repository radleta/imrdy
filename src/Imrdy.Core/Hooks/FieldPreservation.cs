using Imrdy.Core.State;

namespace Imrdy.Core.Hooks;

/// <summary>
/// Preserves fields across state file writes and resolves last message.
/// Port of preserveFields() and resolveLastMessage() from hook-lib.mjs.
/// </summary>
public static class FieldPreservation
{
    /// <summary>
    /// Preserves sound_pack, desktop_index, and icon_style from an existing state file
    /// when writing a new state. New values take precedence over existing ones.
    /// </summary>
    public static StateFileModel PreserveFields(StateFileModel newState, StateFileModel? existing)
    {
        if (existing is null)
        {
            return newState;
        }

        return newState with
        {
            SoundPack = newState.SoundPack ?? existing.SoundPack,
            DesktopIndex = newState.DesktopIndex ?? existing.DesktopIndex,
            IconStyle = newState.IconStyle ?? existing.IconStyle,
            LastTeammateAt = newState.LastTeammateAt ?? existing.LastTeammateAt,
        };
    }

    /// <summary>
    /// Resolves the last_message field with priority: prompt > message > previous value.
    /// Port of resolveLastMessage() from hook-lib.mjs.
    /// </summary>
    public static string ResolveLastMessage(string? prompt, string? message, string? previousMessage)
    {
        if (!string.IsNullOrEmpty(prompt))
        {
            return StateFileModel.TruncateMessage(prompt);
        }

        if (!string.IsNullOrEmpty(message))
        {
            return StateFileModel.TruncateMessage(message);
        }

        return previousMessage ?? "";
    }
}
