using System.IO;
using System.Runtime.CompilerServices;

namespace AltSpacy.Tests;

internal static class TestSetup
{
    /// <summary>Keeps test runs from writing into the real %APPDATA%\AltSpacy\altspacy.log.</summary>
    [ModuleInitializer]
    internal static void RedirectLog() => Log.FilePath = Path.Combine(Path.GetTempPath(), $"altspacy-tests-{Environment.ProcessId}.log");
}
