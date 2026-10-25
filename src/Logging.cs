using System;
using System.IO;
using System.Text;

namespace FpsSocdTool
{
    internal static class Log
    {
        private static readonly object Gate = new object();
        private static string _filePath = string.Empty;
        private static bool _enabled;

        public static string FilePath
        {
            get { return _filePath; }
        }

        public static void Init(string filePath)
        {
            _filePath = filePath ?? string.Empty;
            _enabled = _filePath.Length > 0;
            if (!_enabled)
            {
                return;
            }

            try
            {
                string directory = Path.GetDirectoryName(_filePath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                RotateIfNeeded();
            }
            catch
            {
                // Logging must never break the tool.
            }
        }

        public static void Info(string message)
        {
            Write("INFO", message);
        }

        public static void Warn(string message)
        {
            Write("WARN", message);
        }

        public static void Error(string message)
        {
            Write("ERROR", message);
        }

        public static void Error(string context, Exception exception)
        {
            if (exception == null)
            {
                Error(context);
                return;
            }

            Write("ERROR", context + ": " + exception.GetType().Name + ": " + exception.Message +
                Environment.NewLine + exception.StackTrace);
        }

        private static void Write(string level, string message)
        {
            if (!_enabled)
            {
                return;
            }

            try
            {
                string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " [" + level + "] " +
                    (message ?? string.Empty) + Environment.NewLine;
                lock (Gate)
                {
                    File.AppendAllText(_filePath, line, new UTF8Encoding(false));
                }
            }
            catch
            {
                // Logging must never break the tool.
            }
        }

        private static void RotateIfNeeded()
        {
            try
            {
                if (!File.Exists(_filePath))
                {
                    return;
                }

                FileInfo info = new FileInfo(_filePath);
                if (info.Length < 2 * 1024 * 1024)
                {
                    return;
                }

                string oldPath = _filePath + ".old";
                if (File.Exists(oldPath))
                {
                    File.Delete(oldPath);
                }

                File.Move(_filePath, oldPath);
            }
            catch
            {
                // Ignore rotation failures; new messages still append.
            }
        }
    }
}
