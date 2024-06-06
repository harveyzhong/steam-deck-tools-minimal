using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace CommonHelpers
{
    public static class Log
    {

#if DEBUG
        public static bool LogToTrace = true;
#else
        public static bool LogToTrace = false;
#endif
        public static bool LogToConsole = Environment.UserInteractive;
        public static bool LogToFile = false;
        public static bool LogToFileDebug = false;

        private static String? LogFileFolder;

        private static void EnsureLogFileFolder()
        {
            if (LogFileFolder is not null)
                return;

            var documentsFolder = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var steamControllerDocumentsFolder = Path.Combine(documentsFolder, "SteamDeckTools", "Logs");
            Directory.CreateDirectory(steamControllerDocumentsFolder);
            LogFileFolder = steamControllerDocumentsFolder;
        }

        public static void CleanupLogFiles(DateTime beforeTime)
        {
            EnsureLogFileFolder();

            if (LogFileFolder is null)
                return;

            var searchPattern = String.Format("{0}_*.log", Instance.ApplicationName);
            string[] files = Directory.GetFiles(LogFileFolder, searchPattern);

            foreach (string file in files)
            {
                FileInfo fi = new FileInfo(file);
                if (fi.LastAccessTime >= beforeTime)
                    continue;

                try
                {
                    fi.Delete();
                }
                catch (Exception ex)
                {
                    TraceException("CleanupLog", fi.Name, ex);
                }
            }
        }

        private static void WriteToLogFile(String line)
        {
            EnsureLogFileFolder();

            if (LogFileFolder is null)
                return;

            String logFile = Path.Combine(LogFileFolder, String.Format("{0}_{1}.log",
                Instance.ApplicationName, DateTime.UtcNow.ToString("yyyy-MM-dd")));

            for (int i = 0; i < 3; i++)
            {
                try
                {
                    File.AppendAllText(logFile, String.Format("{0}: {1}: {2}\r\n",
                        DateTime.UtcNow, Process.GetCurrentProcess().Id, line));
                    return;
                }
                catch (IOException)
                {
                    Thread.Sleep(0);
                }
            }
        }

        public static void TraceObject(string name, object subject)
        {
            var serialized = JsonSerializer.Serialize(
                subject,
                new JsonSerializerOptions { IncludeFields = true }
            );
            TraceLine("Object: {0}: {1}", name, serialized);
        }

        public static void TraceLine(string format, params object?[] arg)
        {
            if (!LogToTrace && !LogToConsole && !LogToFile)
                return;

            String line = string.Format(format, arg);
            if (LogToTrace)
                Trace.WriteLine(line);
            if (LogToConsole)
                Console.WriteLine(line);
            if (LogToFile)
                WriteToLogFile(line);
        }

        public static void TraceDebug(string format, params object?[] arg)
        {
            if (!LogToTrace && !LogToConsole && !LogToFileDebug)
                return;

            String line = string.Format(format, arg);
            if (LogToTrace)
                Trace.WriteLine(line);
            if (LogToConsole)
                Console.WriteLine(line);
            if (LogToFileDebug)
                WriteToLogFile(line);
        }

        public static void TraceError(string format, params object?[] arg)
        {
            String line = string.Format(format, arg);
            if (LogToTrace)
                Trace.WriteLine(line);
            if (LogToConsole)
                Console.WriteLine(line);
            if (LogToFile)
                WriteToLogFile(line);
        }

        public static void TraceException(String type, Object? name, Exception e)
        {
            TraceLine("{0}: {1}: Exception: {2}", type, name, e);
        }

        public static void TraceException(String type, Exception e)
        {
            TraceLine("{0}: Exception: {1}", type, e);
        }

        public static void DebugException(String type, Exception e)
        {
        }

        public static void DebugException(String type, Object? name, Exception e)
        {
        }
    }
}
