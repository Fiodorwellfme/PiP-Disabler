using System;
using System.Reflection;
using System.Collections.Generic;
using EFT;
using EFT.Animations;
using EFT.CameraControl;
using Comfort.Common;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Event-driven scope lifecycle. State machine with two states: scoped / not scoped.
    ///
    /// Entry points (event-driven, fire ONCE per transition):
    ///   OnOpticEnabled(os)  — from OpticSight.OnEnable patch
    ///   OnOpticDisabled(os) — from OpticSight.OnDisable patch
    ///   CheckAndUpdate()    — from ChangeAimingMode patch + Update safety net
    ///
    /// Per-frame (zero-alloc maintenance):
    ///   Tick() — ensure lens stays hidden, update variable zoom
    ///
    /// Detection (same as SPT-Dynamic-External-Resolution):
    ///   player.ProceduralWeaponAnimation.IsAiming
    ///   player.ProceduralWeaponAnimation.CurrentScope.IsOptic
    /// </summary>
    internal static class ScopeLifecycle
    {
        // Reflection cache
        private static PropertyInfo _isAimingProp;
        private static PropertyInfo _currentScopeProp;
        private static PropertyInfo _isOpticProp;
        private static bool _reflectionReady;

        // Fast delegates (avoid PropertyInfo.GetValue overhead per frame)
        private static Func<ProceduralWeaponAnimation, bool> _getIsAiming;
        private static Func<ProceduralWeaponAnimation, object> _getCurrentScope;
        private static Func<object, bool> _getIsOptic;

        // State
        private static bool _isScoped;
        private static OpticSight _activeOptic;
        private static OpticSight _lastEnabledOptic; // cache from OnEnable
        private static bool _modBypassedForCurrentScope;

        // Thermal/NV discovery cache (ScopeData component shape is stable at runtime)
        private static Type _scopeDataType;
        private static FieldInfo _scopeDataNightVisionField;
        private static FieldInfo _scopeDataThermalField;
        private static PropertyInfo _scopeDataNightVisionProp;
        private static PropertyInfo _scopeDataThermalProp;
        private static bool _scopeDataMembersSearched;

        private static readonly HashSet<string> _scopeWhitelistNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private static string _scopeWhitelistRawCached;


        public static bool IsScoped => _isScoped;
        public static bool IsModBypassedForCurrentScope => _modBypassedForCurrentScope;
        public static OpticSight ActiveOptic => _activeOptic;

        /// <summary>
        /// Shared optic classification helper for other systems that must make
        /// early decisions before scope state is fully transitioned.
        /// </summary>
        internal static bool IsThermalOrNightVisionOpticForBypass(OpticSight os)
        {
            return IsThermalOrNightVisionOptic(os);
        }

        /// <summary>
        /// Returns true if the given optic matches the AutoBypassNameContains pattern.
        /// Callable from PiPDisabler patches which have a concrete OpticSight reference.
        /// </summary>
        internal static bool IsNameBypassed(OpticSight os)
        {
            return ScopeNameMatchesBypassPattern(os);
        }

        /// <summary>
        /// Returns true if the most recently enabled OpticSight (as seen by the
        /// OnEnable patch — set before CheckAndUpdate / PWA check) matches
        /// AutoBypassNameContains and is still enabled in the scene.
        /// Used by PiPDisabler.ShouldAllowVanillaPiP() which has no concrete
        /// OpticSight but must decide per-frame whether to restore vanilla PiP.
        /// </summary>
        internal static bool IsLastOpticNameBypassed()
        {
            var os = _lastEnabledOptic;
            if (os == null) return false;
            try { if (!os.enabled) return false; } catch { return false; }
            return ScopeNameMatchesBypassPattern(os);
        }

        /// <summary>
        /// One-time reflection setup. Call from plugin Awake.
        /// </summary>
        public static void Init()
        {
            try
            {
                var pwaType = typeof(ProceduralWeaponAnimation);
                _isAimingProp = AccessTools.Property(pwaType, "IsAiming");
                _currentScopeProp = AccessTools.Property(pwaType, "CurrentScope");

                if (_currentScopeProp != null)
                {
                    var scopeType = _currentScopeProp.PropertyType; // SightNBone
                    _isOpticProp = AccessTools.Property(scopeType, "IsOptic");
                }

                _reflectionReady = _isAimingProp != null
                                && _currentScopeProp != null
                                && _isOpticProp != null;

                // Build fast getter delegates to avoid PropertyInfo.GetValue overhead per frame
                if (_reflectionReady)
                {
                    try
                    {
                        _getIsAiming = (Func<ProceduralWeaponAnimation, bool>)
                            Delegate.CreateDelegate(typeof(Func<ProceduralWeaponAnimation, bool>),
                                _isAimingProp.GetGetMethod(true));
                    }
                    catch
                    {
                        _getIsAiming = pwa => (bool)_isAimingProp.GetValue(pwa);
                    }

                    try
                    {
                        var getter = _currentScopeProp.GetGetMethod(true);
                        // CurrentScope returns SightNBone (value may be null), use generic Func
                        _getCurrentScope = pwa => getter.Invoke(pwa, null);
                    }
                    catch
                    {
                        _getCurrentScope = pwa => _currentScopeProp.GetValue(pwa);
                    }

                    try
                    {
                        var scopeType = _currentScopeProp.PropertyType;
                        var isOpticGetter = _isOpticProp.GetGetMethod(true);
                        _getIsOptic = scope => (bool)isOpticGetter.Invoke(scope, null);
                    }
                    catch
                    {
                        _getIsOptic = scope => (bool)_isOpticProp.GetValue(scope);
                    }
                }

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Reflection: IsAiming={_isAimingProp != null}, " +
                    $"CurrentScope={_currentScopeProp != null}, IsOptic={_isOpticProp != null}");
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[ScopeLifecycle] Init failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Called from OpticSight.OnEnable patch. Caches the OpticSight and checks state.
        /// </summary>
        public static void OnOpticEnabled(OpticSight os)
        {
            if (os != null)
                _lastEnabledOptic = os;

            // If already scoped and a DIFFERENT optic enables → genuine mode switch.
            // Guard against sibling mode_000/mode_001 co-activating on scope enter, which
            // would falsely trigger a restore+recut cycle and cause a 1-2 frame mesh flash.
            if (_isScoped && os != null && os != _activeOptic)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Mode switch while scoped: " +
                    $"'{(_activeOptic != null ? _activeOptic.name : "?")}'[{FovController.GetOpticTemplateId(_activeOptic)}] → " +
                    $"'{os.name}'[{FovController.GetOpticTemplateId(os)}]");

                // Update the active optic to the new mode
                _activeOptic = os;

                float minFov = ZoomController.GetMinFov(os);
                bool bypassForMode = ShouldBypassForCurrentOptic(os, minFov);
                if (bypassForMode)
                {
                    _modBypassedForCurrentScope = true;
                    ApplyBypassState(os, minFov, reason: "mode switch");
                    return;
                }

                _modBypassedForCurrentScope = false;

                // Re-extract reticle from the NEW mode's linza
                ReticleRenderer.Cleanup();
                ReticleRenderer.ExtractReticle(os);

                // Re-hide lenses (the new mode's lens might not be hidden yet)
                LensTransparency.HideAllLensSurfaces(os);

                // Recollect housing + weapon renderers for the new mode's geometry.
                ReticleRenderer.SetHousingRenderers(CollectStencilRenderers(os));

                // Show reticle for the new mode (with magnification scaling)
                float modeMag = ZoomController.GetMagnification(os);
                ReticleRenderer.Show(os, modeMag);

                // Notify FOV controller the mode changed so it re-reads ScopeCameraData
                FovController.OnModeSwitch();

                // RESTORE all meshes first, then re-cut with new mode's plane position.
                if (ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
                {
                    MeshSurgeryManager.RestoreForScope(os.transform);
                    MeshSurgeryManager.ApplyForOptic(os);
                }

                // Re-apply camera settings for the new mode's FOV
                CameraSettingsManager.ApplyForOptic(os);

                // Capture weapon base scale/FOV before FOV changes
                Patches.WeaponScalingPatch.CaptureBaseState();

                // Animated FOV change for mode switch (uses configured duration)
                ApplyFov(true);
            }

            CheckAndUpdate("OnOpticEnabled");
        }

        /// <summary>
        /// Called from OpticSight.OnDisable patch. Checks if we should exit.
        /// </summary>
        public static void OnOpticDisabled(OpticSight os)
        {
            ReticleRenderer.Hide();
            ScopeEffectsRenderer.Hide();
            CheckAndUpdate("OnOpticDisabled");
        }

        /// <summary>
        /// Core state check. Reads PWA state via reflection.
        /// Called from ChangeAimingMode patch and Update safety net.
        /// </summary>
        public static void CheckAndUpdate(string caller = "Update")
        {
            _lastCaller = caller;
            if (!_reflectionReady) return;

            bool shouldBeScoped = false;
            string reason = "unknown";

            try
            {
                var player = GetLocalPlayer();
                if (player == null) { reason = "no player"; goto evaluate; }

                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) { reason = "no PWA"; goto evaluate; }

                bool isAiming = _getIsAiming(pwa);
                if (!isAiming) { reason = "not aiming"; goto evaluate; }

                object currentScope = _getCurrentScope(pwa);
                if (currentScope == null) { reason = "no CurrentScope"; goto evaluate; }

                bool isOptic = _getIsOptic(currentScope);
                if (!isOptic) { reason = "not optic"; goto evaluate; }

                var enabledOs = FindEnabledOpticFromPWA();
                if (enabledOs == null)
                {
                    // Hybrid toggle case: CurrentScope may still report optic while the
                    // enabled OpticSight switched off (e.g., now in collimator mode).
                    // Force scope exit immediately so RestoreAll runs without waiting for
                    // a full ADS exit.
                    shouldBeScoped = false;
                    reason = "optic flag true but no enabled OpticSight";
                    goto evaluate;
                }

                shouldBeScoped = true;
                _activeOptic = enabledOs;
                _lastEnabledOptic = enabledOs;
                reason = "aiming+optic+enabled OpticSight";
            }
            catch (Exception ex) { reason = $"exception: {ex.Message}"; }

            evaluate:

            // Log every state CHANGE (not every frame)
            if (shouldBeScoped != _isScoped)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] State change: {(_isScoped ? "SCOPED" : "NOT_SCOPED")} → " +
                    $"{(shouldBeScoped ? "SCOPED" : "NOT_SCOPED")} reason='{reason}' " +
                    $"caller={_lastCaller} frame={Time.frameCount}");
            }

            if (shouldBeScoped && !_isScoped)
            {
                DoScopeEnter();
            }
            else if (!shouldBeScoped && _isScoped)
            {
                DoScopeExit();
            }
        }

        // Caller tag for lightweight state-change logging (replaces expensive StackTrace)
        private static string _lastCaller = "?";

        /// <summary>
        /// Per-frame maintenance. Zero allocations when scoped (just bool checks).
        /// Called from plugin Update.
        /// </summary>
        public static void Tick()
        {
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;

            // Ensure lens stays hidden + update variable zoom
            if (ZoomController.IsActive)
            {
                if (_activeOptic != null)
                {
                    float mag = ZoomController.GetMagnification(_activeOptic);
                    ZoomController.SetZoom(mag);
                    // Update reticle + effects position (smoothed), rotation, and scale each frame
                    ReticleRenderer.UpdateTransform(mag);
                    ScopeEffectsRenderer.UpdateTransform(baseSize: 0f, magnification: mag);
                }
                ZoomController.EnsureLensVisible();

                // Always re-kill other lens surfaces.
                // Pass the ZoomController's managed renderer as exclusion while
                // any other glass/linza surfaces EFT restores get killed again immediately.
                LensTransparency.EnsureHidden(ZoomController.ActiveLensRenderer);
            }
            else
            {
                LensTransparency.EnsureHidden();

                // Update reticle position/rotation/scale
                if (_activeOptic != null)
                {
                    float mag = ZoomController.GetMagnification(_activeOptic);
                    ReticleRenderer.UpdateTransform(mag);
                    ScopeEffectsRenderer.UpdateTransform(baseSize: 0f, magnification: mag);
                }
            }

            // PiP stays disabled via Harmony patches — no per-frame action needed.

            // Per-frame weapon scale compensation (tracks animated FOV transitions)
            Patches.WeaponScalingPatch.UpdateScale();

            // Zeroing input polling
            ZeroingController.Tick();
        }

        /// <summary>
        /// Force exit all mod effects (called on global toggle off or scene change).
        /// Clears cached optic so stale references don't ghost into the next session.
        /// </summary>
        public static void ForceExit()
        {
            if (_isScoped)
                DoScopeExit();
            _modBypassedForCurrentScope = false;
            // Always clear the last-enabled cache so a stale OpticSight reference
            // from before the disable doesn't get used on the next scope enter.
            _lastEnabledOptic = null;
        }

        /// <summary>
        /// Called when the mod is re-enabled at runtime.
        /// Immediately reads current PWA state so the mod catches up if the player
        /// is already scoped — without this, effects stay absent until the next
        /// scope enter/exit event fires.
        /// </summary>
        public static void SyncState()
        {
            // _lastEnabledOptic was cleared by ForceExit; if we're already scoped
            // FindOpticFromPWA() will locate the active one when DoScopeEnter fires.
            CheckAndUpdate("SyncState");
        }

        /// <summary>
        /// Adds/removes the current scope whitelist key in ScopeWhitelistNames.
        /// </summary>
        public static string GetActiveScopeWhitelistKey()
        {
            var os = _activeOptic != null ? _activeOptic : _lastEnabledOptic;
            return ResolveWhitelistScopeKey(os);
        }

        public static void ToggleActiveScopeWhitelistEntry()
        {
            var os = _activeOptic;
            if (os == null)
                os = _lastEnabledOptic;

            if (os == null)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo("[ScopeLifecycle] Whitelist toggle ignored: no active scope");
                return;
            }

            string scopeName = ResolveWhitelistScopeKey(os);
            if (string.IsNullOrWhiteSpace(scopeName))
            {
                ScopeHousingMeshSurgeryPlugin.LogWarn(
                    $"[ScopeLifecycle] Whitelist toggle ignored: no usable scope key for '{os.name}'");
                return;
            }

            RefreshScopeWhitelistCache();

            bool removed;
            if (_scopeWhitelistNames.Contains(scopeName))
            {
                _scopeWhitelistNames.Remove(scopeName);
                removed = true;
            }
            else
            {
                _scopeWhitelistNames.Add(scopeName);
                removed = false;
            }

            ScopeHousingMeshSurgeryPlugin.ScopeWhitelistNames.Value = string.Join(",", _scopeWhitelistNames);
            _scopeWhitelistRawCached = ScopeHousingMeshSurgeryPlugin.ScopeWhitelistNames.Value ?? string.Empty;

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] Whitelist {(removed ? "removed" : "added")}: scopeKey='{scopeName}'");

            if (ScopeHousingMeshSurgeryPlugin.ModEnabled.Value && _isScoped)
            {
                ForceExit();
                SyncState();
            }
        }
        private static string ResolveWhitelistScopeKey(OpticSight os)
        {
            if (os == null) return null;

            // Primary: derive from the object under mod_scope that is not a mount.
            string modScopeName = ResolveNameUnderModScope(os.transform);
            if (!string.IsNullOrWhiteSpace(modScopeName))
                return modScopeName;

            // Secondary: derive from resolved scope root/mode hierarchy, which helps when
            // the OpticSight component is placed outside the mod_scope subtree.
            string scopeRootName = ResolveNameFromScopeRoot(os.transform);
            if (!string.IsNullOrWhiteSpace(scopeRootName))
                return scopeRootName;

            // Fallback for runtimes where hierarchy naming is atypical.
            string templateName = FovController.GetOpticTemplateName(os);
            if (!string.IsNullOrWhiteSpace(templateName)
                && !string.Equals(templateName, "unknown", StringComparison.OrdinalIgnoreCase))
                return templateName;

            string templateId = FovController.GetOpticTemplateId(os);
            if (!string.IsNullOrWhiteSpace(templateId)
                && !string.Equals(templateId, "unknown", StringComparison.OrdinalIgnoreCase))
                return templateId;

            if (!string.IsNullOrWhiteSpace(os.name)
                && IsUsableScopeNameNode(os.name))
                return NormalizeScopeKey(os.name);

            return null;
        }

        private static string ResolveNameUnderModScope(Transform opticTransform)
        {
            if (opticTransform == null) return null;

            Transform modScope = null;
            for (var t = opticTransform; t != null; t = t.parent)
            {
                if (ContainsCI(t.name, "mod_scope"))
                {
                    modScope = t;
                    break;
                }
            }

            if (modScope == null) return null;

            // Walk up the active optic path: pick first node under mod_scope that
            // is not a mount/mode container.
            for (var t = opticTransform; t != null && t != modScope; t = t.parent)
            {
                if (!IsUsableScopeNameNode(t.name)) continue;
                return NormalizeScopeKey(t.name);
            }

            // Some optics expose OpticSight directly on mod_scope. In that case there is
            // no usable parent path between opticTransform and mod_scope, so inspect the
            // immediate children and pick the best scope-like candidate.
            string childName = ResolveBestNameFromModScopeChildren(modScope);
            if (!string.IsNullOrWhiteSpace(childName))
                return childName;

            return null;
        }

        private static string ResolveNameFromScopeRoot(Transform opticTransform)
        {
            if (opticTransform == null) return null;

            Transform scopeRoot = ScopeHierarchy.FindScopeRoot(opticTransform);
            if (scopeRoot == null) return null;

            Transform activeMode = ScopeHierarchy.FindBestMode(scopeRoot);
            for (var t = activeMode != null ? activeMode : scopeRoot; t != null; t = t.parent)
            {
                if (!IsUsableScopeNameNode(t.name)) continue;
                return NormalizeScopeKey(t.name);
            }

            return null;
        }

        private static string ResolveBestNameFromModScopeChildren(Transform modScope)
        {
            if (modScope == null) return null;

            Transform best = null;
            int bestScore = int.MinValue;

            for (int i = 0; i < modScope.childCount; i++)
            {
                var child = modScope.GetChild(i);
                if (child == null || !IsUsableScopeNameNode(child.name))
                    continue;

                int score = 0;
                if (child.gameObject.activeInHierarchy) score += 25;
                if (HasDirectModeChild(child)) score += 40;
                if (ScopeHierarchy.FindDeepChild(child, "backLens") != null
                    || ScopeHierarchy.FindDeepChild(child, "backlens") != null)
                    score += 25;
                if (ScopeHierarchy.FindDeepChild(child, "optic_camera") != null)
                    score += 15;
                if (LooksLikeMountOrAdapterName(child.name))
                    score -= 100;

                if (score > bestScore)
                {
                    best = child;
                    bestScore = score;
                }
            }

            if (best == null) return null;

            string preferred = ResolvePreferredScopeName(best);
            if (!string.IsNullOrWhiteSpace(preferred))
                return preferred;

            return NormalizeScopeKey(best.name);
        }

        private static string ResolvePreferredScopeName(Transform root)
        {
            if (root == null) return null;

            Transform best = null;
            int bestScore = int.MinValue;

            var stack = new Stack<Transform>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var t = stack.Pop();
                if (t == null) continue;

                if (IsUsableScopeNameNode(t.name))
                {
                    int score = 0;
                    string n = t.name ?? string.Empty;
                    if (t.gameObject.activeInHierarchy) score += 20;
                    if (ContainsCI(n, "scope_")) score += 60;
                    if (ContainsCI(n, "sight") || ContainsCI(n, "optic")) score += 25;
                    if (ContainsCI(n, "eotech") || ContainsCI(n, "acog") || ContainsCI(n, "razor")
                        || ContainsCI(n, "vudu") || ContainsCI(n, "tango") || ContainsCI(n, "g33")) score += 20;
                    if (ContainsCI(n, "merge")) score -= 15;
                    if (HasDirectModeChild(t)) score += 25;

                    if (score > bestScore)
                    {
                        best = t;
                        bestScore = score;
                    }
                }

                for (int i = 0; i < t.childCount; i++)
                    stack.Push(t.GetChild(i));
            }

            return best != null ? NormalizeScopeKey(best.name) : null;
        }

        private static bool HasDirectModeChild(Transform t)
        {
            if (t == null) return false;
            for (int i = 0; i < t.childCount; i++)
            {
                var c = t.GetChild(i);
                if (c == null || string.IsNullOrWhiteSpace(c.name))
                    continue;

                if (c.name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)
                    || c.name.Equals("mode", StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        private static bool IsUsableScopeNameNode(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            if (ContainsCI(name, "mount")) return false;
            if (LooksLikeMountOrAdapterName(name)) return false;
            if (LooksLikeWeaponContainerName(name)) return false;
            if (ContainsCI(name, "mod_scope")) return false;
            if (name.StartsWith("mod_", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.StartsWith("mode_", StringComparison.OrdinalIgnoreCase)) return false;
            if (name.Equals("mode", StringComparison.OrdinalIgnoreCase)) return false;
            return true;
        }

        private static bool LooksLikeMountOrAdapterName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            return ContainsCI(name, "riser")
                || ContainsCI(name, "adapter")
                || ContainsCI(name, "bracket")
                || ContainsCI(name, "rail")
                || ContainsCI(name, "ring")
                || ContainsCI(name, "base");
        }

        private static bool LooksLikeWeaponContainerName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;

            return ContainsCI(name, "receiver")
                || ContainsCI(name, "weapon_root")
                || ContainsCI(name, "weapon_")
                || ContainsCI(name, "handguard")
                || ContainsCI(name, "barrel")
                || name.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_LOD1", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_LOD2", StringComparison.OrdinalIgnoreCase)
                || name.EndsWith("_LOD3", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScopeKey(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string key = raw.Trim();

            bool changed;
            do
            {
                changed = false;
                if (key.EndsWith("(Clone)", StringComparison.OrdinalIgnoreCase))
                {
                    key = key.Substring(0, key.Length - "(Clone)".Length).Trim();
                    changed = true;
                }
                if (key.EndsWith("(merge)", StringComparison.OrdinalIgnoreCase))
                {
                    key = key.Substring(0, key.Length - "(merge)".Length).Trim();
                    changed = true;
                }
            }
            while (changed && key.Length > 0);

            return string.IsNullOrWhiteSpace(key) ? null : key;
        }

        private static bool ContainsCI(string s, string token)
        {
            if (s == null || token == null) return false;
            return s.IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool ShouldBypassForCurrentOptic(OpticSight os, float minFov)
        {
            _ = minFov;
            if (os == null) return false;

            if (ShouldBypassByWhitelist(os))
                return true;

            if (ScopeHousingMeshSurgeryPlugin.AutoDisableForVariableScopes.Value
                && (FovController.IsOpticAdjustable(os) || IsThermalOrNightVisionOptic(os)))
                return true;

            if (ScopeNameMatchesBypassPattern(os))
                return true;

            return false;
        }

        private static bool ScopeNameMatchesBypassPattern(OpticSight os)
        {
            if (os == null) return false;
            string raw = ScopeHousingMeshSurgeryPlugin.AutoBypassNameContains?.Value;
            if (string.IsNullOrWhiteSpace(raw)) return false;

            string scopeKey   = ResolveWhitelistScopeKey(os) ?? string.Empty;
            string objectName = os.name ?? string.Empty;

            foreach (string token in raw.Split(new[] { ',', ';', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string t = token.Trim();
                if (string.IsNullOrEmpty(t)) continue;
                if (ContainsCI(scopeKey, t) || ContainsCI(objectName, t))
                {
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ScopeLifecycle] AutoBypassNameContains match: token='{t}'" +
                        $" objectName='{objectName}' scopeKey='{scopeKey}'");
                    return true;
                }
            }
            return false;
        }

        private static bool ShouldBypassByWhitelist(OpticSight os)
        {
            RefreshScopeWhitelistCache();
            if (_scopeWhitelistNames.Count == 0)
                return false;

            string scopeName = ResolveWhitelistScopeKey(os);
            bool allowed = !string.IsNullOrEmpty(scopeName)
                && !string.Equals(scopeName, "unknown", StringComparison.OrdinalIgnoreCase)
                && _scopeWhitelistNames.Contains(scopeName);

            if (!allowed)
            {
                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[ScopeLifecycle] Whitelist bypass: '{os.name}'[scopeKey={scopeName}] is not in ScopeWhitelistNames");
            }

            return !allowed;
        }

        private static void RefreshScopeWhitelistCache()
        {
            string raw = ScopeHousingMeshSurgeryPlugin.ScopeWhitelistNames.Value ?? string.Empty;
            if (string.Equals(raw, _scopeWhitelistRawCached, StringComparison.Ordinal))
                return;

            _scopeWhitelistRawCached = raw;
            _scopeWhitelistNames.Clear();

            var parts = raw.Split(new[] { ',', ';', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                string id = part.Trim();
                if (!string.IsNullOrEmpty(id))
                    _scopeWhitelistNames.Add(id);
            }
        }

        private static bool IsThermalOrNightVisionOptic(OpticSight os)
        {
            if (os == null) return false;

            try
            {
                if (!_scopeDataMembersSearched)
                    DiscoverScopeDataMembers();

                if (_scopeDataType == null)
                    return false;

                Component scopeData = os.GetComponentInParent(_scopeDataType);
                if (scopeData == null)
                    scopeData = os.GetComponentInChildren(_scopeDataType, true);

                // ScopeData may live as a sibling under scope root (not direct parent/child of OpticSight).
                if (scopeData == null)
                {
                    Transform scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                    if (scopeRoot != null)
                    {
                        scopeData = scopeRoot.GetComponentInChildren(_scopeDataType, true);
                        if (scopeData == null && scopeRoot.parent != null)
                            scopeData = scopeRoot.parent.GetComponentInChildren(_scopeDataType, true);
                    }
                }

                if (scopeData == null)
                    return false;

                bool hasNightVision = HasScopeDataReference(
                    scopeData,
                    _scopeDataNightVisionField,
                    _scopeDataNightVisionProp);

                bool hasThermal = HasScopeDataReference(
                    scopeData,
                    _scopeDataThermalField,
                    _scopeDataThermalProp);

                if (hasNightVision || hasThermal)
                {
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ScopeLifecycle] Thermal/NV auto-bypass match: nightVision={hasNightVision} thermal={hasThermal}");
                    return true;
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeLifecycle] Thermal/NV detection failed: {ex.Message}");
            }

            return false;
        }

        private static bool HasScopeDataReference(Component scopeData, FieldInfo field, PropertyInfo prop)
        {
            try
            {
                object value = null;

                if (field != null)
                    value = field.GetValue(scopeData);
                else if (prop != null)
                    value = prop.GetValue(scopeData, null);

                // In Unity-serialized ScopeData, non-zero m_PathID manifests as a non-null object reference.
                return value != null;
            }
            catch
            {
                return false;
            }
        }

        private static void DiscoverScopeDataMembers()
        {
            _scopeDataMembersSearched = true;

            try
            {
                _scopeDataType = AccessTools.TypeByName("EFT.CameraControl.ScopeData");
                if (_scopeDataType == null) return;

                var flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
                _scopeDataNightVisionField = _scopeDataType.GetField("NightVisionData", flags);
                _scopeDataThermalField = _scopeDataType.GetField("ThermalVisionData", flags);

                if (_scopeDataNightVisionField == null)
                    _scopeDataNightVisionProp = _scopeDataType.GetProperty("NightVisionData", flags);

                if (_scopeDataThermalField == null)
                    _scopeDataThermalProp = _scopeDataType.GetProperty("ThermalVisionData", flags);
            }
            catch
            {
                _scopeDataType = null;
            }
        }

        // ===== State transitions =====

        private static void ApplyBypassState(OpticSight os, float minFov, string reason)
        {
            string opticName = os != null ? os.name : "null";
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] Bypassing mod for current scope ({reason}): " +
                $"'{opticName}'[{FovController.GetOpticTemplateId(os)}] " +
                $"key='{ResolveWhitelistScopeKey(os)}' minFov={minFov:F2}° adjustable={FovController.IsOpticAdjustable(os)}");

            // Ensure this path behaves like a full unscope cleanup so non-whitelisted
            // optics are truly vanilla while still staying in ADS.
            RestoreFov();
            Patches.WeaponScalingPatch.RestoreScale();
            ZoomController.Restore();
            ZoomController.ResetScrollZoom();
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();
            LensTransparency.RestoreAll();
            CameraSettingsManager.Restore();
            PiPDisabler.RestoreAllCameras();

            if (ScopeHousingMeshSurgeryPlugin.GetRestoreOnUnscope())
            {
                if (os != null)
                    MeshSurgeryManager.RestoreForScope(os.transform);
                else
                    MeshSurgeryManager.RestoreAll();
            }

            PlaneVisualizer.Hide();
            ZeroingController.Reset();
        }

        private static void DoScopeEnter()
        {
            // Get the OpticSight — cached from OnEnable, or find from pwa
            var os = _lastEnabledOptic;
            if (os == null)
                os = FindOpticFromPWA();
            if (os == null)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    "[ScopeLifecycle] ENTER aborted — no OpticSight found");
                return;
            }

            _isScoped = true;
            _activeOptic = os;
            PerScopeMeshSurgerySettings.SetActiveScope(ResolveWhitelistScopeKey(os));

            float minFov = ZoomController.GetMinFov(os);
            _modBypassedForCurrentScope = ShouldBypassForCurrentOptic(os, minFov);
            if (_modBypassedForCurrentScope)
            {
                ApplyBypassState(os, minFov, reason: "scope enter");
                return;
            }

            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] ENTER: '{os.name}'[{FovController.GetOpticTemplateId(os)}] frame={Time.frameCount}");

            // 1. Restore any black lens materials so ExtractReticle can read OpticSight textures.
            //    (RestoreAll on previous scope-exit may have left sharedMaterials as Unlit/Color.)
            LensTransparency.RestoreBlackLensMaterials();

            // 2. Extract reticle texture BEFORE destroying lens mesh
            ReticleRenderer.ExtractReticle(os);

            // 3. Hide ALL lens surfaces in the scope hierarchy (once)
            LensTransparency.HideAllLensSurfaces(os);

            // 2b. Collect housing + weapon renderers for reticle stencil mask (lens surfaces
            //     are already empty-meshed above, so they won't end up in the list).
            ReticleRenderer.SetHousingRenderers(CollectStencilRenderers(os));

            // 3. Get magnification for reticle scaling and zoom
            float mag = ZoomController.GetMagnification(os);

            // 4. Show reticle overlay at the lens position, scaled for magnification
            ReticleRenderer.Show(os, mag);

            // 4b. Show lens vignette + scope shadow effects
            Transform lensT = os.LensRenderer != null ? os.LensRenderer.transform : os.transform;
            float baseSize = ScopeHousingMeshSurgeryPlugin.GetReticleBaseSize();
            if (baseSize < 0.001f) baseSize = ScopeHousingMeshSurgeryPlugin.GetCylinderRadius() * 2f;
            if (baseSize < 0.001f) baseSize = 0.030f;
            ScopeEffectsRenderer.Show(lensT, baseSize, mag);

            // 5. Mesh surgery (once)
            if (ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
                MeshSurgeryManager.ApplyForOptic(os);

            // 6. Show cut plane visualizer (even without mesh surgery, for debugging)
            if (ScopeHousingMeshSurgeryPlugin.GetShowCutPlane()
                && !ScopeHousingMeshSurgeryPlugin.EnableMeshSurgery.Value)
            {
                ShowPlaneOnly(os);
            }

            // 7. Swap main camera LOD/culling settings with scope camera settings
            CameraSettingsManager.ApplyForOptic(os);

            // 8. Capture weapon base scale/FOV BEFORE changing FOV (for weapon scaling compensation)
            Patches.WeaponScalingPatch.CaptureBaseState();

            // 9. Apply animated FOV zoom (uses FovAnimationDuration)
            ApplyFov(true);

            // 10. Read initial zeroing distance
            ZeroingController.ReadCurrentZeroing();
        }

        private static void DoScopeExit()
        {
            if (!_isScoped) return;

            var prevOptic = _activeOptic;
            ScopeHousingMeshSurgeryPlugin.LogInfo(
                $"[ScopeLifecycle] EXIT: '{(prevOptic != null ? prevOptic.name : "null")}'" +
                $"[{FovController.GetOpticTemplateId(prevOptic)}] frame={Time.frameCount}");

            _isScoped = false;
            _activeOptic = null;
            PerScopeMeshSurgerySettings.ClearActiveScope();

            // If this scope was bypassed, skip mod cleanup paths.
            if (_modBypassedForCurrentScope)
            {
                _modBypassedForCurrentScope = false;
                return;
            }

            // 1. Restore FOV with ADS-matched animation timing
            RestoreFov();

            // 1b. Restore normal weapon model scaling (after FOV is back to normal)
            Patches.WeaponScalingPatch.RestoreScale();

            // 2. Restore zoom controller
            ZoomController.Restore();

            // 2b. Reset cached zoom state
            ZoomController.ResetScrollZoom();

            // 3. Hide reticle overlay + scope effects
            ReticleRenderer.Cleanup();
            ScopeEffectsRenderer.Cleanup();

            // 4. Restore lens
            LensTransparency.RestoreAll();

            // 5. Restore camera LOD/culling settings
            CameraSettingsManager.Restore();

            // 6. Restore meshes
            if (ScopeHousingMeshSurgeryPlugin.GetRestoreOnUnscope())
            {
                if (prevOptic != null)
                    MeshSurgeryManager.RestoreForScope(prevOptic.transform);
                else
                    MeshSurgeryManager.RestoreAll();
            }

            // 7. Hide plane visualizer
            PlaneVisualizer.Hide();

            // 8. Reset zeroing state
            ZeroingController.Reset();
        }

        // ===== FOV Helpers =====

        /// <summary>
        /// Public entry point to re-apply FOV zoom.
        /// Called when scroll wheel changes magnification so the FOV updates
        /// to match the new zoom level without waiting for the next method_23 call.
        /// Uses a short duration for responsive feel.
        /// </summary>
        public static void ReapplyFov()
        {
            if (!_isScoped) return;
            if (_modBypassedForCurrentScope) return;
            ApplyFov(false); // false = short duration for scroll feel
        }

        /// <summary>
        /// Collects stencil-mask renderers: scope housing + (optionally) weapon body.
        /// Weapon renderers are only added when StencilIncludeWeaponMeshes is enabled.
        /// </summary>
        private static List<Renderer> CollectStencilRenderers(OpticSight os)
        {
            var housing = LensTransparency.CollectHousingRenderers(os);
            if (ScopeHousingMeshSurgeryPlugin.StencilIncludeWeaponMeshes != null
                && ScopeHousingMeshSurgeryPlugin.StencilIncludeWeaponMeshes.Value)
            {
                housing.AddRange(LensTransparency.CollectWeaponRenderers(os, housing));
            }
            return housing;
        }

        /// <summary>
        /// Apply the FOV zoom for the current scope, with configurable animation duration.
        /// isTransition=true uses FovAnimationDuration config (scope enter / mode switch).
        /// </summary>
        private static void ApplyFov(bool isTransition)
        {
            try
            {
                if (_modBypassedForCurrentScope) return;
                if (!ScopeHousingMeshSurgeryPlugin.EnableZoom.Value) return;
                if (!CameraClass.Exist) return;

                var player = GetLocalPlayer();
                if (player == null) return;
                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                float playerBaseFov = pwa.Single_2;
                float zoomBaseFov = FovController.ZoomBaselineFov;
                float zoomedFov = FovController.ComputeZoomedFov(playerBaseFov, pwa);

                if (zoomedFov >= 0.5f && zoomedFov < zoomBaseFov)
                {
                    float duration = isTransition
                        ? ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value
                        : 0.1f; // Short duration for variable zoom updates

                    CameraClass.Instance.SetFov(zoomedFov, duration, false);
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ScopeLifecycle] ApplyFov: {zoomedFov:F1}° dur={duration:F2}s");
                }
                else if (isTransition && zoomedFov >= zoomBaseFov)
                {
                    // High-to-low mode switch where new mode has no zoom:
                    // restore to baseline with configured duration so both directions are consistent
                    float duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                    CameraClass.Instance.SetFov(zoomBaseFov, duration, false);
                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[ScopeLifecycle] ApplyFov (restore baseline): {zoomBaseFov:F1}° dur={duration:F2}s");
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeLifecycle] ApplyFov error: {ex.Message}");
            }
        }

        /// <summary>
        /// Restore FOV to baseline using the same ADS animation timing.
        /// </summary>
        private static void RestoreFov()
        {
            try
            {
                if (!CameraClass.Exist) return;
                var cc = CameraClass.Instance;
                if (cc == null) return;

                var player = GetLocalPlayer();
                if (player == null) return;
                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                float baseFov = pwa.Single_2;
                if (baseFov > 30f)
                {
                float duration = ScopeHousingMeshSurgeryPlugin.FovAnimationDuration.Value;
                    cc.SetFov(baseFov, duration, true);
                    ScopeHousingMeshSurgeryPlugin.LogVerbose(
                        $"[ScopeLifecycle] RestoreFov: {baseFov:F1}° dur={duration:F2}s");

                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[ScopeLifecycle] RestoreFov error: {ex.Message}");
            }
        }

        // ===== Other Helpers =====

        /// <summary>
        /// Computes the cut plane and shows the visualizer without actually cutting.
        /// </summary>
        private static void ShowPlaneOnly(OpticSight os)
        {
            try
            {
                var scopeRoot = ScopeHierarchy.FindScopeRoot(os.transform);
                if (scopeRoot == null) return;

                Transform activeMode = os.transform;
                if (!ScopeHierarchy.TryGetPlane(os, scopeRoot, activeMode,
                    out var planePoint, out var planeNormal, out var camPos))
                    return;

                planePoint += planeNormal * ScopeHousingMeshSurgeryPlugin.GetPlaneOffsetMeters();
                PlaneVisualizer.Show(planePoint, planeNormal);
            }
            catch { }
        }

        private static Player GetLocalPlayer()
        {
            try
            {
                var gw = Singleton<GameWorld>.Instance;
                return gw != null ? gw.MainPlayer : null;
            }
            catch { return null; }
        }

        /// <summary>
        /// Fallback: find OpticSight if _lastEnabledOptic wasn't cached.
        /// Uses FindObjectsOfType as last resort.
        /// </summary>
        private static OpticSight FindOpticFromPWA()
        {
            try
            {
                var all = UnityEngine.Object.FindObjectsOfType<OpticSight>();
                foreach (var os in all)
                {
                    if (os != null && os.isActiveAndEnabled)
                        return os;
                }
                foreach (var os in all)
                {
                    if (os != null) return os;
                }
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Finds an enabled OpticSight from cached references only.
        /// No scene-wide FindObjectsOfType — those cause multi-ms hitches.
        /// The OnEnable/OnDisable patches keep _lastEnabledOptic warm.
        /// </summary>
        private static OpticSight FindEnabledOpticFromPWA()
        {
            if (_activeOptic != null && _activeOptic.isActiveAndEnabled)
                return _activeOptic;

            if (_lastEnabledOptic != null && _lastEnabledOptic.isActiveAndEnabled)
                return _lastEnabledOptic;

            // Fallback: probe OpticComponentUpdater instances for a currently bound optic.
            // Some runtime paths briefly miss OnEnable cache updates, but updater binding
            // can still hold a valid OpticSight reference (seen in diagnostics as optic='mode_000').
            var fromUpdaters = FindEnabledOpticFromUpdaters();
            if (fromUpdaters != null)
                return fromUpdaters;

            // During rapid transitions (mode switch), the incoming optic may not
            // be marked enabled yet. Trust cache/updater binding rather than full scene scans.
            return null;
        }

        private static OpticSight FindEnabledOpticFromUpdaters()
        {
            try
            {
                var field = PiPDisabler.GetOpticSightField();
                if (field == null) return null;

                var all = Resources.FindObjectsOfTypeAll<MonoBehaviour>();
                for (int i = 0; i < all.Length; i++)
                {
                    var mb = all[i];
                    if (mb == null || mb.GetType().Name != "OpticComponentUpdater")
                        continue;

                    OpticSight os = null;
                    try { os = field.GetValue(mb) as OpticSight; } catch { }
                    if (os == null || !os.isActiveAndEnabled)
                        continue;

                    _lastEnabledOptic = os;
                    return os;
                }
            }
            catch { }

            return null;
        }

    }
}
