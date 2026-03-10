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
    ///     while scoped.
    /// </summary>
    internal static class LensTransparency
    {
        private struct HiddenEntry
        {
            public MeshFilter Filter;
            public SkinnedMeshRenderer Skinned;
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

        // Truly original materials per renderer, stored once on first KillMesh call.
        // Prevents the black material we apply on RestoreAll from being mistaken for
        // the original if the player scopes in again on the same session.
        private static readonly Dictionary<int, Material[]> _trulyOriginalMaterials =
            new Dictionary<int, Material[]>();

        // Renderers that currently have the black material applied (scope not in use).
        // Tracked so we can restore them before ReticleRenderer reads textures on scope entry,
        // and so we can fully clean up when the mod is disabled.
        private static readonly HashSet<Renderer> _blackenedRenderers = new HashSet<Renderer>();

        // Solid black unlit material applied to lens surfaces when unscoped.
        private static Material _blackLensMaterial;

        private static Mesh GetEmptyMesh()
        {
            if (_emptyMesh != null) return _emptyMesh;
            _emptyMesh = new Mesh();
            _emptyMesh.name = "EmptyLensMesh";
            // Zero vertices, zero triangles. Nothing to render.
            return _emptyMesh;
        }

        private static Material GetBlackLensMaterial()
        {
            if (_blackLensMaterial != null) return _blackLensMaterial;
            // Unlit/Color: solid color, no lighting, no texture — guaranteed opaque black.
            // Falls back to Standard if Unlit/Color isn't in the build's shader set.
            var shader = Shader.Find("Unlit/Color") ?? Shader.Find("Standard");
            _blackLensMaterial = new Material(shader)
            {
                name  = "PiPDisabler_BlackLens",
                color = Color.black,
            };
            return _blackLensMaterial;
        }

        /// <summary>
        /// Replace all material slots on <paramref name="r"/> with a solid black unlit material.
        /// Records the renderer in _blackenedRenderers so it can be un-blackened later.
        /// </summary>
        private static void ApplyBlackMaterial(Renderer r)
        {
            if (r == null) return;
            try
            {
                var blackMat = GetBlackLensMaterial();
                int slotCount = 1;
                try { slotCount = r.sharedMaterials.Length; } catch { }
                if (slotCount < 1) slotCount = 1;

                var blackArray = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                    blackArray[i] = blackMat;

                r.sharedMaterials = blackArray;
                _blackenedRenderers.Add(r);
            }
            catch { }
        }

        /// <summary>
        /// Restores the original sharedMaterials on any lens renderers that currently have
        /// the black material applied.  Call this at scope ENTRY, before
        /// ReticleRenderer.ExtractReticle(), so the texture read sees the real OpticSight
        /// material rather than our Unlit/Color placeholder.
        /// </summary>
        public static void RestoreBlackLensMaterials()
        {
            if (_blackenedRenderers.Count == 0) return;
            foreach (var r in _blackenedRenderers)
            {
                if (r == null) continue;
                int rid = r.GetInstanceID();
                Material[] origMats;
                if (_trulyOriginalMaterials.TryGetValue(rid, out origMats) && origMats != null)
                {
                    try { r.sharedMaterials = origMats; }
                    catch { }
                }
            }
            _blackenedRenderers.Clear();
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[LensTransparency] Restored original materials before scope entry");
        }

        /// <summary>
        /// Full cleanup: restores original materials and clears all internal caches.
        /// Call when the mod is disabled or the plugin is destroyed so EFT's native
        /// PiP rendering can use the lens normally.
        /// </summary>
        public static void FullRestoreAll()
        {
            RestoreBlackLensMaterials(); // also clears _blackenedRenderers
            _trulyOriginalMaterials.Clear();
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                "[LensTransparency] FullRestoreAll: caches cleared");
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

            Transform searchRoot = FindScopeSearchRoot(os.transform);

            // Always dump hierarchy on first enter
            DumpHierarchy(searchRoot);

            // Kill only ACTIVE lens renderers (prevents nuking inactive sibling modes on hybrids)
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            int killed = 0;
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
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
                if (lens != null && lens.gameObject.activeInHierarchy && !ShouldSkipForCollimator(lens))
                {
                    KillMesh(lens);
                    killed++;
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
        /// so this can safely run every frame while scoped.
        ///
        /// NOTE: ForceMaterialTransparent is intentionally NOT called here per-frame.
        /// r.materials allocates new material instances every call → massive GC pressure.
        /// The mesh is already empty (zero geometry = nothing to draw) and
        /// forceRenderingOff = true is set, so material forcing is unnecessary.
        /// Materials are forced once during KillMesh().
        /// </summary>
        public static void EnsureHidden(Renderer excludeRenderer = null)
        {
            var emptyMesh = GetEmptyMesh();
            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];

                // No special per-lens exclusions are needed here
                if (excludeRenderer != null && e.Renderer == excludeRenderer)
                    continue;

                if (e.Skinned != null && e.Skinned.sharedMesh != emptyMesh)
                {
                    e.Skinned.sharedMesh = emptyMesh;
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[LensTransparency] Re-emptied skinned mesh on '{e.Skinned.gameObject.name}'");
                }
                else if (e.Filter != null && e.Filter.sharedMesh != emptyMesh)
                {
                    e.Filter.sharedMesh = emptyMesh;
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[LensTransparency] Re-emptied mesh on '{e.Filter.gameObject.name}'");
                }
                if (e.Renderer != null)
                {
                    // Re-enforce forceRenderingOff (lightweight bool check, no alloc)
                    if (!e.Renderer.forceRenderingOff)
                        e.Renderer.forceRenderingOff = true;
                }
            }
        }

        /// <summary>
        /// Restore all. Called on scope exit.
        ///
        /// When BlackLensWhenUnscoped is enabled (default), the lens geometry is
        /// restored but an opaque black material is applied in place of the original
        /// PiP/sight material.  This prevents the reticle-flash that occurs during
        /// the unscope transition and matches the real-world appearance of scope
        /// glass viewed from outside.  The truly original materials are kept in
        /// _trulyOriginalMaterials so the next scope-enter always saves the correct
        /// originals rather than the black placeholder.
        /// </summary>
        public static void RestoreAll()
        {
            if (_hidden.Count == 0) return;

            bool blackLens = ScopeHousingMeshSurgeryPlugin.BlackLensWhenUnscoped != null
                             && ScopeHousingMeshSurgeryPlugin.BlackLensWhenUnscoped.Value;

            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];
                try
                {
                    // Restore original mesh geometry so the lens body is visible again.
                    if (e.Skinned != null && e.OriginalMesh != null)
                    {
                        e.Skinned.sharedMesh = e.OriginalMesh;
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[LensTransparency] Restored skinned mesh on '{e.Skinned.gameObject.name}' → {e.OriginalMesh.vertexCount} verts");
                    }
                    else if (e.Filter != null && e.OriginalMesh != null)
                    {
                        e.Filter.sharedMesh = e.OriginalMesh;
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[LensTransparency] Restored mesh on '{e.Filter.gameObject.name}' → {e.OriginalMesh.vertexCount} verts");
                    }

                    if (e.Renderer != null)
                    {
                        // Re-enable rendering (lens is visible when unscoped).
                        e.Renderer.forceRenderingOff = e.WasForceOff;

                        if (blackLens)
                        {
                            // Apply solid black material — no PiP texture, no reticle flash.
                            ApplyBlackMaterial(e.Renderer);
                            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                $"[LensTransparency] Applied black lens to '{e.Renderer.gameObject.name}'");
                        }
                        else
                        {
                            // Restore original shared materials (legacy path).
                            if (e.OriginalMaterials != null)
                            {
                                try { e.Renderer.sharedMaterials = e.OriginalMaterials; }
                                catch { }
                            }
                            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                                $"[LensTransparency] Restored renderer '{e.Renderer.gameObject.name}' forceOff={e.WasForceOff}");
                        }
                    }
                }
                catch { }
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] Restored {_hidden.Count} lens meshes" +
                (blackLens ? " (black lens applied)" : ""));
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
            var smr = r as SkinnedMeshRenderer;
            Mesh origMesh = null;

            if (smr != null)
            {
                origMesh = smr.sharedMesh;
                smr.sharedMesh = GetEmptyMesh();
            }
            else if (mf != null)
            {
                origMesh = mf.sharedMesh;
                mf.sharedMesh = GetEmptyMesh();
            }

            // Save the TRULY ORIGINAL shared materials only on first encounter.
            // On subsequent scope-ins the renderer may already have the black material
            // we applied on the previous scope-exit — we must not overwrite the cache.
            int rid = r.GetInstanceID();
            if (!_trulyOriginalMaterials.ContainsKey(rid))
            {
                Material[] firstSeenMats = null;
                try { firstSeenMats = r.sharedMaterials; } catch { }
                if (firstSeenMats != null)
                    _trulyOriginalMaterials[rid] = firstSeenMats;
            }

            Material[] origMats = null;
            _trulyOriginalMaterials.TryGetValue(rid, out origMats);

            var entry = new HiddenEntry
            {
                Filter = mf,
                Skinned = smr,
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
                $"MeshFilter={(mf != null ? "yes" : "NO")}, Skinned={(smr != null ? "yes" : "NO")}");
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
        ///
        /// Uses OrdinalIgnoreCase to avoid ToLowerInvariant() string allocations.
        /// </summary>
        private static bool IsLensSurface(Renderer r)
        {
            // --- Layer 1: GameObject name ---
            var goName = r.gameObject.name;
            if (!string.IsNullOrEmpty(goName))
            {
                if (ContainsCI(goName, "linza") ||
                    ContainsCI(goName, "backlens") || ContainsCI(goName, "back_lens") ||
                    ContainsCI(goName, "frontlens") || ContainsCI(goName, "front_lens") ||
                    ContainsCI(goName, "front_linza"))
                    return true;

                // "glass" only when it looks like a scope lens (not "fiberglass" etc.)
                if (ContainsCI(goName, "glass") &&
                    (ContainsCI(goName, "lod") || ContainsCI(goName, "scope") || ContainsCI(goName, "optic")))
                    return true;
            }

            // --- Layer 2: Mesh name ---
            var mf = r.GetComponent<MeshFilter>();
            if (mf != null && mf.sharedMesh != null)
            {
                var meshName = mf.sharedMesh.name;
                if (!string.IsNullOrEmpty(meshName))
                {
                    if (ContainsCI(meshName, "linza") ||
                        ContainsCI(meshName, "_glass_") ||
                        ContainsCI(meshName, "_glass_lod"))
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
                            shaderName.IndexOf("OpticSight", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            shaderName.IndexOf("BackLens", StringComparison.OrdinalIgnoreCase) >= 0)
                            return true;

                        // Material name (e.g. "_LOD0_linza", "*_lens*")
                        var matName = m.name ?? "";
                        if (!string.IsNullOrEmpty(matName))
                        {
                            if (ContainsCI(matName, "linza") || ContainsCI(matName, "_lens"))
                                return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        /// <summary>Zero-allocation case-insensitive Contains.</summary>
        private static bool ContainsCI(string haystack, string needle)
        {
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool LooksLikeLensName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            return ContainsCI(name, "linza")
                   || ContainsCI(name, "lens")
                   || ContainsCI(name, "glass")
                   || ContainsCI(name, "front_lens")
                   || ContainsCI(name, "back_lens")
                   || ContainsCI(name, "frontlens")
                   || ContainsCI(name, "backlens");
        }


        private static Transform FindScopeSearchRoot(Transform t)
        {
            if (t == null) return null;

            // Prefer a parent that contains the mode_* branches (common in hybrid sights)
            Transform best = t;
            Transform cur = t;

            for (int depth = 0; cur != null && depth < 10; depth++, cur = cur.parent)
            {
                string n = cur.name ?? "";

                // Typical container name
                if (ContainsCI(n, "mod_scope"))
                    best = cur;

                // Or a parent that contains mode_* children
                int modeChildren = 0;
                for (int i = 0; i < cur.childCount; i++)
                {
                    var c = cur.GetChild(i);
                    if (c != null && c.name != null &&
                        c.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase))
                        modeChildren++;
                }
                if (modeChildren >= 1)
                    best = cur;

                // Don’t climb into the whole weapon/player hierarchy
                if (ContainsCI(n, "weapon") || ContainsCI(n, "player") || ContainsCI(n, "hands"))
                    break;
            }

            return best ?? t;
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

        /// <summary>
        /// Returns all active, non-lens renderers in the scope hierarchy.
        /// These are the scope housing/body meshes that should act as a stencil
        /// mask so the reticle is hidden wherever the housing covers screen-centre.
        /// </summary>
        public static List<Renderer> CollectHousingRenderers(OpticSight os)
        {
            var result = new List<Renderer>();
            if (os == null) return result;

            Transform searchRoot = FindScopeSearchRoot(os.transform);
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);

            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (IsLensSurface(r)) continue;
                if (ShouldSkipForCollimator(r)) continue;

                // Must have real geometry (lens renderers are already empty-meshed, but
                // check explicitly so we don't add zero-vert renderers to the mask pass).
                var mf  = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh mesh = mf?.sharedMesh ?? smr?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;

                string meshName = mesh.name ?? string.Empty;
                string goName = r.gameObject.name ?? string.Empty;

                if (ContainsCI(meshName, "LOD1"))
                {
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[LensTransparency] HousingMask -skip LOD1 mesh: go='{goName}' mesh='{meshName}'");
                    continue;
                }

                if (LooksLikeLensName(meshName) || LooksLikeLensName(goName))
                {
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[LensTransparency] HousingMask -skip lens-like mesh: go='{goName}' mesh='{meshName}'");
                    continue;
                }

                result.Add(r);
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[LensTransparency] HousingMask +renderer: go='{r.gameObject.name}'" +
                    $" mesh='{mesh.name}' verts={mesh.vertexCount}" +
                    $" shader='{(r.sharedMaterial?.shader?.name ?? "null")}'");
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[LensTransparency] CollectHousingRenderers: {result.Count} renderer(s)" +
                $" (searchRoot='{searchRoot?.name ?? "null"}' from '{os.name}')");

            return result;
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
