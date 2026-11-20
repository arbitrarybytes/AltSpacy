using System.Diagnostics;
using System.Reflection;

namespace AltSpacy;

public static class AppInfo
{
    public const string Name = "AltSpacy";

    public static string Version { get; } =
        (typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0").Split('+')[0];

    public static string DisplayName => $"{Name} {Version}";

    /// <summary>From &lt;Copyright&gt; in AltSpacy.csproj.</summary>
    public static string Copyright { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyCopyrightAttribute>()?.Copyright ?? "";

    public const string License = "GNU GPL v3";
    public const string LicenseUrl = "https://www.gnu.org/licenses/gpl-3.0.html";

    public static void OpenUri(string uri) => Process.Start(new ProcessStartInfo(uri) { UseShellExecute = true });
}
