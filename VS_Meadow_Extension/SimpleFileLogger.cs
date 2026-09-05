using System;
using System.IO;

public sealed class SimpleFileLogger
{
    private static readonly Lazy<SimpleFileLogger> instance = new Lazy<SimpleFileLogger>(() => new SimpleFileLogger());
    private static readonly object lockObject = new object();
    private readonly string logFilePath;

    private SimpleFileLogger()
    {
        // Define the path for the log file
        string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MyExtensionLogs");
        Directory.CreateDirectory(logDirectory);

        // Generate a unique log file name based on the current date and time
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        logFilePath = Path.Combine(logDirectory, $"log_{timestamp}.txt");
    }

    public static SimpleFileLogger Instance => instance.Value;

    public void Log(string message)
    {
        lock (lockObject)
        {
            File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: {message}{Environment.NewLine}");
        }
    }

    public void Log(Exception ex)
    {
        lock (lockObject)
        {
            File.AppendAllText(logFilePath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss}: Exception: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}");
        }
    }
}
