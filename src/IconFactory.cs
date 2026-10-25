using System;
using System.Drawing;
using System.IO;

namespace FpsSocdTool
{
    internal static class IconFactory
    {
        /// <summary>
        /// Loads the tray icon from a real .ico file. Using a file-backed icon is
        /// more reliable with the Windows shell than a runtime-generated HICON.
        /// </summary>
        internal static Icon LoadTrayIcon(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                {
                    return new Icon(path, new Size(32, 32));
                }

                Log.Warn("托盘图标文件不存在：" + path);
            }
            catch (Exception exception)
            {
                Log.Warn("读取托盘图标失败：" + exception.Message);
            }

            return (Icon)SystemIcons.Application.Clone();
        }
    }
}
