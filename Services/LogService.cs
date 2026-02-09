using System;
using System.IO;
using System.Diagnostics;

namespace RefScrn.Services
{
    public static class LogService
    {
        private static readonly string LogDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RefScrn",
            "logs");

        private static readonly string LogFile = Path.Combine(LogDir, "app.log");

        static LogService()
        {
            try
            {
                if (!Directory.Exists(LogDir))
                {
                    Directory.CreateDirectory(LogDir);
                }
            }
            catch
            {
                // Fallback to current directory if AppData is inaccessible
            }
        }

        public static void Info(string message)
        {
            WriteLog("INFO", message);
        }

        public static void Error(string message, Exception ex = null)
        {
            var content = message;
            if (ex != null)
            {
                content += $"\nException: {ex.Message}\nStackTrace: {ex.StackTrace}";
            }
            WriteLog("ERROR", content);
        }

        private static void WriteLog(string level, string message)
        {
            var logLine = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}\n";
            Debug.WriteLine(logLine);
            
            try
            {
                File.AppendAllText(LogFile, logLine);
            }
            catch
            {
                // Ignore log failures
            }
        }

        public static string GetLogPath() => LogFile;
    }
}
