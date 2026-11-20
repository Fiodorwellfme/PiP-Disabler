using System;
using System.Collections.Generic;
using System.IO;

namespace PiPDisabler
{
    /// <summary>
    /// Files the mod writes for the player (own per-scope edits, measured distances, used zoom levels):
    /// BepInEx/plugins/PiP-Disabler/user-data/. They are not in the release zip, so installing an update
    /// over the old folder keeps them (delete the folder = start fresh). Files of older versions in
    /// BepInEx/config (PiP-Disabler.&lt;name&gt;) are moved here the first time they are used.
    /// </summary>
    internal static class UserData
    {
        internal const string FolderName = "user-data";
        private static readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static string Directory
        {
            get
            {
                string dll = typeof(UserData).Assembly.Location;
                string root = string.IsNullOrEmpty(dll) ? Path.Combine(BepInEx.Paths.PluginPath, "PiP-Disabler") : Path.GetDirectoryName(dll);
                return Path.Combine(root, FolderName);
            }
        }

        private static readonly string[] Files = { "scope-settings.json", "eye-distance.txt", "camera-distance.txt", "used-zoom.txt" };

        /// <summary>At start: move every old BepInEx/config file now, not only the ones this session happens to use.</summary>
        internal static void MoveOldFiles()
        {
            foreach (var f in Files) PathFor(f);
        }

        /// <summary>Full path of a user-data file (created folder, old BepInEx/config copy moved in).</summary>
        internal static string PathFor(string fileName)
        {
            string dir = Directory;
            string path = Path.Combine(dir, fileName);
            if (_checked.Add(fileName))
            {
                try
                {
                    System.IO.Directory.CreateDirectory(dir);
                    string old = Path.Combine(BepInEx.Paths.ConfigPath, "PiP-Disabler." + fileName);
                    if (File.Exists(old))
                    {
                        if (!File.Exists(path))
                        {
                            File.Move(old, path);
                            PiPDisablerPlugin.LogSource.LogInfo($"[UserData] Moved {old} → {path}");
                        }
                        else
                        {
                            PiPDisablerPlugin.LogSource.LogInfo($"[UserData] {path} already exists, old {old} left unused");
                        }
                    }
                }
                catch (Exception ex)
                {
                    PiPDisablerPlugin.LogSource.LogWarning($"[UserData] {fileName}: {ex.Message}");
                }
            }
            return path;
        }
    }
}
