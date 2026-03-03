using System;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Applies main camera FOV from optic magnification using a fixed baseline vertical FOV.
    /// Runs in LateUpdate timing to avoid EFT camera writes stomping the value.
    /// </summary>
    internal static class MagnificationFovDriver
    {
        private static Camera _fpsCam;
        private static bool _scopedActive;
        private static float _originalFov;
        private static float _targetFov;
        private static float _appliedFov;
        private static float _lastMag;

        public static float LastTargetFov => _targetFov;

        public static void OnScopeEnter()
        {
            _fpsCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (_fpsCam != null)
            {
                _originalFov = _fpsCam.fieldOfView;
                _appliedFov = _originalFov;
            }
            else
            {
                _originalFov = 0f;
                _appliedFov = 0f;
            }

            _targetFov = _appliedFov;
            _lastMag = -1f;
            _scopedActive = true;
        }

        public static void OnScopeExit()
        {
            if (_fpsCam != null && _originalFov > 0.1f)
                _fpsCam.fieldOfView = _originalFov;

            _scopedActive = false;
            _lastMag = -1f;
            _targetFov = 0f;
            _appliedFov = 0f;
            _originalFov = 0f;
            _fpsCam = null;
        }

        public static bool SetMagnification(float magnification)
        {
            if (!_scopedActive) return false;

            float minFov = ScopeHousingMeshSurgeryPlugin.ClampFovMinDeg.Value;
            float maxFov = ScopeHousingMeshSurgeryPlugin.ClampFovMaxDeg.Value;
            if (maxFov < minFov)
            {
                float tmp = minFov;
                minFov = maxFov;
                maxFov = tmp;
            }

            float baseline = ScopeHousingMeshSurgeryPlugin.ZoomBaselineFovDeg.Value;
            float fov = FovFromMagnification(baseline, magnification);
            fov = Mathf.Clamp(fov, minFov, maxFov);

            float eps = Mathf.Max(0.0001f, ScopeHousingMeshSurgeryPlugin.FovApplyEpsilon.Value);
            bool changed = Mathf.Abs(_targetFov - fov) > eps;

            _lastMag = magnification;
            _targetFov = fov;
            return changed;
        }

        public static void LateTickApply()
        {
            if (!_scopedActive) return;
            if (!ScopeHousingMeshSurgeryPlugin.EnableMagnificationDrivenFov.Value) return;
            if (!ScopeLifecycle.IsScoped || ScopeLifecycle.IsModBypassedForCurrentScope) return;

            if (_fpsCam == null)
                _fpsCam = ScopeHousingMeshSurgeryPlugin.GetMainCamera();
            if (_fpsCam == null) return;

            float eps = Mathf.Max(0.0001f, ScopeHousingMeshSurgeryPlugin.FovApplyEpsilon.Value);
            if (Mathf.Abs(_targetFov - _appliedFov) <= eps) return;

            _fpsCam.fieldOfView = _targetFov;
            _appliedFov = _targetFov;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MagnificationFovDriver] apply mag={_lastMag:F3}x targetFov={_targetFov:F3}");
        }

        public static float FovFromMagnification(float baselineFovDeg, float magnification)
        {
            magnification = Mathf.Max(0.0001f, magnification);
            float b = baselineFovDeg * Mathf.Deg2Rad * 0.5f;
            float f = 2f * Mathf.Atan(Mathf.Tan(b) / magnification);
            return f * Mathf.Rad2Deg;
        }

        public static float MagnificationFromFov(float baselineFovDeg, float currentFovDeg)
        {
            float b = baselineFovDeg * Mathf.Deg2Rad * 0.5f;
            float c = currentFovDeg * Mathf.Deg2Rad * 0.5f;
            return Mathf.Tan(b) / Mathf.Tan(c);
        }
    }
}
