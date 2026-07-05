using System.IO;

namespace Acvc.Gui.Services;

/// <summary>
/// Rolling build log at %LOCALAPPDATA%\acvc\logs — one entry per build with the
/// full spec and the outcome. The bug-report affordance opens this folder.
/// </summary>
public static class BuildLog
{
    public static string LogFolder
        => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "acvc", "logs");

    public static void Write(string carName, string specText, string outcome)
    {
        try
        {
            Directory.CreateDirectory(LogFolder);
            var path = Path.Combine(LogFolder, $"acvc-{DateTime.Now:yyyyMM}.log");
            File.AppendAllText(path,
                $"---- {DateTime.Now:yyyy-MM-dd HH:mm:ss} | {carName}\n{specText.TrimEnd()}\noutcome: {outcome}\n\n");
        }
        catch (IOException)
        {
            // Logging must never break a build.
        }
    }
}
