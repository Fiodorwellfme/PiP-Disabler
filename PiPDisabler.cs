using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    internal static class PiPDisabler
    {
        // Track original camera state so toggle-off can restore.
        private struct CameraState
        {
            public Camera Cam;
            public bool Enabled;
            public int CullingMask;
            public RenderTexture TargetTexture;
        }

        private static readonly System.Collections.Generic.List<CameraState> _cams = new System.Collections.Generic.List<CameraState>(16);

        // Cached reflection field for OpticSight inside OpticComponentUpdater.
        // The field name is obfuscated and varies between EFT builds, so we
        // discover it by type instead of relying on a hard-coded name.
        private static FieldInfo _opticSightField;
        private static bool _opticSightFieldSearched;

        // BaseOpticCamera is the global PiP camera. Disabling per-scope OpticComponentUpdater cameras
        // alone can still leave BaseOpticCamera rendering and costing performance.
        //
        // IMPORTANT: we disable Camera components (enabled=false, cullingMask=0, targetTexture=null)
        // but keep the GameObject active so our OpticComponentUpdater LateUpdate prefix can still run
        // and keep scope lifecycle working.
        private static readonly System.Collections.Generic.List<Camera> _baseOpticCams =
            new System.Collections.Generic.List<Camera>(4);

        private static int _nextBaseScanFrame = -1;
        private static bool _loggedBase;

        /// <summary>
        /// The optic camera's Transform — synced to the scope's look direction
        /// every frame by OpticComponentUpdater.LateUpdate().
        /// Used by ReticleRenderer for camera alignment.
        /// </summary>
        internal static Transform OpticCameraTransform { get; private set; }
        internal static Transform Debug_LastOpticCameraTransform;
        internal static string Debug_LastOpticCameraSetBy;
        internal static int Debug_LastOpticCameraSetFrame;

        
        // Track OpticSight.enabled state so we can disable it while scoped and restore on un-scope.
        private static readonly System.Collections.Generic.Dictionary<OpticSight, bool> _opticOrigEnabled =
            new System.Collections.Generic.Dictionary<OpticSight, bool>(32);

        // Used to suppress our own OpticSight.OnDisable postfix (same-frame).
        private static readonly System.Collections.Generic.Dictionary<OpticSight, int> _ignoreOnDisableFrame =
            new System.Collections.Generic.Dictionary<OpticSight, int>(32);

internal static void TickBaseOpticCamera()
        {
            if (!ScopeHousingMeshSurgeryPlugin.DisablePiP.Value) return;

            // Auto-bypass for high-mag scopes must re-enable vanilla PiP.
            if (ScopeLifecycle.IsModBypassedForCurrentScope)
            {
                RestoreAllCameras();
                return;
            }

            // Scan occasionally to find BaseOpticCamera if/when it spawns.
            if (_baseOpticCams.Count == 0 || Time.frameCount >= _nextBaseScanFrame)
            {
                TryFindBaseOpticCameras();
                _nextBaseScanFrame = Time.frameCount + 60; // ~1s @ 60fps
            }

            // Re-apply disable every frame for the cached cameras (cheap).
            for (int i = 0; i < _baseOpticCams.Count; i++)
            {
                var cam = _baseOpticCams[i];
                if (cam == null) continue;
                ForceDisable(cam);
            }
        }


internal static void SetOpticSightEnabled(OpticSight os, bool enabled)
{
    if (os == null) return;

    try
    {
        if (!enabled)
        {
            // Store baseline once, then force-disable.
            if (!_opticOrigEnabled.ContainsKey(os))
                _opticOrigEnabled[os] = os.enabled;

            _ignoreOnDisableFrame[os] = Time.frameCount;
            if (os.enabled) os.enabled = false;
        }
        else
        {
            if (_opticOrigEnabled.TryGetValue(os, out var wasEnabled))
            {
                // Restore to baseline (do not force-enable if baseline was disabled).
                if (wasEnabled && !os.enabled)
                    os.enabled = true;

                _opticOrigEnabled.Remove(os);
            }

            _ignoreOnDisableFrame.Remove(os);
        }
    }
    catch { /* ignore */ }
}

internal static bool ShouldIgnoreOnDisable(OpticSight os)
{
    if (os == null) return false;

    if (_ignoreOnDisableFrame.TryGetValue(os, out var f))
    {
        if (f == Time.frameCount)
        {
            // One-shot suppression (only for the OnDisable we just triggered).
            _ignoreOnDisableFrame.Remove(os);
            return true;
        }

        // Stale entry (e.g. scene change) -> clear.
        if (Time.frameCount - f > 10)
            _ignoreOnDisableFrame.Remove(os);
    }

    return false;
}

        private static void TryFindBaseOpticCameras()
        {
            _baseOpticCams.Clear();

            try
            {
                var cams = Resources.FindObjectsOfTypeAll<Camera>();
                for (int i = 0; i < cams.Length; i++)
                {
                    var cam = cams[i];
                    if (cam == null) continue;

                    var go = cam.gameObject;
                    if (go == null) continue;

                    // Skip prefabs/assets.
                    if (!go.scene.IsValid()) continue;

                    var n = go.name;
                    if (n == "BaseOpticCamera(Clone)" || n == "BaseOpticCamera")
                    {
                        // Collect this camera and any child cameras.
                        var all = go.GetComponentsInChildren<Camera>(true);
                        for (int c = 0; c < all.Length; c++)
                        {
                            var cc = all[c];
                            if (cc != null && !_baseOpticCams.Contains(cc))
                                _baseOpticCams.Add(cc);
                        }

                        if (!_baseOpticCams.Contains(cam))
                            _baseOpticCams.Add(cam);

                        if (!_loggedBase)
                        {
                            _loggedBase = true;
                            ScopeHousingMeshSurgeryPlugin.LogInfo(
                                $"[PiPDisabler] Found BaseOpticCamera: {n} (cameras: {_baseOpticCams.Count})");
                        }
                        break;
                    }
                }
            }
            catch { /* ignore */ }
        }

        private static bool ShouldSkipPiPDisableForHighMagnification(OpticComponentUpdater updater)
        {
            if (!ScopeHousingMeshSurgeryPlugin.AutoDisableForHighMagnificationScopes.Value) return false;
            if (updater == null) return false;

            try
            {
                var field = GetOpticSightField();
                if (field == null) return false;

                var os = field.GetValue(updater) as OpticSight;
                if (os == null) return false;

                return ZoomController.GetMinFov(os) < ScopeHousingMeshSurgeryPlugin.HighMagnificationFovThreshold.Value;
            }
            catch
            {
                return false;
            }
        }

        internal static FieldInfo GetOpticSightField()
        {
            if (!_opticSightFieldSearched)
            {
                _opticSightFieldSearched = true;
                // Search all instance fields (including private) for one of type OpticSight
                var fields = typeof(OpticComponentUpdater).GetFields(
                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                foreach (var f in fields)
                {
                    if (f.FieldType == typeof(OpticSight))
                    {
                        _opticSightField = f;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[PiPDisabler] Found OpticSight field on OpticComponentUpdater: '{f.Name}'");
                        break;
                    }
                }
                if (_opticSightField == null)
                    ScopeHousingMeshSurgeryPlugin.LogWarn(
                        "[PiPDisabler] Could not find any OpticSight field on OpticComponentUpdater!");
            }
            return _opticSightField;
        }

        public static void RestoreAllCameras()
        {
            for (int i = 0; i < _cams.Count; i++)
            {
                var st = _cams[i];
                if (st.Cam == null) continue;
                try
                {
                    st.Cam.enabled = st.Enabled;
                    st.Cam.cullingMask = st.CullingMask;
                    st.Cam.targetTexture = st.TargetTexture;
                }
                catch { /* ignore */ }
            }

// Restore OpticSight.enabled states we changed.
try
{
    foreach (var kv in _opticOrigEnabled)
    {
        var os = kv.Key;
        if (os == null) continue;
        if (kv.Value && !os.enabled)
            os.enabled = true;
    }
}
catch { /* ignore */ }

_opticOrigEnabled.Clear();
_ignoreOnDisableFrame.Clear();

            _cams.Clear();

            _baseOpticCams.Clear();
            _loggedBase = false;
            _nextBaseScanFrame = -1;
            OpticCameraTransform = null;
            Debug_LastOpticCameraTransform = null;
            Debug_LastOpticCameraSetBy = null;
            Debug_LastOpticCameraSetFrame = 0;
        }

        private static void ForceDisable(Camera cam)
        {
            if (cam == null) return;

            // Store only once
            for (int i = 0; i < _cams.Count; i++)
                if (_cams[i].Cam == cam) return;

            var st = new CameraState
            {
                Cam = cam,
                Enabled = cam.enabled,
                CullingMask = cam.cullingMask,
                TargetTexture = cam.targetTexture as RenderTexture
            };
            _cams.Add(st);

            try { cam.enabled = false; } catch { }

            try
            {
                if (cam.targetTexture != null)
                    cam.targetTexture = null;
            }
            catch { /* ignore */ }

            try { cam.cullingMask = 0; } catch { }
        }

        internal sealed class OpticComponentUpdaterCopyComponentFromOptic_DisablePiP : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
                => AccessTools.Method(typeof(OpticComponentUpdater), nameof(OpticComponentUpdater.CopyComponentFromOptic));

            [PatchPostfix]
            private static void Postfix(OpticComponentUpdater __instance)
            {
                if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return;
                if (!ScopeHousingMeshSurgeryPlugin.DisablePiP.Value) return;
                if (__instance == null) return;
                if (ShouldSkipPiPDisableForHighMagnification(__instance)) return;

                // Cache the optic camera transform for ReticleRenderer camera alignment
                OpticCameraTransform = __instance.transform;
                Debug_LastOpticCameraTransform = OpticCameraTransform;
                Debug_LastOpticCameraSetBy = __instance.name;
                Debug_LastOpticCameraSetFrame = Time.frameCount;

                var cam = __instance.GetComponent<Camera>();
                ForceDisable(cam);
            }
        }

        internal sealed class OpticComponentUpdaterLateUpdate_DisablePiP : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
                => AccessTools.Method(typeof(OpticComponentUpdater), "LateUpdate");

            /// <summary>
            /// Instead of skipping the entire LateUpdate (which kills transform
            /// updates used for aim alignment and zeroing), we let it run but
            /// ensure the optic camera cannot actually render.
            ///
            /// LateUpdate does two things:
            ///   1. Syncs the optic camera transform to match the weapon pose
            ///      → we NEED this for correct aim alignment at all ranges
            ///   2. Triggers the PiP render (Camera.Render / RT write)
            ///      → we want to SKIP this
            ///
            /// With Camera.enabled=false, cullingMask=0, and targetTexture=null,
            /// step 2 becomes a no-op even though LateUpdate executes.  The
            /// transform sync in step 1 still runs, preserving aim alignment.
            /// </summary>
            [PatchPrefix]
            private static bool Prefix(OpticComponentUpdater __instance)
            {
                if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return true;
                if (!ScopeHousingMeshSurgeryPlugin.DisablePiP.Value) return true;
                if (ShouldSkipPiPDisableForHighMagnification(__instance)) return true;

                // Ensure the camera can't render, but let LateUpdate run for transforms.
                var cam = __instance != null ? __instance.GetComponent<Camera>() : null;

                if (__instance != null)
                {
                    OpticCameraTransform = __instance.transform;
                    Debug_LastOpticCameraTransform = OpticCameraTransform;
                    Debug_LastOpticCameraSetBy = __instance.name;
                    Debug_LastOpticCameraSetFrame = Time.frameCount;
                }

                ForceDisable(cam);

                // Return TRUE — let the original LateUpdate execute.
                // It will update transforms normally.  Any Camera.Render() calls
                // become no-ops because the camera is disabled with no target.
                return true;
            }
        }

        internal sealed class OpticSightLensFade_NoPipPatch : ModulePatch
        {
            protected override MethodBase GetTargetMethod()
                => AccessTools.Method(typeof(OpticSight), nameof(OpticSight.LensFade));

            [PatchPrefix]
            private static bool Prefix(OpticSight __instance)
            {
                if (!ScopeHousingMeshSurgeryPlugin.ModEnabled.Value) return true;
                if (!ScopeHousingMeshSurgeryPlugin.DisablePiP.Value) return true;
                if (ScopeLifecycle.IsModBypassedForCurrentScope) return true;

                // In No-PiP mode, block LensFade to avoid material state issues.
                // Lens hiding is handled by SSAA signal only — do NOT call
                // LensTransparency here (fires per-frame, causes thrashing).
                return false;
            }
        }
    }
}
