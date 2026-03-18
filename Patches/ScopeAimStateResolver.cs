using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using UnityEngine;

namespace PiPDisabler
{
    internal enum ScopeSubScopeKind
    {
        Unknown = 0,
        Optic = 1,
        IntegratedIrons = 2,
    }

    internal sealed class ScopeAimStateSnapshot
    {
        public Weapon Weapon;
        public int GlobalAimIndex;
        public OpticSight ActiveOptic;
        public object SightComponent;
        public int ScopesCount;
        public int SelectedScopeIndex;
        public Transform SightRoot;
        public Transform ActiveAimTransform;
        public string ActiveAimPath;
        public List<Transform> LocalAimEntries;
        public ScopeSubScopeKind Kind;
        public string ClassificationReason;
        public string GlobalMappingSummary;
    }

    internal static class ScopeAimStateResolver
    {
        private static Type _sightComponentType;
        private static bool _sightComponentTypeSearched;
        private static PropertyInfo _selectedScopeIndexProp;
        private static PropertyInfo _scopesCountProp;
        private static PropertyInfo _aimIndexProp;

        internal static ScopeAimStateSnapshot Resolve(OpticSight os)
        {
            if (os == null) return null;

            var snapshot = new ScopeAimStateSnapshot
            {
                ActiveOptic = os,
                LocalAimEntries = new List<Transform>(),
                Kind = ScopeSubScopeKind.Unknown,
                ClassificationReason = "unresolved",
                ActiveAimPath = "(none)",
                GlobalMappingSummary = "(unavailable)"
            };

            try
            {
                snapshot.Weapon = GetCurrentWeapon();
                snapshot.GlobalAimIndex = GetAimIndex(snapshot.Weapon);
                snapshot.SightComponent = ResolveSightComponent(os);
                snapshot.ScopesCount = GetScopesCount(snapshot.SightComponent);
                snapshot.SelectedScopeIndex = GetSelectedScopeIndex(snapshot.SightComponent, snapshot.ScopesCount);
                snapshot.SightRoot = ResolveSightRoot(os);
                snapshot.LocalAimEntries = CollectAimEntries(snapshot.SightRoot);

                if (snapshot.LocalAimEntries.Count > 0 &&
                    snapshot.SelectedScopeIndex >= 0 &&
                    snapshot.SelectedScopeIndex < snapshot.LocalAimEntries.Count)
                {
                    snapshot.ActiveAimTransform = snapshot.LocalAimEntries[snapshot.SelectedScopeIndex];
                    snapshot.ActiveAimPath = ScopeHierarchy.GetRelativePath(snapshot.ActiveAimTransform, snapshot.SightRoot);
                }

                snapshot.Kind = Classify(os, snapshot, out var reason);
                snapshot.ClassificationReason = reason;
                snapshot.GlobalMappingSummary = BuildGlobalMappingSummary(snapshot.Weapon, snapshot.SightComponent, snapshot.GlobalAimIndex);
            }
            catch (Exception ex)
            {
                snapshot.ClassificationReason = $"exception: {ex.Message}";
            }

            return snapshot;
        }

        internal static int GetCurrentWeaponAimIndex()
        {
            return GetAimIndex(GetCurrentWeapon());
        }

        private static ScopeSubScopeKind Classify(OpticSight os, ScopeAimStateSnapshot snapshot, out string reason)
        {
            var opticTransforms = CollectOpticScopeTransforms(snapshot.SightRoot);

            if (snapshot.ActiveAimTransform != null)
            {
                for (int i = 0; i < opticTransforms.Count; i++)
                {
                    if (opticTransforms[i] == snapshot.ActiveAimTransform)
                    {
                        reason = $"active aim matches optic ScopeTransform '{snapshot.ActiveAimTransform.name}'";
                        return ScopeSubScopeKind.Optic;
                    }
                }

                if (snapshot.LocalAimEntries.Count > 1 && opticTransforms.Count > 0)
                {
                    reason = $"active aim '{snapshot.ActiveAimTransform.name}' is outside optic ScopeTransform set";
                    return ScopeSubScopeKind.IntegratedIrons;
                }
            }

            Transform ownScopeTransform = null;
            try { ownScopeTransform = os.ScopeTransform; } catch { }

            if (ownScopeTransform != null &&
                snapshot.ActiveAimTransform != null &&
                ownScopeTransform == snapshot.ActiveAimTransform)
            {
                reason = $"active aim matches active OpticSight.ScopeTransform '{ownScopeTransform.name}'";
                return ScopeSubScopeKind.Optic;
            }

            if (snapshot.ScopesCount <= 1)
            {
                reason = "single-scope sight";
                return ScopeSubScopeKind.Optic;
            }

            reason = "no optic/integrated-irons classification match";
            return ScopeSubScopeKind.Unknown;
        }

        private static Transform ResolveSightRoot(OpticSight os)
        {
            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (scopeRoot == null) return os.transform;
            if (scopeRoot.parent != null) return scopeRoot.parent;
            return scopeRoot;
        }

        private static List<Transform> CollectAimEntries(Transform sightRoot)
        {
            var result = new List<Transform>();
            if (sightRoot == null) return result;

            var all = sightRoot.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < all.Length; i++)
            {
                var t = all[i];
                if (t == null || t == sightRoot) continue;

                string name = t.name ?? string.Empty;
                if (name.IndexOf("aim_camera", StringComparison.OrdinalIgnoreCase) < 0) continue;

                result.Add(t);
            }

            result.Sort(CompareAimTransforms);
            return result;
        }

        private static List<Transform> CollectOpticScopeTransforms(Transform sightRoot)
        {
            var result = new List<Transform>();
            if (sightRoot == null) return result;

            var optics = sightRoot.GetComponentsInChildren<OpticSight>(true);
            for (int i = 0; i < optics.Length; i++)
            {
                var optic = optics[i];
                if (optic == null) continue;

                Transform scopeTransform = null;
                try { scopeTransform = optic.ScopeTransform; } catch { }
                if (scopeTransform == null || result.Contains(scopeTransform)) continue;

                result.Add(scopeTransform);
            }

            return result;
        }

        private static int CompareAimTransforms(Transform a, Transform b)
        {
            int numA = ParseTrailingNumber(a != null ? a.name : null);
            int numB = ParseTrailingNumber(b != null ? b.name : null);
            int compare = numA.CompareTo(numB);
            if (compare != 0) return compare;
            return string.CompareOrdinal(a != null ? a.name : null, b != null ? b.name : null);
        }

        private static int ParseTrailingNumber(string value)
        {
            if (string.IsNullOrEmpty(value)) return int.MaxValue;

            int end = value.Length - 1;
            while (end >= 0 && char.IsDigit(value[end])) end--;
            if (end == value.Length - 1) return int.MaxValue;

            string digits = value.Substring(end + 1);
            return int.TryParse(digits, out var parsed) ? parsed : int.MaxValue;
        }

        private static object ResolveSightComponent(OpticSight os)
        {
            try { return FovController.GetSightComponentForOptic(os); }
            catch { return null; }
        }

        private static int GetSelectedScopeIndex(object sightComponent, int scopesCount)
        {
            if (sightComponent == null) return 0;

            try
            {
                if (_selectedScopeIndexProp == null)
                {
                    _selectedScopeIndexProp = sightComponent.GetType().GetProperty("SelectedScopeIndex",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (_selectedScopeIndexProp != null)
                {
                    int value = (int)_selectedScopeIndexProp.GetValue(sightComponent, null);
                    if (scopesCount > 0)
                        value = Mathf.Abs(value) % scopesCount;
                    return value;
                }
            }
            catch { }

            return 0;
        }

        private static int GetScopesCount(object sightComponent)
        {
            if (sightComponent == null) return 1;

            try
            {
                if (_scopesCountProp == null)
                {
                    _scopesCountProp = sightComponent.GetType().GetProperty("ScopesCount",
                        BindingFlags.Public | BindingFlags.Instance);
                }

                if (_scopesCountProp != null)
                    return Math.Max(1, (int)_scopesCountProp.GetValue(sightComponent, null));
            }
            catch { }

            return 1;
        }

        private static Weapon GetCurrentWeapon()
        {
            var player = PiPDisablerPlugin.GetLocalPlayer();
            var firearmController = player != null ? player.HandsController as Player.FirearmController : null;
            return firearmController != null ? firearmController.Item as Weapon : null;
        }

        private static int GetAimIndex(Weapon weapon)
        {
            if (weapon == null) return 0;

            try
            {
                if (_aimIndexProp == null)
                {
                    _aimIndexProp = typeof(Weapon).GetProperty("AimIndex", BindingFlags.Public | BindingFlags.Instance);
                }

                if (_aimIndexProp != null)
                {
                    object reactive = _aimIndexProp.GetValue(weapon, null);
                    if (reactive != null)
                    {
                        var valueProp = reactive.GetType().GetProperty("Value", BindingFlags.Public | BindingFlags.Instance);
                        if (valueProp != null)
                            return (int)valueProp.GetValue(reactive, null);
                    }
                }
            }
            catch { }

            return 0;
        }

        private static string BuildGlobalMappingSummary(Weapon weapon, object targetSightComponent, int activeGlobalAimIndex)
        {
            if (weapon == null) return "(no weapon)";

            try
            {
                var modsProp = weapon.GetType().GetProperty("Mods", BindingFlags.Public | BindingFlags.Instance);
                var mods = modsProp != null ? modsProp.GetValue(weapon, null) as IEnumerable<Item> : null;
                if (mods == null) return "(weapon mods unavailable)";

                int globalIndex = 0;
                var chunks = new List<string>();

                foreach (var mod in mods)
                {
                    if (mod == null) continue;

                    var sightComponent = GetSightComponent(mod);
                    if (sightComponent == null) continue;

                    int count = GetScopesCount(sightComponent);
                    int end = globalIndex + count - 1;
                    bool isTarget = ReferenceEquals(sightComponent, targetSightComponent);
                    string label = mod.Id ?? "sight";
                    chunks.Add($"{(isTarget ? "*" : string.Empty)}{label}[{globalIndex}-{end}]");
                    globalIndex += count;
                }

                if (chunks.Count == 0) return "(no sight mapping)";
                return $"aimIndex={activeGlobalAimIndex} map={string.Join(", ", chunks)}";
            }
            catch (Exception ex)
            {
                return $"(mapping failed: {ex.Message})";
            }
        }

        private static object GetSightComponent(Item item)
        {
            if (item == null) return null;

            try
            {
                if (!_sightComponentTypeSearched)
                {
                    _sightComponentTypeSearched = true;
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        _sightComponentType = asm.GetType("EFT.InventoryLogic.SightComponent");
                        if (_sightComponentType != null) break;
                    }

                    if (_sightComponentType == null)
                    {
                        foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            foreach (var type in asm.GetTypes())
                            {
                                if (type.Name == "SightComponent")
                                {
                                    _sightComponentType = type;
                                    break;
                                }
                            }

                            if (_sightComponentType != null) break;
                        }
                    }
                }

                if (_sightComponentType == null) return null;

                var method = item.GetType().GetMethod("GetItemComponent", BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return null;

                return method.MakeGenericMethod(_sightComponentType).Invoke(item, null);
            }
            catch
            {
                return null;
            }
        }
    }
}
