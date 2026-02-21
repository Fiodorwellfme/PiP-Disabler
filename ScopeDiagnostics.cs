using System.Text;
using System.Linq;
using EFT.CameraControl;
using EFT;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// On-demand diagnostics dump, triggered by DiagnosticsKey.
    ///
    /// Output covers:
    ///   - Active scope hierarchy (name, root, mode, path to blacklist entry)
    ///   - Current magnification and ScopeZoomHandler data
    ///   - All active cut-plane config values
    ///   - BackLens position and resolved plane normal
    ///   - Target mesh names that WOULD be cut
    ///   - Whether the scope is currently blacklisted
    ///
    /// The "Add to blacklist" line at the bottom can be copy-pasted directly
    /// into the ScopeBlacklist config entry.
    ///
    /// BLACKLIST LOGIC:
    ///   ScopeBlacklist is a comma-separated list of scope root names.
    ///   When the active scope root name contains any blacklist entry (case-insensitive),
    ///   mesh surgery AND the reticle overlay are both suppressed for that scope.
    ///   Use this for scopes where the mesh cut doesn't look right.
    /// </summary>
    internal static class ScopeDiagnostics
    {
        /// <summary>
        /// Returns true if the given scope root name matches any blacklist entry.
        /// Called from ScopeLifecycle.DoScopeEnter().
        /// </summary>
        public static bool IsBlacklisted(string scopeRootName)
        {
            if (string.IsNullOrWhiteSpace(scopeRootName)) return false;
            string csv = ScopeHousingMeshSurgeryPlugin.ScopeBlacklist.Value;
            if (string.IsNullOrWhiteSpace(csv)) return false;

            string lower = scopeRootName.ToLowerInvariant();
            foreach (var entry in csv.Split(','))
            {
                string e = entry.Trim().ToLowerInvariant();
                if (e.Length > 0 && lower.Contains(e))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// Dump full diagnostics for the currently active optic.
        /// Logs to BepInEx LogInfo — visible in the console and BepInEx log file.
        /// </summary>
        public static void Dump(OpticSight os)
        {
            var sb = new StringBuilder();
            sb.AppendLine("[Diagnostics] ============= SCOPE DUMP =============");

            sb.AppendLine("[Diagnostics] --- Scene / Cameras ---");
            sb.AppendLine($"[Diagnostics]   Scene            : {SceneManager.GetActiveScene().name}");
            sb.AppendLine($"[Diagnostics]   isBatchMode      : {Application.isBatchMode}");
            sb.AppendLine($"[Diagnostics]   gfxDevice        : {SystemInfo.graphicsDeviceType}");

            var cams = Resources.FindObjectsOfTypeAll<Camera>();
            sb.AppendLine($"[Diagnostics]   Camera count     : {cams.Length}");
            foreach (var c in cams.OrderBy(x => x.depth))
            {
                if (c == null) continue;

                var rt = c.targetTexture;
                bool looksOptic = (c.name?.ToLowerInvariant().Contains("optic") ?? false) ||
                                  (c.gameObject.name?.ToLowerInvariant().Contains("optic") ?? false);
                if (!looksOptic && c.name != "FPS Camera") continue;

                sb.AppendLine($"[Diagnostics]   Cam '{c.name}' enabled={c.enabled} active={c.gameObject.activeInHierarchy} " +
                              $"depth={c.depth} cullMask=0x{c.cullingMask:X} " +
                              $"RT={(rt ? rt.name : "null")} {(rt ? $"{rt.width}x{rt.height}" : "")}");
            }

            sb.AppendLine("[Diagnostics] --- OpticComponentUpdaters ---");
            var updaters = Resources.FindObjectsOfTypeAll<MonoBehaviour>()
                .Where(mb => mb != null && mb.GetType().Name == "OpticComponentUpdater")
                .ToArray();

            sb.AppendLine($"[Diagnostics]   Updater count    : {updaters.Length}");

            var opticField = PiPDisabler.GetOpticSightField();
            foreach (var u in updaters)
            {
                var go = u.gameObject;
                var p = u.GetComponentInParent<Player>();
                string playerName = p ? p.name : "null";
                bool isYour = p && p.IsYourPlayer;

                OpticSight uOptic = null;
                try { uOptic = opticField?.GetValue(u) as OpticSight; } catch { }

                sb.AppendLine($"[Diagnostics]   Updater '{go.name}' optic='{(uOptic ? uOptic.name : "null")}' " +
                              $"isYour={isYour} player='{playerName}'");
            }

            sb.AppendLine("[Diagnostics] --- Mod Binding ---");
            var t = PiPDisabler.Debug_LastOpticCameraTransform;
            sb.AppendLine($"[Diagnostics]   Last OpticTf     : {(t ? t.name : "null")} path={(t ? GetPath(t) : "null")}");
            sb.AppendLine($"[Diagnostics]   Set by / frame   : {PiPDisabler.Debug_LastOpticCameraSetBy} / {PiPDisabler.Debug_LastOpticCameraSetFrame}");

            if (os == null)
            {
                sb.AppendLine("[Diagnostics] No active OpticSight — not currently scoped.");
                sb.AppendLine("[Diagnostics] ==========================================");
                ScopeHousingMeshSurgeryPlugin.LogInfo(sb.ToString());
                return;
            }

            // ── Identity ─────────────────────────────────────────────────────
            sb.AppendLine($"[Diagnostics] OpticSight.name : {os.name}");
            sb.AppendLine($"[Diagnostics] GameObject path : {GetPath(os.transform)}");

            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            string rootName = scopeRoot != null ? scopeRoot.name : "(NOT FOUND)";
            sb.AppendLine($"[Diagnostics] Scope root       : {rootName}");
            sb.AppendLine($"[Diagnostics] Blacklisted      : {(IsBlacklisted(rootName) ? "YES (mesh surgery + reticle skipped)" : "no")}");

            // ── Magnification ─────────────────────────────────────────────────
            float mag = 1f;
            try
            {
                var szh = os.GetComponentInParent<ScopeZoomHandler>()
                       ?? os.GetComponentInChildren<ScopeZoomHandler>();
                if (szh != null)
                {
                    float fov = szh.FiledOfView; // EFT typo "Filed"
                    mag = 35f / fov;
                    sb.AppendLine($"[Diagnostics] Magnification   : {mag:F2}x  (ScopeZoomHandler.FiledOfView = {fov:F3})");
                }
                else
                {
                    sb.AppendLine($"[Diagnostics] Magnification   : no ScopeZoomHandler — using DefaultZoom ({ScopeHousingMeshSurgeryPlugin.DefaultZoom.Value:F1}x)");
                }
            }
            catch (System.Exception ex)
            {
                sb.AppendLine($"[Diagnostics] Magnification   : ERROR — {ex.Message}");
            }

            // ── Cut plane ─────────────────────────────────────────────────────
            sb.AppendLine("[Diagnostics] --- Cut Plane Config ---");
            sb.AppendLine($"[Diagnostics]   CutMode          : {ScopeHousingMeshSurgeryPlugin.CutMode.Value}");
            sb.AppendLine($"[Diagnostics]   PlaneNormalAxis  : {ScopeHousingMeshSurgeryPlugin.PlaneNormalAxis.Value}");
            sb.AppendLine($"[Diagnostics]   PlaneOffsetMeters: {ScopeHousingMeshSurgeryPlugin.PlaneOffsetMeters.Value:F4}");
            sb.AppendLine($"[Diagnostics]   CylinderRadius   : {ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value:F4}");
            sb.AppendLine($"[Diagnostics]   MidCylinderRadius: {ScopeHousingMeshSurgeryPlugin.MidCylinderRadius.Value:F4} @ pos={ScopeHousingMeshSurgeryPlugin.MidCylinderPosition.Value:F2}");
            sb.AppendLine($"[Diagnostics]   FarCylinderRadius: {ScopeHousingMeshSurgeryPlugin.FarCylinderRadius.Value:F4}");
            sb.AppendLine($"[Diagnostics]   CutStartOffset   : {ScopeHousingMeshSurgeryPlugin.CutStartOffset.Value:F4}");
            sb.AppendLine($"[Diagnostics]   CutLength        : {ScopeHousingMeshSurgeryPlugin.CutLength.Value:F4}");
            sb.AppendLine($"[Diagnostics]   NearPreserveDepth: {ScopeHousingMeshSurgeryPlugin.NearPreserveDepth.Value:F4}");
            sb.AppendLine($"[Diagnostics]   CutRadius        : {ScopeHousingMeshSurgeryPlugin.CutRadius.Value:F4}");
            sb.AppendLine($"[Diagnostics]   RemoveCameraSide : {ScopeHousingMeshSurgeryPlugin.RemoveCameraSide.Value}");

            // ── Plane resolution ──────────────────────────────────────────────
            sb.AppendLine("[Diagnostics] --- Plane Resolution ---");
            if (scopeRoot != null)
            {
                var activeMode = ScopeHierarchy.FindBestMode(scopeRoot);
                sb.AppendLine($"[Diagnostics]   Active mode     : {(activeMode != null ? activeMode.name : "(none)")}");

                var backLens = ScopeHierarchy.FindDeepChild(
                    activeMode ?? scopeRoot, "backLens");
                if (backLens != null)
                    sb.AppendLine($"[Diagnostics]   backLens pos    : {backLens.position:F4}  local fwd: {backLens.forward:F4}");
                else
                    sb.AppendLine($"[Diagnostics]   backLens        : NOT FOUND");

                if (ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode ?? scopeRoot,
                    out var pp, out var pn, out _))
                {
                    pp += pn * ScopeHousingMeshSurgeryPlugin.PlaneOffsetMeters.Value;
                    sb.AppendLine($"[Diagnostics]   Plane point     : {pp:F4}");
                    sb.AppendLine($"[Diagnostics]   Plane normal    : {pn:F4}");
                }
                else
                {
                    sb.AppendLine($"[Diagnostics]   Plane           : TryGetPlane FAILED — no reference transform found");
                }

                // ── Target meshes ─────────────────────────────────────────────
                var targets = ScopeHierarchy.FindTargetMeshFilters(
                    scopeRoot, activeMode ?? scopeRoot);
                sb.AppendLine($"[Diagnostics] --- Target Meshes ({targets.Count}) ---");

                // Show the search root (may be parent of scope root)
                Transform searchParent = scopeRoot.parent;
                if (searchParent != null)
                {
                    var plo = (searchParent.name ?? "").ToLowerInvariant();
                    if (plo.Contains("scope") || plo.Contains("mod_") || plo.Contains("optic"))
                        sb.AppendLine($"[Diagnostics]   Search root expanded to: '{searchParent.name}'");
                }

                foreach (var mf in targets)
                {
                    if (mf == null) continue;
                    string relPath = GetRelativePath(mf.transform, searchParent ?? scopeRoot);
                    bool isUnderScopeRoot = mf.transform.IsChildOf(scopeRoot);
                    sb.AppendLine($"[Diagnostics]   {(isUnderScopeRoot ? "  " : "↑ ")}" +
                        $"{mf.gameObject.name}  mesh={mf.sharedMesh?.name}  " +
                        $"verts={mf.sharedMesh?.vertexCount}  path={relPath}");
                }
                if (targets.Count == 0)
                    sb.AppendLine($"[Diagnostics]   (none — check ExcludeNameContainsCsv or CutRadius)");
            }
            else
            {
                sb.AppendLine($"[Diagnostics]   Scope root NOT found — mesh surgery impossible.");
            }

            // ── Reticle ───────────────────────────────────────────────────────
            sb.AppendLine("[Diagnostics] --- Reticle ---");
            sb.AppendLine($"[Diagnostics]   ShowReticle      : {ScopeHousingMeshSurgeryPlugin.ShowReticle.Value}");
            sb.AppendLine($"[Diagnostics]   ReticleBaseSize  : {ScopeHousingMeshSurgeryPlugin.ReticleBaseSize.Value:F4}");
            sb.AppendLine($"[Diagnostics]   FlipHorizontal   : {ScopeHousingMeshSurgeryPlugin.ReticleFlipHorizontal.Value}");
            sb.AppendLine($"[Diagnostics]   SmoothingFrames  : {ScopeHousingMeshSurgeryPlugin.ReticleSmoothingFrames.Value}");
            sb.AppendLine($"[Diagnostics]   JitterThreshold  : {ScopeHousingMeshSurgeryPlugin.ReticleJitterThreshold.Value:F5}");

            // ── Blacklist hint ────────────────────────────────────────────────
            sb.AppendLine("[Diagnostics] --- Blacklist ---");
            string current = ScopeHousingMeshSurgeryPlugin.ScopeBlacklist.Value;
            sb.AppendLine($"[Diagnostics]   Current blacklist : {(string.IsNullOrWhiteSpace(current) ? "(empty)" : current)}");
            sb.AppendLine($"[Diagnostics]   Add this scope    : {rootName}");
            if (!string.IsNullOrWhiteSpace(current))
                sb.AppendLine($"[Diagnostics]   New value would be: {current},{rootName}");

            sb.AppendLine("[Diagnostics] ==========================================");

            ScopeHousingMeshSurgeryPlugin.LogInfo(sb.ToString());
        }

        private static string GetPath(Transform t)
        {
            if (t == null) return "(null)";
            var parts = new System.Collections.Generic.List<string>();
            var cur = t;
            while (cur != null)
            {
                parts.Insert(0, cur.name ?? "?");
                cur = cur.parent;
            }
            return string.Join("/", parts);
        }

        private static string GetRelativePath(Transform t, Transform root)
        {
            if (t == null) return "(null)";
            var parts = new System.Collections.Generic.List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                parts.Insert(0, cur.name ?? "?");
            return parts.Count > 0 ? string.Join("/", parts) : t.name ?? "?";
        }
    }
}
