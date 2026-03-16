using System.Reflection;
using EFT;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;

namespace PiPDisabler.Patches
{
    /// <summary>
    /// Keeps scoped FOV stable while ADS freelook is active and temporarily
    /// restores uncut meshes to avoid clipping while looking around.
    /// </summary>
    internal sealed class FreelookFOVpatch : ModulePatch
    {
        private static bool _freelookActive;
        private static float _cachedScopeFov;
        private static OpticSight _cachedOptic;

        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(Player), nameof(Player.Look),
                new[] { typeof(float), typeof(float), typeof(bool) });

        [PatchPrefix]
        private static void Prefix(Player __instance, ref bool __state)
        {
            __state = __instance != null && __instance.MouseLookControl;
        }

        [PatchPostfix]
        private static void Postfix(Player __instance, bool __state)
        {
            if (__instance == null)
            {
                ResetState();
                return;
            }

            bool isFreelookNow = __instance.MouseLookControl;
            bool enteredFreelook = !__state && isFreelookNow;
            bool exitedFreelook = __state && !isFreelookNow;

            bool scopedAndAiming = ScopeLifecycle.IsScoped
                                   && !ScopeLifecycle.IsModBypassedForCurrentScope
                                   && __instance.ProceduralWeaponAnimation != null
                                   && __instance.ProceduralWeaponAnimation.IsAiming;

            if (!scopedAndAiming)
            {
                if (_freelookActive)
                    ResetState();
                return;
            }

            var activeOptic = ScopeLifecycle.ActiveOptic;
            if (activeOptic == null)
            {
                return;
            }

            if (enteredFreelook)
            {
                _freelookActive = true;
                _cachedOptic = activeOptic;

                if (CameraClass.Exist && CameraClass.Instance != null)
                    _cachedScopeFov = CameraClass.Instance.Fov;

                if (PiPDisablerPlugin.EnableMeshSurgery.Value)
                    MeshSurgeryManager.RestoreForScope(activeOptic.transform);
            }

            if (exitedFreelook && _freelookActive)
            {
                if (CameraClass.Exist && CameraClass.Instance != null && _cachedScopeFov > 0.1f)
                    CameraClass.Instance.SetFov(_cachedScopeFov, 0.05f, true);

                if (PiPDisablerPlugin.EnableMeshSurgery.Value)
                {
                    if (_cachedOptic != null)
                        MeshSurgeryManager.ApplyForOptic(_cachedOptic);
                    else
                        MeshSurgeryManager.ApplyForOptic(activeOptic);
                }

                ResetState();
            }
        }

        private static void ResetState()
        {
            _freelookActive = false;
            _cachedScopeFov = 0f;
            _cachedOptic = null;
        }
    }
}
