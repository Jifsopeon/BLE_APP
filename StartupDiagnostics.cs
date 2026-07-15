using System.Diagnostics;

namespace BLE_APP
{
    internal static class StartupDiagnostics
    {
        public static string LogPath { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "BLE_APP",
            "startup.log");

        public static void Reset()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.WriteAllText(LogPath, string.Empty);
        }

        public static void Log(string message)
        {
            Debug.WriteLine(message);
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        }
    }
}
