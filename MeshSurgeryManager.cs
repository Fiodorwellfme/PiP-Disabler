using System;
using System.Collections.Generic;
using System.Linq;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    public static class MeshSurgeryManager
    {
        /// <summary>
        /// Tracks one modified MeshFilter.
        /// Lifecycle:
        ///   1. Save OriginalAssetMesh (the non-readable asset mesh)
        ///   2. Create CutMesh via GPU copy + plane cut
        ///   3. Assign mf.sharedMesh = CutMesh
        ///   4. On restore: mf.sharedMesh = OriginalAssetMesh, Destroy(CutMesh)
        /// Only ONE Mesh allocation per target. No leaks.
        /// </summary>
        private sealed class MeshState
        {
            public Mesh OriginalAssetMesh;   // The original (non-readable) asset mesh
            public Mesh CutMesh;             // Our GPU-copied + cut mesh (to be Destroyed on restore)
            public bool Applied;
        }

        private static readonly Dictionary<MeshFilter, MeshState> _tracked =
            new Dictionary<MeshFilter, MeshState>(64);
        private static bool _loggedGpuCopy;

        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null) return;

            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (!scopeRoot) return;

            Transform activeMode = null;
            if (os.transform.name != null &&
                (os.transform.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                 || os.transform.name.Equals("mode", StringComparison.OrdinalIgnoreCase)))
                activeMode = os.transform;
            else
                activeMode = ScopeHierarchy.FindBestMode(scopeRoot);

            if (!activeMode) activeMode = os.transform;

            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode,
                out var planePoint, out var planeNormal, out var camPos, out var backLensTf))
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose("[MeshSurgery] TryGetPlane failed — no plane found.");
                return;
            }

            planePoint += planeNormal * ScopeHousingMeshSurgeryPlugin.PlaneOffsetMeters.Value;

            bool keepPositive = DecideKeepPositive(planePoint, planeNormal, camPos);
            var keepSide = keepPositive
                ? MeshPlaneCutter.KeepSide.Positive
                : MeshPlaneCutter.KeepSide.Negative;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MeshSurgery] Plane: point={planePoint:F4} normal={planeNormal:F4} keepSide={keepSide}");

            // Show visualizer (if enabled)
            PlaneVisualizer.Show(planePoint, planeNormal);

            var targets = ScopeHierarchy.FindTargetMeshFilters(scopeRoot, activeMode);
            float cutRadius = ScopeHousingMeshSurgeryPlugin.CutRadius.Value;

            foreach (var mf in targets)
            {
                if (!mf || !mf.sharedMesh) continue;

                // Radius filter: skip meshes too far from the cut center.
                if (cutRadius > 0f)
                {
                    var boundsCenter = mf.GetComponent<Renderer>()?.bounds.center ?? mf.transform.position;
                    float dist = Vector3.Distance(boundsCenter, planePoint);
                    if (dist > cutRadius)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] Skipping '{mf.sharedMesh.name}' — dist={dist:F4} > radius={cutRadius:F4}");
                        continue;
                    }
                }

                // Already applied? Skip.
                if (_tracked.TryGetValue(mf, out var existing) && existing.Applied)
                    continue;

                // First time: save original and create cut mesh.
                Mesh originalAsset = mf.sharedMesh;

                try
                {
                    // Step 1: GPU copy to create a readable mesh
                    Mesh readable = MeshPlaneCutter.MakeReadableMeshCopy(originalAsset);
                    if (readable == null)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] GPU copy returned null for '{originalAsset.name}' — skipping.");
                        continue;
                    }

                    readable.name = originalAsset.name + "_readable";

                    if (!_loggedGpuCopy)
                    {
                        _loggedGpuCopy = true;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            "[MeshSurgery] Created readable mesh copies via GPU buffer. Plane cutting enabled.");
                    }

                    int vertsBefore = readable.vertexCount;

                    // Step 2: Cut the readable mesh in-place
                    bool ok;
                    bool isCylinder = ScopeHousingMeshSurgeryPlugin.CutMode.Value == "Cylinder";

                    if (isCylinder)
                    {
                        Transform linzaTf = ScopeHierarchy.FindLinzaTransform(scopeRoot, activeMode);
                        float lensRadius = ScopeHierarchy.EstimateLensRadius(os, scopeRoot, activeMode);
                        float p1Offset = ScopeHousingMeshSurgeryPlugin.Plane1Offset.Value;
                        float p1Radius = Mathf.Max(0.001f,
                            lensRadius * ScopeHousingMeshSurgeryPlugin.Plane1RadiusMultiplier.Value);
                        float p2Offset = ScopeHousingMeshSurgeryPlugin.Plane2Offset.Value;
                        float p2Radius = ScopeHousingMeshSurgeryPlugin.Plane2Radius.Value;
                        float p3Offset = ScopeHousingMeshSurgeryPlugin.Plane3Offset.Value;
                        float p3Radius = ScopeHousingMeshSurgeryPlugin.Plane3Radius.Value;
                        float p4Offset = ScopeHousingMeshSurgeryPlugin.Plane4Offset.Value;
                        float p4Radius = ScopeHousingMeshSurgeryPlugin.Plane4Radius.Value;

                        float[] offsets = { p1Offset, p2Offset, p3Offset, p4Offset };
                        float[] radii = { p1Radius, p2Radius, p3Radius, p4Radius };
                        SortProfile(offsets, radii);

                        Vector3 profileOrigin = linzaTf != null
                            ? linzaTf.position
                            : (backLensTf != null ? backLensTf.position : planePoint);

                        ok = MeshPlaneCutter.CutMeshRadialProfile(readable, mf.transform,
                            profileOrigin, planeNormal, offsets, radii, keepInside: false);

                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] 4-plane cut '{originalAsset.name}': " +
                            $"P1(o={offsets[0]:F4},r={radii[0]:F4}) P2(o={offsets[1]:F4},r={radii[1]:F4}) " +
                            $"P3(o={offsets[2]:F4},r={radii[2]:F4}) P4(o={offsets[3]:F4},r={radii[3]:F4}) lensR={lensRadius:F4}");
                    }
                    else
                    {
                        ok = MeshPlaneCutter.CutMeshDirect(readable, mf.transform,
                            planePoint, planeNormal, keepSide);
                    }

                    if (!ok)
                    {
                        // Cut removed everything — clear the mesh to make it empty
                        readable.Clear();
                        readable.name = originalAsset.name + "_CUT_EMPTY";
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] Cut removed all geometry from '{originalAsset.name}' — applying empty mesh.");
                    }
                    else
                    {
                        readable.name = originalAsset.name + "_CUT";
                    }
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[MeshSurgery] Cut '{originalAsset.name}': {vertsBefore} → {readable.vertexCount} verts");

                    // Step 3: Swap onto the MeshFilter
                    mf.sharedMesh = readable;

                    // Step 4: Track for restore
                    _tracked[mf] = new MeshState
                    {
                        OriginalAssetMesh = originalAsset,
                        CutMesh = readable,
                        Applied = true
                    };
                }
                catch (Exception ex)
                {
                    ScopeHousingMeshSurgeryPlugin.LogError(
                        $"[MeshSurgery] Failed on '{originalAsset.name}': {ex.Message}");
                }
            }
        }

        public static void RestoreForScope(Transform anyTransformUnderScope)
        {
            var scopeRoot = ScopeHierarchy.FindScopeRoot(anyTransformUnderScope);
            if (!scopeRoot) return;

            // Use the same expanded search root as FindTargetMeshFilters
            // so we restore mount meshes that were found above scopeRoot.
            Transform searchRoot = scopeRoot;
            for (var p = scopeRoot.parent; p != null; p = p.parent)
            {
                var pName = p.name ?? "";
                var plo = pName.ToLowerInvariant();
                if (plo.Contains("weapon") || plo.Contains("receiver") || plo.Contains("anim"))
                    break;
                if (plo.Contains("scope") || plo.Contains("mod_") || plo.Contains("optic") || plo.Contains("mount"))
                { searchRoot = p; continue; }
                break;
            }

            var toRestore = _tracked.Keys
                .Where(mf => mf && mf.transform && mf.transform.IsChildOf(searchRoot))
                .ToArray();

            if (toRestore.Length == 0) return;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MeshSurgery] RestoreForScope: {toRestore.Length} meshes to restore (searchRoot='{searchRoot.name}')");

            foreach (var mf in toRestore)
                RestoreMeshFilter(mf);
        }

        public static void RestoreAll()
        {
            var keys = _tracked.Keys.ToArray();
            if (keys.Length == 0) return;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MeshSurgery] RestoreAll: {keys.Length} meshes to restore");

            foreach (var mf in keys)
                RestoreMeshFilter(mf);
        }

        private static void RestoreMeshFilter(MeshFilter mf)
        {
            if (!mf) { _tracked.Remove(mf); return; }
            if (!_tracked.TryGetValue(mf, out var st) || st == null) return;

            // Restore original asset mesh
            try
            {
                mf.sharedMesh = st.OriginalAssetMesh;
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[MeshSurgery] Restored '{st.OriginalAssetMesh?.name}' on {mf.gameObject.name}");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError(
                    $"[MeshSurgery] Restore failed: {ex.Message}");
            }

            // Destroy our created mesh to free GPU/CPU memory
            try
            {
                if (st.CutMesh != null)
                    UnityEngine.Object.Destroy(st.CutMesh);
            }
            catch { }

            _tracked.Remove(mf);
        }

        private static bool DecideKeepPositive(Vector3 planePoint, Vector3 planeNormal, Vector3 camPos)
        {
            if (ScopeHousingMeshSurgeryPlugin.ForceManualKeepSide.Value)
                return ScopeHousingMeshSurgeryPlugin.ManualKeepPositive.Value;

            float d = Vector3.Dot(planeNormal, camPos - planePoint);
            bool cameraIsPositive = d >= 0f;

            if (ScopeHousingMeshSurgeryPlugin.RemoveCameraSide.Value)
                return !cameraIsPositive;

            return cameraIsPositive;
        }

        private static void SortProfile(float[] offsets, float[] radii)
        {
            for (int i = 0; i < offsets.Length - 1; i++)
            {
                for (int j = i + 1; j < offsets.Length; j++)
                {
                    if (offsets[j] < offsets[i])
                    {
                        float to = offsets[i]; offsets[i] = offsets[j]; offsets[j] = to;
                        float tr = radii[i]; radii[i] = radii[j]; radii[j] = tr;
                    }
                }
            }
        }
    }

    internal static class ScopeHierarchy
    {
        /// <summary>
        /// Find the scope root transform by walking up from any child transform.
        /// Strategy:
        ///   1. First pass: find a parent with mode_* children (multi-mode scopes like Valday)
        ///   2. Fallback: find a parent that has a 'backLens' child (single-mode scopes like Bravo 4x30)
        ///   3. Fallback: find a parent whose name contains 'scope' (broad catch)
        /// </summary>
        public static Transform FindScopeRoot(Transform any)
        {
            // Pass 1: mode-based (most specific — handles multi-mode scopes)
            for (var t = any; t != null; t = t.parent)
            {
                if (HasModeChild(t)) return t;
            }

            // Pass 2: backLens-based (handles single-mode scopes with direct backLens child)
            for (var t = any; t != null; t = t.parent)
            {
                if (HasDirectChild(t, "backLens") || HasDirectChild(t, "backlens"))
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ScopeHierarchy] FindScopeRoot fallback (backLens child): '{t.name}'");
                    return t;
                }
            }

            // Pass 3: name-based (last resort — find something that looks like a scope)
            for (var t = any; t != null; t = t.parent)
            {
                if (t.name != null)
                {
                    var lo = t.name.ToLowerInvariant();
                    if (lo.Contains("scope") && !lo.StartsWith("mod_scope"))
                    {
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[ScopeHierarchy] FindScopeRoot fallback (name match): '{t.name}'");
                        return t;
                    }
                }
            }

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeHierarchy] FindScopeRoot FAILED for '{any?.name}' — no scope root found");
            return null;
        }

        private static bool HasDirectChild(Transform t, string childName)
        {
            if (t == null) return false;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c != null && c.name != null &&
                    c.name.Equals(childName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool HasModeChild(Transform t)
        {
            if (t == null) return false;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null || c.name == null) continue;
                // Match "mode_000", "mode_001" etc AND plain "mode"
                if (c.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                    || c.name.Equals("mode", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsModeNode(string name)
        {
            if (name == null) return false;
            return name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                || name.Equals("mode", StringComparison.OrdinalIgnoreCase);
        }

        public static Transform FindBestMode(Transform scopeRoot)
        {
            if (scopeRoot == null) return null;

            Transform firstActive = null;
            Transform withBackLens = null;

            for (int i = 0; i < scopeRoot.childCount; i++)
            {
                var c = scopeRoot.GetChild(i);
                if (c == null || !IsModeNode(c.name)) continue;

                if (c.gameObject.activeInHierarchy && firstActive == null)
                    firstActive = c;

                if (c.gameObject.activeInHierarchy)
                {
                    var bl = FindDeepChild(c, "backLens");
                    if (bl != null) { withBackLens = c; break; }
                }
            }

            if (withBackLens != null) return withBackLens;
            if (firstActive != null) return firstActive;

            for (int i = 0; i < scopeRoot.childCount; i++)
            {
                var c = scopeRoot.GetChild(i);
                if (c != null && IsModeNode(c.name))
                    return c;
            }
            return null;
        }

        public static Transform FindDeepChild(Transform root, string nameEquals)
        {
            if (root == null) return null;
            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;
                if (t.name != null && string.Equals(t.name, nameEquals, StringComparison.OrdinalIgnoreCase))
                    return t;

                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }
            return null;
        }

        public static bool TryGetPlane(OpticSight os, Transform scopeRoot, Transform activeMode,
            out Vector3 planePoint, out Vector3 planeNormal, out Vector3 camPos, out Transform backLensTransform)
        {
            planePoint = default;
            planeNormal = default;
            camPos = default;
            backLensTransform = null;

            Transform viewerTf = null;
            try { viewerTf = os != null ? os.ScopeTransform : null; } catch { }

            if (viewerTf != null) camPos = viewerTf.position;
            else { var mc = ScopeHousingMeshSurgeryPlugin.GetMainCamera(); camPos = mc != null ? mc.transform.position : activeMode.position; }

            // Find the best reference transform for the cut plane.
            Transform refTransform = null;

            var backLens = FindDeepChild(activeMode, "backLens");
            if (backLens != null)
            {
                planePoint = backLens.position;
                refTransform = backLens;
                backLensTransform = backLens;
            }

            if (refTransform == null)
            {
                try
                {
                    var lr = os != null ? os.LensRenderer : null;
                    if (lr != null)
                    {
                        planePoint = lr.bounds.center;
                        refTransform = lr.transform;
                    }
                }
                catch { }
            }

            if (refTransform == null)
            {
                var lens = scopeRoot.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(t =>
                    {
                        if (t == null || t.name == null) return false;
                        var n = t.name.ToLowerInvariant();
                        return n.Contains("lens") || n.Contains("linza") || n.Contains("glass");
                    });

                if (lens != null)
                {
                    planePoint = lens.position;
                    refTransform = lens;
                }
            }

            if (refTransform == null)
            {
                Transform opticCamTf = FindDeepChild(activeMode, "optic_camera");
                if (opticCamTf != null)
                {
                    planePoint = opticCamTf.position + opticCamTf.forward * 0.02f;
                    refTransform = opticCamTf;
                }
            }

            if (refTransform == null) return false;

            // Determine the plane normal based on config.
            planeNormal = GetConfiguredNormal(refTransform);

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeHierarchy] TryGetPlane: ref='{refTransform.name}', " +
                $"axis={ScopeHousingMeshSurgeryPlugin.PlaneNormalAxis.Value}, " +
                $"normal={planeNormal:F3}");

            return true;
        }

        /// <summary>
        /// Returns the plane normal based on the PlaneNormalAxis config.
        /// Auto = transform.forward (game default).
        /// X/Y/Z/-X/-Y/-Z = that local axis of the reference transform.
        /// </summary>
        private static Vector3 GetConfiguredNormal(Transform refTransform)
        {
            string axis = ScopeHousingMeshSurgeryPlugin.PlaneNormalAxis.Value ?? "Auto";

            switch (axis)
            {
                case "X":  return  refTransform.right;
                case "-X": return -refTransform.right;
                case "Y":  return  refTransform.up;
                case "-Y": return -refTransform.up;
                case "Z":  return  refTransform.forward;
                case "-Z": return -refTransform.forward;
                default:   return  refTransform.forward; // "Auto"
            }
        }

        public static List<MeshFilter> FindTargetMeshFilters(Transform scopeRoot, Transform activeMode)
        {
            var all = UnityEngine.Object.FindObjectsOfType<MeshFilter>(true);
            var result = new List<MeshFilter>(all.Length);
            for (int i = 0; i < all.Length; i++)
            {
                var mf = all[i];
                if (!mf || !mf.sharedMesh) continue;
                result.Add(mf);
            }

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeHierarchy] FindTargets global: {result.Count} mesh filters in scene");
            return result;
        }

        /// <summary>
        /// Find all scope roots under a search root that are NOT the active scope root.
        /// A scope root is any transform with mode_* children.
        /// </summary>
        private static void CollectOtherScopeRoots(Transform searchRoot, Transform activeScopeRoot,
            List<Transform> results)
        {
            var stack = new Stack<Transform>();
            stack.Push(searchRoot);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;

                // If this is a scope root (has mode_* children) and it's not the active one, record it
                if (t != activeScopeRoot && HasModeChild(t))
                {
                    results.Add(t);
                    continue; // don't recurse into other scopes
                }

                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }
        }

        public static float EstimateLensRadius(OpticSight os, Transform scopeRoot, Transform activeMode)
        {
            try
            {
                if (os != null && os.LensRenderer != null)
                {
                    var ext = os.LensRenderer.bounds.extents;
                    float r = Mathf.Max(ext.x, ext.y, ext.z);
                    if (r > 0.0005f) return r;
                }
            }
            catch { }

            var linza = FindLinzaTransform(scopeRoot, activeMode);
            if (linza != null)
            {
                var mr = linza.GetComponent<MeshRenderer>();
                if (mr != null)
                {
                    var ext = mr.bounds.extents;
                    float r = Mathf.Max(ext.x, ext.y, ext.z);
                    if (r > 0.0005f) return r;
                }

                var mrs = linza.GetComponentsInChildren<MeshRenderer>(true);
                for (int i = 0; i < mrs.Length; i++)
                {
                    if (mrs[i] == null) continue;
                    var ext = mrs[i].bounds.extents;
                    float r = Mathf.Max(ext.x, ext.y, ext.z);
                    if (r > 0.0005f) return r;
                }
            }

            return Mathf.Max(0.001f, ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value);
        }

        public static Transform FindLinzaTransform(Transform scopeRoot, Transform activeMode)
        {
            if (activeMode != null)
            {
                var inMode = FindDeepChild(activeMode, "linza");
                if (inMode != null) return inMode;
            }

            return scopeRoot != null ? FindDeepChild(scopeRoot, "linza") : null;
        }
    }
}
