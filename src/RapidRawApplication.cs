namespace Loupedeck.RapidRawPlugin
{
    using System;

    /// <summary>
    /// Binds the plugin to RapidRAW so its profile activates when RapidRAW is in
    /// the foreground. The Windows executable is <c>RapidRAW.exe</c> (Tauri
    /// package name); the tauri dev build runs under the same name.
    /// </summary>
    public class RapidRawApplication : ClientApplication
    {
        public RapidRawApplication()
        {
        }

        protected override Boolean IsProcessNameSupported(String processName) =>
            processName.ContainsNoCase("rapidraw");

        /// <summary>macOS bundle identifier from tauri.conf.json. Unverified on a Mac.</summary>
        protected override String GetBundleName() => "io.github.CyberTimon.RapidRAW";
    }
}
