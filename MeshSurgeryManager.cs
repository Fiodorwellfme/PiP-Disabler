using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
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
        // Reusable temp list for restore operations (avoids per-call heap allocation)
        private static readonly List<MeshFilter> _restoreTemp = new List<MeshFilter>(64);
        private static bool _loggedGpuCopy;

        public static void ClearPersistentCache()
        {
            string cacheDir = ScopeHousingMeshSurgeryPlugin.GetMeshCutCacheDirectory();
            if (!Directory.Exists(cacheDir)) return;

            try
            {
                int removed = 0;
                foreach (string cacheFile in Directory.GetFiles(cacheDir, "*.bin"))
                {
                    File.Delete(cacheFile);
                    removed++;
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[MeshSurgery] Cleared persistent mesh cache: removed {removed} file(s).");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogWarn(
                    $"[MeshSurgery] Failed to clear persistent cache: {ex.Message}");
            }
        }

        private static class MeshCutCache
        {
            private const int Version = 1;

            public static bool TryLoad(string key, out Mesh mesh)
            {
                mesh = null;
                string path = GetPath(key);
                if (!File.Exists(path)) return false;

                try
                {
                    using (var fs = File.OpenRead(path))
                    using (var br = new BinaryReader(fs))
                    {
                        if (br.ReadInt32() != Version) return false;

                        int vertexCount = br.ReadInt32();
                        if (vertexCount < 0) return false;

                        var vertices = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            vertices[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                        var normals = new Vector3[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            normals[i] = new Vector3(br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                        var tangents = new Vector4[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            tangents[i] = new Vector4(br.ReadSingle(), br.ReadSingle(), br.ReadSingle(), br.ReadSingle());

                        var uv = new Vector2[vertexCount];
                        for (int i = 0; i < vertexCount; i++)
                            uv[i] = new Vector2(br.ReadSingle(), br.ReadSingle());

                        var indexFormat = (UnityEngine.Rendering.IndexFormat)br.ReadInt32();
                        int subMeshCount = br.ReadInt32();
                        if (subMeshCount <= 0) return false;

                        var cachedMesh = new Mesh();
                        cachedMesh.name = "mesh_cut_cached";
                        cachedMesh.indexFormat = indexFormat;
                        cachedMesh.vertices = vertices;
                        cachedMesh.normals = normals;
                        cachedMesh.tangents = tangents;
                        cachedMesh.uv = uv;
                        cachedMesh.subMeshCount = subMeshCount;

                        for (int s = 0; s < subMeshCount; s++)
                        {
                            int triCount = br.ReadInt32();
                            if (triCount < 0) return false;

                            var tris = new int[triCount];
                            for (int t = 0; t < triCount; t++)
                                tris[t] = br.ReadInt32();
                            cachedMesh.SetTriangles(tris, s, true);
                        }

                        cachedMesh.RecalculateBounds();
                        mesh = cachedMesh;
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose($"[MeshSurgery] Cache load failed '{path}': {ex.Message}");
                    return false;
                }
            }

            public static void Save(string key, Mesh mesh)
            {
                if (mesh == null) return;

                string path = GetPath(key);
                string tmpPath = path + ".tmp";

                try
                {
                    var vertices = mesh.vertices ?? Array.Empty<Vector3>();
                    var normals = mesh.normals;
                    var tangents = mesh.tangents;
                    var uv = mesh.uv;

                    if (normals == null || normals.Length != vertices.Length)
                    {
                        mesh.RecalculateNormals();
                        normals = mesh.normals;
                    }
                    if (tangents == null || tangents.Length != vertices.Length)
                        tangents = new Vector4[vertices.Length];
                    if (uv == null || uv.Length != vertices.Length)
                        uv = new Vector2[vertices.Length];

                    using (var fs = File.Create(tmpPath))
                    using (var bw = new BinaryWriter(fs))
                    {
                        bw.Write(Version);
                        bw.Write(vertices.Length);

                        for (int i = 0; i < vertices.Length; i++)
                        {
                            bw.Write(vertices[i].x); bw.Write(vertices[i].y); bw.Write(vertices[i].z);
                        }
                        for (int i = 0; i < normals.Length; i++)
                        {
                            bw.Write(normals[i].x); bw.Write(normals[i].y); bw.Write(normals[i].z);
                        }
                        for (int i = 0; i < tangents.Length; i++)
                        {
                            bw.Write(tangents[i].x); bw.Write(tangents[i].y); bw.Write(tangents[i].z); bw.Write(tangents[i].w);
                        }
                        for (int i = 0; i < uv.Length; i++)
                        {
                            bw.Write(uv[i].x); bw.Write(uv[i].y);
                        }

                        bw.Write((int)mesh.indexFormat);
                        bw.Write(mesh.subMeshCount);
                        for (int s = 0; s < mesh.subMeshCount; s++)
                        {
                            var tris = mesh.GetTriangles(s);
                            bw.Write(tris.Length);
                            for (int t = 0; t < tris.Length; t++) bw.Write(tris[t]);
                        }
                    }

                    if (File.Exists(path)) File.Delete(path);
                    File.Move(tmpPath, path);
                }
                catch (Exception ex)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose($"[MeshSurgery] Cache save failed '{path}': {ex.Message}");
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }

            public static string BuildKey(Transform scopeRoot, Transform activeMode, MeshFilter mf, Mesh originalAsset, MeshPlaneCutter.KeepSide keepSide, bool isCylinder)
            {
                var sb = new StringBuilder(512);
                sb.Append("v2|");
                sb.Append(scopeRoot != null ? scopeRoot.name : "scope").Append('|');
                sb.Append(activeMode != null ? activeMode.name : "mode").Append('|');
                sb.Append(GetRelativePath(scopeRoot, mf != null ? mf.transform : null)).Append('|');
                sb.Append(originalAsset != null ? originalAsset.name : "null").Append('|');
                sb.Append(originalAsset != null ? originalAsset.vertexCount : 0).Append('|');
                sb.Append(originalAsset != null ? originalAsset.subMeshCount : 0).Append('|');
                sb.Append((int)keepSide).Append('|');
                sb.Append(isCylinder ? "cyl" : "plane").Append('|');

                if (isCylinder)
                {
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetCylinderRadius());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetCutStartOffset());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetCutLength());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetNearPreserveDepth());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane2Position());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane2Radius());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane3Position());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane3Radius());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane4Position());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane4Radius());
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlane1OffsetMeters());
                }
                else
                {
                    AppendFloat(sb, ScopeHousingMeshSurgeryPlugin.GetPlaneOffsetMeters());
                }

                using (var sha = SHA256.Create())
                {
                    var bytes = Encoding.UTF8.GetBytes(sb.ToString());
                    var hash = sha.ComputeHash(bytes);
                    var hex = new StringBuilder(hash.Length * 2);
                    for (int i = 0; i < hash.Length; i++)
                        hex.Append(hash[i].ToString("x2"));
                    string scopeName = Sanitize(scopeRoot != null ? scopeRoot.name : "scope");
                    string meshName = Sanitize(originalAsset != null ? originalAsset.name : "mesh");
                    return scopeName + "__" + meshName + "__" + hex;
                }
            }

            private static string GetPath(string key)
            {
                return Path.Combine(ScopeHousingMeshSurgeryPlugin.GetMeshCutCacheDirectory(), key + ".bin");
            }

            private static string GetRelativePath(Transform root, Transform child)
            {
                if (child == null) return "none";
                if (root == null) return child.name ?? "unnamed";

                var nodes = new List<string>();
                for (var t = child; t != null; t = t.parent)
                {
                    nodes.Add(t.name ?? "unnamed");
                    if (t == root) break;
                }
                nodes.Reverse();
                return string.Join("/", nodes.ToArray());
            }

            private static string Sanitize(string value)
            {
                if (string.IsNullOrEmpty(value)) return "unknown";
                var invalid = Path.GetInvalidFileNameChars();
                var sb = new StringBuilder(value.Length);
                for (int i = 0; i < value.Length; i++)
                {
                    char c = value[i];
                    bool bad = false;
                    for (int j = 0; j < invalid.Length; j++)
                    {
                        if (c == invalid[j]) { bad = true; break; }
                    }
                    if (bad || char.IsWhiteSpace(c)) sb.Append('_');
                    else sb.Append(c);
                }
                return sb.ToString();
            }

            private static void AppendFloat(StringBuilder sb, float v)
            {
                sb.Append(Mathf.Round(v * 1000f) / 1000f).Append('|');
            }
        }

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

            bool isCylinderMode = ScopeHousingMeshSurgeryPlugin.GetCutMode() == "Cylinder";
            float plane1Offset = isCylinderMode
                ? ScopeHousingMeshSurgeryPlugin.GetPlane1OffsetMeters()
                : ScopeHousingMeshSurgeryPlugin.GetPlaneOffsetMeters();
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
            float cutRadius = ScopeHousingMeshSurgeryPlugin.GetCutRadius();

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
                    bool isCylinder = ScopeHousingMeshSurgeryPlugin.GetCutMode() == "Cylinder";
                    string cacheKey = MeshCutCache.BuildKey(scopeRoot, activeMode, mf, originalAsset, keepSide, isCylinder);

                    Mesh readable;
                    if (MeshCutCache.TryLoad(cacheKey, out var cachedMesh))
                    {
                        readable = cachedMesh;
                        readable.name = originalAsset.name + "_CUT_CACHED";
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[MeshSurgery] Loaded cached cut mesh for '{originalAsset.name}'.");
                    }
                    else
                    {
                        // Step 1: GPU copy to create a readable mesh
                        readable = MeshPlaneCutter.MakeReadableMeshCopy(originalAsset);
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

                        if (isCylinder)
                        {
                            float nearR = ScopeHousingMeshSurgeryPlugin.GetCylinderRadius();
                            float startOff = ScopeHousingMeshSurgeryPlugin.GetCutStartOffset();
                            float cutLen = ScopeHousingMeshSurgeryPlugin.GetCutLength();
                            float preserve = ScopeHousingMeshSurgeryPlugin.GetNearPreserveDepth();
                            float p2 = ScopeHousingMeshSurgeryPlugin.GetPlane2Position();
                            float r2 = ScopeHousingMeshSurgeryPlugin.GetPlane2Radius();
                            float p3 = ScopeHousingMeshSurgeryPlugin.GetPlane3Position();
                            float r3 = ScopeHousingMeshSurgeryPlugin.GetPlane3Radius();
                            float p4 = ScopeHousingMeshSurgeryPlugin.GetPlane4Position();
                            float r4 = ScopeHousingMeshSurgeryPlugin.GetPlane4Radius();

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

                        MeshCutCache.Save(cacheKey, readable);
                    }

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

            // Mirror the ExpandSearchToWeaponRoot expansion from FindTargetMeshFilters
            // so weapon-body meshes that were cut can also be restored.
            if (ScopeHousingMeshSurgeryPlugin.GetExpandSearchToWeaponRoot())
            {
                for (var p = searchRoot.parent; p != null; p = p.parent)
                {
                    if ((p.name ?? "").StartsWith("Weapon_root", StringComparison.OrdinalIgnoreCase))
                    {
                        searchRoot = p;
                        break;
                    }
                }
            }

            // Collect keys to restore without LINQ allocation.
            // Reuse a temporary list to avoid per-call heap allocs.
            var toRestore = _restoreTemp;
            toRestore.Clear();
            foreach (var kv in _tracked)
            {
                var mf = kv.Key;
                if (mf && mf.transform && mf.transform.IsChildOf(searchRoot))
                    toRestore.Add(mf);
            }

            if (toRestore.Count == 0) return;

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MeshSurgery] RestoreForScope: {toRestore.Count} meshes to restore (searchRoot='{searchRoot.name}')");

            for (int i = 0; i < toRestore.Count; i++)
                RestoreMeshFilter(toRestore[i]);
            toRestore.Clear();
        }

        public static void RestoreAll()
        {
            if (_tracked.Count == 0) return;

            // Collect keys into temp list to avoid modifying dict during iteration
            var keys = _restoreTemp;
            keys.Clear();
            foreach (var kv in _tracked)
                keys.Add(kv.Key);

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[MeshSurgery] RestoreAll: {keys.Count} meshes to restore");

            for (int i = 0; i < keys.Count; i++)
                RestoreMeshFilter(keys[i]);
            keys.Clear();
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
            float d = Vector3.Dot(planeNormal, camPos - planePoint);
            bool cameraIsPositive = d >= 0f;
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
                $"axis={ScopeHousingMeshSurgeryPlugin.GetPlaneNormalAxis()}, " +
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
            string axis = ScopeHousingMeshSurgeryPlugin.GetPlaneNormalAxis() ?? "Auto";

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

            Transform GetModeAncestor(Transform t, Transform searchRoot)
            {
                for (var p = t; p != null && p != searchRoot; p = p.parent)
                    if (p.name != null && (p.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                        || p.name.Equals("mode", StringComparison.OrdinalIgnoreCase)))
                        return p;
                return null;
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

            // Optional: climb further up to the Weapon_root node to include weapon body meshes.
            // Normally the loop above stops at any parent whose name contains "weapon" or "anim".
            // With ExpandSearchToWeaponRoot the search climbs through those intermediate nodes
            // until it finds a transform whose name starts with "Weapon_root".
            // Path example: Weapon_root/Weapon_root_anim/weapon/mod_scope/<scope>
            if (ScopeHousingMeshSurgeryPlugin.GetExpandSearchToWeaponRoot())
            {
                for (var p = searchRoot.parent; p != null; p = p.parent)
                {
                    if ((p.name ?? "").StartsWith("Weapon_root", StringComparison.OrdinalIgnoreCase))
                    {
                        searchRoot = p;
                        ScopeHousingMeshSurgeryPlugin.LogVerbose(
                            $"[ScopeHierarchy] ExpandSearchToWeaponRoot: climbed to '{searchRoot.name}'");
                        break;
                    }
                }
            }

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
            int skippedMode = 0, skippedOther = 0;

            foreach (var mf in searchRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;

                // Skip meshes under other scope roots (sibling scopes, canted sights)
                if (otherScopeRoots.Count > 0 && IsUnderOtherScope(mf.transform))
                { skippedOther++; continue; }

                // Skip meshes belonging to a DIFFERENT mode (not the active one)
                var modeAncestor = GetModeAncestor(mf.transform, searchRoot);
                if (modeAncestor != null && activeMode != null && modeAncestor != activeMode)
                { skippedMode++; continue; }

                result.Add(mf);
            }

            ScopeHousingMeshSurgeryPlugin.LogVerbose(
                $"[ScopeHierarchy] FindTargets from '{searchRoot.name}': " +
                $"{result.Count} targets, skipped: mode={skippedMode} otherScope={skippedOther}");

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

    }
}
