using System;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    internal static class CameraSettingsManager
    {
        private static float _savedLodBias;
        private static float _savedFarClip;
        private static float[] _savedCullDistances;
        private static int _savedMaxLodLevel;
        private static bool _applied;

        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null)
                return;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam == null)
                return;

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
                    $"[CameraSettings] Saved: lodBias={_savedLodBias:F2} farClip={_savedFarClip:F0} maxLOD={_savedMaxLodLevel}");
            }

            float scopeFov = 0f;
            float scopeFarClip = 0f;
            if (TryGetScopeCameraData(os, out scopeFov, out scopeFarClip))
            {
                PiPDisablerPlugin.LogVerbose(
                    $"[CameraSettings] ScopeCameraData: FOV={scopeFov:F2} FarClip={scopeFarClip:F0}");
            }

            float magnification = FovController.GetEffectiveMagnification();
            if (magnification < 0.1f)
                magnification = scopeFov > 0.1f ? 35f / scopeFov : 1f;

            float manualLodBias = PiPDisablerPlugin.GetManualLodBiasForCurrentMap();
            float newLodBias = manualLodBias > 0f
                ? manualLodBias
                : _savedLodBias * Mathf.Max(magnification, 1f);
            QualitySettings.lodBias = newLodBias;

            int manualMaxLod = PiPDisablerPlugin.ManualMaximumLodLevel != null
                ? PiPDisablerPlugin.ManualMaximumLodLevel.Value
                : -1;
            int appliedMaxLod = manualMaxLod >= 0 ? manualMaxLod : 0;
            QualitySettings.maximumLODLevel = appliedMaxLod;

            if (scopeFarClip > cam.farClipPlane)
                cam.farClipPlane = scopeFarClip;

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
                $"[CameraSettings] Applied: lodBias {_savedLodBias:F2}→{newLodBias:F2} (mag={magnification:F1}x) farClip={cam.farClipPlane:F0} maxLOD={appliedMaxLod}");
        }

        public static void Restore()
        {
            if (!_applied)
                return;

            QualitySettings.lodBias = _savedLodBias;
            QualitySettings.maximumLODLevel = _savedMaxLodLevel;

            var cam = PiPDisablerPlugin.GetMainCamera();
            if (cam != null)
            {
                cam.farClipPlane = _savedFarClip;
                if (_savedCullDistances != null)
                    cam.layerCullDistances = _savedCullDistances;
            }

            PiPDisablerPlugin.LogInfo(
                $"[CameraSettings] Restored: lodBias={_savedLodBias:F2} farClip={_savedFarClip:F0} maxLOD={_savedMaxLodLevel}");

            _applied = false;
        }

        private static bool TryGetScopeCameraData(OpticSight os, out float fov, out float farClip)
        {
            fov = 0f;
            farClip = 0f;

            ScopeCameraData data = os.GetComponent<ScopeCameraData>();
            if (data == null)
                data = os.GetComponentInChildren<ScopeCameraData>(true);
            if (data == null)
                data = os.GetComponentInParent<ScopeCameraData>();

            if (data == null)
            {
                Transform root = GetScopeRoot(os.transform);
                foreach (var candidate in root.GetComponentsInChildren<ScopeCameraData>(true))
                {
                    if (IsOnSameMode(candidate.transform, os.transform))
                    {
                        data = candidate;
                        break;
                    }
                }
            }

            if (data == null)
                return false;

            fov = data.FieldOfView;
            farClip = data.FarClipPlane;
            return fov > 0.1f;
        }

        private static Transform GetScopeRoot(Transform from)
        {
            Transform root = from;
            while (root.parent != null)
            {
                string parentName = root.parent.name ?? string.Empty;
                if (parentName.StartsWith("scope_", StringComparison.OrdinalIgnoreCase))
                {
                    root = root.parent;
                    break;
                }
                root = root.parent;
            }
            return root;
        }

        private static bool IsOnSameMode(Transform a, Transform b)
            => PiPDisablerPlugin.IsOnSameMode(a, b);
    }
}
