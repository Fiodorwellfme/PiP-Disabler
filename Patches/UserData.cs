using System;
using System.IO;

namespace PiPDisabler
{
    /// <summary>
    /// Files the mod writes for the player (own per-scope edits): BepInEx/plugins/PiP-Disabler/user-data/.
    /// They are not in the release zip, so installing an update over the old folder keeps them
    /// (delete the folder = start fresh).
    /// </summary>
    internal static class UserData
    {
        internal const string FolderName = "user-data";

        internal static string Directory
        {
            get
            {
                string dll = typeof(UserData).Assembly.Location;
                string root = string.IsNullOrEmpty(dll) ? Path.Combine(BepInEx.Paths.PluginPath, "PiP-Disabler") : Path.GetDirectoryName(dll);
                return Path.Combine(root, FolderName);
            }
        }

        /// <summary>Full path of a user-data file (the folder is created if missing).</summary>
        internal static string PathFor(string fileName)
        {
            string dir = Directory;
            try { System.IO.Directory.CreateDirectory(dir); }
            catch (Exception ex) { PiPDisablerPlugin.LogSource.LogWarning($"[UserData] {dir}: {ex.Message}"); }
            return Path.Combine(dir, fileName);
        }
    }
}
