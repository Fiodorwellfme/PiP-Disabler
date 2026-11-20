using BepInEx.Bootstrap;
using EFT;
using EFT.CameraControl;
using UnityEngine;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// 7Bpencil's MagicOpticMount (MIT) renders a thermal/NV device mounted in front of a normal
    /// optic by switching the vanilla PiP optic camera to that device's ScopeData effects
    /// (OpticComponentUpdater.CopyComponentFromOptic postfix). PiP-Disabler replaces that camera,
    /// so the device's image was lost. When such an aligned device is on the weapon, the current
    /// optic is bypassed so vanilla PiP (and MagicOpticMount) renders it.
    ///
    /// The alignment test mirrors MagicOpticMount's own AreSightsAligned.
    /// </summary>
    internal static class MagicOpticMountCompat
    {
        private const string MagicOpticMountGuid = "7Bpencil.MagicOpticMount";
        private const float MaxAngle = 45f;
        private const float MaxDistance = 0.006f; // 6 mm

        private static bool? _installed;

        private static bool Installed
        {
            get
            {
                if (!_installed.HasValue)
                    _installed = Chainloader.PluginInfos.ContainsKey(MagicOpticMountGuid);
                return _installed.Value;
            }
        }

        public static bool ShouldBypass(OpticSight os)
        {
            if (os == null || !Installed) return false;

            try
            {
                var player = Helpers.GetLocalPlayer();
                var pwa = player?.ProceduralWeaponAnimation;
                if (pwa == null || pwa.ScopeAimTransforms == null || pwa.ScopeAimTransforms.Count == 0)
                    return false;

                // The device itself is thermal/NV — nothing for MagicOpticMount to add.
                if (IsSpecialOptic(os)) return false;

                Transform opticBone = FindBoneFor(pwa, os);
                if (opticBone == null) return false;

                var firearmController = player.HandsController as Player.FirearmController;
                var weaponPrefab = firearmController?.Firearms?.WeaponPrefab;
                var weaponRoot = weaponPrefab != null ? weaponPrefab.Hierarchy?.GetTransform(ECharacterWeaponBones.weapon) : null;
                if (weaponRoot == null) return false;
                Vector3 weaponForward = -weaponRoot.up;

                foreach (var sight in pwa.ScopeAimTransforms)
                {
                    if (sight == null || !sight.IsOptic || sight.Bone == null || sight.Bone == opticBone) continue;
                    var cache = sight.ScopePrefabCache;
                    if (cache == null) continue;

                    var other = cache.CurrentModOpticSight;
                    if (other == null || !IsSpecialOptic(other)) continue;

                    if (AreSightsAligned(opticBone, sight.Bone, weaponForward))
                    {
                        PiPDisablerPlugin.DebugLogInfo(
                            $"[MagicOpticMountCompat] '{os.name}' has an aligned thermal/NV device '{other.name}' in front — using vanilla PiP.");
                        return true;
                    }
                }
            }
            catch
            {
                // fall through: never block the mod on a failed probe
            }

            return false;
        }

        // ── Re-sync after a bypass ──────────────────────────────────────────
        // ApplyBypassState hands the optic back to vanilla with CopyComponentFromOptic. On a sight
        // switch that runs before EFT points pwa.CurrentScope at the new optic, so MagicOpticMount's
        // postfix skips it ("not us") and vanilla's part switches the device thermal off again.
        // Repeat the call once the current scope really is this optic.

        private const int ResyncWindowFrames = 120;
        private static OpticSight _pendingResync;
        private static int _resyncUntilFrame;

        public static void RequestResync(OpticSight os)
        {
            if (os == null || !Installed) return;
            _pendingResync = os;
            _resyncUntilFrame = Time.frameCount + ResyncWindowFrames;
        }

        public static void Tick()
        {
            var os = _pendingResync;
            if (os == null) return;

            if (Time.frameCount > _resyncUntilFrame || !ScopeLifecycle.IsModBypassedForCurrentScope || !os.enabled)
            {
                _pendingResync = null;
                return;
            }

            try
            {
                var pwa = Helpers.GetLocalPlayer()?.ProceduralWeaponAnimation;
                var current = pwa?.CurrentScope;
                if (current == null || !current.IsOptic) return;
                var cache = current.ScopePrefabCache;
                if (cache == null || cache.CurrentModOpticSight != os) return;

                _pendingResync = null;
                if (!ShouldBypass(os)) return;

                var updater = CameraManager.Exist ? CameraManager.Instance?.OpticCameraManager?.Updater : null;
                if (updater == null) return;
                updater.CopyComponentFromOptic(os);
                PiPDisablerPlugin.DebugLogInfo($"[MagicOpticMountCompat] Re-synced optic camera for '{os.name}' (device effect re-applied).");
            }
            catch (System.Exception ex)
            {
                _pendingResync = null;
                PiPDisablerPlugin.DebugLogInfo($"[MagicOpticMountCompat] Re-sync failed: {ex.Message}");
            }
        }

        private static bool IsSpecialOptic(OpticSight os)
        {
            var data = os.ScopeData;
            if (data == null) return false;
            return (data.ThermalVisionData != null && data.ThermalVisionData.ThermalVision)
                   || (data.NightVisionData != null && data.NightVisionData.NightVision);
        }

        private static Transform FindBoneFor(EFT.Animations.ProceduralWeaponAnimation pwa, OpticSight os)
        {
            foreach (var sight in pwa.ScopeAimTransforms)
            {
                var cache = sight?.ScopePrefabCache;
                if (cache == null) continue;
                for (int m = 0; m < cache.ModesCount; m++)
                {
                    if (cache.GetOpticSight(m) == os)
                        return sight.Bone;
                }
            }
            return pwa.CurrentScope?.Bone;
        }

        private static bool AreSightsAligned(Transform opticBone, Transform specialOpticBone, Vector3 weaponForward)
        {
            // device is in front of the optic
            if (Vector3.Angle(specialOpticBone.position - opticBone.position, weaponForward) > MaxAngle)
                return false;

            // and close to the optic's view axis
            return SqDistPointSegment(opticBone.position, opticBone.position + weaponForward, specialOpticBone.position)
                   <= MaxDistance * MaxDistance;
        }

        private static float SqDistPointSegment(Vector3 a, Vector3 b, Vector3 c)
        {
            Vector3 ab = b - a;
            Vector3 ac = c - a;
            float e = Vector3.Dot(ac, ab);
            if (e <= 0f) return Vector3.Dot(ac, ac);
            float f = Vector3.Dot(ab, ab);
            if (e >= f)
            {
                Vector3 bc = c - b;
                return Vector3.Dot(bc, bc);
            }
            return Vector3.Dot(ac, ac) - e * e / f;
        }
    }
}
