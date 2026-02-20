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
                out var planePoint, out var planeNormal, out var camPos))
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose("[MeshSurgery] TryGetPlane failed — no plane found.");
                return;
            }

            bool isCylinderMode = ScopeHousingMeshSurgeryPlugin.CutMode.Value == "Cylinder";
            float plane1Offset = isCylinderMode
                ? ScopeHousingMeshSurgeryPlugin.Plane1OffsetMeters.Value
                : ScopeHousingMeshSurgeryPlugin.PlaneOffsetMeters.Value;
            planePoint += planeNormal * plane1Offset;

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
                        float nearR = ScopeHousingMeshSurgeryPlugin.CylinderRadius.Value;
                        float startOff = ScopeHousingMeshSurgeryPlugin.CutStartOffset.Value;
                        float cutLen = ScopeHousingMeshSurgeryPlugin.CutLength.Value;
                        float preserve = ScopeHousingMeshSurgeryPlugin.NearPreserveDepth.Value;
                        float p2 = ScopeHousingMeshSurgeryPlugin.Plane2Position.Value;
                        float r2 = ScopeHousingMeshSurgeryPlugin.Plane2Radius.Value;
                        float p3 = ScopeHousingMeshSurgeryPlugin.Plane3Position.Value;
                        float r3 = ScopeHousingMeshSurgeryPlugin.Plane3Radius.Value;
                        float p4 = ScopeHousingMeshSurgeryPlugin.Plane4Position.Value;
                        float r4 = ScopeHousingMeshSurgeryPlugin.Plane4Radius.Value;

                        ok = MeshPlaneCutter.CutMeshFrustum(readable, mf.transform,
                            planePoint, planeNormal, nearR, r4, startOff, cutLen,
                            keepInside: false, midRadius: r2, midPosition: p2,
                            nearPreserveDepth: preserve,
                            plane3Radius: r3, plane3Position: p3, plane4Position: p4);
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] Frustum cut '{originalAsset.name}': p1R={nearR:F4}@0.00 " +
                            $"p2R={r2:F4}@{p2:F2} p3R={r3:F4}@{p3:F2} p4R={r4:F4}@{p4:F2} " +
                            $"start={startOff:F4} len={cutLen:F4} offset={plane1Offset:F4}" +
                            (preserve > 0f ? $" preserve={preserve:F4}" : ""));
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
    }

    internal static class ScopeHierarchy
    {
        /// <summary>
        /// Find the scope root transform by walking up from any child transform.
        /// Strategy:
        ///   1. First pass: find a parent with mode_* children (multi-mode scopes like Valday)
        ///   2. Fallback: find a parent that has a 'backLens' child (single-mode scopes like Bravo 4x30)
        ///   3. Fallback: find a parent whose name contains 'scope' (broad catch, includes mod_scope)
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
                    if (lo.Contains("scope"))
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
            out Vector3 planePoint, out Vector3 planeNormal, out Vector3 camPos)
        {
            planePoint = default;
            planeNormal = default;
            camPos = default;

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
            if (scopeRoot == null) return new List<MeshFilter>();

            var excludes = ParseExcludes(ScopeHousingMeshSurgeryPlugin.ExcludeNameContainsCsv.Value);

            bool IsExcluded(string s)
            {
                if (string.IsNullOrEmpty(s)) return false;
                var l = s.ToLowerInvariant();
                for (int i = 0; i < excludes.Count; i++)
                    if (l.Contains(excludes[i])) return true;
                return false;
            }

            Transform GetModeAncestor(Transform t, Transform searchRoot)
            {
                for (var p = t; p != null && p != searchRoot; p = p.parent)
                    if (p.name != null && (p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                        || p.name.Equals("mode", StringComparison.OrdinalIgnoreCase)))
                        return p;
                return null;
            }

            bool IsLensNode(Transform t, Transform searchRoot)
            {
                for (var p = t; p != null && p != searchRoot; p = p.parent)
                {
                    if (p == null || p.name == null) continue;
                    var n = p.name.ToLowerInvariant();
                    if (n.Contains("optic_camera")) return true;
                    if (n == "backlens" || n.StartsWith("backlens")) return true;
                    if (n.Contains("linza")) return true;
                    if (n == "frontlens" || n.StartsWith("frontlens")) return true;
                    if (n == "scopereticleoverlay") return true;
                }
                return false;
            }

            // Determine search root: go up through intermediate containers to catch
            // housing + mount meshes.  EFT hierarchy variants:
            //
            //   mount_xxx(Clone)/               ← mount LODs often HERE
            //     mod_scope_000/                ← intermediate container
            //       scope_xxx(Clone)/           ← scopeRoot (has mode_* children)
            //         mode_000/ | mode/
            //
            //   mod_scope_xxx/                  ← housing meshes often HERE
            //     scope_xxx(Clone)/             ← scopeRoot
            //       mode_000/ | mode/
            //
            // We climb up through parents that look like scope/mod containers,
            // stopping at the weapon root or a non-scope parent.
            Transform searchRoot = scopeRoot;
            for (var p = scopeRoot.parent; p != null; p = p.parent)
            {
                var pName = p.name ?? "";
                var plo = pName.ToLowerInvariant();
                // Stop at weapon root, receiver, or anything that's clearly not scope-related
                if (plo.Contains("weapon") || plo.Contains("receiver") || plo.Contains("anim"))
                    break;
                // Climb through scope/mod/optic/mount containers
                if (plo.Contains("scope") || plo.Contains("mod_") || plo.Contains("optic") || plo.Contains("mount"))
                {
                    searchRoot = p;
                    continue; // keep climbing
                }
                break; // unknown parent — stop
            }
            if (searchRoot != scopeRoot)
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeHierarchy] Expanded search root: '{scopeRoot.name}' → '{searchRoot.name}'");

            // Collect all OTHER scope roots under searchRoot so we can skip their subtrees
            var otherScopeRoots = new List<Transform>(4);
            if (searchRoot != scopeRoot)
            {
                CollectOtherScopeRoots(searchRoot, scopeRoot, otherScopeRoots);
                if (otherScopeRoots.Count > 0)
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ScopeHierarchy] Found {otherScopeRoots.Count} sibling scope root(s) — will exclude their subtrees");
            }

            bool IsUnderOtherScope(Transform t)
            {
                for (int i = 0; i < otherScopeRoots.Count; i++)
                    if (t.IsChildOf(otherScopeRoots[i])) return true;
                return false;
            }

            var result = new List<MeshFilter>(64);
            int skippedLens = 0, skippedExclude = 0, skippedMode = 0, skippedOther = 0;

            foreach (var mf in searchRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;

                // Skip meshes under other scope roots (sibling scopes, canted sights)
                if (otherScopeRoots.Count > 0 && IsUnderOtherScope(mf.transform))
                { skippedOther++; continue; }

                if (IsLensNode(mf.transform, searchRoot))
                { skippedLens++; continue; }

                var goName = mf.transform.name ?? "";
                var meshName = mf.sharedMesh.name ?? "";

                if (IsExcluded(goName) || IsExcluded(meshName))
                { skippedExclude++; continue; }

                // Skip meshes belonging to a DIFFERENT mode (not the active one)
                var modeAncestor = GetModeAncestor(mf.transform, searchRoot);
                if (modeAncestor != null && activeMode != null && modeAncestor != activeMode)
                { skippedMode++; continue; }

                result.Add(mf);
            }

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeHierarchy] FindTargets from '{searchRoot.name}': " +
                $"{result.Count} targets, skipped: lens={skippedLens} exclude={skippedExclude} " +
                $"mode={skippedMode} otherScope={skippedOther}");

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

        private static List<string> ParseExcludes(string csv)
        {
            var list = new List<string>();
            if (string.IsNullOrWhiteSpace(csv)) return list;
            foreach (var part in csv.Split(','))
            {
                var t = part.Trim();
                if (t.Length == 0) continue;
                list.Add(t.ToLowerInvariant());
            }
            return list;
        }
    }
}
