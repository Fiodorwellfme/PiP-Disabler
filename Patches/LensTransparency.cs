using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Makes scope lens surfaces invisible by swapping them to transparent materials
    /// while keeping the lens geometry alive for reticle masking.
    /// </summary>
    internal static class LensTransparency
    {
        private struct HiddenEntry
        {
            public Renderer Renderer;
            public bool WasForceOff;
            public Material[] OriginalMaterials;
        }

        private static readonly List<HiddenEntry> _hidden = new List<HiddenEntry>(16);
        // Shader property IDs (cached for perf)
        private static readonly int _propColor = Shader.PropertyToID("_Color");
        private static readonly int _propSwitchToSight = Shader.PropertyToID("_SwitchToSight");
        private static readonly int _propMode = Shader.PropertyToID("_Mode");
        private static readonly int _propSrcBlend = Shader.PropertyToID("_SrcBlend");
        private static readonly int _propDstBlend = Shader.PropertyToID("_DstBlend");
        private static readonly int _propZWrite = Shader.PropertyToID("_ZWrite");
        private static readonly int _propAlphaClip = Shader.PropertyToID("_AlphaClip");
        private static readonly int _propSurface = Shader.PropertyToID("_Surface");
        private static readonly int _propBlend = Shader.PropertyToID("_Blend");
        private static readonly int _propCull = Shader.PropertyToID("_Cull");

        // Truly original materials per renderer, stored once on first hide call.
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
        private static Material _transparentLensMaterial;

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

        private static Material GetTransparentLensMaterial()
        {
            if (_transparentLensMaterial != null) return _transparentLensMaterial;

            var shader =
                Shader.Find("Unlit/Transparent") ??
                Shader.Find("Sprites/Default") ??
                Shader.Find("Legacy Shaders/Transparent/Diffuse") ??
                Shader.Find("Standard");

            _transparentLensMaterial = new Material(shader)
            {
                name = "PiPDisabler_TransparentLens",
                color = new Color(1f, 1f, 1f, 0f),
                renderQueue = 3000,
            };
            ConfigureTransparentMaterial(_transparentLensMaterial);
            return _transparentLensMaterial;
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
            PiPDisablerPlugin.LogInfo(
                "[LensTransparency] Restored original materials before scope entry");
        }

        /// <summary>
        /// Full cleanup: restores original materials and clears all internal caches.
        /// Call when the mod is disabled or the plugin is destroyed so EFT's native
        /// PiP rendering can use the lens normally.
        /// </summary>
        public static void FullRestoreAll()
        {
            RestoreAll();
            RestoreBlackLensMaterials(); // also clears _blackenedRenderers
            _trulyOriginalMaterials.Clear();
            PiPDisablerPlugin.LogInfo(
                "[LensTransparency] FullRestoreAll: caches cleared");
        }

        /// <summary>
        /// Called on scope enter. Finds all visible lens surfaces and swaps them to a
        /// transparent material so the geometry can still be used as a reticle mask.
        /// </summary>
        public static void HideAllLensSurfaces(OpticSight os)
        {
            if (os == null) return;

            bool shouldHide = PiPDisablerPlugin.MakeLensesTransparent.Value
                              || PiPDisablerPlugin.DisablePiP.Value;
            if (!shouldHide) return;

            Transform searchRoot = FindScopeSearchRoot(os.transform);

            // Always dump hierarchy on first enter
            DumpHierarchy(searchRoot);

            // Hide only active lens renderers so inactive sibling modes remain untouched.
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            int hidden = 0;
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (IsLensSurface(r) && !ShouldSkipForCollimator(r))
                {
                    HideLensRenderer(r);
                    hidden++;
                }
            }

            // Also hide the specific LensRenderer.
            try
            {
                var lens = os.LensRenderer;
                if (lens != null && lens.gameObject.activeInHierarchy && !ShouldSkipForCollimator(lens))
                {
                    HideLensRenderer(lens);
                    hidden++;
                }
            }
            catch { }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] Applied transparent lens material to {hidden} surfaces (searchRoot='{searchRoot.name}')");
        }

        /// <summary>
        /// Per-frame: re-apply the transparent lens material if EFT restores the
        /// original shared materials while the scope is active.
        ///
        /// Accepts an optional exclusion renderer (the ZoomController's managed lens)
        /// so this can safely run every frame while scoped.
        ///
        /// Uses shared materials only, so it stays allocation-free during the scoped loop.
        /// </summary>
        public static void EnsureHidden(Renderer excludeRenderer = null)
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];

                // No special per-lens exclusions are needed here
                if (excludeRenderer != null && e.Renderer == excludeRenderer)
                    continue;

                if (e.Renderer != null)
                {
                    EnsureTransparentMaterial(e.Renderer);
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

            bool blackLens = PiPDisablerPlugin.BlackLensWhenUnscoped != null
                             && PiPDisablerPlugin.BlackLensWhenUnscoped.Value;

            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];
                try
                {
                    if (e.Renderer != null)
                    {
                        e.Renderer.forceRenderingOff = e.WasForceOff;

                        if (blackLens)
                        {
                            // Apply solid black material — no PiP texture, no reticle flash.
                            ApplyBlackMaterial(e.Renderer);
                            PiPDisablerPlugin.LogVerbose(
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
                            PiPDisablerPlugin.LogVerbose(
                                $"[LensTransparency] Restored renderer '{e.Renderer.gameObject.name}' forceOff={e.WasForceOff}");
                        }
                    }
                }
                catch { }
            }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] Restored {_hidden.Count} lens renderers" +
                (blackLens ? " (black lens applied)" : ""));
            _hidden.Clear();
        }

        // ===== Core =====

        private static void HideLensRenderer(Renderer r)
        {
            if (r == null) return;

            // Already tracked?
            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i].Renderer == r) return;

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
                Renderer = r,
                WasForceOff = r.forceRenderingOff,
                OriginalMaterials = origMats,
            };
            _hidden.Add(entry);

            EnsureTransparentMaterial(r);

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] Transparent lens: '{r.gameObject.name}' mats={GetMaterialSlotCount(r)}");
        }

        private static void EnsureTransparentMaterial(Renderer r)
        {
            if (r == null) return;
            try
            {
                var transparentMat = GetTransparentLensMaterial();
                int slotCount = GetMaterialSlotCount(r);
                var transparentArray = new Material[slotCount];
                for (int i = 0; i < slotCount; i++)
                    transparentArray[i] = transparentMat;

                r.forceRenderingOff = false;
                r.sharedMaterials = transparentArray;
            }
            catch { }
        }

        private static int GetMaterialSlotCount(Renderer r)
        {
            if (r == null) return 1;
            try
            {
                int slotCount = r.sharedMaterials != null ? r.sharedMaterials.Length : 0;
                return slotCount > 0 ? slotCount : 1;
            }
            catch
            {
                return 1;
            }
        }

        private static void ConfigureTransparentMaterial(Material material)
        {
            if (material == null) return;

            if (material.HasProperty(_propColor))
                material.SetColor(_propColor, new Color(1f, 1f, 1f, 0f));
            if (material.HasProperty(_propSwitchToSight))
                material.SetFloat(_propSwitchToSight, 0f);
            if (material.HasProperty(_propMode))
                material.SetFloat(_propMode, 3f);
            if (material.HasProperty(_propSrcBlend))
                material.SetInt(_propSrcBlend, (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            if (material.HasProperty(_propDstBlend))
                material.SetInt(_propDstBlend, (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            if (material.HasProperty(_propZWrite))
                material.SetInt(_propZWrite, 0);
            if (material.HasProperty(_propAlphaClip))
                material.SetFloat(_propAlphaClip, 0f);
            if (material.HasProperty(_propSurface))
                material.SetFloat(_propSurface, 1f);
            if (material.HasProperty(_propBlend))
                material.SetFloat(_propBlend, 0f);
            if (material.HasProperty(_propCull))
                material.SetFloat(_propCull, (float)UnityEngine.Rendering.CullMode.Off);

            material.DisableKeyword("_ALPHATEST_ON");
            material.EnableKeyword("_ALPHABLEND_ON");
            material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            material.DisableKeyword("_SURFACE_TYPE_OPAQUE");
            material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            material.renderQueue = 3000;
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
        internal static bool IsLensSurface(Renderer r)
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

        internal static bool IsLensMeshFilter(MeshFilter mf)
        {
            if (mf == null) return false;

            var renderer = mf.GetComponent<Renderer>();
            if (renderer != null && IsLensSurface(renderer))
                return true;

            string goName = mf.gameObject != null ? mf.gameObject.name : string.Empty;
            string meshName = mf.sharedMesh != null ? mf.sharedMesh.name : string.Empty;
            return LooksLikeLensName(goName) || LooksLikeLensName(meshName);
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
                   || ContainsCI(name, "glass");
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
                    PiPDisablerPlugin.LogInfo(
                        $"[LensTransparency] HousingMask -skip LOD1 mesh: go='{goName}' mesh='{meshName}'");
                    continue;
                }

                if (LooksLikeLensName(meshName) || LooksLikeLensName(goName))
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[LensTransparency] HousingMask -skip lens-like mesh: go='{goName}' mesh='{meshName}'");
                    continue;
                }

                result.Add(r);
                PiPDisablerPlugin.LogInfo(
                    $"[LensTransparency] HousingMask +renderer: go='{r.gameObject.name}'" +
                    $" mesh='{mesh.name}' verts={mesh.vertexCount}" +
                    $" shader='{(r.sharedMaterial?.shader?.name ?? "null")}'");
            }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] CollectHousingRenderers: {result.Count} renderer(s)" +
                $" (searchRoot='{searchRoot?.name ?? "null"}' from '{os.name}')");

            return result;
        }

        /// <summary>
        /// Returns active lens renderers that still have geometry and should define the
        /// reticle visibility window.
        /// </summary>
        public static List<Renderer> CollectLensRenderers(OpticSight os)
        {
            var result = new List<Renderer>();
            if (os == null) return result;

            Transform searchRoot = FindScopeSearchRoot(os.transform);
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (!IsLensSurface(r)) continue;
                if (ShouldSkipForCollimator(r)) continue;

                var mf = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh mesh = mf?.sharedMesh ?? smr?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;

                result.Add(r);
            }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] CollectLensRenderers: {result.Count} renderer(s)" +
                $" (searchRoot='{searchRoot?.name ?? "null"}' from '{os.name}')");

            return result;
        }

        // ===== Weapon mesh stencil collection =====

        /// <summary>
        /// Walks up from <paramref name="from"/> looking for a transform whose name
        /// is exactly "weapon" (case-insensitive).  This is the EFT node that parents
        /// all weapon components — body, handguard, scope mounts, etc.
        /// Returns null if not found before hitting a player/hands/camera boundary.
        /// </summary>
        private static Transform FindWeaponRoot(Transform from)
        {
            if (from == null) return null;
            for (Transform cur = from; cur != null; cur = cur.parent)
            {
                string n = cur.name ?? "";
                if (string.Equals(n, "weapon", StringComparison.OrdinalIgnoreCase))
                    return cur;
                // Don't climb out of the weapon rig into the player/camera hierarchy.
                if (ContainsCI(n, "player") || ContainsCI(n, "hands") || ContainsCI(n, "camera"))
                    break;
            }
            return null;
        }

        /// <summary>
        /// Returns active, solid weapon-body renderers under the "weapon" root that
        /// are not already in <paramref name="alreadyCollected"/> and are not lens
        /// surfaces.  These are added to the stencil mask so the reticle is
        /// suppressed wherever the physical weapon body occludes screen-centre.
        /// </summary>
        public static List<Renderer> CollectWeaponRenderers(OpticSight os,
                                                            List<Renderer> alreadyCollected)
        {
            var result = new List<Renderer>();
            if (os == null) return result;

            Transform weaponRoot = FindWeaponRoot(os.transform);
            if (weaponRoot == null)
            {
                PiPDisablerPlugin.LogInfo(
                    "[LensTransparency] CollectWeaponRenderers: no 'weapon' root found");
                return result;
            }

            // Build a fast exclusion set from already-collected housing renderers.
            var excludeSet = new HashSet<Renderer>(
                alreadyCollected ?? new List<Renderer>());

            var allRenderers = weaponRoot.GetComponentsInChildren<Renderer>(true);
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (excludeSet.Contains(r)) continue;
                if (IsLensSurface(r)) continue;
                if (ShouldSkipForCollimator(r)) continue;

                var mf  = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh mesh = mf?.sharedMesh ?? smr?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;

                string meshName = mesh.name ?? string.Empty;
                string goName   = r.gameObject.name ?? string.Empty;

                if (ContainsCI(meshName, "LOD1")) continue;
                if (LooksLikeLensName(meshName) || LooksLikeLensName(goName)) continue;

                result.Add(r);
                PiPDisablerPlugin.LogInfo(
                    $"[LensTransparency] WeaponMask +renderer: go='{goName}'" +
                    $" mesh='{meshName}' verts={mesh.vertexCount}");
            }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] CollectWeaponRenderers: {result.Count} renderer(s)" +
                $" (weaponRoot='{weaponRoot.name}')");

            return result;
        }

        // ===== Diagnostics =====

        private static bool _dumpedOnce;
        private static void DumpHierarchy(Transform root)
        {
            if (_dumpedOnce && !PiPDisablerPlugin.VerboseLogging.Value)
                return;
            _dumpedOnce = true;

            PiPDisablerPlugin.LogInfo(
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

                PiPDisablerPlugin.LogInfo(
                    $"[LensTransparency]   {(isLens ? "★LENS★" : "      ")} " +
                    $"'{path}' mesh='{meshName}' verts={verts} enabled={r.enabled} " +
                    $"active={r.gameObject.activeSelf} layer={r.gameObject.layer} " +
                    $"mats=[{matInfo}]");
            }

            PiPDisablerPlugin.LogInfo(
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
