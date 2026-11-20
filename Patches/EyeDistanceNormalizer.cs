using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Same scope, same look on every weapon (3.6). The eyepiece sits at a different distance from the
    /// eye on each weapon (rail position, mount, ADS pose), so with one weapon scale per scope the lens
    /// circle came out up to ~1.5x bigger on one rifle than on another at the same zoom.
    ///
    /// EFT stretches the first-person weapon only along z around a frame F behind the camera
    /// (localScale (1,1,s), only while rendering). At render time the eyepiece is taken into F's space
    /// and put back with scale 1: that is its distance from F at scale 1 (L1), whatever the current scale.
    /// The weapon scale is then multiplied by k = (reference + a) / L1 (a = camera distance in front of
    /// F), which puts the eyepiece at the reference camera distance on every weapon. L1 does not depend
    /// on the scale, so changing the scale never feeds back into k.
    ///
    /// L1 is stored once ADS has settled, per weapon + scope + mount position, and used from the first
    /// frame of the next aim. It is never changed while it holds (only a clearly different pose stores
    /// a new value), so the pixel hole and the stored mesh cuts are not disturbed.
    /// </summary>
    internal static class EyeDistanceNormalizer
    {
        private const float SettleTime = 0.4f;      // L1 stable this long → settled
        private const float SettleTolerance = 0.0015f;
        private const float StoreChange = 0.005f;   // a stored value is replaced only past this
        private const float MinFactor = 0.5f, MaxFactor = 2f;
        // A new or changed measurement is blended in over this time instead of in one frame
        // (PR #92 review: the one-frame jump 0.4 s into the first aim looked like a hitch).
        private const float BlendTime = 0.35f;

        private struct Known { public float L1, A; }

        private static readonly Dictionary<string, Known> _known = new Dictionary<string, Known>();
        private static bool _loaded;

        private static OpticSight _os;
        private static string _key;
        private static int _keyFrame = -1;
        private static float _candidate = -1f, _candidateA, _since;
        private static bool _settledThisAim;

        // Blend from the previous correction (or none) to the new one, for _blendKey only.
        private static string _blendKey;
        private static bool _blendFromKnown;
        private static Known _blendFrom;
        private static float _blendStart = -1f;

        private static bool Enabled => Settings.NormalizeEyeDistance != null && Settings.NormalizeEyeDistance.Value;

        /// <summary>
        /// Weapon scale for the scope being aimed, from the per-scope / global scale s0 (unchanged without data).
        /// The eyepiece's camera distance at scale s is s·L1 − a, so the scale that puts it at s0·reference is
        /// (s0·reference + a) / L1. Not a plain multiplier: the stretch pivot sits at a different distance behind
        /// the camera on each weapon (3.6 test: M4 64 mm, SV-98 142 mm), and a multiplier that matches the
        /// weapons at s0 = 1 drifts apart again at high zoom (s0 ≈ 2.65: ~23%).
        /// </summary>
        internal static float Apply(float s0)
        {
            if (!Enabled || !ScopeLifecycle.IsScoped || ScopeLifecycle.IsModBypassedForCurrentScope) return s0;
            string key = CurrentKey();
            if (key == null) return s0;
            Load();
            if (!_known.TryGetValue(key, out var k)) return s0;
            float target = ScaleFor(k, s0);
            float t = BlendT(key);
            if (t >= 1f) return target;
            float from = _blendFromKnown ? ScaleFor(_blendFrom, s0) : s0;
            return Mathf.Lerp(from, target, t);
        }

        /// <summary>0..1 (smoothstep) while a new measurement for this key is being blended in, else 1.</summary>
        private static float BlendT(string key)
        {
            if (_blendStart < 0f || key != _blendKey) return 1f;
            float x = (Time.unscaledTime - _blendStart) / BlendTime;
            if (x >= 1f) { _blendStart = -1f; return 1f; }
            return Mathf.SmoothStep(0f, 1f, Mathf.Max(0f, x));
        }

        private static float ScaleFor(Known k, float s0)
        {
            if (k.L1 <= 0.02f || s0 <= 0f) return s0;
            float reference = Mathf.Clamp(Settings.ReferenceEyeDistanceMm.Value, 50f, 600f) / 1000f;
            float s = (s0 * reference + k.A) / k.L1;
            return Mathf.Clamp(s, s0 * MinFactor, s0 * MaxFactor);
        }

        /// <summary>Settings changed: re-apply the scale with the new reference / on-off.</summary>
        internal static void OnSettingsChanged()
        {
            if (ScopeLifecycle.IsScoped) Patches.WeaponScalingPatch.UpdateScale();
        }

        // ── Key: weapon template + scope + where the scope sits on the weapon ──
        private static string CurrentKey()
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (os == null) { _os = null; _key = null; return null; }
            if (os == _os && _key != null && Time.frameCount - _keyFrame < 120) return _key;
            _os = os;
            _key = BuildKey(os);
            _keyFrame = Time.frameCount;
            return _key;
        }

        private static string BuildKey(OpticSight os)
        {
            try
            {
                var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                if (scopeRoot == null) return null;
                var weaponRoot = MeshSurgeryManager.FindWeaponTransform(scopeRoot);
                if (weaponRoot == null) return null;
                string weapon = "?";
                try { weapon = Helpers.GetLocalPlayer()?.HandsController?.Item?.TemplateId.ToString() ?? "?"; } catch { }
                string scope = PerScopeMeshSurgerySettings.ActiveScopeKey;
                if (string.IsNullOrEmpty(scope)) scope = scopeRoot.name;
                // Scope position on the weapon from local transforms only: the stretch sits above the
                // weapon root, so this is the same while rendering and in Update. 5 mm steps.
                Matrix4x4 m = Matrix4x4.identity;
                for (var t = scopeRoot; t != null && t != weaponRoot; t = t.parent)
                    m = Matrix4x4.TRS(t.localPosition, t.localRotation, t.localScale) * m;
                Vector3 p = m.MultiplyPoint3x4(Vector3.zero) * 200f;
                return string.Format(CultureInfo.InvariantCulture, "{0}|{1}|{2},{3},{4}",
                    weapon, scope, Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
            }
            catch { return null; }
        }

        // ── Measurement at render time (called from MeshSurgeryManager's main-camera pre-cull) ──
        internal static void OnPreCullMain(Camera cam)
        {
            var os = ScopeLifecycle.ActiveOptic;
            if (!Enabled || cam == null || os == null || !ScopeLifecycle.IsScoped || ScopeLifecycle.IsModBypassedForCurrentScope)
            {
                _candidate = -1f;
                _settledThisAim = false;
                return;
            }
            if (os != _os) { _candidate = -1f; _settledThisAim = false; }
            string key = CurrentKey();
            // While a new measurement is blended in, the weapon scale is refreshed every frame.
            if (_blendStart >= 0f && key == _blendKey) Patches.WeaponScalingPatch.UpdateScale();
            if (key == null || _settledThisAim) return;
            if (!Measure(os, cam, out float l1, out float a, out float dNow)) { _candidate = -1f; return; }

            // Both must hold still: during the ADS move the camera slides relative to the stretch frame
            // while the eyepiece's distance from that frame stays the same (3.6 test: a went 9 → 54 mm).
            float now = Time.unscaledTime;
            if (_candidate < 0f || Mathf.Abs(l1 - _candidate) > SettleTolerance || Mathf.Abs(a - _candidateA) > SettleTolerance)
            {
                _candidate = l1; _candidateA = a; _since = now;
                return;
            }
            if (now - _since < SettleTime) return;

            _settledThisAim = true; // one decision per aim
            Load();
            bool had = _known.TryGetValue(key, out var old);
            float sNow = MeshSurgeryManager.CurrentRibcage();
            PiPDisablerPlugin.DebugLogInfo(string.Format(CultureInfo.InvariantCulture,
                "[EyeDistance] aim settled '{0}': eyepiece now {1:F0}mm from the camera at weapon scale {2:F3} (scale-1 distance {3:F0}mm, pivot {4:F0}mm behind){5}",
                key, dNow * 1000f, sNow, (l1 - a) * 1000f, a * 1000f, had ? $", stored {(old.L1 - old.A) * 1000f:F0}/{old.A * 1000f:F0}mm" : ""));
            // A different pose (another ADS stance, hideout range vs raid) moves the pivot too, not only L1.
            if (had && Mathf.Abs(old.L1 - l1) <= StoreChange && Mathf.Abs(old.A - a) <= StoreChange) return;

            var k = new Known { L1 = l1, A = a };
            _blendKey = key;
            _blendFromKnown = had;
            _blendFrom = old;
            _blendStart = Time.unscaledTime;
            _known[key] = k;
            Save();
            PiPDisablerPlugin.LogSource.LogInfo(string.Format(CultureInfo.InvariantCulture,
                "[EyeDistance] '{0}': eyepiece {1:F0}mm from the camera at weapon scale 1 (pivot {2:F0}mm behind) → weapon scale at s0=1 → {3:F3}, at s0=2.65 → {5:F3}{4}",
                key, (l1 - a) * 1000f, a * 1000f, ScaleFor(k, 1f), had ? $" (was {(old.L1 - old.A) * 1000f:F0}mm)" :  " (new)", ScaleFor(k, 2.65f)));
            Patches.WeaponScalingPatch.UpdateScale();
        }

        /// <summary>Eyepiece distance from the stretch frame along the view at scale 1 (l1) and the camera's distance in front of that frame (a).</summary>
        private static bool Measure(OpticSight os, Camera cam, out float l1, out float a, out float dNow)
        {
            l1 = a = dNow = 0f;
            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (scopeRoot == null) return false;
            var activeMode = MeshSurgeryManager.ResolveActiveModeFor(os, scopeRoot);
            if (!activeMode) activeMode = os.transform;
            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode, out var planePoint, out var planeNormal, out _))
                return false;
            planePoint += planeNormal * PerScopeMeshSurgerySettings.GetPlane1OffsetMeters();

            var weaponRoot = MeshSurgeryManager.FindWeaponTransform(scopeRoot);
            float s = MeshSurgeryManager.CurrentRibcage();
            var frame = MeshSurgeryManager.FindStretchFrame(weaponRoot, s);
            if (frame == null) return false;

            // Into the frame's space (this removes its (1,1,s) scale), back out with scale 1.
            Vector3 local = frame.InverseTransformPoint(planePoint);
            Vector3 fwd = cam.transform.forward;
            l1 = Vector3.Dot(frame.rotation * local, fwd);
            a = Vector3.Dot(cam.transform.position - frame.position, fwd);
            dNow = Vector3.Dot(planePoint - cam.transform.position, fwd);
            return l1 > 0.05f && l1 < 1.5f && a > -0.3f && a < 0.5f;
        }

        // ── Storage: <mod folder>/user-data/eye-distance.txt (key, L1 m, a m) ──
        private static string FilePath => UserData.PathFor("eye-distance.txt");

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;
            try
            {
                if (!File.Exists(FilePath)) return;
                foreach (var line in File.ReadAllLines(FilePath))
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 3) continue;
                    if (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float l1) &&
                        float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float a))
                        _known[parts[0]] = new Known { L1 = l1, A = a };
                }
            }
            catch (Exception ex) { PiPDisablerPlugin.LogSource.LogWarning("[EyeDistance] load failed: " + ex.Message); }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>(_known.Count);
                foreach (var kv in _known)
                    lines.Add(string.Format(CultureInfo.InvariantCulture, "{0}\t{1:F4}\t{2:F4}", kv.Key, kv.Value.L1, kv.Value.A));
                File.WriteAllLines(FilePath, lines);
            }
            catch (Exception ex) { PiPDisablerPlugin.LogSource.LogWarning("[EyeDistance] save failed: " + ex.Message); }
        }
    }
}
