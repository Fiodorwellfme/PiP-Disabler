using System;
using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Swaps main camera LOD bias and culling settings with scope-appropriate values during ADS.
    ///
    /// This manager reads the scope's ScopeCameraData (FieldOfView, FarClipPlane, etc.) and
    /// applies a scoped LOD bias curve driven by the first valid world hit in front of the weapon.
    ///
    /// From Elcan ScopeCameraData:
    ///   FieldOfView = 5.75 (4x mode) / 23 (1x mode)
    ///   NearClipPlane = 0.05
    ///   FarClipPlane = 1000
    ///   OpticCullingMask = 1
    ///   OpticCullingMaskScale = 1
    ///
    /// Settings modified:
    ///   QualitySettings.lodBias          — set from the scoped distance curve
    ///   Camera.main.farClipPlane         — set to scope's FarClipPlane if greater
    ///   Camera.layerCullDistances        — adjusted for scope's culling scale
    /// </summary>
    internal static class CameraSettingsManager
    {
        // Saved original values for restore
        private static float _savedLodBias;
        private static float _savedFarClip;
        private static float[] _savedCullDistances;
        private static int _savedMaxLodLevel;
        private static bool _applied;

        // Reflection cache for ScopeCameraData fields
        private static Type _scdType;
        private static FieldInfo _scdFovField;
        private static FieldInfo _scdFarClipField;
        private static FieldInfo _scdNearClipField;
        private static FieldInfo _scdCullingMaskField;
        private static FieldInfo _scdCullingScaleField;
        private static bool _scdSearched;
        private const float RaycastMaxDistance = 2000f;
        private const float RaycastStartOffset = 0.05f;
        private const float AutoCurveMaxDistance = 400f;

        /// <summary>
        /// Apply scope-optimized camera settings for the active optic.
        /// Call on scope enter and mode switch.
        /// </summary>
        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null) return;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam == null) return;

            // Save originals (only on first apply, not re-apply from mode switch)
            if (!_applied)
            {
                _savedLodBias = QualitySettings.lodBias;
                _savedFarClip = cam.farClipPlane;
                _savedCullDistances = cam.layerCullDistances != null
                    ? (float[])cam.layerCullDistances.Clone()
                    : null;
                _savedMaxLodLevel = QualitySettings.maximumLODLevel;
                _applied = true;

                PiPDisablerPlugin.LogVerbose(
                    $"[CameraSettings] Saved: lodBias={_savedLodBias:F2} farClip={_savedFarClip:F0} " +
                    $"maxLOD={_savedMaxLodLevel}");
            }

            // Read scope's ScopeCameraData for its settings
            float scopeFov = 0f;
            float scopeFarClip = 0f;

            if (TryGetScopeCameraData(os, out scopeFov, out scopeFarClip))
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[CameraSettings] ScopeCameraData: FOV={scopeFov:F2} FarClip={scopeFarClip:F0}");
            }

            // Calculate magnification — prefer template zoom (matches HUD)
            float magnification = FovController.GetEffectiveMagnification();
            if (magnification < 0.1f)
                magnification = scopeFov > 0.1f ? 35f / scopeFov : 1f;

            // === Apply LOD bias ===
            float hitDistance;
            bool hasHitDistance;
            float newLodBias = CalculateScopedLodBias(os, out hitDistance, out hasHitDistance);
            QualitySettings.lodBias = newLodBias;

            // Force highest LOD by default unless overridden by manual max LOD level.
            int manualMaxLod = PiPDisablerPlugin.ManualMaximumLodLevel != null
                ? PiPDisablerPlugin.ManualMaximumLodLevel.Value
                : -1;
            int appliedMaxLod = manualMaxLod >= 0 ? manualMaxLod : 0;
            QualitySettings.maximumLODLevel = appliedMaxLod;

            // === Apply far clip plane ===
            if (scopeFarClip > cam.farClipPlane)
                cam.farClipPlane = scopeFarClip;

            // === Adjust layer cull distances for scope magnification ===
            // Increase cull distances proportionally so objects stay visible when zoomed
            if (_savedCullDistances != null)
            {
                float manualCullMultiplier = PiPDisablerPlugin.ManualCullingMultiplier != null
                    ? PiPDisablerPlugin.ManualCullingMultiplier.Value
                    : 0f;
                float cullingMultiplier = manualCullMultiplier > 0f
                    ? manualCullMultiplier
                    : Mathf.Max(magnification, 1f);

                float[] newCull = (float[])_savedCullDistances.Clone();
                for (int i = 0; i < newCull.Length; i++)
                {
                    if (newCull[i] > 0f)
                        newCull[i] *= cullingMultiplier;
                }
                cam.layerCullDistances = newCull;
            }

            PiPDisablerPlugin.LogInfo(
                $"[CameraSettings] Applied: lodBias {_savedLodBias:F2}→{newLodBias:F2} " +
                $"(mag={magnification:F1}x, hit={(hasHitDistance ? $"{hitDistance:F1}m" : $">{AutoCurveMaxDistance:F0}m")}) " +
                $"farClip={cam.farClipPlane:F0} maxLOD={appliedMaxLod}");
        }

        /// <summary>
        /// Update the scoped LOD bias while staying ADS.
        /// </summary>
        public static void UpdateForOptic(OpticSight os)
        {
            if (!_applied || os == null) return;

            float hitDistance;
            QualitySettings.lodBias = CalculateScopedLodBias(os, out hitDistance, out _);
        }

        /// <summary>
        /// Restore original camera settings. Call on scope exit.
        /// </summary>
        public static void Restore()
        {
            if (!_applied) return;

            QualitySettings.lodBias = _savedLodBias;
            QualitySettings.maximumLODLevel = _savedMaxLodLevel;

            var cam = PiPDisablerPlugin.GetMainCamera();            if (cam != null)
            {
                cam.farClipPlane = _savedFarClip;
                if (_savedCullDistances != null)
                    cam.layerCullDistances = _savedCullDistances;
            }

            PiPDisablerPlugin.LogInfo(
                $"[CameraSettings] Restored: lodBias={_savedLodBias:F2} " +
                $"farClip={_savedFarClip:F0} maxLOD={_savedMaxLodLevel}");

            _applied = false;
        }

        /// <summary>
        /// Try to read ScopeCameraData from the scope hierarchy via reflection.
        /// </summary>
        private static bool TryGetScopeCameraData(OpticSight os, out float fov, out float farClip)
        {
            fov = 0f;
            farClip = 0f;

            DiscoverType();

            if (_scdType == null) return false;

            try
            {
                // Find the ScopeCameraData component on the same mode as the active optic
                Component scd = os.GetComponent(_scdType);
                if (scd == null) scd = os.GetComponentInChildren(_scdType);
                if (scd == null) scd = os.GetComponentInParent(_scdType);

                // Search scope root as fallback
                if (scd == null)
                {
                    Transform root = os.transform;
                    while (root.parent != null)
                    {
                        var pn = root.parent.name ?? "";
                        if (pn.StartsWith("scope_", StringComparison.OrdinalIgnoreCase))
                        { root = root.parent; break; }
                        root = root.parent;
                    }

                    // Find ScopeCameraData on same mode
                    foreach (var comp in root.GetComponentsInChildren(_scdType, true))
                    {
                        if (IsOnSameMode(comp.transform, os.transform))
                        {
                            scd = comp;
                            break;
                        }
                    }
                }

                if (scd == null) return false;

                if (_scdFovField != null)
                    fov = (float)_scdFovField.GetValue(scd);
                if (_scdFarClipField != null)
                    farClip = (float)_scdFarClipField.GetValue(scd);

                return fov > 0.1f;
            }
            catch { return false; }
        }

        private static bool IsOnSameMode(Transform a, Transform b)
            => PiPDisablerPlugin.IsOnSameMode(a, b);

        private static float CalculateScopedLodBias(OpticSight os, out float hitDistance, out bool hasHitDistance)
        {
            float manualLodBias = PiPDisablerPlugin.GetManualLodBiasForCurrentMap();
            if (manualLodBias > 0f)
            {
                hitDistance = 0f;
                hasHitDistance = false;
                return manualLodBias;
            }

            hasHitDistance = TryGetWorldHitDistance(os, out hitDistance);
            float distanceForCurve = hasHitDistance ? hitDistance : AutoCurveMaxDistance;
            return PiPDisablerPlugin.EvaluateAutoLodBiasForDistance(distanceForCurve);
        }

        private static bool TryGetWorldHitDistance(OpticSight os, out float hitDistance)
        {
            hitDistance = 0f;
            if (os == null) return false;
            if (!TryGetWeaponRay(os, out var weaponRoot, out var origin, out var direction)) return false;

            var hits = Physics.RaycastAll(origin, direction, RaycastMaxDistance, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;

            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var localPlayer = PiPDisablerPlugin.GetLocalPlayer();
            foreach (var hit in hits)
            {
                var hitTransform = hit.collider != null ? hit.collider.transform : null;
                if (hitTransform == null) continue;
                if (weaponRoot != null && hitTransform.IsChildOf(weaponRoot)) continue;
                if (localPlayer != null && hitTransform.IsChildOf(localPlayer.transform)) continue;
                if (hit.distance <= 0.001f) continue;

                hitDistance = hit.distance;
                return true;
            }

            return false;
        }

        private static bool TryGetWeaponRay(OpticSight os, out Transform weaponRoot, out Vector3 origin, out Vector3 direction)
        {
            weaponRoot = FindWeaponRoot(os.transform);
            direction = os.transform.forward;
            origin = Vector3.zero;

            if (weaponRoot == null) return false;

            Transform best = null;
            float bestScore = float.MinValue;

            foreach (var candidate in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (candidate == null) continue;

                float score = ScoreWeaponRayCandidate(candidate.name);
                if (score < 0f) continue;

                Vector3 delta = candidate.position - weaponRoot.position;
                float forward = Vector3.Dot(direction, delta);
                if (forward < -0.05f) continue;

                score += forward;
                if (score <= bestScore) continue;

                best = candidate;
                bestScore = score;
            }

            if (best == null) return false;

            origin = best.position + direction * RaycastStartOffset;
            return true;
        }

        private static Transform FindWeaponRoot(Transform from)
        {
            if (from == null) return null;

            for (Transform cur = from; cur != null; cur = cur.parent)
            {
                string name = cur.name ?? string.Empty;
                if (string.Equals(name, "weapon", StringComparison.OrdinalIgnoreCase))
                    return cur;
                if (name.IndexOf("player", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("hands", StringComparison.OrdinalIgnoreCase) >= 0
                    || name.IndexOf("camera", StringComparison.OrdinalIgnoreCase) >= 0)
                    break;
            }

            return null;
        }

        private static float ScoreWeaponRayCandidate(string name)
        {
            if (string.IsNullOrEmpty(name)) return -1f;

            string lower = name.ToLowerInvariant();
            if (lower.Contains("fireport")) return 100f;
            if (lower.Contains("muzzle")) return 90f;
            if (lower.Contains("mod_muzzle")) return 85f;
            if (lower.Contains("barrel")) return 75f;
            if (lower.Contains("weapon_ln")) return 70f;
            if (lower.Contains("front")) return 50f;
            return -1f;
        }

        private static void DiscoverType()
        {
            if (_scdSearched) return;
            _scdSearched = true;

            // Try known names
            string[] names = { "EFT.CameraControl.ScopeCameraData", "ScopeCameraData" };
            foreach (var name in names)
            {
                try
                {
                    var t = AccessTools.TypeByName(name);
                    if (t != null && typeof(MonoBehaviour).IsAssignableFrom(t))
                    {
                        CacheFields(t);
                        if (_scdFovField != null) return;
                    }
                }
                catch { }
            }

            // Assembly scan: MonoBehaviour with FieldOfView + NearClipPlane + FarClipPlane
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    foreach (var type in asm.GetTypes())
                    {
                        if (!typeof(MonoBehaviour).IsAssignableFrom(type)) continue;
                        var f1 = type.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
                        var f2 = type.GetField("NearClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        var f3 = type.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
                        if (f1 == null || f2 == null || f3 == null) continue;
                        if (f1.FieldType != typeof(float)) continue;

                        CacheFields(type);
                        PiPDisablerPlugin.LogInfo(
                            $"[CameraSettings] Found ScopeCameraData: {type.FullName}");
                        return;
                    }
                }
                catch { }
            }
        }

        private static void CacheFields(Type t)
        {
            _scdType = t;
            _scdFovField = t.GetField("FieldOfView", BindingFlags.Public | BindingFlags.Instance);
            _scdFarClipField = t.GetField("FarClipPlane", BindingFlags.Public | BindingFlags.Instance);
            _scdNearClipField = t.GetField("NearClipPlane", BindingFlags.Public | BindingFlags.Instance);
            _scdCullingMaskField = t.GetField("OpticCullingMask", BindingFlags.Public | BindingFlags.Instance);
            _scdCullingScaleField = t.GetField("OpticCullingMaskScale", BindingFlags.Public | BindingFlags.Instance);
        }
    }
}
