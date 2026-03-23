using System;
using System.Collections.Generic;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler
{
    /// <summary>
    /// Makes scope lens surfaces transparent while keeping their geometry alive.
    ///
    /// The lens mesh now serves two jobs:
    ///   1. stay invisible in the main scene;
    ///   2. remain drawable in the reticle stencil pass so the reticle only appears
    ///      where the lens is actually visible.
    /// </summary>
    internal static class LensTransparency
    {
        private struct HiddenEntry
        {
            public Renderer Renderer;
            public Material[] OriginalMaterials;
            public Material[] TransparentMaterials;
        }

        private static readonly List<HiddenEntry> _hidden = new List<HiddenEntry>(16);

        // Shader property IDs (cached for perf)
        private static readonly int _propColor = Shader.PropertyToID("_Color");
        private static readonly int _propSwitchToSight = Shader.PropertyToID("_SwitchToSight");

        /// <summary>
        /// Full cleanup for shutdown or mod disable.
        /// </summary>
        public static void FullRestoreAll()
        {
            RestoreAll();
        }

        /// <summary>
        /// Called on scope enter. Finds and makes ALL lens surfaces transparent.
        /// Searches from an expanded root so glass/lens meshes that live above
        /// scopeRoot still get handled.
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

            // Process only ACTIVE lens renderers (prevents touching inactive sibling modes on hybrids)
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);
            int killed = 0;
            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (IsLensSurfaceRenderer(r) && !ShouldSkipForCollimator(r))
                {
                    MakeTransparent(r);
                    killed++;
                }
            }

            // Also process the specific LensRenderer (belt-and-suspenders)
            try
            {
                var lens = os.LensRenderer;
                if (lens != null && lens.gameObject.activeInHierarchy && !ShouldSkipForCollimator(lens))
                {
                    MakeTransparent(lens);
                    killed++;
                }
            }
            catch { }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] Transparentized {killed} lens surfaces (searchRoot='{searchRoot.name}')");
        }

        /// <summary>
        /// Per-frame: re-apply transparent lens materials if EFT swaps them back.
        ///
        /// Accepts an optional exclusion renderer so this can safely run every frame while scoped.
        /// </summary>
        public static void EnsureHidden(Renderer excludeRenderer = null)
        {
            for (int i = 0; i < _hidden.Count; i++)
            {
                var e = _hidden[i];
                if (excludeRenderer != null && e.Renderer == excludeRenderer)
                    continue;
                EnsureTransparentMaterials(e.Renderer, e.TransparentMaterials);
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
                    if (e.Renderer != null && e.OriginalMaterials != null)
                    {
                        try { e.Renderer.sharedMaterials = e.OriginalMaterials; }
                        catch { }
                        PiPDisablerPlugin.LogVerbose(
                            $"[LensTransparency] Restored renderer '{e.Renderer.gameObject.name}' materials");
                    }

                    DestroyMaterials(e.TransparentMaterials);
                }
                catch { }
            }

            PiPDisablerPlugin.LogInfo($"[LensTransparency] Restored {_hidden.Count} lens renderers");
            _hidden.Clear();
        }

        // ===== Core =====

        private static void MakeTransparent(Renderer r)
        {
            if (r == null) return;

            for (int i = 0; i < _hidden.Count; i++)
                if (_hidden[i].Renderer == r) return;

            Material[] origMats = null;
            try { origMats = r.sharedMaterials; } catch { }
            if (origMats == null || origMats.Length == 0) return;

            Material[] transparentMats = CreateTransparentMaterials(origMats);
            if (transparentMats == null || transparentMats.Length == 0) return;

            var entry = new HiddenEntry
            {
                Renderer = r,
                OriginalMaterials = origMats,
                TransparentMaterials = transparentMats,
            };
            _hidden.Add(entry);

            EnsureTransparentMaterials(r, transparentMats);

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] Transparentized lens '{r.gameObject.name}' with {transparentMats.Length} material(s)");
        }

        private static Material[] CreateTransparentMaterials(Material[] source)
        {
            if (source == null || source.Length == 0) return null;

            var result = new Material[source.Length];
            for (int i = 0; i < source.Length; i++)
            {
                var src = source[i];
                if (src == null) continue;

                var copy = new Material(src)
                {
                    name = src.name + "__PiPTransparentLens"
                };
                ApplyTransparentProperties(copy);
                result[i] = copy;
            }
            return result;
        }

        private static void EnsureTransparentMaterials(Renderer r, Material[] transparentMats)
        {
            if (r == null || transparentMats == null || transparentMats.Length == 0) return;

            try
            {
                bool needsAssign = false;
                var current = r.sharedMaterials;
                if (current == null || current.Length != transparentMats.Length)
                {
                    needsAssign = true;
                }
                else
                {
                    for (int i = 0; i < current.Length; i++)
                    {
                        if (!ReferenceEquals(current[i], transparentMats[i]))
                        {
                            needsAssign = true;
                            break;
                        }
                    }
                }

                for (int i = 0; i < transparentMats.Length; i++)
                    ApplyTransparentProperties(transparentMats[i]);

                if (needsAssign)
                    r.sharedMaterials = transparentMats;
            }
            catch { }
        }

        private static void ApplyTransparentProperties(Material m)
        {
            if (m == null) return;

            if (m.HasProperty(_propColor))
                m.SetColor(_propColor, new Color(0f, 0f, 0f, 0f));

            if (m.HasProperty(_propSwitchToSight))
                m.SetFloat(_propSwitchToSight, 0f);

            m.renderQueue = 4000;
        }

        private static void DestroyMaterials(Material[] materials)
        {
            if (materials == null) return;
            for (int i = 0; i < materials.Length; i++)
            {
                var mat = materials[i];
                if (mat != null)
                    UnityEngine.Object.Destroy(mat);
            }
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
        internal static bool IsLensSurfaceRenderer(Renderer r)
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
        /// Returns active lens renderers with live geometry for the reticle stencil pass.
        /// The reticle is only drawn where these lens surfaces are visible.
        /// </summary>
        public static List<Renderer> CollectLensMaskRenderers(OpticSight os)
        {
            var result = new List<Renderer>();
            if (os == null) return result;

            Transform searchRoot = FindScopeSearchRoot(os.transform);
            var allRenderers = searchRoot.GetComponentsInChildren<Renderer>(true);

            foreach (var r in allRenderers)
            {
                if (r == null) continue;
                if (!r.gameObject.activeInHierarchy) continue;
                if (!IsLensSurfaceRenderer(r)) continue;
                if (ShouldSkipForCollimator(r)) continue;

                var mf = r.GetComponent<MeshFilter>();
                var smr = r as SkinnedMeshRenderer;
                Mesh mesh = mf?.sharedMesh ?? smr?.sharedMesh;
                if (mesh == null || mesh.vertexCount == 0) continue;

                result.Add(r);
                PiPDisablerPlugin.LogInfo(
                    $"[LensTransparency] LensMask +renderer: go='{r.gameObject.name}'" +
                    $" mesh='{mesh.name}' verts={mesh.vertexCount}" +
                    $" shader='{(r.sharedMaterial?.shader?.name ?? "null")}'");
            }

            PiPDisablerPlugin.LogInfo(
                $"[LensTransparency] CollectLensMaskRenderers: {result.Count} renderer(s)" +
                $" (searchRoot='{searchRoot?.name ?? "null"}' from '{os.name}')");

            return result;
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
                if (IsLensSurfaceRenderer(r)) continue;
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
                if (IsLensSurfaceRenderer(r)) continue;
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

                bool isLens = IsLensSurfaceRenderer(r);
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
