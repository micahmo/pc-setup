using System.IO;

namespace PCSetup.Services;

/// <summary>Appends to Documents\PC Setup\logs\&lt;timestamp&gt;.log and notifies the UI.</summary>
public static class Log
{
    // Documents rather than AppData: easy to find, and Explorer reliably opens it from the app.
    public static string Folder { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PC Setup", "logs");

    public static string FilePath { get; } = Path.Combine(Folder, $"{DateTime.Now:yyyyMMdd-HHmmss}.log");

    public static event Action<string>? LineWritten;

    private static readonly Lock Gate = new();

    public static void Write(string line)
    {
        var stamped = $"{DateTime.Now:HH:mm:ss}  {line}";
        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Folder);
                File.AppendAllText(FilePath, stamped + Environment.NewLine);
            }
            catch (IOException) { }
        }
        LineWritten?.Invoke(stamped);
    }
}
