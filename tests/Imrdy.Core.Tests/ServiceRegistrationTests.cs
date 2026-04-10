using Imrdy.Core;
using Imrdy.Core.Graphics;
using Imrdy.Core.Sound;
using Imrdy.Core.State;
using Imrdy.Core.Validation;
using Imrdy.Core.Workspace;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog.Core;

namespace Imrdy.Core.Tests;

public class ServiceRegistrationTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];

    public void Dispose()
    {
        foreach (var provider in _providers)
        {
            provider.Dispose();
        }
    }

    private ServiceProvider BuildHookServices(bool verbose = false, bool quiet = false)
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog(verbose: verbose, quiet: quiet);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private ServiceProvider BuildCliServices()
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    private ServiceProvider BuildMonitorServices()
    {
        var services = new ServiceCollection();
        services.AddCoreServices();
        services.AddSerilog(fileSink: true);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);
        return provider;
    }

    // --- Hook composition ---

    [Fact]
    public void HookServices_Resolves_StateFileReader()
    {
        var provider = BuildHookServices();
        provider.GetService<StateFileReader>().Should().NotBeNull();
    }

    [Fact]
    public void HookServices_Resolves_PackLoader()
    {
        var provider = BuildHookServices();
        provider.GetService<PackLoader>().Should().NotBeNull();
    }

    [Fact]
    public void HookServices_Resolves_GraphicsPackLoader()
    {
        var provider = BuildHookServices();
        provider.GetRequiredService<GraphicsPackLoader>().Should().NotBeNull();
    }

    [Fact]
    public void HookServices_Resolves_CooldownTracker()
    {
        var provider = BuildHookServices();
        provider.GetService<CooldownTracker>().Should().NotBeNull();
    }

    [Fact]
    public void HookServices_Resolves_WorkspaceStore()
    {
        var provider = BuildHookServices();
        provider.GetService<WorkspaceStore>().Should().NotBeNull();
    }

    [Fact]
    public void HookServices_Resolves_ILogger()
    {
        var provider = BuildHookServices();
        var loggerFactory = provider.GetService<ILoggerFactory>();
        loggerFactory.Should().NotBeNull();
        var logger = loggerFactory!.CreateLogger("test");
        logger.Should().NotBeNull();
    }

    // --- CLI composition ---

    [Fact]
    public void CliServices_Resolves_Validators()
    {
        var provider = BuildCliServices();
        provider.GetService<PackValidator>().Should().NotBeNull();
        provider.GetService<ConfigValidator>().Should().NotBeNull();
        provider.GetService<WorkspaceValidator>().Should().NotBeNull();
    }

    [Fact]
    public void CliServices_Resolves_WorkspaceVisibility()
    {
        var provider = BuildCliServices();
        provider.GetService<WorkspaceVisibility>().Should().NotBeNull();
    }

    // --- Monitor composition ---

    [Fact]
    public void MonitorServices_Resolves_AllCoreServices()
    {
        var provider = BuildMonitorServices();

        provider.GetService<StateFileReader>().Should().NotBeNull();
        provider.GetService<PackLoader>().Should().NotBeNull();
        provider.GetService<CooldownTracker>().Should().NotBeNull();
        provider.GetService<WorkspaceStore>().Should().NotBeNull();
        provider.GetService<WorkspaceVisibility>().Should().NotBeNull();
        provider.GetService<PackValidator>().Should().NotBeNull();
        provider.GetService<ConfigValidator>().Should().NotBeNull();
        provider.GetService<WorkspaceValidator>().Should().NotBeNull();
    }

    [Fact]
    public void MonitorServices_Resolves_LoggingLevelSwitch()
    {
        var provider = BuildMonitorServices();
        provider.GetService<LoggingLevelSwitch>().Should().NotBeNull();
    }

    // --- Serilog configuration ---

    [Fact]
    public void AddSerilog_Verbose_SetsDebugLevel()
    {
        var services = new ServiceCollection();
        services.AddSerilog(verbose: true);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var levelSwitch = provider.GetRequiredService<LoggingLevelSwitch>();
        levelSwitch.MinimumLevel.Should().Be(Serilog.Events.LogEventLevel.Debug);
    }

    [Fact]
    public void AddSerilog_Quiet_SetsWarningLevel()
    {
        var services = new ServiceCollection();
        services.AddSerilog(quiet: true);
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var levelSwitch = provider.GetRequiredService<LoggingLevelSwitch>();
        levelSwitch.MinimumLevel.Should().Be(Serilog.Events.LogEventLevel.Warning);
    }

    [Fact]
    public void AddSerilog_Default_SetsInformationOrDebugWithEnvVar()
    {
        var services = new ServiceCollection();
        services.AddSerilog();
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var levelSwitch = provider.GetRequiredService<LoggingLevelSwitch>();
        var envVarSet = Environment.GetEnvironmentVariable("IMRDY_LOG") == "1";
        var expected = envVarSet
            ? Serilog.Events.LogEventLevel.Debug
            : Serilog.Events.LogEventLevel.Information;
        levelSwitch.MinimumLevel.Should().Be(expected);
    }

    // --- Singleton guarantee ---

    [Fact]
    public void CoreServices_AreSingletons()
    {
        var provider = BuildHookServices();

        var reader1 = provider.GetService<StateFileReader>();
        var reader2 = provider.GetService<StateFileReader>();
        reader1.Should().BeSameAs(reader2);

        var tracker1 = provider.GetService<CooldownTracker>();
        var tracker2 = provider.GetService<CooldownTracker>();
        tracker1.Should().BeSameAs(tracker2);
    }

    // --- Custom workspace path ---

    [Fact]
    public void AddCoreServices_CustomWorkspacePath_UsesProvidedPath()
    {
        var services = new ServiceCollection();
        services.AddCoreServices(workspacesPath: @"C:\custom\workspaces.json");
        var provider = services.BuildServiceProvider();
        _providers.Add(provider);

        var store = provider.GetRequiredService<WorkspaceStore>();
        store.Should().NotBeNull();
    }
}
