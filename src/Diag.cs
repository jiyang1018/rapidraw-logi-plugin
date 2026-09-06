namespace Loupedeck.RapidRawPlugin
{
    using System;
    using System.IO;
    using System.Text;

    /// <summary>
    /// Diagnostic log for the plugin, written to <c>&lt;repo&gt;\logs\logi-rapidraw-plugin.log</c>
    /// when running sideloaded from the repo (else <c>%TEMP%</c>), and mirrored into
    /// the Logi Plugin Service's own log once the plugin has loaded.
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
        private static readonly String Path = ResolvePath();

        /// <summary>
        /// Prefer &lt;repo&gt;\logs\ so the log sits next to the sources. The repo is
        /// found through the sideload link the build writes
        /// (%LOCALAPPDATA%\Logi\LogiPluginService\Plugins\RapidRawPlugin.link, whose
        /// content is &lt;repo&gt;\bin\&lt;Config&gt;\), or failing that from the DLL's own
        /// location. The service may load the DLL from a shadow copy, which is why
        /// the link is tried first. Last resort: %TEMP%.
        /// </summary>
        private static String ResolvePath()
        {
            const String name = "logi-rapidraw-plugin.log";

            foreach (var root in CandidateRepoRoots())
            {
                try
                {
                    if (root != null && Directory.Exists(System.IO.Path.Combine(root, "src")))
                    {
                        var logs = System.IO.Path.Combine(root, "logs");
                        Directory.CreateDirectory(logs);
                        return System.IO.Path.Combine(logs, name);
                    }
                }
                catch
                {
                    // try the next candidate
                }
            }

            return System.IO.Path.Combine(System.IO.Path.GetTempPath(), name);
        }

        private static System.Collections.Generic.IEnumerable<String> CandidateRepoRoots()
        {
            String fromLink = null;
            try
            {
                // Windows: %LOCALAPPDATA%\Logi\LogiPluginService\Plugins\RapidRawPlugin.link
                // macOS:   ~/Library/Application Support/Logi/LogiPluginService/Plugins/RapidRawPlugin.link
                var candidates = new[]
                {
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "Logi", "LogiPluginService", "Plugins", "RapidRawPlugin.link"),
                    System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                        "Library", "Application Support", "Logi", "LogiPluginService", "Plugins", "RapidRawPlugin.link"),
                };
                foreach (var link in candidates)
                {
                    if (File.Exists(link))
                    {
                        var baseDir = File.ReadAllText(link).Trim().TrimEnd('\\', '/');
                        // <repo>/bin/Debug -> <repo>
                        fromLink = System.IO.Path.GetDirectoryName(System.IO.Path.GetDirectoryName(baseDir));
                        break;
                    }
                }
            }
            catch
            {
                // no link, or unreadable
            }

            if (fromLink != null)
            {
                yield return fromLink;
            }

            String fromDll = null;
            try
            {
                var dll = typeof(Diag).Assembly.Location;
                if (!String.IsNullOrEmpty(dll))
                {
                    // <repo>\bin\Debug\bin\RapidRawPlugin.dll -> <repo>
                    fromDll = new DirectoryInfo(System.IO.Path.GetDirectoryName(dll)).Parent?.Parent?.Parent?.FullName;
                }
            }
            catch
            {
                // shadow-copied or dynamic
            }

            if (fromDll != null)
            {
                yield return fromDll;
            }
        }

        /// <summary>Set false to silence the file log entirely.</summary>
        internal static Boolean Enabled { get; set; } = true;

        internal static String FilePath => Path;

        private static PluginLogFile _sink;

        internal static void Attach(PluginLogFile log)
        {
            _sink = log;
            Info("Log attached. This file: " + Path);
            try
            {
                Info("Plugin assembly: " + typeof(Diag).Assembly.Location);
            }
            catch
            {
                // informational only
            }
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
