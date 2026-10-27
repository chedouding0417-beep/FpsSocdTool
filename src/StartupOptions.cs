using System;

namespace FpsSocdTool
{
    internal static class StartupOptions
    {
        /// <summary>Set by --start-enabled for logon validation; normal starts use config.</summary>
        internal static bool ForceStartEnabled;
    }
}
