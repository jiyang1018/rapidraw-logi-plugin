namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Diagnostic log for the plugin, written to
    /// <c>%TEMP%\logi-rapidraw-plugin.log</c> and mirrored into the Logi Plugin
    /// Service's own log once the plugin has loaded.
    /// </summary>
    /// <remarks>
    /// <c>PluginLog</c> is not part of PluginApi.dll (it is a helper the project
    /// template generates into your own source), so this uses <c>Plugin.Log</c>
    /// directly plus a plain file. Every method swallows its own exceptions:
    /// logging must never be the reason a dial stops working.
    /// </remarks>
    internal static class Diag
    {
        private const Int64 MaxBytes = 512 * 1024;

        private static readonly Object Gate = new Object();
        private static readonly String Path =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "logi-rapidraw-plugin.log");

        /// <summary>Set false to silence the file log entirely.</summary>
        internal static Boolean Enabled { get; set; } = true;

        internal static String FilePath => Path;

        private static PluginLogFile _sink;

        internal static void Attach(PluginLogFile log)
        {
            _sink = log;
            Info("Log attached. This file: " + Path);
        }

        internal static void Info(String message) => Write("INFO ", message);

        internal static void Warning(String message) => Write("WARN ", message);

        internal static void Error(String message) => Write("ERROR", message);

        internal static void Error(String message, Exception ex) =>
            Write("ERROR", message + " :: " + ex.GetType().Name + ": " + ex.Message);

        private static void Write(String level, String message)
        {
            var sink = _sink;
            if (sink != null)
            {
                try
                {
                    switch (level)
                    {
                        case "WARN ": sink.Warning(message); break;
                        case "ERROR": sink.Error(message); break;
                        default: sink.Info(message); break;
                    }
                }
                catch
                {
                    // Never let the SDK logger take the plugin down with it.
                }
            }

            if (!Enabled)
            {
                return;
            }

            try
            {
                lock (Gate)
                {
                    var file = new FileInfo(Path);
                    if (file.Exists && file.Length > MaxBytes)
                    {
                        file.Delete();
                    }

                    File.AppendAllText(
                        Path,
                        $"[{DateTime.Now:HH:mm:ss}] {level} {message}{Environment.NewLine}",
                        new UTF8Encoding(false));
                }
            }
            catch
            {
                // A full disk or a locked file is not worth propagating.
            }
        }
    }
}
