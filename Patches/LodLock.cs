using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// While scoped, keep the weapon on its full-detail models (LOD0).
    ///
    /// Some scopes (Razor HD Gen II) ship a low-poly "_LOD1" copy of the body whose eyepiece is a
    /// solid reflective cap a few millimetres behind the lens. Normally it sits hidden inside the
    /// LOD0 body, but once the hole is cut the cap is what the camera sees through the lens
    /// (LensProbe: 17/17 rays hit the LOD1 cap). So on scope enter:
    ///   - every LODGroup under the weapon is forced to LOD0 (ForceLOD(0)); restored with ForceLOD(-1);
    ///   - a renderer named "*_LOD1..9" that is not part of any LODGroup but has a "_LOD0" twin
    ///     under the weapon is switched off (forceRenderingOff), restored on exit.
    /// </summary>
    internal static class LodLock
    {
        private static readonly Regex LodSuffix = new Regex(@"_LOD([1-9])", RegexOptions.IgnoreCase);

        private static readonly List<LODGroup> _forced = new List<LODGroup>();
        private static readonly List<KeyValuePair<Renderer, bool>> _hidden = new List<KeyValuePair<Renderer, bool>>();
        // Renderers that are not drawn while locked (higher LOD levels of forced groups + hidden twins).
        private static readonly HashSet<Renderer> _notDrawn = new HashSet<Renderer>();

        public static bool IsNotDrawn(Renderer r) => r != null && _notDrawn.Contains(r);

        public static void Apply(OpticSight os)
        {
            Restore();
            if (os == null || !Settings.ModEnabled.Value) return;

            Transform root = MeshSurgeryManager.FindWeaponTransform(os.transform);
            if (root == null) return;

            var log = new StringBuilder();
            var inGroup = new HashSet<Renderer>();

            foreach (var g in root.GetComponentsInChildren<LODGroup>(false))
            {
                if (g == null || !g.enabled) continue;
                LOD[] lods;
                try { lods = g.GetLODs(); } catch { continue; }
                if (lods == null || lods.Length < 2) continue;

                var lod0 = new HashSet<Renderer>();
                if (lods[0].renderers != null)
                    foreach (var r in lods[0].renderers) if (r != null) lod0.Add(r);
                for (int i = 0; i < lods.Length; i++)
                {
                    if (lods[i].renderers == null) continue;
                    foreach (var r in lods[i].renderers)
                    {
                        if (r == null) continue;
                        inGroup.Add(r);
                        if (i > 0 && !lod0.Contains(r)) _notDrawn.Add(r);
                    }
                }

                g.ForceLOD(0);
                _forced.Add(g);
                log.Append($" group '{g.name}' ({lods.Length} levels, LOD0={lod0.Count} renderers)");
            }

            // Name-based twins that no LODGroup manages: both would be drawn, the low-poly one
            // hidden inside the detailed one until the hole exposes it.
            var all = root.GetComponentsInChildren<Renderer>(false);
            var names = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);
            foreach (var r in all) if (r != null) names.Add(r.name);

            foreach (var r in all)
            {
                if (r == null || inGroup.Contains(r) || !r.enabled || r.forceRenderingOff) continue;
                var m = LodSuffix.Match(r.name);
                if (!m.Success) continue;
                string twin = r.name.Substring(0, m.Index) + "_LOD0" + r.name.Substring(m.Index + m.Length);
                if (!names.Contains(twin)) continue;

                _hidden.Add(new KeyValuePair<Renderer, bool>(r, r.forceRenderingOff));
                r.forceRenderingOff = true;
                _notDrawn.Add(r);
                log.Append($" hid '{r.name}' (twin of '{twin}', no LODGroup)");
            }

            PiPDisablerPlugin.DebugLogInfo(_forced.Count == 0 && _hidden.Count == 0
                ? $"[LodLock] nothing to lock under '{root.name}' (no LODGroup, no _LOD1 twins)"
                : $"[LodLock] locked to LOD0 under '{root.name}':{log}");
        }

        /// <summary>Per-frame: keep hidden twins hidden if something re-enables them.</summary>
        public static void Enforce()
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                var r = _hidden[i].Key;
                if (r != null && !r.forceRenderingOff) r.forceRenderingOff = true;
            }
        }

        public static void Restore()
        {
            if (_forced.Count == 0 && _hidden.Count == 0) { _notDrawn.Clear(); return; }
            foreach (var g in _forced)
                try { if (g != null) g.ForceLOD(-1); } catch { }
            foreach (var kv in _hidden)
                try { if (kv.Key != null) kv.Key.forceRenderingOff = kv.Value; } catch { }
            PiPDisablerPlugin.DebugLogInfo($"[LodLock] restored {_forced.Count} LOD groups, {_hidden.Count} hidden twins");
            _forced.Clear();
            _hidden.Clear();
            _notDrawn.Clear();
        }
    }
}
