using System.Diagnostics;

namespace Moewshare.Launcher.Services;

public static class ShellHelper
{
    public static void OpenFolder(string folder)
    {
        var psi = OperatingSystem.IsWindows() ? new ProcessStartInfo("explorer.exe")
                : OperatingSystem.IsMacOS() ? new ProcessStartInfo("open")
                : new ProcessStartInfo("xdg-open");
        psi.UseShellExecute = false;
        psi.ArgumentList.Add(folder);
        using var _ = Process.Start(psi);
    }
}
