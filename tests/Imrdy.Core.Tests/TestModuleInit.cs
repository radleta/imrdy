using System.Runtime.CompilerServices;

namespace Imrdy.Core.Tests;

internal static class TestModuleInit
{
    internal static string TestHome { get; private set; } = null!;

    [ModuleInitializer]
    internal static void Init()
    {
        TestHome = Path.Combine(Path.GetTempPath(), "imrdy-core-tests-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(TestHome);
        Environment.SetEnvironmentVariable("IMRDY_HOME", TestHome);
    }
}
