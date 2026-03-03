using System;
using System.Collections.Generic;
using EFT.CameraControl;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Scope whitelist system. When enabled, only scopes whose identifier
    /// appears in the whitelist will have mod effects applied. All others
    /// are bypassed (vanilla PiP behaviour).
    ///
    /// The scope identifier is the name of the scope_* object under mod_scope
    /// in the weapon hierarchy, with the "(Clone)" suffix stripped:
    ///
    ///   mod_scope_000/
    ///     scope_dovetail_mosin_scope_pu_35(Clone)   → "scope_dovetail_mosin_scope_pu_35"
    ///
    /// This is the same node that FindScopeRoot() returns in most cases.
    ///
    /// Use the configurable WhitelistToggleKey while scoped to add/remove
    /// the current scope from the whitelist at runtime.
    /// </summary>
    internal static class ScopeWhitelist
    {
        /// <summary>
        /// Returns the identifier for the currently active scope.
        /// Walks the hierarchy to find the scope root (the scope_* object
        /// under mod_scope) and strips the "(Clone)" suffix.
        /// </summary>
        public static string GetScopeIdentifier(OpticSight os)
        {
            if (os == null) return null;

            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (scopeRoot == null) return null;

            return StripCloneSuffix(scopeRoot.name);
        }

        /// <summary>
        /// Returns true if the given scope name is in the whitelist.
        /// When the whitelist feature is disabled, always returns true (all scopes pass).
        /// An empty whitelist with the feature enabled means NO scopes pass.
        /// </summary>
        public static bool IsWhitelisted(string scopeName)
        {
            if (!ScopeHousingMeshSurgeryPlugin.EnableScopeWhitelist.Value)
                return true;

            if (string.IsNullOrWhiteSpace(scopeName)) return false;

            string csv = ScopeHousingMeshSurgeryPlugin.ScopeWhitelistEntries.Value;
            if (string.IsNullOrWhiteSpace(csv)) return false;

            string lower = scopeName.ToLowerInvariant();
            foreach (var entry in csv.Split(','))
            {
                string e = entry.Trim().ToLowerInvariant();
                if (e.Length > 0 && lower == e)
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Adds or removes a scope name from the whitelist config entry.
        /// Returns true if the scope was added, false if it was removed.
        /// </summary>
        public static bool Toggle(string scopeName)
        {
            if (string.IsNullOrWhiteSpace(scopeName)) return false;

            string csv = ScopeHousingMeshSurgeryPlugin.ScopeWhitelistEntries.Value ?? "";
            var entries = new List<string>();
            foreach (var part in csv.Split(','))
            {
                var trimmed = part.Trim();
                if (trimmed.Length > 0) entries.Add(trimmed);
            }

            string lower = scopeName.ToLowerInvariant();
            int idx = -1;
            for (int i = 0; i < entries.Count; i++)
            {
                if (entries[i].ToLowerInvariant() == lower)
                {
                    idx = i;
                    break;
                }
            }

            if (idx >= 0)
            {
                entries.RemoveAt(idx);
                ScopeHousingMeshSurgeryPlugin.ScopeWhitelistEntries.Value =
                    string.Join(",", entries.ToArray());
                return false; // removed
            }
            else
            {
                entries.Add(scopeName);
                ScopeHousingMeshSurgeryPlugin.ScopeWhitelistEntries.Value =
                    string.Join(",", entries.ToArray());
                return true; // added
            }
        }

        private static string StripCloneSuffix(string name)
        {
            if (name == null) return null;
            const string suffix = "(Clone)";
            if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return name.Substring(0, name.Length - suffix.Length);
            return name;
        }
    }
}
