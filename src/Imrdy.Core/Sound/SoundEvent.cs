namespace Imrdy.Core.Sound;

/// <summary>
/// 7 sound events matching pack.json folder structure.
/// </summary>
public enum SoundEvent
{
    SessionStart,
    GettingToWork,
    NeedsYou,
    Forgotten,
    Finished,
    SessionEnd,
    Combo,
}

public static class SoundEventExtensions
{
    private static readonly Dictionary<SoundEvent, string> FolderNames = new()
    {
        [SoundEvent.SessionStart] = "session_start",
        [SoundEvent.GettingToWork] = "getting_to_work",
        [SoundEvent.NeedsYou] = "needs_you",
        [SoundEvent.Forgotten] = "forgotten",
        [SoundEvent.Finished] = "finished",
        [SoundEvent.SessionEnd] = "session_end",
        [SoundEvent.Combo] = "combo",
    };

    /// <summary>
    /// Gets the pack.json folder name for this sound event.
    /// </summary>
    public static string ToFolderName(this SoundEvent soundEvent)
    {
        return FolderNames[soundEvent];
    }

    /// <summary>
    /// Parses a folder name string to a SoundEvent.
    /// Returns null if the folder name is not recognized.
    /// </summary>
    public static SoundEvent? FromFolderName(string folderName)
    {
        foreach (var (key, value) in FolderNames)
        {
            if (string.Equals(value, folderName, StringComparison.OrdinalIgnoreCase))
            {
                return key;
            }
        }

        return null;
    }
}
