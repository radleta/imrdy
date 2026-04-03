using System.Diagnostics;
using System.Text.Json;
using FluentAssertions;
using Imrdy.Integration.Tests.Helpers;
using Xunit;
using Xunit.Abstractions;

namespace Imrdy.Integration.Tests;

[Trait("Category", "Benchmark")]
public class PerformanceBenchmarks : IDisposable
{
    private readonly CliTestFixture _cli = new();
    private readonly TempDirectoryFixture _temp = new();
    private readonly ITestOutputHelper _output;

    public PerformanceBenchmarks(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task Startup_Benchmark()
    {
        const int runs = 3;
        const long thresholdMs = 500;
        var timings = new long[runs];

        for (var i = 0; i < runs; i++)
        {
            var sw = Stopwatch.StartNew();
            var (exitCode, _, _) = await _cli.RunAsync("--version", workingDirectory: _temp.Path);
            sw.Stop();
            timings[i] = sw.ElapsedMilliseconds;
            exitCode.Should().Be(0);
        }

        Array.Sort(timings);
        var median = timings[runs / 2];

        _output.WriteLine($"Startup time: {median}ms (threshold: {thresholdMs}ms)");
        _output.WriteLine($"  All runs: {string.Join(", ", timings.Select(t => $"{t}ms"))}");

        if (median > thresholdMs)
        {
            _output.WriteLine($"  WARNING: Startup time {median}ms exceeds {thresholdMs}ms threshold");
        }
    }

    [Fact]
    public async Task Hook_Benchmark()
    {
        const int runs = 3;
        const long thresholdMs = 100;
        var timings = new long[runs];

        var hookJson = JsonSerializer.Serialize(new
        {
            session_id = $"bench-{Guid.NewGuid():N}"[..32],
            hook_event_name = "Stop",
            cwd = "/d/dev/test",
        });

        // Clean up state files after benchmark
        var sessionId = JsonDocument.Parse(hookJson).RootElement.GetProperty("session_id").GetString()!;
        var statePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".imrdy", "sessions", $"{sessionId}.json");

        try
        {
            for (var i = 0; i < runs; i++)
            {
                var sw = Stopwatch.StartNew();
                var (exitCode, _, _) = await _cli.RunAsync("hook", stdin: hookJson, workingDirectory: _temp.Path);
                sw.Stop();
                timings[i] = sw.ElapsedMilliseconds;
                exitCode.Should().Be(0);
            }

            Array.Sort(timings);
            var median = timings[runs / 2];

            _output.WriteLine($"Hook time: {median}ms (threshold: {thresholdMs}ms)");
            _output.WriteLine($"  All runs: {string.Join(", ", timings.Select(t => $"{t}ms"))}");

            if (median > thresholdMs)
            {
                _output.WriteLine($"  WARNING: Hook time {median}ms exceeds {thresholdMs}ms threshold");
            }
        }
        finally
        {
            try { if (File.Exists(statePath)) File.Delete(statePath); } catch { }
        }
    }

    public void Dispose()
    {
        _temp.Dispose();
    }
}
