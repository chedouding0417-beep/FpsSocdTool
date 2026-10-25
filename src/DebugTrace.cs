using System;
using System.IO;
using System.Text;

namespace FpsSocdTool
{
    /// <summary>
    /// Opt-in diagnostics (--debug-input). Writes to the desktop because the
    /// normal data directory can be restricted in some environments.
    /// </summary>
    internal static class DebugTrace
    {
        internal static bool Enabled;

        private static readonly object Gate = new object();
        private static readonly string LogPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "fps-socd-debug.log");

        internal static void Reset()
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    if (File.Exists(LogPath))
                    {
                        File.Delete(LogPath);
                    }
                }
            }
            catch
            {
            }
        }

        internal static void Write(string message)
        {
            if (!Enabled)
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    File.AppendAllText(LogPath,
                        DateTime.Now.ToString("HH:mm:ss.fff") + " " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }
    }
}
