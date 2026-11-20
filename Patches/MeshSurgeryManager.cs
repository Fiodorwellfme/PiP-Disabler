using System;
using System.Collections.Generic;
using System.Linq;
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
            // Automatic hole (2.8.0): one set of cut meshes per weapon stretch ("variant"), up to
            // MaxVariants; Active is the set on screen. Entries[i].CutMesh points into it.
            public readonly List<CutVariant> Variants = new List<CutVariant>(4);
            public CutVariant Active;
            public string ScopeKey;
            public float LastUsed;
        }

        private sealed class CutVariant
        {
            public int Rib, Eye;
            public Mesh[] Meshes;
            public bool Complete, Predicted;
            public float LastUsed;
        }

        private sealed class RaidWeaponCache
        {
            public float LastUsed;
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
        private static int _lastCutAttemptFrame;
        private static object _inventoryEventSource;
        private static Delegate _addItemHandler;
        private static Delegate _removeItemHandler;
        private static int _lastAttemptTargets;
        private static int _lastAttemptEntries;
        private static int _lastAttemptReadableCopyFailures;
        private static bool _lastAttemptWeaponRootFound;
        private static bool _lastAttemptPlaneFound;
        private static string _lastAttemptOptic = "<none>";
        private static string _lastAttemptScopeRoot = "<none>";
        private static string _lastAttemptActiveMode = "<none>";

        public static void ApplyForOptic(OpticSight os)
        {
            if (os == null) return;
            EnsureRenderHook();

            // Experimental pixel hole (2.8.5): nothing is cut; the body is hidden inside the lens per pixel.
            if (PixelHole.Enabled)
            {
                RestoreForScope(os.transform); // original meshes back if a cut was on
                if (PixelHole.TryBegin(os)) return;
            }

            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (!scopeRoot) return;

            var activeMode = ResolveActiveMode(os, scopeRoot);
            var cache = GetOrCreateCurrentWeaponCache(scopeRoot, activeMode);
            if (cache == null) return;

            string currentSignature = BuildCutSettingsSignature();
            if (cache.Built && !cache.Dirty && !string.Equals(cache.SettingsSignature, currentSignature, StringComparison.Ordinal))
            {
                cache.Dirty = true;
                PiPDisablerPlugin.DebugLogInfo("[MeshSurgery] Cut settings changed; marking weapon cache dirty.");
            }

            _applyCache = cache; // background cuts for this cache go on screen right away
            if (cache.Dirty || !cache.Built)
            {
                RebuildCutCacheForOptic(cache, os, scopeRoot, activeMode);
            }
            else
            {
                ReapplyCachedCutMeshes(cache, scopeRoot);
                if (cache.Dirty)
                    RebuildCutCacheForOptic(cache, os, scopeRoot, activeMode);
            }
        }

        public static void RestoreForScope(Transform anyTransformUnderScope)
        {
            PixelHole.End();
            var scopeRoot = ScopeHierarchy.FindScopeRoot(anyTransformUnderScope);
            if (!scopeRoot) return;

            var cache = _currentWeaponCache;
            if (cache == null || cache.WeaponRoot == null) return;

            if (scopeRoot.gameObject != cache.WeaponRoot && !scopeRoot.IsChildOf(cache.WeaponRoot.transform))
                return;

            _applyCache = null;
            RestoreOriginalMeshes(cache);
            RestoreLightEffectMeshesUnderRoot(cache.WeaponRoot.transform);
            RestoreWeaponSphereObjectsUnderRoot(cache.WeaponRoot.transform);
        }

        public static void RestoreAll()
        {
            PixelHole.End();
            _applyCache = null;
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
            UnhookRender();
            RestoreAll();
            DestroyCurrentWeaponCache();
            UnbindInventoryEvents();
            _lastCutAttemptFrame = 0;
        }

        /// <summary>
        /// Returns true if the current weapon cache has at least one successfully
        /// applied cut mesh entry. Used by ScopeLifecycle.Tick() to detect whether
        /// the initial mesh surgery silently produced zero cuts (e.g. GPU buffers
        /// not ready on the first frame, or TryGetPlane returned a degenerate position).
        /// </summary>
        public static bool HasSuccessfulCut()
        {
            var cache = _currentWeaponCache;
            if (cache == null) return false;
            if (!cache.Built) return false;
            if (_recut != null && ReferenceEquals(_recut.Cache, cache)) return true; // background cut in progress
            for (int i = 0; i < cache.Entries.Count; i++)
            {
                var entry = cache.Entries[i];
                if (entry != null && entry.Applied && entry.Filter != null && entry.CutMesh != null)
                    return true;
            }
            return false;
        }

        public static string GetLastAttemptDebugSnapshot()
        {
            return
                $"optic='{_lastAttemptOptic}' scopeRoot='{_lastAttemptScopeRoot}' mode='{_lastAttemptActiveMode}' " +
                $"weaponRootFound={_lastAttemptWeaponRootFound} planeFound={_lastAttemptPlaneFound} " +
                $"targets={_lastAttemptTargets} entries={_lastAttemptEntries} readableCopyFailures={_lastAttemptReadableCopyFailures} " +
                $"frame={_lastCutAttemptFrame}";
        }

        /// <summary>
        /// Force a full rebuild of the current weapon cache.
        /// Called from ScopeLifecycle.Tick() when mesh surgery produced zero entries
        /// on the initial ADS frame (GPU buffers / transform positions weren't ready).
        /// Returns true if the retry produced at least one cut entry.
        /// </summary>
        public static bool RetryPendingCut(OpticSight os)
        {
            if (os == null) return false;

            // Throttle: don't retry more than once every 3 frames
            if (Time.frameCount - _lastCutAttemptFrame < 3) return false;
            _lastCutAttemptFrame = Time.frameCount;

            var cache = _currentWeaponCache;
            if (cache == null)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][Retry] No current weapon cache — calling ApplyForOptic. frame={Time.frameCount}");
                ApplyForOptic(os);
                cache = _currentWeaponCache;
                return HasSuccessfulCut();
            }

            // Force a full rebuild by marking dirty
            PiPDisablerPlugin.DebugLogInfo(
                $"[MeshSurgery][Retry] Forcing rebuild: Built={cache.Built} Entries={cache.Entries.Count} frame={Time.frameCount}");
            cache.Dirty = true;
            ApplyForOptic(os);

            bool success = HasSuccessfulCut();
            PiPDisablerPlugin.DebugLogInfo(
                $"[MeshSurgery][Retry] Result: Entries={cache.Entries.Count} success={success} frame={Time.frameCount}");
            return success;
        }

        private static CutProfileCache GetOrCreateCurrentWeaponCache(Transform scopeRoot, Transform activeMode)
        {
            var player = Singleton<GameWorld>.Instance != null ? Singleton<GameWorld>.Instance.MainPlayer : null;
            var fc = player != null ? player.HandsController as Player.FirearmController : null;
            var weapon = fc != null ? fc.Item as Weapon : null;

            var weaponRootTf = FindWeaponTransform(scopeRoot);
            var weaponRoot = weaponRootTf != null ? weaponRootTf.gameObject : null;
            if (weapon == null || weaponRoot == null) return null;

            // During a weapon switch the hands already hold the new gun while the scope found may
            // still be the old gun's. Filing that scope under the new gun's id mixed the two guns'
            // cuts (2.8.3: switching back to a gun showed its uncut scope body for ~1 s).
            try
            {
                var heldPrefab = fc.ControllerGameObject != null ? fc.ControllerGameObject.GetComponent<WeaponPrefab>() : null;
                var scopePrefab = weaponRootTf.GetComponentInParent<WeaponPrefab>();
                if (heldPrefab != null && scopePrefab != null && !ReferenceEquals(heldPrefab, scopePrefab))
                {
                    PiPDisablerPlugin.DebugLogInfo("[MeshSurgery] Scope belongs to another gun than the one in hands (weapon switch) — skipped");
                    return null;
                }
            }
            catch { }

            string weaponId = !string.IsNullOrEmpty(weapon.Id) ? weapon.Id : weapon.TemplateId;
            if (!_raidCaches.TryGetValue(weaponId, out var weaponCache) || weaponCache == null)
            {
                weaponCache = new RaidWeaponCache { WeaponId = weaponId };
                _raidCaches[weaponId] = weaponCache;
            }

            weaponCache.WeaponItem = weapon;
            weaponCache.LastUsed = Time.realtimeSinceStartup;
            TrimWeaponCaches(weaponId);

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
                TrimProfiles(weaponCache, profileCache);
                weaponCache.Profiles[profileKey] = profileCache;
            }
            else
            {
                profileCache.WeaponRoot = weaponRoot;
            }
            profileCache.LastUsed = Time.realtimeSinceStartup;

            if (!ReferenceEquals(_currentWeaponCache, profileCache) && _currentWeaponCache != null)
                RestoreOriginalMeshes(_currentWeaponCache);

            if (!string.Equals(_currentWeaponId, weaponId, StringComparison.Ordinal))
            {
                PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Weapon cache switched: '{weapon.TemplateId}' ({weaponId})");
            }

            _currentWeaponId = weaponId;
            _currentWeaponCache = profileCache;

            BindInventoryEvents(player);
            return profileCache;
        }

        private static void RebuildCutCacheForOptic(CutProfileCache cache, OpticSight os, Transform scopeRoot, Transform activeMode, bool precut = false)
        {
            if (cache == null || os == null || scopeRoot == null) return;
            if (!activeMode) activeMode = os.transform;
            CancelAsyncRecut("full rebuild");
            _lastAttemptOptic = os.name;
            _lastAttemptScopeRoot = scopeRoot.name;
            _lastAttemptActiveMode = activeMode.name;
            _lastAttemptWeaponRootFound = false;
            _lastAttemptPlaneFound = false;
            _lastAttemptTargets = 0;
            _lastAttemptEntries = 0;
            _lastAttemptReadableCopyFailures = 0;

            var weaponRootTf = FindWeaponTransform(scopeRoot);
            if (weaponRootTf == null)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][DEBUG] FindWeaponTransform returned null for scopeRoot='{scopeRoot.name}' frame={Time.frameCount}");
                return;
            }
            _lastAttemptWeaponRootFound = true;
            cache.WeaponRoot = weaponRootTf.gameObject;

            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode,
                out var planePoint, out var planeNormal, out var camPos))
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][DEBUG] TryGetPlane FAILED — no plane found. " +
                    $"os='{os.name}' scopeRoot='{scopeRoot.name}' activeMode='{activeMode.name}' frame={Time.frameCount}");
                return;
            }
            _lastAttemptPlaneFound = true;

            bool isCylinderMode = true;
            float plane1Offset = isCylinderMode
                ? PerScopeMeshSurgerySettings.GetPlane1OffsetMeters()
                : PerScopeMeshSurgerySettings.GetPlaneOffsetMeters();
            planePoint += planeNormal * plane1Offset;

            var keepSide = DecideKeepPositive(planePoint, planeNormal, camPos)
                ? MeshPlaneCutter.KeepSide.Positive
                : MeshPlaneCutter.KeepSide.Negative;


            // Automatic hole (2.7.6): remove exactly what the real camera sees through the eyepiece
            // lens — every face whose projection from the camera lands inside the lens circle, in
            // front of or behind the lens, whichever way it faces. What lands outside is what is
            // seen around the lens, so the scope's outside stays as it is on screen.
            bool autoCut = false;
            float autoEyeDist = 0f, autoLensR = 0f, autoWidth = 1f;
            if (PerScopeMeshSurgerySettings.IsAutoCut())
            {
                float lensR = LensTransparency.GetEyepieceLensRadius(scopeRoot);
                if (lensR > 0.003f && lensR < 0.05f)
                {
                    // Camera distance changes with weapon scale/zoom; ScopeLifecycle re-fits once the
                    // view has settled.
                    RememberAutoPlane(scopeRoot, planePoint, planeNormal);
                    int cutRib = RibBucket(RenderRibcage());
                    bool knownEye = HasSettledBucket(cutRib);
                    LastAutoApexBucketUsed = GetAutoBucketForCut(cutRib);
                    autoEyeDist = DefaultApexDistance * Mathf.Pow(ApexBucketStep, LastAutoApexBucketUsed);
                    autoLensR = lensR;
                    autoWidth = PerScopeMeshSurgerySettings.GetCutWidthMultiplierRaw();
                    autoCut = true;
                    PiPDisablerPlugin.LogSource.LogInfo(
                        $"[MeshSurgery] Auto hole (line of sight): lens r={lensR * 1000f:F1}mm, camera {autoEyeDist * 1000f:F0}mm behind it ({(knownEye ? "remembered for this zoom" : "live, not settled yet")}), " +
                        $"weapon stretch={CurrentRibcage():F3}, width={autoWidth:F2} → removes what projects inside {lensR * autoWidth * 970f:F1}mm (eye side) / {lensR * autoWidth * 1000f:F1}mm (2cm+ past the lens) on the lens plane");
                }
                else
                {
                    PiPDisablerPlugin.LogSource.LogInfo(
                        $"[MeshSurgery] Auto hole skipped (eyepiece lens radius {lensR * 1000f:F1}mm not usable) — using manual hole values.");
                }
            }

            if (precut && !autoCut) return; // pre-cut is only for the automatic hole (no hitchy manual cut)

            RestoreOriginalMeshes(cache);
            DestroyCutMeshes(cache);
            cache.Entries.Clear();

            var targets = ScopeHierarchy.FindTargetMeshFilters(scopeRoot, activeMode);
            _lastAttemptTargets = targets.Count;
            float cutRadius = 0;

            if (!precut)
            {
                DisableLightEffectMeshesForScope(scopeRoot);
                DisableWeaponSphereObjects(scopeRoot);
            }

            foreach (var mf in targets)
            {
                if (!mf || !mf.sharedMesh) continue;

                var renderer = mf.GetComponent<Renderer>();
                if (autoCut)
                {
                    // Automatic hole: the parts are cut in the background at render time (no hitch,
                    // and with the weapon stretch EFT applies only while rendering).
                    if (precut && renderer != null && LensTransparency.IsLensSurfaceRenderer(renderer)) continue;
                    cache.Entries.Add(new CutMeshEntry
                    {
                        Filter = mf, OriginalMesh = mf.sharedMesh, CutMesh = null, Applied = false,
                        FilterPath = GetRelativePath(weaponRootTf, mf.transform)
                    });
                    continue;
                }
                var boundsCenter = renderer != null ? renderer.bounds.center : mf.transform.position;
                float distFromPlane = Vector3.Distance(boundsCenter, planePoint);

                if (cutRadius > 0f && distFromPlane > cutRadius)
                    continue;

                Mesh originalAsset = mf.sharedMesh;
                Mesh readable = null;

                try
                {
                    bool isCylinder = true;
                    readable = MeshPlaneCutter.MakeReadableMeshCopy(originalAsset);
                    if (readable == null)
                    {
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[MeshSurgery][DEBUG] MakeReadableMeshCopy returned null for '{originalAsset.name}' " +
                            $"(isReadable={originalAsset.isReadable} verts={originalAsset.vertexCount}) frame={Time.frameCount}");
                        _lastAttemptReadableCopyFailures++;
                        continue;
                    }

                    if (!_loggedGpuCopy)
                    {
                        _loggedGpuCopy = true;
                        PiPDisablerPlugin.DebugLogInfo(
                            "[MeshSurgery] Created readable mesh copies via GPU buffer. Plane cutting enabled.");
                    }

                    int vertsBefore = readable.vertexCount;
                    bool ok;
                    if (isCylinder && autoCut)
                    {
                        ok = MeshPlaneCutter.CutMeshSightCone(readable, mf.transform,
                            planePoint, planeNormal, autoEyeDist, autoLensR, autoWidth);
                    }
                    else if (isCylinder)
                    {
                        float nearR = PerScopeMeshSurgerySettings.GetPlane1Radius();
                        float startOff = PerScopeMeshSurgerySettings.GetCutStartOffset();
                        float cutLen = PerScopeMeshSurgerySettings.GetCutLength();
                        float preserve = PerScopeMeshSurgerySettings.GetNearPreserveDepth();
                        float p2 = PerScopeMeshSurgerySettings.GetPlane2PositionNormalized(cutLen);
                        float r2 = PerScopeMeshSurgerySettings.GetPlane2Radius();
                        float p3 = PerScopeMeshSurgerySettings.GetPlane3Position();
                        float r3 = PerScopeMeshSurgerySettings.GetPlane3Radius();
                        float p4 = PerScopeMeshSurgerySettings.GetPlane4Position();
                        float r4 = PerScopeMeshSurgerySettings.GetPlane4Radius();
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

                    if (autoCut)
                    {
                        var rend = mf.GetComponent<Renderer>();
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[MeshSurgery] Cut '{originalAsset.name}' (go='{mf.name}', {LensProbe.DescribeMaterial(rend)}, det={(mf.transform.localToWorldMatrix.determinant < 0f ? "-" : "+")}): " +
                            $"{vertsBefore} → {readable.vertexCount} verts; tris {MeshPlaneCutter.LastTris}: removed {MeshPlaneCutter.LastSightRemoved} in the line of sight, split {MeshPlaneCutter.LastSightSplit} on the lens edge{(ok ? "" : " (whole part was in the line of sight)")}");
                    }
                    else
                    {
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[MeshSurgery] Cut '{originalAsset.name}': {vertsBefore} → {readable.vertexCount} verts");
                    }

                    mf.sharedMesh = readable;
                    var cutMesh = readable;
                    readable = null; // owned by the cache now
                    cache.Entries.Add(new CutMeshEntry
                    {
                        Filter = mf,
                        OriginalMesh = originalAsset,
                        CutMesh = cutMesh,
                        Applied = true,
                        FilterPath = GetRelativePath(weaponRootTf, mf.transform)
                    });
                }
                catch (Exception ex)
                {
                    if (readable != null)
                        UnityEngine.Object.Destroy(readable);
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[MeshSurgery] Failed on '{originalAsset.name}': {ex.Message}");
                }
            }

            cache.Built = true;
            cache.Dirty = false;
            cache.ScopeKey = PerScopeMeshSurgerySettings.ActiveScopeKey;
            cache.SettingsSignature = BuildCutSettingsSignature();
            if (autoCut && cache.Entries.Count > 0)
            {
                // 2.8.4: one cut for the stretch now. A pre-cut (weapon held, not aimed) is only
                // provisional and is confirmed once the aim settles. Cuts for the other used zoom
                // levels are made after the aim has settled (QueueCutAhead): EFT stretches the
                // weapon along a body axis, and in the lowered (not aimed) pose that axis is not
                // along the barrel, so a stretch predicted there skews the cut (wrong hole after a
                // restart / in the next raid until Home was pressed).
                StartJob(cache, scopeRoot, planePoint, planeNormal, autoEyeDist, autoLensR, autoWidth, LastAutoApexBucketUsed,
                    precut ? "pre-cut while holding the weapon" : "first cut",
                    targetRib: int.MinValue, predicted: false, activate: !precut, cancelOthers: !precut, provisional: precut);
            }
            _lastCutAttemptFrame = Time.frameCount;
            _lastAttemptEntries = cache.Entries.Count;

            if (cache.Entries.Count == 0)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][DEBUG] RebuildCutCache finished with ZERO entries! " +
                    $"targets={targets.Count} os='{os.name}' scopeRoot='{scopeRoot.name}' " +
                    $"activeMode='{activeMode.name}' frame={Time.frameCount}");
            }
            else
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][DEBUG] RebuildCutCache OK: {cache.Entries.Count} entries from {targets.Count} targets. frame={Time.frameCount}");
            }
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

            DisableLightEffectMeshesForScope(scopeRoot);
            DisableWeaponSphereObjects(scopeRoot);

            // No finished cut on show (cut ahead while holding the gun, or the last cut was stopped
            // by a weapon switch): put the most recently used stored cut on at once instead of
            // throwing all stored cuts away and showing the uncut scope until a new one is done.
            if (cache.Active == null || !cache.Active.Complete)
            {
                CutVariant best = null;
                int ribNow = RibBucket(RenderRibcage());
                foreach (var v in cache.Variants)
                    if (v.Complete && v.Rib == ribNow) { best = v; break; }
                if (best == null)
                    foreach (var v in cache.Variants)
                        if (v.Complete && (best == null || v.LastUsed > best.LastUsed)) best = v;
                if (best != null)
                {
                    ActivateVariant(cache, best);
                    PiPDisablerPlugin.DebugLogInfo(
                        $"[MeshSurgery] Aim: put on the stored cut for weapon stretch {Mathf.Pow(RibBucketStep, best.Rib):F2}{(best.Predicted ? " (made ahead)" : "")}");
                }
            }

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
            if (_recut != null && ReferenceEquals(_recut.Cache, cache)) _recut = null;
            _jobQueue.RemoveAll(j => ReferenceEquals(j.Cache, cache));

            var owned = new HashSet<Mesh>();
            foreach (var v in cache.Variants)
                if (v.Meshes != null)
                    foreach (var m in v.Meshes)
                        if (m != null && owned.Add(m)) { try { UnityEngine.Object.Destroy(m); } catch { } }
            cache.Variants.Clear();
            cache.Active = null;

            foreach (var entry in cache.Entries)
            {
                if (entry?.CutMesh != null && !owned.Contains(entry.CutMesh))
                {
                    try { UnityEngine.Object.Destroy(entry.CutMesh); }
                    catch { }
                }
                if (entry != null) entry.CutMesh = null;
            }
        }

        private static void DestroyCurrentWeaponCache()
        {
            _recut = null;
            _jobQueue.Clear();
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
                PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Failed to bind inventory events: {ex.Message}");
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

        private static void OnItemAdded(EFT.InventoryLogic.AddItemEventArgs args)
        {
            if (args == null || args.Status != CommandStatus.Succeed)
                return;

            MarkCacheDirtyIfMeaningful(args.Item, args.To);
        }

        private static void OnItemRemoved(EFT.InventoryLogic.RemoveItemEventArgs args)
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
                // A part with no cut yet is fine (it stays uncut until the background cut reaches it);
                // only a part that can no longer be found means the gun changed.
                if (entry == null || string.IsNullOrEmpty(entry.FilterPath))
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

            if (item is EFT.InventoryLogic.Magazine)
                return true;

            if (item is EFT.InventoryLogic.Ammo)
                return true;

            return false;
        }

        // ── Automatic hole: apex = the real camera ──
        private const float DefaultApexDistance = 0.12f;
        private const float ApexBucketStep = 1.03f;
        private static Transform _autoScopeRoot;
        private static Vector3 _autoPlaneLocal, _autoNormalLocal;

        private static void RememberAutoPlane(Transform scopeRoot, Vector3 planePoint, Vector3 planeNormal)
        {
            _autoScopeRoot = scopeRoot;
            _autoPlaneLocal = scopeRoot.InverseTransformPoint(planePoint);
            _autoNormalLocal = scopeRoot.InverseTransformDirection(planeNormal);
        }

        /// <summary>Distance from the main camera to the eyepiece along the bore, in 3% buckets.</summary>
        internal static int GetAutoApexBucket()
        {
            if (!_inRender && Time.frameCount - _renderSampleFrame < 30 && _renderEyeBucket != int.MinValue)
                return _renderEyeBucket;
            Camera cam0 = null;
            try { cam0 = Helpers.GetMainCamera(); } catch { }
            return LiveApexBucket(cam0);
        }

        private static int LiveApexBucket(Camera cam)
        {
            float e = DefaultApexDistance;
            try
            {
                if (_autoScopeRoot != null && cam != null)
                {
                    Vector3 p = _autoScopeRoot.TransformPoint(_autoPlaneLocal);
                    Vector3 n = _autoScopeRoot.TransformDirection(_autoNormalLocal).normalized;
                    float d = Vector3.Dot(p - cam.transform.position, n);
                    if (d > 0.02f && d < 1.5f) e = d;
                }
            }
            catch { }
            return Mathf.RoundToInt(Mathf.Log(e / DefaultApexDistance) / Mathf.Log(ApexBucketStep));
        }

        internal static void ForgetAutoPlane() => _autoScopeRoot = null;

        // ── Weapon stretch (2.7.8) ──
        // EFT stretches the whole weapon along its length with the zoom (FirstPersonStrategy sets
        // Ribcage / HandsHierarchy localScale = (1, 1, RibcageScaleCurrent)). A cut made at one
        // stretch is wrong at another: parts in front of the eyepiece move in/out of the lens on
        // screen, so the cut either leaves bits inside the lens or bites into turrets/mounts
        // visible around it. The cut therefore remembers the stretch it was made for and is redone
        // (spread over frames, see TickAsyncRecut) when the settled stretch changes.
        private const float RibBucketStep = 1.03f;

        internal static float CurrentRibcage()
        {
            try
            {
                var p = Helpers.GetLocalPlayer();
                if (p != null && p.RibcageScaleCurrent > 0.05f) return p.RibcageScaleCurrent;
            }
            catch { }
            return 1f;
        }

        internal static int RibBucket(float s) => Mathf.RoundToInt(Mathf.Log(Mathf.Max(0.05f, s)) / Mathf.Log(RibBucketStep));

        // ── Stored cuts per weapon stretch (2.8.0) ──
        // Each zoom level stretches the weapon differently, so each needs its own cut. Up to
        // MaxVariants cuts per scope are kept in memory; zooming to a level that was cut before
        // switches to it at once instead of waiting and re-cutting ("pop"). All of it is freed at
        // raid end. The zoom levels a scope was used at are saved (used-zoom file) and cut ahead
        // while holding the weapon, with the stretch predicted from EFT's (1,1,s) scaling.
        private const int MaxVariants = 4;

        /// <summary>True when the cut on screen does not fit the current view (after it settled).</summary>
        internal static bool AutoCutIsStale(int eyeBucket, int ribBucket, out string why)
        {
            why = null;
            var cache = _currentWeaponCache;
            if (cache == null || !cache.Built || cache.Entries.Count == 0) return false;
            var v = cache.Active;
            if (v == null) why = "no cut for this view yet";
            else if (!v.Complete) why = "previous cut was interrupted";
            else if (v.Rib != ribBucket) why = $"weapon stretch {Mathf.Pow(RibBucketStep, v.Rib):F2}->{Mathf.Pow(RibBucketStep, ribBucket):F2}";
            else if (System.Math.Abs(eyeBucket - v.Eye) >= 2) why = $"camera distance {v.Eye}->{eyeBucket}";
            else if (v.Predicted) why = "confirm the cut made ahead";
            return why != null;
        }

        /// <summary>Zoom changed: show a stored cut for this stretch right away, if there is one.</summary>
        internal static bool TrySwitchVariant(int ribBucket)
        {
            var c = _currentWeaponCache;
            if (c == null || !c.Built || (c.Active != null && c.Active.Rib == ribBucket)) return false;
            if (_recut != null && ReferenceEquals(_recut.Cache, c) && _recut.Activate) return false;
            CutVariant best = null;
            foreach (var v in c.Variants)
                if (v.Complete && v.Rib == ribBucket) { best = v; break; }
            if (best == null) return false;
            ActivateVariant(c, best);
            PiPDisablerPlugin.DebugLogInfo(
                $"[MeshSurgery] Switched to the stored cut for weapon stretch {Mathf.Pow(RibBucketStep, best.Rib):F2}{(best.Predicted ? " (made ahead)" : "")} — no re-cut");
            return true;
        }

        private static void ActivateVariant(CutProfileCache c, CutVariant v)
        {
            c.Active = v;
            v.LastUsed = Time.realtimeSinceStartup;
            bool apply = ReferenceEquals(_applyCache, c);
            for (int i = 0; i < c.Entries.Count; i++)
            {
                var e = c.Entries[i];
                if (e == null) continue;
                Mesh m = v.Meshes != null && i < v.Meshes.Length ? v.Meshes[i] : null;
                e.CutMesh = m;
                if (apply && e.Filter != null)
                {
                    e.Filter.sharedMesh = m != null ? m : e.OriginalMesh;
                    e.Applied = m != null;
                }
            }
        }

        private static CutVariant GetOrCreateVariant(CutProfileCache c, int rib, CutVariant keep)
        {
            foreach (var v in c.Variants)
                if (v.Rib == rib) return v;
            while (c.Variants.Count >= MaxVariants)
            {
                CutVariant lru = null;
                foreach (var v in c.Variants)
                    if (v != c.Active && v != keep && (lru == null || v.LastUsed < lru.LastUsed)) lru = v;
                if (lru == null) break;
                DestroyVariant(c, lru);
            }
            var nv = new CutVariant { Rib = rib, Meshes = new Mesh[c.Entries.Count], LastUsed = Time.realtimeSinceStartup };
            c.Variants.Add(nv);
            return nv;
        }

        private static void DestroyVariant(CutProfileCache c, CutVariant v)
        {
            if (v.Meshes != null)
                for (int i = 0; i < v.Meshes.Length; i++)
                {
                    var m = v.Meshes[i];
                    if (m == null) continue;
                    ReleaseFromEntry(c, i, m);
                    try { UnityEngine.Object.Destroy(m); } catch { }
                }
            c.Variants.Remove(v);
            if (c.Active == v) c.Active = null;
            PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Dropped the stored cut for weapon stretch {Mathf.Pow(RibBucketStep, v.Rib):F2} (keeping {MaxVariants})");
        }

        // A mesh about to be destroyed must not stay on screen: fall back to the active cut or the original.
        private static void ReleaseFromEntry(CutProfileCache c, int i, Mesh m)
        {
            if (i >= c.Entries.Count) return;
            var e = c.Entries[i];
            if (e == null || e.CutMesh != m) return;
            Mesh repl = c.Active != null && c.Active.Meshes != null && i < c.Active.Meshes.Length && c.Active.Meshes[i] != m ? c.Active.Meshes[i] : null;
            e.CutMesh = repl;
            if (e.Applied && e.Filter != null)
            {
                e.Filter.sharedMesh = repl != null ? repl : e.OriginalMesh;
                e.Applied = repl != null;
            }
        }

        private static void TrimProfiles(RaidWeaponCache wc, CutProfileCache keep)
        {
            // A weapon keeps at most 2 cut profiles (e.g. after a settings change the old one is dropped).
            while (wc.Profiles.Count >= 2)
            {
                string lruKey = null; float lruT = float.MaxValue;
                foreach (var kv in wc.Profiles)
                    if (kv.Value != keep && kv.Value != _currentWeaponCache && kv.Value != null && kv.Value.LastUsed < lruT) { lruKey = kv.Key; lruT = kv.Value.LastUsed; }
                if (lruKey == null) break;
                var p = wc.Profiles[lruKey];
                RestoreOriginalMeshes(p);
                DestroyCutMeshes(p);
                p.Entries.Clear();
                wc.Profiles.Remove(lruKey);
                PiPDisablerPlugin.DebugLogInfo("[MeshSurgery] Dropped an old cut profile of this weapon");
            }
        }

        private static void TrimWeaponCaches(string keepId)
        {
            // At most 3 weapons keep their cuts.
            while (_raidCaches.Count > 3)
            {
                string lruKey = null; float lruT = float.MaxValue;
                foreach (var kv in _raidCaches)
                    if (kv.Key != keepId && kv.Value != null && kv.Value.LastUsed < lruT) { lruKey = kv.Key; lruT = kv.Value.LastUsed; }
                if (lruKey == null) break;
                foreach (var p in _raidCaches[lruKey].Profiles.Values)
                {
                    if (p == _currentWeaponCache) continue;
                    RestoreOriginalMeshes(p);
                    DestroyCutMeshes(p);
                    p.Entries.Clear();
                }
                _raidCaches.Remove(lruKey);
                PiPDisablerPlugin.DebugLogInfo("[MeshSurgery] Dropped the cuts of a weapon not used recently");
            }
        }

        // ── Raid end: free everything ──
        private static object _lastGameWorld;
        internal static void CheckRaidChanged()
        {
            object gw = null;
            try { gw = Singleton<GameWorld>.Instance; } catch { }
            if (ReferenceEquals(gw, _lastGameWorld)) return;
            bool had = _raidCaches.Count > 0;
            _lastGameWorld = gw;
            if (!had) return;
            DestroyCurrentWeaponCache();
            PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] {(gw == null ? "Raid ended" : "New raid")}: freed all stored cuts");
        }

        private sealed class RecutJob
        {
            public CutProfileCache Cache;
            public Transform ScopeRoot;
            public Vector3 PlaneLocal, NormalLocal;
            public float Eye, LensR, Width;
            public int EyeBucket, TargetRib, Next, Removed, Split, Done;
            public bool Predicted, Provisional, Activate, Started2;
            public CutVariant Variant;
            public Transform StretchFrame;
            public float[] HousingCap;
            public float Margin;
            public string Reason;
            public float Started;
            public double Ms;
        }
        private static RecutJob _recut;
        private static readonly List<RecutJob> _jobQueue = new List<RecutJob>();
        internal static bool RecutRunning => (_recut != null && _recut.Activate) || _jobQueue.Exists(j => j.Activate);
        private static bool _recutFinished;

        /// <summary>Was a background cut of the current view completed since the last call?</summary>
        internal static bool ConsumeRecutFinished()
        {
            bool f = _recutFinished;
            _recutFinished = false;
            return f;
        }

        /// <summary>Redo the automatic cut for the current view, a few meshes per frame (no hitch).</summary>
        internal static bool StartAsyncRecut(OpticSight os, int eyeBucket, string reason)
        {
            var cache = _currentWeaponCache;
            if (os == null || cache == null || !cache.Built || cache.Entries.Count == 0) return false;
            if (!PerScopeMeshSurgerySettings.IsAutoCut()) return false;
            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (!scopeRoot) return false;
            var activeMode = ResolveActiveMode(os, scopeRoot);
            if (!activeMode) activeMode = os.transform;
            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode, out var planePoint, out var planeNormal, out _))
                return false;
            planePoint += planeNormal * PerScopeMeshSurgerySettings.GetPlane1OffsetMeters();
            float lensR = LensTransparency.GetEyepieceLensRadius(scopeRoot);
            if (lensR <= 0.003f || lensR >= 0.05f) return false;

            RememberAutoPlane(scopeRoot, planePoint, planeNormal);
            StartJob(cache, scopeRoot, planePoint, planeNormal, DefaultApexDistance * Mathf.Pow(ApexBucketStep, eyeBucket),
                lensR, PerScopeMeshSurgerySettings.GetCutWidthMultiplierRaw(), eyeBucket, reason,
                targetRib: int.MinValue, predicted: false, activate: true, cancelOthers: true);
            return true;
        }

        /// <summary>
        /// Aim settled (2.8.4): cut ahead, in the background, the other zoom levels this scope was
        /// used at (weapon stretch predicted from the stretch now — valid here, in the aimed pose).
        /// </summary>
        internal static int QueueCutAhead(OpticSight os, int nowRib)
        {
            var cache = _currentWeaponCache;
            if (os == null || cache == null || !cache.Built || cache.Entries.Count == 0 || !PerScopeMeshSurgerySettings.IsAutoCut()) return 0;
            var used = GetUsedZooms(cache.ScopeKey);
            if (used == null || used.Count == 0) return 0;
            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (!scopeRoot) return 0;
            var activeMode = ResolveActiveMode(os, scopeRoot);
            if (!activeMode) activeMode = os.transform;
            if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode, out var planePoint, out var planeNormal, out _)) return 0;
            planePoint += planeNormal * PerScopeMeshSurgerySettings.GetPlane1OffsetMeters();
            float lensR = LensTransparency.GetEyepieceLensRadius(scopeRoot);
            if (lensR <= 0.003f || lensR >= 0.05f) return 0;
            int queued = 0;
            foreach (var u in used)
            {
                if (System.Math.Abs(u.Key - nowRib) <= 1) continue;
                bool have = false;
                foreach (var v in cache.Variants) if (v.Complete && v.Rib == u.Key) { have = true; break; }
                if (!have && _recut != null && ReferenceEquals(_recut.Cache, cache) && _recut.TargetRib == u.Key) have = true;
                if (!have) foreach (var j in _jobQueue) if (ReferenceEquals(j.Cache, cache) && j.TargetRib == u.Key) { have = true; break; }
                if (have) continue;
                StartJob(cache, scopeRoot, planePoint, planeNormal, DefaultApexDistance * Mathf.Pow(ApexBucketStep, u.Value),
                    lensR, PerScopeMeshSurgerySettings.GetCutWidthMultiplierRaw(), u.Value, $"cut ahead for zoom stretch {Mathf.Pow(RibBucketStep, u.Key):F2}",
                    targetRib: u.Key, predicted: true, activate: false, cancelOthers: false);
                queued++;
            }
            return queued;
        }

        /// <summary>English one-liner of the cut on screen, for the always-on settle log.</summary>
        internal static string DescribeActiveCut()
        {
            var c = _currentWeaponCache;
            if (c == null || !c.Built) return "no cut cache";
            if (c.Active == null) return "no cut on screen";
            return $"cut on screen made for stretch {Mathf.Pow(RibBucketStep, c.Active.Rib):F2}, camera {DefaultApexDistance * Mathf.Pow(ApexBucketStep, c.Active.Eye) * 1000f:F0}mm" +
                   $"{(c.Active.Complete ? "" : " (unfinished)")}{(c.Active.Predicted ? " (made ahead)" : "")}";
        }

        internal static float BucketToMm(int eyeBucket) => DefaultApexDistance * Mathf.Pow(ApexBucketStep, eyeBucket) * 1000f;

        private static void StartJob(CutProfileCache cache, Transform scopeRoot, Vector3 planePoint, Vector3 planeNormal,
            float eye, float lensR, float width, int eyeBucket, string reason,
            int targetRib, bool predicted, bool activate, bool cancelOthers, bool provisional = false)
        {
            if (cancelOthers) CancelAsyncRecut("restarted", all: false);
            EnsureRenderHook();
            var job = new RecutJob
            {
                Cache = cache,
                ScopeRoot = scopeRoot,
                PlaneLocal = scopeRoot.InverseTransformPoint(planePoint),
                NormalLocal = scopeRoot.InverseTransformDirection(planeNormal),
                Eye = eye,
                LensR = lensR,
                Width = width,
                EyeBucket = eyeBucket,
                TargetRib = targetRib,
                Predicted = predicted,
                Provisional = provisional,
                Activate = activate,
                Reason = reason,
                Started = Time.realtimeSinceStartup
            };
            if (activate) _jobQueue.Insert(0, job); else _jobQueue.Add(job);
            PiPDisablerPlugin.DebugLogInfo(
                $"[MeshSurgery] Cut queued in the background ({reason}): camera {eye * 1000f:F0}mm, {cache.Entries.Count} parts");
        }

        // ── Render-time work (2.7.9) ──
        // EFT stretches the weapon only while rendering: ProceduralWeaponAnimation.ApplyFovAdjustments
        // sets Ribcage/HandsHierarchy localScale = (1,1,RibcageScaleCurrent), yields WaitForEndOfFrame
        // and resets it. In Update the weapon is never stretched (2.7.8 re-cut at 0.74/1.25/2.65
        // removed exactly the same triangles). Cutting, measuring the camera distance and the probe
        // therefore run in Camera.onPreCull of the main camera, where the weapon is as on screen.
        private static bool _renderHooked;
        private static bool _inRender;
        private static CutProfileCache _applyCache;
        private static int _renderEyeBucket = int.MinValue, _renderSampleFrame = -1000;
        private static float _renderRib = 1f;
        private static System.Action _renderPending;
        internal static bool InRender => _inRender;

        internal static void EnsureRenderHook()
        {
            if (_renderHooked) return;
            _renderHooked = true;
            Camera.onPreCull += OnPreCullAny;
        }

        internal static void UnhookRender()
        {
            if (!_renderHooked) return;
            _renderHooked = false;
            Camera.onPreCull -= OnPreCullAny;
        }

        /// <summary>Run once at the next render of the main camera (weapon as on screen).</summary>
        internal static void RunAtRender(System.Action a)
        {
            EnsureRenderHook();
            _renderPending += a;
        }

        private static void OnPreCullAny(Camera c)
        {
            if (_inRender || c == null) return;
            Camera main;
            try { main = Helpers.GetMainCamera(); } catch { return; }
            if (c != main) return;
            _inRender = true;
            try
            {
                // Camera turned to the scope first, so the pixel hole and measurements see the view as rendered.
                ReticleRenderer.AlignMainCameraEarly(c);
                EyeDistanceNormalizer.OnPreCullMain(c);
                if (_autoScopeRoot != null)
                {
                    _renderEyeBucket = LiveApexBucket(c);
                    _renderRib = CurrentRibcage();
                    _renderSampleFrame = Time.frameCount;
                }
                PixelHole.OnPreCullMain(c);
                TickAsyncRecut();
                var pending = _renderPending;
                _renderPending = null;
                pending?.Invoke();
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] render-time work failed: {ex.Message}"); }
            finally { _inRender = false; }
        }

        /// <summary>Weapon stretch as last rendered (Update never sees it).</summary>
        internal static float RenderRibcage() => Time.frameCount - _renderSampleFrame < 30 ? _renderRib : CurrentRibcage();

        // ── Pre-cut (2.7.9): cut the held weapon's scope before the first aim ──
        internal static void PrecutForOptic(OpticSight os)
        {
            if (os == null || PixelHole.Enabled || !PerScopeMeshSurgerySettings.IsAutoCut() || _recut != null || _jobQueue.Count > 0) return;
            var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
            if (!scopeRoot) return;
            var activeMode = ResolveActiveMode(os, scopeRoot);
            var cache = GetOrCreateCurrentWeaponCache(scopeRoot, activeMode);
            if (cache == null || cache.Built) return;
            EnsureRenderHook();
            RebuildCutCacheForOptic(cache, os, scopeRoot, activeMode, precut: true);
        }

        /// <summary>One-line state for the F12 status panel.</summary>
        internal static string GetAutoCutStatus()
        {
            if (PixelHole.Active)
                return Localization.T("hole.pixel", "Hole: hidden per pixel (experimental) — no cutting");
            var c = _currentWeaponCache;
            int stored = 0;
            if (c != null) foreach (var v in c.Variants) if (v.Complete) stored++;
            string keep = Localization.T("hole.stored", "stored zoom cuts {0}/{1}", stored, MaxVariants);
            if (_recut != null)
                return Localization.T("hole.cutting", "Cutting the hole {0}/{1}{2} · {3}", _recut.Done, _recut.Cache.Entries.Count,
                                  _recut.Predicted ? Localization.T("hole.ahead", " (ahead)") : "", keep);
            if (c == null || !c.Built || c.Active == null) return Localization.T("hole.notcut", "Hole: not cut yet · {0}", keep);
            float mm = DefaultApexDistance * Mathf.Pow(ApexBucketStep, c.Active.Eye) * 1000f;
            float st = Mathf.Pow(RibBucketStep, c.Active.Rib);
            return Localization.T("hole.cut", "Hole: camera {0:F0}mm · weapon stretch {1:F2} · {2}", mm, st, keep);
        }

        /// <summary>Stop background cuts: by default only the one(s) for the current view; all=true also pre-cuts.</summary>
        internal static void CancelAsyncRecut(string why, bool all = false)
        {
            int removed = _jobQueue.RemoveAll(j => all || j.Activate);
            if (_recut != null && (all || _recut.Activate))
            {
                PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Background cut stopped ({why}) after {_recut.Done}/{_recut.Cache.Entries.Count} parts");
                _recut = null; // its variant stays incomplete, so the next settle check redoes it
            }
            else if (removed > 0)
                PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] {removed} queued background cut(s) dropped ({why})");
        }

        // The transform EFT stretches with (1,1,s) while rendering, found among the weapon's parents.
        internal static Transform FindStretchFrame(Transform weaponRoot, float sNow)
        {
            if (weaponRoot == null) return null;
            Transform found = null; int count = 0;
            if (Mathf.Abs(sNow - 1f) > 0.02f)
            {
                for (var p = weaponRoot; p != null; p = p.parent)
                {
                    var ls = p.localScale;
                    if (Mathf.Abs(ls.z - sNow) < 0.01f && Mathf.Abs(ls.x - 1f) < 0.01f && Mathf.Abs(ls.y - 1f) < 0.01f)
                    { found = p; count++; }
                }
                return count == 1 ? found : null;
            }
            // Stretch 1.0 shows no scale: use the frame EFT scales (FirstPersonStrategy:
            // Ribcage.Original and HandsHierarchy.Self) that holds the weapon — only if exactly one does.
            try
            {
                var player = Helpers.GetLocalPlayer();
                Transform rib = player?.PlayerBones?.Ribcage?.Original;
                Transform self = player?.HandsController?.HandsHierarchy?.Self;
                bool underRib = rib != null && weaponRoot.IsChildOf(rib);
                bool underSelf = self != null && weaponRoot.IsChildOf(self);
                if (underRib ^ underSelf) return underRib ? rib : self;
            }
            catch { }
            return null;
        }

        /// <summary>Per frame at render time: cut parts within a small time budget.</summary>
        internal static void TickAsyncRecut()
        {
            // A cut for the view on screen goes before cuts made ahead (those resume afterwards).
            if (_recut != null && !_recut.Activate && _jobQueue.Count > 0 && _jobQueue[0].Activate)
            {
                _jobQueue.Insert(1, _recut);
                _recut = null;
            }
            if (_recut == null)
            {
                if (_jobQueue.Count == 0) return;
                _recut = _jobQueue[0];
                _jobQueue.RemoveAt(0);
                if (_recut.Started2 && (_recut.Variant == null || !_recut.Cache.Variants.Contains(_recut.Variant)))
                {
                    _recut = null; // its stored set was dropped meanwhile
                    return;
                }
            }
            var job = _recut;
            if (job.Cache == null || job.ScopeRoot == null || !job.Cache.Built ||
                (!ReferenceEquals(job.Cache, _currentWeaponCache)))
            {
                CancelAsyncRecut("weapon changed", all: true);
                return;
            }
            var cache = job.Cache;
            float sNow = CurrentRibcage();
            if (!job.Started2)
            {
                job.Started2 = true;
                int rib = job.TargetRib == int.MinValue ? RibBucket(sNow) : job.TargetRib;
                if (job.Predicted)
                {
                    job.StretchFrame = FindStretchFrame(cache.WeaponRoot != null ? cache.WeaponRoot.transform : null, sNow);
                    if (job.StretchFrame == null)
                    {
                        PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Pre-cut for stretch {Mathf.Pow(RibBucketStep, rib):F2} skipped: weapon stretch frame not found (stretch now {sNow:F3})");
                        _recut = null;
                        return;
                    }
                }
                if (job.Predicted)
                {
                    foreach (var ex in cache.Variants)
                        if (ex.Rib == rib && (!ex.Predicted || ex == cache.Active))
                        {
                            PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Cut ahead for stretch {Mathf.Pow(RibBucketStep, rib):F2} skipped: a real cut for it exists");
                            _recut = null;
                            return;
                        }
                }
                job.TargetRib = rib;
                job.Variant = GetOrCreateVariant(cache, rib, null);
                job.Variant.Eye = job.EyeBucket;
                job.Variant.Complete = false;
                job.Variant.Predicted = job.Predicted || job.Provisional; // confirmed after the aim settles
                if (job.Activate) cache.Active = job.Variant;
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery] Cut started ({job.Reason}): weapon stretch {Mathf.Pow(RibBucketStep, rib):F2}{(job.Predicted ? $" predicted from {sNow:F3} via '{job.StretchFrame.name}'" : "")}");
            }

            // Predicted stretch: rescale the rendered weapon along the stretch frame's z from now to target.
            Matrix4x4? pre = null;
            if (job.Predicted && job.StretchFrame != null)
            {
                float r = Mathf.Pow(RibBucketStep, job.TargetRib) / Mathf.Max(0.05f, sNow);
                if (Mathf.Abs(r - 1f) > 0.001f)
                {
                    var f = job.StretchFrame.localToWorldMatrix;
                    pre = f * Matrix4x4.Scale(new Vector3(1f, 1f, r)) * f.inverse;
                }
            }

            var sw = System.Diagnostics.Stopwatch.StartNew();
            bool applyNew = ReferenceEquals(_applyCache, cache);
            Vector3 planePoint = job.ScopeRoot.TransformPoint(job.PlaneLocal);
            Vector3 planeNormal = job.ScopeRoot.TransformDirection(job.NormalLocal).normalized;
            if (pre.HasValue)
            {
                var m = pre.Value;
                planePoint = m.MultiplyPoint3x4(planePoint);
                planeNormal = m.inverse.transpose.MultiplyVector(planeNormal).normalized;
            }
            if (job.HousingCap == null)
            {
                // Eyepiece housing outline (once per job): caps the off-axis margin.
                job.Margin = Settings.HoleOffAxisMargin != null ? Settings.HoleOffAxisMargin.Value / 1000f : 0f;
                job.HousingCap = job.Margin > 0f ? MeasureHousing(cache, job.ScopeRoot, pre, planePoint, planeNormal, job.Eye, job.LensR * Mathf.Max(0.05f, job.Width)) : new float[0];
            }
            var entries = cache.Entries;
            var variant = job.Variant;
            if (variant.Meshes == null || variant.Meshes.Length != entries.Count) variant.Meshes = new Mesh[entries.Count];
            while (job.Next < entries.Count)
            {
                int i = job.Next++;
                var entry = entries[i];
                if (entry == null || entry.Filter == null || entry.OriginalMesh == null) continue;
                Mesh readable = null;
                try
                {
                    readable = MeshPlaneCutter.MakeReadableMeshCopy(entry.OriginalMesh);
                    if (readable != null)
                    {
                        bool ok = MeshPlaneCutter.CutMeshSightCone(readable, entry.Filter.transform,
                            planePoint, planeNormal, job.Eye, job.LensR, job.Width, pre,
                            job.Margin, job.HousingCap.Length > 0 ? job.HousingCap : null);
                        if (!ok) { readable.Clear(); readable.name = entry.OriginalMesh.name + "_CUT_EMPTY"; }
                        else readable.name = entry.OriginalMesh.name + "_CUT";
                        job.Removed += MeshPlaneCutter.LastSightRemoved;
                        job.Split += MeshPlaneCutter.LastSightSplit;

                        var old = variant.Meshes[i];
                        variant.Meshes[i] = readable;
                        if (cache.Active == variant)
                        {
                            entry.CutMesh = readable;
                            if (entry.Applied || applyNew) { entry.Filter.sharedMesh = readable; entry.Applied = true; }
                        }
                        readable = null;
                        if (old != null)
                        {
                            ReleaseFromEntry(cache, i, old);
                            UnityEngine.Object.Destroy(old);
                        }
                        job.Done++;
                    }
                }
                catch (Exception ex)
                {
                    PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Cut failed on '{entry.OriginalMesh.name}': {ex.Message}");
                }
                finally
                {
                    if (readable != null) UnityEngine.Object.Destroy(readable);
                }
                if (sw.Elapsed.TotalMilliseconds > 2.0) break; // keep frames smooth
            }
            job.Ms += sw.Elapsed.TotalMilliseconds;
            if (job.Next < entries.Count) return;

            variant.Complete = true;
            variant.LastUsed = Time.realtimeSinceStartup;
            if (job.Activate) LastAutoApexBucketUsed = job.EyeBucket;
            _recut = null;
            if (!job.Predicted && !job.Provisional)
            {
                _recutFinished = job.Activate;
                if (job.Activate) RecordUsedZoom(cache.ScopeKey, job.TargetRib, job.EyeBucket);
            }
            PiPDisablerPlugin.DebugLogInfo(
                $"[MeshSurgery] Background cut done ({job.Reason}, weapon stretch {Mathf.Pow(RibBucketStep, job.TargetRib):F2}): {job.Done} parts, removed {job.Removed} triangles in the line of sight, split {job.Split}, " +
                $"{job.Ms:F0}ms of work over {(Time.realtimeSinceStartup - job.Started) * 1000f:F0}ms");
        }

        // ── Zoom levels used per scope (2.8.0), saved so the next raid can cut them ahead ──
        private static readonly Dictionary<string, List<KeyValuePair<int, int>>> _usedZooms =
            new Dictionary<string, List<KeyValuePair<int, int>>>(StringComparer.OrdinalIgnoreCase);
        private static bool _usedLoaded;
        private static string UsedZoomPath => UserData.PathFor("used-zoom.txt");

        private static void LoadUsedZooms()
        {
            if (_usedLoaded) return;
            _usedLoaded = true;
            try
            {
                if (!System.IO.File.Exists(UsedZoomPath)) return;
                foreach (var line in System.IO.File.ReadAllLines(UsedZoomPath))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) continue;
                    var list = new List<KeyValuePair<int, int>>();
                    foreach (var part in line.Substring(eq + 1).Split(';'))
                    {
                        var kv = part.Split(':');
                        if (kv.Length == 2 && int.TryParse(kv[0], out int r) && int.TryParse(kv[1], out int e))
                            list.Add(new KeyValuePair<int, int>(r, e));
                    }
                    if (list.Count > 0) _usedZooms[line.Substring(0, eq).Trim()] = list;
                }
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Used-zoom file not read: {ex.Message}"); }
        }

        private static List<KeyValuePair<int, int>> GetUsedZooms(string key)
        {
            LoadUsedZooms();
            return !string.IsNullOrEmpty(key) && _usedZooms.TryGetValue(key, out var l) ? new List<KeyValuePair<int, int>>(l) : null;
        }

        private static void RecordUsedZoom(string key, int rib, int eye)
        {
            if (string.IsNullOrEmpty(key) || rib == int.MinValue) return;
            LoadUsedZooms();
            if (!_usedZooms.TryGetValue(key, out var list)) _usedZooms[key] = list = new List<KeyValuePair<int, int>>();
            if (list.Count > 0 && list[0].Key == rib && list[0].Value == eye) return;
            list.RemoveAll(kv => System.Math.Abs(kv.Key - rib) <= 1);
            list.Insert(0, new KeyValuePair<int, int>(rib, eye));
            if (list.Count > MaxVariants) list.RemoveRange(MaxVariants, list.Count - MaxVariants);
            try
            {
                var lines = new List<string>();
                foreach (var kv in _usedZooms)
                {
                    var parts = new List<string>();
                    foreach (var z in kv.Value) parts.Add(z.Key + ":" + z.Value);
                    lines.Add(kv.Key + "=" + string.Join(";", parts.ToArray()));
                }
                string tmp = UsedZoomPath + ".tmp";
                System.IO.File.WriteAllLines(tmp, lines.ToArray());
                if (System.IO.File.Exists(UsedZoomPath)) System.IO.File.Delete(UsedZoomPath);
                System.IO.File.Move(tmp, UsedZoomPath);
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Used-zoom file not saved: {ex.Message}"); }
        }

        private static float[] MeasureHousing(CutProfileCache cache, Transform scopeRoot, Matrix4x4? pre,
            Vector3 planePoint, Vector3 planeNormal, float eye, float lensR)
        {
            var verts = new List<Vector3>(8192);
            foreach (var e in cache.Entries)
            {
                if (e == null || e.Filter == null || e.OriginalMesh == null) continue;
                if (e.Filter.transform != scopeRoot && !e.Filter.transform.IsChildOf(scopeRoot)) continue;
                Mesh r = null;
                try
                {
                    r = MeshPlaneCutter.MakeReadableMeshCopy(e.OriginalMesh);
                    if (r == null) continue;
                    var m = pre.HasValue ? pre.Value * e.Filter.transform.localToWorldMatrix : e.Filter.transform.localToWorldMatrix;
                    foreach (var lv in r.vertices) verts.Add(m.MultiplyPoint3x4(lv));
                }
                catch { }
                finally { if (r != null) UnityEngine.Object.Destroy(r); }
            }
            var cap = MeshPlaneCutter.ComputeHousingOutline(verts, planePoint, planeNormal, eye, lensR);
            float mn = float.MaxValue, mx = 0f;
            foreach (var c in cap) { mn = Mathf.Min(mn, c); mx = Mathf.Max(mx, c); }
            PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Eyepiece housing outline: {mn / lensR:F2}–{mx / lensR:F2}× lens radius ({verts.Count} vertices) — off-axis margin stays inside it");
            return cap;
        }

        /// <summary>Original (uncut) mesh of a part, if the current cut replaced it.</summary>
        internal static Mesh GetOriginalMesh(MeshFilter mf)
        {
            var cache = _currentWeaponCache;
            if (cache == null || mf == null) return null;
            foreach (var e in cache.Entries)
                if (e != null && e.Filter == mf) return e.OriginalMesh;
            return null;
        }

        // ── Settled camera distance per scope (2.7.7) ──
        // The first cut happens while the aim animation is still moving the camera, so its distance
        // is wrong and a full recut followed ~0.6 s later (a visible hitch on every aim). The
        // settled distance is the same every time for a scope, so it is remembered (and saved) and
        // used for the first cut; the cached cut is then reused and no recut is needed.
        private static readonly Dictionary<string, int> _settledBucket = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private static bool _settledLoaded;
        private static string SettledFilePath => UserData.PathFor("camera-distance.txt");

        // 2.8.4: kept per zoom level ("scope|stretch bucket"). One value per scope stored the last
        // settled distance whatever the zoom (e.g. 575 mm at stretch 2.65), so the next raid's
        // first cut at 1x was made for the wrong camera distance until Home re-cut it.
        private static string SettledKey(string scope, int rib) => scope + "|" + rib;

        private static void LoadSettled()
        {
            if (_settledLoaded) return;
            _settledLoaded = true;
            try
            {
                if (!System.IO.File.Exists(SettledFilePath)) return;
                foreach (var line in System.IO.File.ReadAllLines(SettledFilePath))
                {
                    int eq = line.LastIndexOf('=');
                    if (eq <= 0) continue;
                    string k = line.Substring(0, eq).Trim();
                    if (k.IndexOf('|') < 0) continue; // old per-scope value (any zoom): not usable
                    if (int.TryParse(line.Substring(eq + 1).Trim(), out int b))
                        _settledBucket[k] = b;
                }
                PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Loaded settled camera distance for {_settledBucket.Count} scope zoom levels");
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Camera distance file not read: {ex.Message}"); }
        }

        private static bool TryGetKnownBucket(int rib, out int b)
        {
            b = 0;
            LoadSettled();
            string key = PerScopeMeshSurgerySettings.ActiveScopeKey;
            if (string.IsNullOrEmpty(key)) return false;
            if (_settledBucket.TryGetValue(SettledKey(key, rib), out b)) return true;
            // A real cut made at this zoom level before (used-zoom file) knows its distance too.
            var used = GetUsedZooms(key);
            if (used != null)
                foreach (var u in used)
                    if (System.Math.Abs(u.Key - rib) <= 1) { b = u.Value; return true; }
            return false;
        }

        /// <summary>Camera-distance bucket to cut with: the one remembered for this zoom level, else the live one.</summary>
        internal static int GetAutoBucketForCut(int rib)
            => TryGetKnownBucket(rib, out int b) ? b : GetAutoApexBucket();

        internal static bool HasSettledBucket(int rib) => TryGetKnownBucket(rib, out _);

        internal static void RememberSettledBucket(int bucket, int rib)
        {
            LoadSettled();
            string scope = PerScopeMeshSurgerySettings.ActiveScopeKey;
            if (string.IsNullOrEmpty(scope)) return;
            string key = SettledKey(scope, rib);
            if (_settledBucket.TryGetValue(key, out int old) && old == bucket) return;
            _settledBucket[key] = bucket;
            try
            {
                var lines = new List<string>(_settledBucket.Count);
                foreach (var kv in _settledBucket) lines.Add(kv.Key + "=" + kv.Value);
                string tmp = SettledFilePath + ".tmp";
                System.IO.File.WriteAllLines(tmp, lines.ToArray());
                if (System.IO.File.Exists(SettledFilePath)) System.IO.File.Delete(SettledFilePath);
                System.IO.File.Move(tmp, SettledFilePath);
            }
            catch (Exception ex) { PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Camera distance file not saved: {ex.Message}"); }
            PiPDisablerPlugin.DebugLogInfo($"[MeshSurgery] Remembered settled camera distance for '{key}': {DefaultApexDistance * Mathf.Pow(ApexBucketStep, bucket) * 1000f:F0}mm (bucket {bucket})");
        }

        internal static bool TryGetAutoPlaneWorld(out Transform scopeRoot, out Vector3 point, out Vector3 normal)
        {
            scopeRoot = _autoScopeRoot;
            point = default; normal = default;
            if (scopeRoot == null) return false;
            point = scopeRoot.TransformPoint(_autoPlaneLocal);
            normal = scopeRoot.TransformDirection(_autoNormalLocal).normalized;
            return true;
        }

        /// <summary>Camera-distance bucket the last automatic cut was made with.</summary>
        internal static int LastAutoApexBucketUsed { get; private set; } = int.MinValue;

        private static string BuildCutSettingsSignature()
        {
            return string.Join("|", new[]
            {
                // Auto: the camera distance is per stored cut (variant), not part of the profile —
                // it changes with the zoom and used to throw all stored cuts away (2.8.0 log).
                PerScopeMeshSurgerySettings.IsAutoCut() ? "Auto" : "Cylinder",
                PerScopeMeshSurgerySettings.GetCutWidthMultiplierRaw().ToString("F3"),
                PerScopeMeshSurgerySettings.GetPlaneOffsetMeters().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane1OffsetMeters().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane1Radius().ToString("F4"),
                PerScopeMeshSurgerySettings.GetCutStartOffset().ToString("F4"),
                PerScopeMeshSurgerySettings.GetCutLength().ToString("F4"),
                PerScopeMeshSurgerySettings.GetNearPreserveDepth().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane2PositionNormalized(PerScopeMeshSurgerySettings.GetCutLength()).ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane2Radius().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane3Position().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane3Radius().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane4Position().ToString("F4"),
                PerScopeMeshSurgerySettings.GetPlane4Radius().ToString("F4"),
            });
        }

        internal static Transform ResolveActiveModeFor(OpticSight os, Transform scopeRoot) => ResolveActiveMode(os, scopeRoot);

        /// <summary>Hide the scope's glow/sphere objects (as a cut does) — used by the pixel hole.</summary>
        internal static void DisableScopeExtras(Transform scopeRoot)
        {
            DisableLightEffectMeshesForScope(scopeRoot);
            DisableWeaponSphereObjects(scopeRoot);
        }

        internal static void RestoreScopeExtras(Transform weaponRoot)
        {
            RestoreLightEffectMeshesUnderRoot(weaponRoot);
            RestoreWeaponSphereObjectsUnderRoot(weaponRoot);
        }

        internal static Transform FindWeaponTransform(Transform scopeRoot)
        {
            for (var p = scopeRoot; p != null; p = p.parent)
            {
                if (p.name != null && p.name.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                    return p;
            }
            return null;
        }

        private static void DisableWeaponSphereObjects(Transform scopeRoot)
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

        private static void DisableLightEffectMeshesForScope(Transform scopeRoot)
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
                    PiPDisablerPlugin.DebugLogInfo(
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
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[ScopeHierarchy] FindScopeRoot fallback (name match): '{t.name}'");
                        return t;
                    }
                }
            }

            PiPDisablerPlugin.DebugLogInfo(
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

        private static bool IsTextMeshProOwnedMesh(MeshFilter mf)
        {
            if (mf == null || mf.transform == null) return false;

            for (var t = mf.transform; t != null; t = t.parent)
            {
                var components = t.GetComponents<Component>();
                for (int i = 0; i < components.Length; i++)
                {
                    var component = components[i];
                    if (component == null) continue;

                    var type = component.GetType();
                    string fullName = type.FullName ?? string.Empty;
                    string name = type.Name ?? string.Empty;

                    if (fullName.StartsWith("TMPro.", StringComparison.Ordinal) ||
                        name.StartsWith("TMP_", StringComparison.Ordinal) ||
                        name.StartsWith("TextMeshPro", StringComparison.Ordinal))
                    {
                        return true;
                    }
                }
            }

            return false;
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
            else { var mc = Helpers.GetMainCamera(); camPos = mc != null ? mc.transform.position : activeMode.position; }

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

            if (refTransform == null)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[ScopeHierarchy][DEBUG] TryGetPlane: ALL fallbacks failed. " +
                    $"os='{(os != null ? os.name : "null")}' activeMode='{activeMode.name}' " +
                    $"scopeRoot='{scopeRoot.name}' frame={Time.frameCount}. " +
                    $"Checked: backLens=null, LensRenderer=null, lens/linza/glass=null, optic_camera=null");
                return false;
            }

            // Determine the plane normal based on config.
            planeNormal = GetConfiguredNormal(refTransform);

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeHierarchy][DEBUG] TryGetPlane OK: ref='{refTransform.name}', " +
                $"planePoint={planePoint:F4}, normal={planeNormal:F3}, " +
                $"frame={Time.frameCount}");

            return true;
        }

        private static Vector3 GetConfiguredNormal(Transform refTransform)
        {
            return -refTransform.up;
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

            if (PerScopeMeshSurgerySettings.GetExpandSearchToWeaponRoot())
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

            Transform searchRoot = null;
            for (var p = scopeRoot; p != null; p = p.parent)
            {
                if (p.name != null && p.name.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                {
                    searchRoot = p;
                    break;
                }
            }

            if (searchRoot == null)
            {
                PiPDisablerPlugin.DebugLogInfo(
                    $"[MeshSurgery][DebugCandidates] FindTargetMeshFilters could not find weapon root for '{scopeRoot.name}'");
                return new List<MeshFilter>();
            }

            var result = new List<MeshFilter>(64);
            int inspected = 0;

            foreach (var mf in searchRoot.GetComponentsInChildren<MeshFilter>(true))
            {
                if (!mf || !mf.sharedMesh) continue;
                inspected++;

                string relSearchPath = null;

                if (IsVolatileWeaponPath(relSearchPath))
                {
                    continue;
                }

                if (ContainsPatronToken(relSearchPath) || ContainsPatronToken(mf.gameObject.name) || ContainsPatronToken(mf.sharedMesh.name))
                    continue;

                if (IsTextMeshProOwnedMesh(mf))
                    continue;

                var renderer = mf.GetComponent<Renderer>();
                if (renderer != null && LensTransparency.IsLensSurfaceRenderer(renderer))
                    continue;

                result.Add(mf);
            }

            PiPDisablerPlugin.DebugLogInfo(
                $"[ScopeHierarchy] FindTargets from '{searchRoot.name}': " +
                $"{result.Count} targets");

            return result;
        }

        private static bool IsVolatileWeaponPath(string relativePath)
        {
            if (string.IsNullOrEmpty(relativePath))
                return false;

            var path = relativePath.ToLowerInvariant();
            return path.Contains("patron_in_weapon")
                || path.Contains("mod_magazine")
                || path.Contains("mod_magazine_new");
        }

        private static bool ContainsPatronToken(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.IndexOf("patron", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>

    }
}
