using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Makes scope lens surfaces invisible by REPLACING THEIR MESH with an empty mesh
    /// AND forcing material properties to transparent as belt-and-suspenders.
    ///
    /// Why previous approaches failed:
    ///   - renderer.enabled = false:      EFT re-enables it, or CommandBuffer ignores it
    ///   - forceRenderingOff = true:       CommandBuffer/Graphics.DrawMesh bypasses this
    ///   - gameObject.SetActive(false):    EFT re-activates, or object was already inactive
    ///   - gameObject.layer = 31:          Graphics.DrawMesh ignores layers
    ///   - material swap to transparent:   CommandBuffer uses cached material reference
    ///
    /// What works: set MeshFilter.mesh to an EMPTY mesh.
    ///   No vertices = no triangles = nothing to draw.
    ///   CommandBuffer, Graphics.DrawMesh, DrawRenderer ALL need geometry.
    ///   Zero geometry = zero rendering. Period.
    ///
    /// v4.7 additions:
    ///   - Broader IsLensSurface detection (glass, frontlens, material name matching)
    ///   - Material property forcing (_Color alpha=0, _SwitchToSight=0) as fallback
    ///   - Expanded search root (climbs through mod_scope/mount parents like MeshSurgeryManager)
    ///   - EnsureHidden now accepts an exclusion renderer so it always runs even when
    ///     shader zoom is active (skipping only the ZoomController's managed lens)
    /// </summary>
    internal static class LensTransparency
    {
        private struct HiddenEntry
        {
            public MeshFilter Filter;
            public Mesh OriginalMesh;
            public Renderer Renderer;
            public bool WasEnabled;
            public bool WasForceOff;
            public Material[] OriginalMaterials; // saved for property restore
        }

        private static readonly List<HiddenEntry> _hidden = new List<HiddenEntry>(16);
        private static Mesh _emptyMesh;

        // Shader property IDs (cached for perf)
        private static readonly int _propColor = Shader.PropertyToID("_Color");
        private static readonly int _propSwitchToSight = Shader.PropertyToID("_SwitchToSight");

        private static Mesh GetEmptyMesh()
        {
            if (_emptyMesh != null) return _emptyMesh;
            _emptyMesh = new Mesh();
            _emptyMesh.name = "EmptyLensMesh";
            // Zero vertices, zero triangles. Nothing to render.
            return _emptyMesh;
        }

        /// <summary>
        /// Called on scope enter. Finds and removes geometry from ALL lens surfaces.
        /// Now searches from an expanded root (same logic as MeshSurgeryManager) to
        /// catch glass/lens meshes that live above scopeRoot in the hierarchy.
        /// </summary>
        public static void HideAllLensSurfaces(OpticSight os)
        {
            if (os == null) return;

            bool shouldHide = ScopeHousingMeshSurgeryPlugin.MakeLensesTransparent.Value
                              || ScopeHousingMeshSurgeryPlugin.DisablePiP.Value;
            if (!shouldHide) return;

            // Only operate inside the current optic mode subtree.
            // Hybrid sights keep multiple mode_* branches, and killing lenses outside
            // the active OpticSight transform can destroy the collimator reticle.
            Transform searchRoot = os.transform;

            // Always dump hierarchy on first enter
            DumpHierarchy(searchRoot);

            // Kill lens renderers only inside the active optic mode subtree.
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            int killed = 0;
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!IsDescendantOf(r.transform, os.transform)) continue;
                if (IsLensSurface(r) && !ShouldSkipForCollimator(r))
                {
                    KillMesh(r);
                    killed++;
                }
            }

            // Also kill the specific LensRenderer (belt-and-suspenders)
            try
            {
                var lens = os.LensRenderer;
                if (lens != null)
                {
                    if (IsDescendantOf(lens.transform, os.transform) && !ShouldSkipForCollimator(lens))
                    {
                        KillMesh(lens);
                        killed++;
                    }
                }
            }
            catch { }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] Destroyed geometry on {killed} lens surfaces (searchRoot='{searchRoot.name}')");
        }

        /// <summary>
        /// Legacy single-renderer API.
        /// </summary>
        public static void HideLens(Renderer lens)
        {
            if (lens == null) return;
            bool shouldHide = ScopeHousingMeshSurgeryPlugin.MakeLensesTransparent.Value
                              || ScopeHousingMeshSurgeryPlugin.DisablePiP.Value;
            if (!shouldHide) return;
            KillMesh(lens);
        }

        /// <summary>
        /// Per-frame: re-apply empty mesh if EFT somehow restores geometry.
        /// Also keep renderer disabled as secondary measure.
        ///
        /// Accepts an optional exclusion renderer (the ZoomController's managed lens)
        /// so this can safely run every frame even when shader zoom is active.
        /// </summary>
        public static void EnsureHidden(Renderer excludeRenderer = null)
        {
            var emptyMesh = GetEmptyMesh();
            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];

                // Skip the renderer managed by ZoomController (shader zoom needs it alive)
                if (excludeRenderer != null && e.Renderer == excludeRenderer)
                    continue;

                if (e.Filter != null && e.Filter.sharedMesh != emptyMesh)
                {
                    e.Filter.sharedMesh = emptyMesh;
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[LensTransparency] Re-emptied mesh on '{e.Filter.gameObject.name}'");
                }
                if (e.Renderer != null)
                {
                    // Re-force material properties (EFT can reset these via CommandBuffer)
                    ForceMaterialTransparent(e.Renderer);
                }
            }
        }

        /// <summary>
        /// Restore all. Called on scope exit.
        /// </summary>
        public static void RestoreAll()
        {
            if (_hidden.Count == 0) return;

            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];
                try
                {
                    if (e.Filter != null && e.OriginalMesh != null)
                    {
                        e.Filter.sharedMesh = e.OriginalMesh;
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[LensTransparency] Restored mesh on '{e.Filter.gameObject.name}' → {e.OriginalMesh.vertexCount} verts");
                    }
                    if (e.Renderer != null)
                    {
                        e.Renderer.forceRenderingOff = e.WasForceOff;

                        // Restore original shared materials (undoes our property forcing)
                        if (e.OriginalMaterials != null)
                        {
                            try { e.Renderer.sharedMaterials = e.OriginalMaterials; }
                            catch { }
                        }

                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[LensTransparency] Restored renderer '{e.Renderer.gameObject.name}' forceOff={e.WasForceOff}");
                    }
                }
                catch { }
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] Restored {_hidden.Count} lens meshes");
            _hidden.Clear();
        }

        // ===== Core =====

        private static void KillMesh(Renderer r)
        {
            if (r == null) return;

            // Already tracked?
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i].Renderer == r) return;

            var mf = r.GetComponent<MeshFilter>();
            Mesh origMesh = null;

            if (mf != null)
            {
                origMesh = mf.sharedMesh;
                mf.sharedMesh = GetEmptyMesh();
            }

            // Save original shared materials for restore
            Material[] origMats = null;
            try { origMats = r.sharedMaterials; } catch { }

            var entry = new HiddenEntry
            {
                Filter = mf,
                OriginalMesh = origMesh,
                Renderer = r,
                WasEnabled = r.enabled,
                WasForceOff = r.forceRenderingOff,
                OriginalMaterials = origMats,
            };
            _hidden.Add(entry);

            // Keep renderer enabled state untouched; hybrid sights can manage this
            // dynamically between optic/collimator modes.
            r.forceRenderingOff = true;

            // Force material properties to transparent as tertiary kill switch.
            // This catches cases where EFT uses Graphics.DrawMesh with the material
            // reference — even with empty mesh, the material state gets cleaned up.
            ForceMaterialTransparent(r);

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] MESH DESTROYED: '{r.gameObject.name}' " +
                $"(had {(origMesh != null ? origMesh.vertexCount.ToString() : "?")} verts → 0) " +
                $"MeshFilter={(mf != null ? "yes" : "NO")}");
        }

        /// <summary>
        /// Force all materials on a renderer to be fully transparent.
        /// Belt-and-suspenders: even if EFT's CommandBuffer re-enables the mesh,
        /// the material will render nothing visible.
        /// </summary>
        private static void ForceMaterialTransparent(Renderer r)
        {
            if (r == null) return;
            try
            {
                var mats = r.materials; // creates instances (safe to modify)
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                {
                    var m = mats[i];
                    if (m == null) continue;

                    if (m.HasProperty(_propColor))
                    {
                        m.SetColor(_propColor, new Color(0, 0, 0, 0));
                        changed = true;
                    }

                    if (m.HasProperty(_propSwitchToSight))
                    {
                        m.SetFloat(_propSwitchToSight, 0f);
                        changed = true;
                    }

                    // Force transparent render queue so it can't occlude anything
                    m.renderQueue = 4000;
                }
                if (changed)
                    r.materials = mats;
            }
            catch { }
        }

        /// <summary>
        /// Determines if a Renderer is a lens/glass surface that should be hidden.
        ///
        /// Detection layers:
        ///   1. GameObject name patterns (linza, backlens, back_lens, glass, frontlens, front_linza)
        ///   2. Mesh name patterns (glass, linza)
        ///   3. Shader name patterns (CW FX/OpticSight, CW FX/BackLens)
        ///   4. Material name patterns (linza, glass, lens)
        /// </summary>
        private static bool IsLensSurface(Renderer r)
        {
            // --- Layer 1: GameObject name ---
            var goName = r.gameObject.name;
            if (!string.IsNullOrEmpty(goName))
            {
                var lo = goName.ToLowerInvariant();
                if (lo.Contains("linza") ||
                    lo.Contains("backlens") || lo.Contains("back_lens") ||
                    lo.Contains("frontlens") || lo.Contains("front_lens") ||
                    lo.Contains("front_linza"))
                    return true;

                // "glass" only when it looks like a scope lens (not "fiberglass" etc.)
                // Match: *_glass_LOD*, *glass*lod*, scope*glass*
                if (lo.Contains("glass") && (lo.Contains("lod") || lo.Contains("scope") || lo.Contains("optic")))
                    return true;
            }

            // --- Layer 2: Mesh name ---
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var meshName = mf.sharedMesh.name;
                if (!string.IsNullOrEmpty(meshName))
                {
                    var mlo = meshName.ToLowerInvariant();
                    if (mlo.Contains("linza") || mlo.Contains("_glass_") || mlo.Contains("_glass_lod"))
                        return true;
                }
            }

            // --- Layer 3 & 4: Shader name + Material name ---
            try
            {
                var mats = r.sharedMaterials;
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var m = mats[i];
                        if (m == null) continue;

                        var shaderName = m.shader?.name ?? "";
                        if (shaderName == "CW FX/OpticSight" ||
                            shaderName == "CW FX/BackLens" ||
                            shaderName.Contains("OpticSight") ||
                            shaderName.Contains("BackLens"))
                            return true;

                        // Material name (e.g. "_LOD0_linza", "*_lens*")
                        var matName = m.name ?? "";
                        if (!string.IsNullOrEmpty(matName))
                        {
                            var matLo = matName.ToLowerInvariant();
                            if (matLo.Contains("linza") || matLo.Contains("_lens"))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private static bool IsDescendantOf(Transform child, Transform parent)
        {
            if (child == null || parent == null) return false;
            return child == parent || child.IsChildOf(parent);
        }

        private static bool ShouldSkipForCollimator(Renderer r)
        {
            if (r == null) return true;

            try
            {
                if (r.GetComponentInParent<CollimatorSight>(true) != null)
                    return true;
            }
            catch { }

            try
            {
                var mats = r.sharedMaterials;
                if (mats != null)
                {
                    for (int i = 0; i < mats.Length; i++)
                    {
                        var shaderName = mats[i]?.shader?.name;
                        if (!string.IsNullOrEmpty(shaderName) &&
                            shaderName.IndexOf("Collimator", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;
                    }
                }
            }
            catch { }

            return false;
        }

        // ===== Diagnostics =====

        private static bool _dumpedOnce;
        private static void DumpHierarchy(Transform root)
        {
            if (_dumpedOnce && !ScopeHousingMeshSurgeryPlugin.VerboseLogging.Value)
                return;
            _dumpedOnce = true;

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] === SCOPE HIERARCHY DUMP: '{root.name}' ===");

            var allRenderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null) continue;

                string matInfo = "";
                try
                {
                    var mats = r.sharedMaterials;
                    if (mats != null && mats.Length > 0)
                    {
                        var parts = new List<string>();
                        for (int i = 0; i < mats.Length; i++)
                        {
                            var m = mats[i];
                            if (m == null) { parts.Add("null"); continue; }
                            parts.Add($"{m.name}[shader={m.shader?.name ?? "?"}]");
                        }
                        matInfo = string.Join("; ", parts);
                    }
                }
                catch { matInfo = "(error)"; }

                var mf = r.GetComponent<MeshFilter>();
                int verts = -1;
                string meshName = "";
                if (mf != null && mf.sharedMesh != null)
                {
                    verts = mf.sharedMesh.vertexCount;
                    meshName = mf.sharedMesh.name ?? "";
                }

                bool isLens = IsLensSurface(r);
                string path = GetRelativePath(r.transform, root);

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[LensTransparency]   {(isLens ? "★LENS★" : "      ")} " +
                    $"'{path}' mesh='{meshName}' verts={verts} enabled={r.enabled} " +
                    $"active={r.gameObject.activeSelf} layer={r.gameObject.layer} " +
                    $"mats=[{matInfo}]");
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] === END DUMP ({allRenderers.Length} renderers) ===");
        }

        private static string GetRelativePath(Transform t, Transform root)
        {
            var parts = new List<string>();
            for (var cur = t; cur != null && cur != root; cur = cur.parent)
                parts.Add(cur.name ?? "?");
            parts.Reverse();
            return string.Join("/", parts);
        }
    }
}
