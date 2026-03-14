using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using EFT;
using EFT.CameraControl;
using EFT.InventoryLogic;
using Comfort.Common;
using UnityEngine;

namespace PiPDisabler
{
    public static class MeshSurgeryManager
    {
        private sealed class CutMeshEntry
        {
            public MeshFilter Filter;
            public Mesh OriginalMesh;
            public Mesh CutMesh;
            public bool Applied;
            public string FilterPath;
        }

        private sealed class CutProfileCache
        {
            public GameObject WeaponRoot;
            public readonly List<CutMeshEntry> Entries = new List<CutMeshEntry>(64);
            public bool Built;
            public bool Dirty = true;
            public string SettingsSignature;
            public string ProfileKey;
        }

        private sealed class RaidWeaponCache
        {
            public string WeaponId;
            public Weapon WeaponItem;
            public readonly Dictionary<string, CutProfileCache> Profiles = new Dictionary<string, CutProfileCache>(4);
        }

        private sealed class LightFxState
        {
            public bool WasActiveSelf;
            public bool DisabledByUs;
        }

        private sealed class SphereState
        {
            public bool WasActiveSelf;
            public bool DisabledByUs;
        }

        private static readonly Dictionary<string, RaidWeaponCache> _raidCaches = new Dictionary<string, RaidWeaponCache>(16);
        private static CutProfileCache _currentWeaponCache;
        private static string _currentWeaponId;
        private static readonly Dictionary<GameObject, LightFxState> _disabledLightFx =
            new Dictionary<GameObject, LightFxState>(32);
        private static readonly Dictionary<GameObject, SphereState> _disabledWeaponSpheres =
            new Dictionary<GameObject, SphereState>(32);
        private static bool _loggedGpuCopy;
        private static object _inventoryEventSource;
        private static Delegate _addItemHandler;
        private static Delegate _removeItemHandler;


        public static void ClearPersistentCache()
        {
            string cacheDir = PiPDisablerPlugin.GetMeshCutCacheDirectory();
            if (!Directory.Exists(cacheDir)) return;

            try
            {
                int removed = 0;
                foreach (string cacheFile in Directory.GetFiles(cacheDir, "*.bin"))
                {
                    File.Delete(cacheFile);
                    removed++;
                }

                PiPDisablerPlugin.LogInfo(
                    $"[MeshSurgery] Cleared persistent mesh cache: removed {removed} file(s).");
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogWarn(
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
                    PiPDisablerPlugin.LogVerbose($"[MeshSurgery] Cache load failed '{path}': {ex.Message}");
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
                    PiPDisablerPlugin.LogVerbose($"[MeshSurgery] Cache save failed '{path}': {ex.Message}");
                    try { if (File.Exists(tmpPath)) File.Delete(tmpPath); } catch { }
                }
            }

            public static string BuildKey(Transform scopeRoot, Transform activeMode, MeshFilter mf, Mesh originalAsset, MeshPlaneCutter.KeepSide keepSide, bool isCylinder)
            {
                var sb = new StringBuilder(512);
                sb.Append("v3|");
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
                    AppendFloat(sb, PiPDisablerPlugin.GetCylinderRadius());
                    AppendFloat(sb, PiPDisablerPlugin.GetCutStartOffset());
                    AppendFloat(sb, PiPDisablerPlugin.GetCutLength());
                    AppendFloat(sb, PiPDisablerPlugin.GetNearPreserveDepth());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane2PositionNormalized(PiPDisablerPlugin.GetCutLength()));
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane2Radius());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane3Position());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane3Radius());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane4Position());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane4Radius());
                    AppendFloat(sb, PiPDisablerPlugin.GetPlane1OffsetMeters());
                }
                else
                {
                    AppendFloat(sb, PiPDisablerPlugin.GetPlaneOffsetMeters());
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
                return Path.Combine(PiPDisablerPlugin.GetMeshCutCacheDirectory(), key + ".bin");
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

            var activeMode = ResolveActiveMode(os, scopeRoot);
            var cache = GetOrCreateCurrentWeaponCache(scopeRoot, activeMode);
            if (cache == null) return;

            string currentSignature = BuildCutSettingsSignature();
            if (cache.Built && !cache.Dirty && !string.Equals(cache.SettingsSignature, currentSignature, StringComparison.Ordinal))
            {
                cache.Dirty = true;
                PiPDisablerPlugin.LogVerbose("[MeshSurgery] Cut settings changed; marking weapon cache dirty.");
            }

            if (cache.Dirty || !cache.Built)
                RebuildCutCacheForOptic(cache, os, scopeRoot, activeMode);
            else
                ReapplyCachedCutMeshes(cache, scopeRoot);
        }

        public static void RestoreForScope(Transform anyTransformUnderScope)
        {
            var scopeRoot = ScopeHierarchy.FindScopeRoot(anyTransformUnderScope);
            if (!scopeRoot) return;

            var cache = _currentWeaponCache;
            if (cache == null || cache.WeaponRoot == null) return;

            if (scopeRoot.gameObject != cache.WeaponRoot && !scopeRoot.IsChildOf(cache.WeaponRoot.transform))
                return;

            RestoreOriginalMeshes(cache);
            RestoreLightEffectMeshesUnderRoot(cache.WeaponRoot.transform);
            RestoreWeaponSphereObjectsUnderRoot(cache.WeaponRoot.transform);
        }

        public static void RestoreAll()
        {
            foreach (var weaponCache in _raidCaches.Values)
            {
                if (weaponCache == null) continue;
                foreach (var profile in weaponCache.Profiles.Values)
                    RestoreOriginalMeshes(profile);
            }

            var lightKeys = _disabledLightFx.Keys.ToArray();
            var sphereKeys = _disabledWeaponSpheres.Keys.ToArray();
            if (lightKeys.Length == 0 && sphereKeys.Length == 0) return;

            foreach (var go in lightKeys)
            {
                if (go != null && _disabledLightFx.TryGetValue(go, out var st) && st != null && st.DisabledByUs)
                {
                    try
                    {
                        if (st.WasActiveSelf && !go.activeSelf)
                            go.SetActive(true);
                    }
                    catch { }
                }
                _disabledLightFx.Remove(go);
            }

            foreach (var go in sphereKeys)
            {
                if (go != null && _disabledWeaponSpheres.TryGetValue(go, out var st) && st != null && st.DisabledByUs)
                {
                    try
                    {
                        if (st.WasActiveSelf && !go.activeSelf)
                            go.SetActive(true);
                    }
                    catch { }
                }
                _disabledWeaponSpheres.Remove(go);
            }
        }

        public static void CleanupForShutdown()
        {
            RestoreAll();
            DestroyCurrentWeaponCache();
            UnbindInventoryEvents();
        }

        private static CutProfileCache GetOrCreateCurrentWeaponCache(Transform scopeRoot, Transform activeMode)
        {
            var player = Singleton<GameWorld>.Instance != null ? Singleton<GameWorld>.Instance.MainPlayer : null;
            var fc = player != null ? player.HandsController as Player.FirearmController : null;
            var weapon = fc != null ? fc.Item as Weapon : null;

            var weaponRootTf = FindWeaponTransform(scopeRoot);
            var weaponRoot = weaponRootTf != null ? weaponRootTf.gameObject : null;
            if (weapon == null || weaponRoot == null) return null;

            string weaponId = !string.IsNullOrEmpty(weapon.Id) ? weapon.Id : weapon.TemplateId;
            if (!_raidCaches.TryGetValue(weaponId, out var weaponCache) || weaponCache == null)
            {
                weaponCache = new RaidWeaponCache { WeaponId = weaponId };
                _raidCaches[weaponId] = weaponCache;
            }

            weaponCache.WeaponItem = weapon;

            string profileKey = BuildProfileKey(weaponRootTf, scopeRoot, activeMode, BuildCutSettingsSignature());
            if (!weaponCache.Profiles.TryGetValue(profileKey, out var profileCache) || profileCache == null)
            {
                profileCache = new CutProfileCache
                {
                    WeaponRoot = weaponRoot,
                    ProfileKey = profileKey,
                    Built = false,
                    Dirty = true
                };
                weaponCache.Profiles[profileKey] = profileCache;
            }
            else
            {
                profileCache.WeaponRoot = weaponRoot;
            }

            if (!ReferenceEquals(_currentWeaponCache, profileCache) && _currentWeaponCache != null)
                RestoreOriginalMeshes(_currentWeaponCache);

            if (!string.Equals(_currentWeaponId, weaponId, StringComparison.Ordinal))
            {
                PiPDisablerPlugin.LogVerbose($"[MeshSurgery] Weapon cache switched: '{weapon.TemplateId}' ({weaponId})");
            }

            _currentWeaponId = weaponId;
            _currentWeaponCache = profileCache;

            BindInventoryEvents(player);
            return profileCache;
        }

        private static void RebuildCutCacheForOptic(CutProfileCache cache, OpticSight os, Transform scopeRoot, Transform activeMode)
        {
            if (cache == null || os == null || scopeRoot == null) return;
            if (!activeMode) activeMode = os.transform;
            var weaponRootTf = FindWeaponTransform(scopeRoot);
            if (weaponRootTf == null) return;
            cache.WeaponRoot = weaponRootTf.gameObject;

            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode,
                out var planePoint, out var planeNormal, out var camPos))
            {
                PiPDisablerPlugin.LogVerbose("[MeshSurgery] TryGetPlane failed — no plane found.");
                return;
            }

            bool isCylinderMode = PiPDisablerPlugin.GetCutMode() == "Cylinder";
            float plane1Offset = isCylinderMode
                ? PiPDisablerPlugin.GetPlane1OffsetMeters()
                : PiPDisablerPlugin.GetPlaneOffsetMeters();
            planePoint += planeNormal * plane1Offset;

            var keepSide = DecideKeepPositive(planePoint, planeNormal, camPos)
                ? MeshPlaneCutter.KeepSide.Positive
                : MeshPlaneCutter.KeepSide.Negative;

            PlaneVisualizer.Show(planePoint, planeNormal);

            RestoreOriginalMeshes(cache);
            DestroyCutMeshes(cache);
            cache.Entries.Clear();

            var targets = ScopeHierarchy.FindTargetMeshFilters(scopeRoot, activeMode);
            float cutRadius = PiPDisablerPlugin.GetCutRadius();
            bool logCandidates = PiPDisablerPlugin.GetDebugLogCutCandidates();

            DisableLightEffectMeshesForScope(scopeRoot, logCandidates);
            DisableWeaponSphereObjects(scopeRoot, logCandidates);

            foreach (var mf in targets)
            {
                if (!mf || !mf.sharedMesh) continue;

                var renderer = mf.GetComponent<Renderer>();
                var boundsCenter = renderer != null ? renderer.bounds.center : mf.transform.position;
                float distFromPlane = Vector3.Distance(boundsCenter, planePoint);

                if (cutRadius > 0f && distFromPlane > cutRadius)
                    continue;

                Mesh originalAsset = mf.sharedMesh;

                try
                {
                    bool isCylinder = PiPDisablerPlugin.GetCutMode() == "Cylinder";
                    string cacheKey = MeshCutCache.BuildKey(scopeRoot, activeMode, mf, originalAsset, keepSide, isCylinder);

                    Mesh readable;
                    if (MeshCutCache.TryLoad(cacheKey, out var cachedMesh))
                    {
                        readable = cachedMesh;
                        readable.name = originalAsset.name + "_CUT_CACHED";
                    }
                    else
                    {
                        readable = MeshPlaneCutter.MakeReadableMeshCopy(originalAsset);
                        if (readable == null)
                            continue;

                        if (!_loggedGpuCopy)
                        {
                            _loggedGpuCopy = true;
                            PiPDisablerPlugin.LogInfo(
                                "[MeshSurgery] Created readable mesh copies via GPU buffer. Plane cutting enabled.");
                        }

                        int vertsBefore = readable.vertexCount;
                        bool ok;
                        if (isCylinder)
                        {
                            float nearR = PiPDisablerPlugin.GetCylinderRadius();
                            float startOff = PiPDisablerPlugin.GetCutStartOffset();
                            float cutLen = PiPDisablerPlugin.GetCutLength();
                            float preserve = PiPDisablerPlugin.GetNearPreserveDepth();
                            float p2 = PiPDisablerPlugin.GetPlane2PositionNormalized(cutLen);
                            float r2 = PiPDisablerPlugin.GetPlane2Radius();
                            float p3 = PiPDisablerPlugin.GetPlane3Position();
                            float r3 = PiPDisablerPlugin.GetPlane3Radius();
                            float p4 = PiPDisablerPlugin.GetPlane4Position();
                            float r4 = PiPDisablerPlugin.GetPlane4Radius();

                            ok = MeshPlaneCutter.CutMeshFrustum(readable, mf.transform,
                                planePoint, planeNormal, nearR, r4, startOff, cutLen,
                                keepInside: false, midRadius: r2, midPosition: p2,
                                nearPreserveDepth: preserve,
                                plane3Radius: r3, plane3Position: p3, plane4Position: p4);
                        }
                        else
                        {
                            ok = MeshPlaneCutter.CutMeshDirect(readable, mf.transform,
                                planePoint, planeNormal, keepSide);
                        }

                        if (!ok)
                        {
                            readable.Clear();
                            readable.name = originalAsset.name + "_CUT_EMPTY";
                        }
                        else
                        {
                            readable.name = originalAsset.name + "_CUT";
                        }

                        PiPDisablerPlugin.LogVerbose(
                            $"[MeshSurgery] Cut '{originalAsset.name}': {vertsBefore} → {readable.vertexCount} verts");

                        MeshCutCache.Save(cacheKey, readable);
                    }

                    mf.sharedMesh = readable;
                    cache.Entries.Add(new CutMeshEntry
                    {
                        Filter = mf,
                        OriginalMesh = originalAsset,
                        CutMesh = readable,
                        Applied = true,
                        FilterPath = GetRelativePath(weaponRootTf, mf.transform)
                    });
                }
                catch (Exception ex)
                {
                    PiPDisablerPlugin.LogError(
                        $"[MeshSurgery] Failed on '{originalAsset.name}': {ex.Message}");
                }
            }

            cache.Built = true;
            cache.Dirty = false;
            cache.SettingsSignature = BuildCutSettingsSignature();
        }

        private static void ReapplyCachedCutMeshes(CutProfileCache cache, Transform scopeRoot)
        {
            var weaponRootTf = FindWeaponTransform(scopeRoot);
            if (weaponRootTf == null)
            {
                cache.Dirty = true;
                return;
            }

            cache.WeaponRoot = weaponRootTf.gameObject;
            if (!TryRebindEntries(cache, weaponRootTf))
            {
                cache.Dirty = true;
                return;
            }

            bool logCandidates = PiPDisablerPlugin.GetDebugLogCutCandidates();
            DisableLightEffectMeshesForScope(scopeRoot, logCandidates);
            DisableWeaponSphereObjects(scopeRoot, logCandidates);

            foreach (var entry in cache.Entries)
            {
                if (entry == null || entry.Filter == null || entry.CutMesh == null)
                    continue;

                entry.Filter.sharedMesh = entry.CutMesh;
                entry.Applied = true;
            }
        }

        private static void RestoreOriginalMeshes(CutProfileCache cache)
        {
            if (cache == null) return;

            foreach (var entry in cache.Entries)
            {
                if (entry == null || entry.Filter == null)
                    continue;

                if (!entry.Applied)
                    continue;

                if (entry.OriginalMesh != null)
                    entry.Filter.sharedMesh = entry.OriginalMesh;

                entry.Applied = false;
            }
        }

        private static void DestroyCutMeshes(CutProfileCache cache)
        {
            if (cache == null) return;

            foreach (var entry in cache.Entries)
            {
                if (entry?.CutMesh != null)
                {
                    try { UnityEngine.Object.Destroy(entry.CutMesh); }
                    catch { }
                }
            }
        }

        private static void DestroyCurrentWeaponCache()
        {
            foreach (var weaponCache in _raidCaches.Values)
            {
                if (weaponCache == null) continue;
                foreach (var profile in weaponCache.Profiles.Values)
                {
                    RestoreOriginalMeshes(profile);
                    DestroyCutMeshes(profile);
                    profile.Entries.Clear();
                }
                weaponCache.Profiles.Clear();
            }
            _raidCaches.Clear();
            _currentWeaponCache = null;
            _currentWeaponId = null;
        }

        private static void BindInventoryEvents(Player player)
        {
            var inventory = player != null ? player.InventoryController : null;
            if (inventory == null || ReferenceEquals(_inventoryEventSource, inventory))
                return;

            UnbindInventoryEvents();

            try
            {
                var type = inventory.GetType();
                var addEvent = type.GetEvent("AddItemEvent");
                var removeEvent = type.GetEvent("RemoveItemEvent");
                if (addEvent == null || removeEvent == null)
                    return;

                _addItemHandler = Delegate.CreateDelegate(addEvent.EventHandlerType, null,
                    typeof(MeshSurgeryManager).GetMethod(nameof(OnItemAdded), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));
                _removeItemHandler = Delegate.CreateDelegate(removeEvent.EventHandlerType, null,
                    typeof(MeshSurgeryManager).GetMethod(nameof(OnItemRemoved), System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static));

                addEvent.AddEventHandler(inventory, _addItemHandler);
                removeEvent.AddEventHandler(inventory, _removeItemHandler);
                _inventoryEventSource = inventory;
            }
            catch (Exception ex)
            {
                PiPDisablerPlugin.LogVerbose($"[MeshSurgery] Failed to bind inventory events: {ex.Message}");
            }
        }

        private static void UnbindInventoryEvents()
        {
            if (_inventoryEventSource == null)
                return;

            try
            {
                var type = _inventoryEventSource.GetType();
                var addEvent = type.GetEvent("AddItemEvent");
                var removeEvent = type.GetEvent("RemoveItemEvent");

                if (addEvent != null && _addItemHandler != null)
                    addEvent.RemoveEventHandler(_inventoryEventSource, _addItemHandler);
                if (removeEvent != null && _removeItemHandler != null)
                    removeEvent.RemoveEventHandler(_inventoryEventSource, _removeItemHandler);
            }
            catch { }

            _addItemHandler = null;
            _removeItemHandler = null;
            _inventoryEventSource = null;
        }

        private static void OnItemAdded(GEventArgs2 args)
        {
            if (args == null || args.Status != CommandStatus.Succeed)
                return;

            MarkCacheDirtyIfMeaningful(args.Item, args.To);
        }

        private static void OnItemRemoved(GEventArgs3 args)
        {
            if (args == null || args.Status != CommandStatus.Succeed)
                return;

            MarkCacheDirtyIfMeaningful(args.Item, args.From);
        }

        private static void MarkCacheDirtyIfMeaningful(Item item, ItemAddress address)
        {
            if (address == null)
                return;

            if (IsIgnoredWeaponChange(item, address))
                return;

            var owner = FindOwningWeapon(address, item);
            if (owner == null)
                return;

            string ownerId = !string.IsNullOrEmpty(owner.Id) ? owner.Id : owner.TemplateId;
            if (!_raidCaches.TryGetValue(ownerId, out var weaponCache) || weaponCache == null)
                return;

            foreach (var profile in weaponCache.Profiles.Values)
            {
                if (profile != null)
                    profile.Dirty = true;
            }
        }

        private static Weapon FindOwningWeapon(ItemAddress address, Item changedItem)
        {
            if (changedItem is Weapon changedWeapon)
                return changedWeapon;

            var parents = address.GetAllParentItems(false);
            if (parents == null)
                return null;

            foreach (var parent in parents)
            {
                if (parent is Weapon weapon)
                    return weapon;
            }

            return null;
        }

        private static Transform ResolveActiveMode(OpticSight os, Transform scopeRoot)
        {
            Transform activeMode;
            if (os.transform.name != null &&
                (os.transform.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                 || os.transform.name.Equals("mode", StringComparison.OrdinalIgnoreCase)))
                activeMode = os.transform;
            else
                activeMode = ScopeHierarchy.FindBestMode(scopeRoot);

            if (!activeMode) activeMode = os.transform;
            return activeMode;
        }

        private static bool TryRebindEntries(CutProfileCache cache, Transform weaponRoot)
        {
            foreach (var entry in cache.Entries)
            {
                if (entry == null || entry.CutMesh == null || string.IsNullOrEmpty(entry.FilterPath))
                    return false;

                var tf = FindRelativeTransform(weaponRoot, entry.FilterPath);
                if (tf == null)
                    return false;

                var mf = tf.GetComponent<MeshFilter>();
                if (mf == null)
                    return false;

                entry.Filter = mf;
                if (entry.OriginalMesh == null)
                    entry.OriginalMesh = mf.sharedMesh;
            }

            return true;
        }

        private static string BuildProfileKey(Transform weaponRoot, Transform scopeRoot, Transform activeMode, string settingsSignature)
        {
            return string.Join("|", new[]
            {
                GetRelativePath(weaponRoot, scopeRoot),
                GetRelativePath(weaponRoot, activeMode),
                settingsSignature ?? string.Empty
            });
        }

        private static string GetRelativePath(Transform root, Transform child)
        {
            if (child == null) return string.Empty;
            if (root == null) return child.name ?? "unnamed";

            var nodes = new List<string>();
            for (var t = child; t != null; t = t.parent)
            {
                nodes.Add(t.name ?? "unnamed");
                if (t == root)
                    break;
            }

            nodes.Reverse();
            return string.Join("/", nodes.ToArray());
        }

        private static Transform FindRelativeTransform(Transform root, string relativePath)
        {
            if (root == null || string.IsNullOrEmpty(relativePath)) return null;
            if (relativePath == root.name) return root;

            var segments = relativePath.Split('/');
            int start = segments.Length > 0 && string.Equals(segments[0], root.name, StringComparison.Ordinal) ? 1 : 0;
            Transform current = root;
            for (int i = start; i < segments.Length; i++)
            {
                if (string.IsNullOrEmpty(segments[i])) continue;
                current = current.Find(segments[i]);
                if (current == null) return null;
            }

            return current;
        }

        private static bool IsIgnoredWeaponChange(Item item, ItemAddress address)
        {
            if (address?.Container is Slot slot &&
                slot.ID == EWeaponModType.mod_magazine.ToString())
                return true;

            if (item is MagazineItemClass)
                return true;

            if (item is AmmoItemClass)
                return true;

            return false;
        }

        private static string BuildCutSettingsSignature()
        {
            return string.Join("|", new[]
            {
                PiPDisablerPlugin.GetCutMode() ?? string.Empty,
                PiPDisablerPlugin.GetPlaneOffsetMeters().ToString("F4"),
                PiPDisablerPlugin.GetPlane1OffsetMeters().ToString("F4"),
                PiPDisablerPlugin.GetCylinderRadius().ToString("F4"),
                PiPDisablerPlugin.GetCutStartOffset().ToString("F4"),
                PiPDisablerPlugin.GetCutLength().ToString("F4"),
                PiPDisablerPlugin.GetNearPreserveDepth().ToString("F4"),
                PiPDisablerPlugin.GetPlane2PositionNormalized(PiPDisablerPlugin.GetCutLength()).ToString("F4"),
                PiPDisablerPlugin.GetPlane2Radius().ToString("F4"),
                PiPDisablerPlugin.GetPlane3Position().ToString("F4"),
                PiPDisablerPlugin.GetPlane3Radius().ToString("F4"),
                PiPDisablerPlugin.GetPlane4Position().ToString("F4"),
                PiPDisablerPlugin.GetPlane4Radius().ToString("F4"),
                PiPDisablerPlugin.GetCutRadius().ToString("F4")
            });
        }

        private static Transform FindWeaponTransform(Transform scopeRoot)
        {
            for (var p = scopeRoot; p != null; p = p.parent)
            {
                if (p.name != null && p.name.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static void DisableWeaponSphereObjects(Transform scopeRoot, bool logCandidates)
        {
            var weaponRoot = FindWeaponTransform(scopeRoot);
            if (weaponRoot == null) return;

            foreach (var t in weaponRoot.GetComponentsInChildren<Transform>(true))
            {
                if (t == null || t.gameObject == null) continue;
                if (!t.name.Equals("Sphere", StringComparison.OrdinalIgnoreCase)) continue;

                var go = t.gameObject;
                if (!_disabledWeaponSpheres.TryGetValue(go, out var st))
                {
                    st = new SphereState { WasActiveSelf = go.activeSelf, DisabledByUs = false };
                    _disabledWeaponSpheres[go] = st;
                }

                if (go.activeSelf)
                {
                    go.SetActive(false);
                    st.DisabledByUs = true;
                    if (logCandidates)
                    {
                        PiPDisablerPlugin.LogInfo(
                            $"[MeshSurgery][DebugCandidates] disable sphere path='{ScopeHierarchy.GetRelativePath(go.transform, weaponRoot)}' go='{go.name}'");
                    }
                }
            }
        }

        private static void RestoreWeaponSphereObjectsUnderRoot(Transform weaponRoot)
        {
            if (weaponRoot == null || _disabledWeaponSpheres.Count == 0) return;

            var keys = _disabledWeaponSpheres.Keys.ToArray();
            foreach (var go in keys)
            {
                if (go == null)
                {
                    _disabledWeaponSpheres.Remove(go);
                    continue;
                }

                if (go.transform == null || !go.transform.IsChildOf(weaponRoot))
                    continue;

                if (_disabledWeaponSpheres.TryGetValue(go, out var st) && st != null && st.DisabledByUs)
                {
                    try
                    {
                        if (st.WasActiveSelf && !go.activeSelf)
                            go.SetActive(true);
                    }
                    catch { }
                }

                _disabledWeaponSpheres.Remove(go);
            }
        }

        private static void DisableLightEffectMeshesForScope(Transform scopeRoot, bool logCandidates)
        {
            var lightFxTargets = ScopeHierarchy.FindLightEffectMeshFilters(scopeRoot);
            if (lightFxTargets.Count == 0) return;

            foreach (var mf in lightFxTargets)
            {
                if (mf == null || mf.gameObject == null) continue;

                var go = mf.gameObject;
                if (!_disabledLightFx.TryGetValue(go, out var st))
                {
                    st = new LightFxState { WasActiveSelf = go.activeSelf, DisabledByUs = false };
                    _disabledLightFx[go] = st;
                }

                if (go.activeSelf)
                {
                    go.SetActive(false);
                    st.DisabledByUs = true;
                    if (logCandidates)
                    {
                        PiPDisablerPlugin.LogInfo(
                            $"[MeshSurgery][DebugCandidates] disable lightEffect path='{ScopeHierarchy.GetRelativePath(go.transform, scopeRoot)}' go='{go.name}'");
                    }
                }
            }
        }

        private static void RestoreLightEffectMeshesUnderRoot(Transform searchRoot)
        {
            if (searchRoot == null || _disabledLightFx.Count == 0) return;

            var keys = _disabledLightFx.Keys.ToArray();
            foreach (var go in keys)
            {
                if (go == null)
                {
                    _disabledLightFx.Remove(go);
                    continue;
                }

                if (go.transform == null || !go.transform.IsChildOf(searchRoot))
                    continue;

                if (_disabledLightFx.TryGetValue(go, out var st) && st != null && st.DisabledByUs)
                {
                    try
                    {
                        if (st.WasActiveSelf && !go.activeSelf)
                            go.SetActive(true);
                    }
                    catch { }
                }

                _disabledLightFx.Remove(go);
            }
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
                    PiPDisablerPlugin.LogVerbose(
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
                        PiPDisablerPlugin.LogVerbose(
                            $"[ScopeHierarchy] FindScopeRoot fallback (name match): '{t.name}'");
                        return t;
                    }
                }
            }

            PiPDisablerPlugin.LogVerbose(
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

        private static bool LooksLikeScopeRootByName(Transform t)
        {
            if (t == null || string.IsNullOrEmpty(t.name)) return false;

            var lo = t.name.ToLowerInvariant();
            return lo.Contains("scope")
                || lo.Contains("optic")
                || lo.Contains("sight")
                || lo.Contains("collimator");
        }

        private static bool IsLikelyScopeRootForExclusion(Transform t)
        {
            // Many non-optic tactical devices (DBAL/flashlights/lasers) also expose
            // mode_* children. For sibling-scope exclusion, require additional optic
            // signals so those tactical attachments stay cuttable.
            if (!HasModeChild(t)) return false;

            if (LooksLikeScopeRootByName(t)) return true;

            if (HasDirectChild(t, "backLens") || HasDirectChild(t, "backlens"))
                return true;

            if (FindDeepChild(t, "backLens") != null || FindDeepChild(t, "backlens") != null)
                return true;

            if (FindDeepChild(t, "optic_camera") != null)
                return true;

            // Parent container hint (e.g. mod_scope_XXX/<scopeRoot>)
            var parentName = t.parent != null ? (t.parent.name ?? string.Empty).ToLowerInvariant() : string.Empty;
            return parentName.Contains("scope") || parentName.Contains("optic");
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

        public static string GetRelativePath(Transform t, Transform root)
        {
            if (t == null) return "null";

            var parts = new List<string>();
            for (var p = t; p != null; p = p.parent)
            {
                parts.Add(p.name ?? "unnamed");
                if (p == root) break;
            }
            parts.Reverse();
            return string.Join("/", parts.ToArray());
        }

        private static bool IsLikelyLightEffectMesh(MeshFilter mf, Transform searchRoot)
        {
            if (mf == null || mf.transform == null) return false;

            string goName = (mf.gameObject.name ?? string.Empty).ToLowerInvariant();
            string meshName = (mf.sharedMesh != null ? mf.sharedMesh.name : string.Empty).ToLowerInvariant();

            bool isSphereMesh = goName == "sphere" || meshName == "sphere";
            if (!isSphereMesh) return false;

            // Common EFT flashlight/laser visual emitters are nested under light_* nodes.
            // These are glow helpers and should not be plane-cut, otherwise they can bloom
            // into solid white blobs in the optic image.
            string relPath = GetRelativePath(mf.transform, searchRoot).ToLowerInvariant();
            bool underLightNode = relPath.Contains("/light_") || relPath.EndsWith("/light");
            if (!underLightNode) return false;

            return true;
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
            else { var mc = PiPDisablerPlugin.GetMainCamera(); camPos = mc != null ? mc.transform.position : activeMode.position; }

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

            PiPDisablerPlugin.LogVerbose(
                $"[ScopeHierarchy] TryGetPlane: ref='{refTransform.name}', " +
                $"axis={PiPDisablerPlugin.GetPlaneNormalAxis()}, " +
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
            string axis = PiPDisablerPlugin.GetPlaneNormalAxis() ?? "Auto";

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

        public static List<MeshFilter> FindLightEffectMeshFilters(Transform scopeRoot)
        {
            var result = new List<MeshFilter>(8);
            if (scopeRoot == null) return result;

            // Keep search-root expansion aligned with FindTargetMeshFilters.
            Transform searchRoot = scopeRoot;
            for (var p = scopeRoot.parent; p != null; p = p.parent)
            {
                var pName = p.name ?? "";
                var plo = pName.ToLowerInvariant();
                if (plo.Contains("weapon") || plo.Contains("anim"))
                    break;
                if (plo.Contains("scope") || plo.Contains("mod_") || plo.Contains("optic") || plo.Contains("mount") || plo.Contains("receiver") || plo.Contains("reciever"))
                {
                    searchRoot = p;
                    continue;
                }
                break;
            }

            if (PiPDisablerPlugin.GetExpandSearchToWeaponRoot())
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

            foreach (var mf in searchRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;
                if (IsLikelyLightEffectMesh(mf, searchRoot))
                    result.Add(mf);
            }

            return result;
        }

        public static List<MeshFilter> FindTargetMeshFilters(Transform scopeRoot, Transform activeMode)
        {
            if (scopeRoot == null) return new List<MeshFilter>();
            bool logCandidates = PiPDisablerPlugin.GetDebugLogCutCandidates();

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
                // Stop at weapon root/anim nodes. We intentionally do NOT stop on
                // receiver nodes because many attachments (e.g. handguards and tactical
                // devices) live under the same receiver branch and should be cut too.
                if (plo.Contains("weapon") || plo.Contains("anim"))
                    break;
                // Climb through scope/mod/optic/mount containers and receiver branches.
                if (plo.Contains("scope") || plo.Contains("mod_") || plo.Contains("optic") || plo.Contains("mount") || plo.Contains("receiver") || plo.Contains("reciever"))
                {
                    searchRoot = p;
                    continue; // keep climbing
                }
                break; // unknown parent — stop
            }
            if (searchRoot != scopeRoot)
                PiPDisablerPlugin.LogVerbose(
                    $"[ScopeHierarchy] Expanded search root: '{scopeRoot.name}' → '{searchRoot.name}'");

            // Optional: climb further up to the Weapon_root node to include weapon body meshes.
            // Normally the loop above stops at any parent whose name contains "weapon" or "anim".
            // With ExpandSearchToWeaponRoot the search climbs through those intermediate nodes
            // until it finds a transform whose name starts with "Weapon_root".
            // Path example: Weapon_root/Weapon_root_anim/weapon/mod_scope/<scope>
            if (PiPDisablerPlugin.GetExpandSearchToWeaponRoot())
            {
                for (var p = searchRoot.parent; p != null; p = p.parent)
                {
                    if ((p.name ?? "").StartsWith("Weapon_root", StringComparison.OrdinalIgnoreCase))
                    {
                        searchRoot = p;
                        PiPDisablerPlugin.LogVerbose(
                            $"[ScopeHierarchy] ExpandSearchToWeaponRoot: climbed to '{searchRoot.name}'");
                        break;
                    }
                }
            }

            var result = new List<MeshFilter>(64);
            int skippedMode = 0, skippedOther = 0, skippedLightFx = 0;
            int inspected = 0;

            if (logCandidates)
            {
                PiPDisablerPlugin.LogInfo(
                    $"[MeshSurgery][DebugCandidates] FindTargetMeshFilters searchRoot='{searchRoot.name}' scopeRoot='{scopeRoot.name}' activeMode='{(activeMode != null ? activeMode.name : "<null>")}'");
            }

            foreach (var mf in searchRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;
                inspected++;

                string relSearchPath = null;
                if (logCandidates)
                    relSearchPath = GetRelativePath(mf.transform, searchRoot);

                result.Add(mf);

                if (logCandidates)
                {
                    PiPDisablerPlugin.LogInfo(
                        $"[MeshSurgery][DebugCandidates] cuttable path='{relSearchPath}' go='{mf.gameObject.name}' mesh='{mf.sharedMesh.name}' verts={mf.sharedMesh.vertexCount} active={mf.gameObject.activeInHierarchy}");
                }
            }

            PiPDisablerPlugin.LogVerbose(
                $"[ScopeHierarchy] FindTargets from '{searchRoot.name}': " +
                $"{result.Count} targets, skipped: mode={skippedMode} otherScope={skippedOther} lightFx={skippedLightFx}");

            if (logCandidates)
            {
                PiPDisablerPlugin.LogInfo(
                    $"[MeshSurgery][DebugCandidates] FindTargetMeshFilters summary inspected={inspected} cuttable={result.Count} skippedMode={skippedMode} skippedOtherScope={skippedOther} skippedLightFx={skippedLightFx}");
            }

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

                // If this is a likely scope root (not just any mode_* device) and it's not
                // the active one, record it so we can skip sibling scope subtrees.
                if (t != activeScopeRoot && IsLikelyScopeRootForExclusion(t))
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
