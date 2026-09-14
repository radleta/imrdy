namespace Imrdy.Core.Publishing;

/// <summary>One publisher's beat, named by the publisher itself.</summary>
/// <param name="Name">
/// The machine name written inside the beat — the same value the publisher stamps into
/// <c>origin_machine</c>. For a timestamp-only beat from a publisher that predates the name
/// being carried, the filename token stands in.
/// </param>
/// <param name="BeatAt">When that publisher last beat.</param>
/// <param name="NameIsToken">
/// True only for a timestamp-only beat, where <paramref name="Name"/> is the flattened token. The
/// connections window reads it to leave <c>Add…</c>'s name empty rather than seed a record that
/// would never match the publisher's real <c>origin_machine</c> (ruling r-11).
/// </param>
public sealed record MachineBeat(string Name, DateTimeOffset BeatAt, bool NameIsToken = false);

/// <summary>
/// Turns a heartbeat directory into named publishers, so a file-sink publisher can answer the
/// question "which publishers exist?" — see <c>facts.md</c> <c>f-filesink-no-socket</c>. A file
/// sink opens no socket, sends no <c>hello</c> and never reaches <c>WireListener.Health()</c>, so
/// any receiver-side surface that enumerates publishers from the listener alone is blind to it.
/// The beat is what it has instead.
/// </summary>
public static class HeartbeatMachines
{
    /// <summary>
    /// Reads the beat directory beside <paramref name="sessionsDirectory"/>, for a process with
    /// no tray. <c>imrdy links</c> needs this because its records-only fallback (r-2) builds its
    /// view model from <c>publishers.json</c> alone, and a publisher the operator never
    /// registered is not in it. The tray holds a refreshed <see cref="HeartbeatWatch"/> already
    /// and reads <see cref="HeartbeatWatch.Beats"/> directly.
    /// </summary>
    public static IReadOnlyList<MachineBeat> Read(string sessionsDirectory)
    {
        var watch = new HeartbeatWatch(PublisherHeartbeat.DirectoryFor(sessionsDirectory));
        watch.Refresh();
        return [.. watch.Beats.Values];
    }
}
