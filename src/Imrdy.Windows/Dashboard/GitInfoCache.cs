using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Imrdy.Core.Display;
using Microsoft.Extensions.Logging;

namespace Imrdy.Windows.Dashboard;

/// <summary>
/// Per-cwd cache of git branch + dirty-count, populated via a synchronous shell-out to
/// <c>git status --porcelain --branch</c>. The caller wraps <see cref="FetchAndStore"/>
/// in <c>Task.Run</c> to keep the shell-out off the UI thread.
/// </summary>
/// <remarks>
/// TTL is 30 seconds. After expiry, <see cref="TryGetCached"/> returns null and the caller
/// should kick off a fresh fetch. Thread-safe: <see cref="ConcurrentDictionary"/> provides
/// snapshot-consistent reads without explicit locking.
/// </remarks>
internal sealed class GitInfoCache
{
    private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(30);

    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.OrdinalIgnoreCase);
    private readonly ILogger _logger;

    public GitInfoCache(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<GitInfoCache>();
    }

    /// <summary>
    /// Returns the cached <see cref="GitInfo"/> for <paramref name="cwd"/> when the entry
    /// exists AND was stored within the last 30 seconds; otherwise returns <c>null</c>.
    /// Safe to call from the UI thread — no I/O, no blocking.
    /// </summary>
    public GitInfo? TryGetCached(string cwd)
    {
        if (!_cache.TryGetValue(cwd, out var entry))
            return null;

        if (DateTimeOffset.UtcNow - entry.StoredAt >= Ttl)
            return null;

        return entry.Info;
    }

    /// <summary>
    /// Shells out to <c>git status --porcelain --branch</c> synchronously, parses the output,
    /// and stores the result (or <c>null</c> on any failure) in the cache.
    /// MUST be called from a background thread — 2s WaitForExit can block.
    ///
    /// All exceptions are caught internally and logged at Debug; this method never throws.
    /// Because exceptions are caught, the <c>Task.Run(() => FetchAndStore(cwd))</c> continuation
    /// in <see cref="HoverDashboardController"/> always runs to completion — no
    /// <c>TaskContinuationOptions.OnlyOnRanToCompletion</c> guard is needed.
    /// </summary>
    public void FetchAndStore(string cwd)
    {
        try
        {
            var psi = new ProcessStartInfo("git", "status --porcelain --branch")
            {
                WorkingDirectory = cwd,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
                UseShellExecute = false,
            };

            using var process = Process.Start(psi);
            if (process is null)
            {
                _logger.LogDebug("GitInfoCache: Process.Start returned null for cwd={Cwd}", cwd);
                StoreNull(cwd);
                return;
            }

            // Read all stdout before WaitForExit to avoid deadlock on large output.
            var output = process.StandardOutput.ReadToEnd();

            if (!process.WaitForExit(2000))
            {
                _logger.LogDebug("GitInfoCache: git timed out after 2s for cwd={Cwd}, killing", cwd);
                try { process.Kill(); } catch { /* TOCTOU: process may have already exited */ }
                StoreNull(cwd);
                return;
            }

            var info = ParseGitOutput(output);
            _cache[cwd] = new CacheEntry(info, DateTimeOffset.UtcNow);
            _logger.LogDebug("GitInfoCache: stored branch={Branch} dirty={Dirty} for cwd={Cwd}",
                info?.Branch, info?.DirtyCount, cwd);
        }
        catch (Exception ex)
        {
            // Win32Exception from process spawn failure, IOException from stdout read,
            // InvalidOperationException from process-already-exited races after Kill, etc.
            _logger.LogDebug(ex, "GitInfoCache: fetch failed for cwd={Cwd}", cwd);
            StoreNull(cwd);
        }
    }

    private void StoreNull(string cwd)
    {
        // Storing null with a fresh timestamp prevents rapid repeated fetches on failing paths.
        _cache[cwd] = new CacheEntry(null, DateTimeOffset.UtcNow);
    }

    /// <summary>
    /// Parses <c>git status --porcelain --branch</c> output into a <see cref="GitInfo"/>.
    /// Returns <c>null</c> when the output is empty or the branch header is missing.
    /// </summary>
    internal static GitInfo? ParseGitOutput(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;

        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length == 0)
            return null;

        // First line: ## branch...remote [ahead N, behind N]
        var header = lines[0];
        if (!header.StartsWith("## ", StringComparison.Ordinal))
            return null;

        var headerBody = header[3..]; // strip leading "## "

        // Strip "No commits yet on " prefix (fresh repo)
        if (headerBody.StartsWith("No commits yet on ", StringComparison.Ordinal))
            headerBody = headerBody["No commits yet on ".Length..];

        // Branch is everything before the first "..."
        var ellipsis = headerBody.IndexOf("...", StringComparison.Ordinal);
        var branch = ellipsis >= 0 ? headerBody[..ellipsis] : headerBody;
        branch = branch.Trim();

        // Ahead/behind: use word-boundary patterns that handle both the standalone form
        // "[ahead 2]" and the comma-separated form "[ahead 2, behind 1]".
        // "ahead" is always preceded by "["; "behind" may be preceded by "[" or ", ".
        var aheadMatch = Regex.Match(header, @"\[ahead (\d+)");
        var behindMatch = Regex.Match(header, @"(?:\[|, )behind (\d+)");
        var ahead = aheadMatch.Success ? int.Parse(aheadMatch.Groups[1].Value) : 0;
        var behind = behindMatch.Success ? int.Parse(behindMatch.Groups[1].Value) : 0;

        // Dirty count: lines 2+ that are not untracked and not empty
        // Porcelain v1: each changed file has a 2-char status code followed by a space
        var dirtyCount = 0;
        for (var i = 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.Length < 2) continue;
            // Skip untracked files ("??" prefix)
            if (line[0] == '?' && line[1] == '?') continue;
            dirtyCount++;
        }

        return new GitInfo(branch, dirtyCount, ahead, behind);
    }

    private readonly record struct CacheEntry(GitInfo? Info, DateTimeOffset StoredAt);
}
