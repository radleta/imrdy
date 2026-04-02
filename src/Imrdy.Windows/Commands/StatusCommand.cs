using System.Text.Json;
using Imrdy.Core.State;
using Imrdy.Core.Workspace;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;

namespace Imrdy.Windows.Commands;

/// <summary>
/// Shows active sessions and workspaces.
/// Human output: Spectre Table. JSON output: array of objects.
/// </summary>
internal static class StatusCommand
{
    private static readonly string SessionsDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".imrdy", "sessions");

    public static int Run(ServiceProvider services, bool json)
    {
        var stateReader = services.GetRequiredService<StateFileReader>();
        var workspaceStore = services.GetRequiredService<WorkspaceStore>();
        var console = services.GetRequiredService<IAnsiConsole>();

        try
        {
            var sessions = Directory.Exists(SessionsDir)
                ? stateReader.ReadAllStateFiles(SessionsDir)
                : [];
            var workspaces = workspaceStore.Load();

            if (json)
            {
                return OutputJson(sessions, workspaces);
            }

            return OutputTable(console, sessions, workspaces);
        }
        catch (Exception ex)
        {
            console.MarkupLine($"[red]Error:[/] {Markup.Escape(ex.Message)}");
            return 2;
        }
    }

    private static int OutputJson(IReadOnlyList<StateFileModel> sessions, WorkspaceConfig workspaces)
    {
        var output = new
        {
            sessions = sessions.Select(s => new
            {
                session_id = s.SessionId,
                status = s.Status,
                project = s.Project,
                cwd = s.Cwd,
                desktop_index = s.DesktopIndex,
                sound_pack = s.SoundPack,
                session_name = s.SessionName,
                timestamp = s.Timestamp,
            }),
            workspaces = workspaces.Workspaces.Select(w => new
            {
                name = w.Name,
                path = w.Path,
                desktop = w.Desktop,
            }),
        };

        Console.WriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));
        return 0;
    }

    private static int OutputTable(IAnsiConsole console, IReadOnlyList<StateFileModel> sessions, WorkspaceConfig workspaces)
    {
        if (sessions.Count == 0 && workspaces.Workspaces.Count == 0)
        {
            console.MarkupLine("[dim]No active sessions or workspaces.[/]");
            return 0;
        }

        if (sessions.Count > 0)
        {
            var table = new Table();
            table.AddColumn("Session");
            table.AddColumn("Project");
            table.AddColumn("Status");
            table.AddColumn("Desktop");
            table.AddColumn("Age");
            table.AddColumn("Pack");

            foreach (var s in sessions.OrderBy(s => s.Project))
            {
                var statusColor = s.Status switch
                {
                    "busy" => "yellow",
                    "idle" => "green",
                    "attention" or "permission" => "red",
                    "end" => "dim",
                    _ => "white",
                };

                var age = DateTimeOffset.UtcNow - s.Timestamp;
                var ageStr = age.TotalMinutes < 1 ? $"{age.Seconds}s"
                    : age.TotalHours < 1 ? $"{(int)age.TotalMinutes}m"
                    : age.TotalDays < 1 ? $"{(int)age.TotalHours}h"
                    : $"{(int)age.TotalDays}d";

                var sessionDisplay = string.IsNullOrEmpty(s.SessionName)
                    ? Markup.Escape(s.SessionId[..Math.Min(8, s.SessionId.Length)])
                    : Markup.Escape(s.SessionName);

                table.AddRow(
                    sessionDisplay,
                    Markup.Escape(s.Project),
                    $"[{statusColor}]{Markup.Escape(s.Status)}[/]",
                    s.DesktopIndex?.ToString() ?? "-",
                    ageStr,
                    Markup.Escape(s.SoundPack ?? "-"));
            }

            console.MarkupLine($"[bold]Sessions ({sessions.Count}):[/]");
            console.Write(table);
        }

        if (workspaces.Workspaces.Count > 0)
        {
            var wsTable = new Table();
            wsTable.AddColumn("Name");
            wsTable.AddColumn("Path");
            wsTable.AddColumn("Desktop");

            foreach (var w in workspaces.Workspaces.OrderBy(w => w.Name))
            {
                wsTable.AddRow(
                    Markup.Escape(w.Name),
                    Markup.Escape(w.Path),
                    w.Desktop.ToString());
            }

            console.WriteLine();
            console.MarkupLine($"[bold]Workspaces ({workspaces.Workspaces.Count}):[/]");
            console.Write(wsTable);
        }

        return 0;
    }
}
