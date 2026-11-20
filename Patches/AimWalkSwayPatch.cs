using System.Reflection;
using EFT.Animations;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    // "Aim Walk Sway Reduction (%)": while aiming, walking bob (WalkEffector: camera + hands curves,
    // scaled by Intensity/Overweight) and movement inertia sway (MotionEffector movement part, scaled
    // by Intensity) are lowered by a flat percent. At high zoom the narrow FOV magnifies every bob,
    // which some players find nauseating. The intensities are scaled only for the call and restored
    // afterwards, so the game's own values are never changed. Mouse-look sway is not touched.
    // 2.8.3: also the whole-screen head bob — the camera follows the body animation by EFT's
    // "Head bobbing" game setting (ProceduralWeaponAnimation.FarAimPlane, the Lerp weight of
    // CameraAnimatedFP*TP in the camera rotation); that weight is scaled too.
    internal static class AimWalkSway
    {
        private static float _factor = 1f;
        private static int _frame = -1;
        private static float _lastTime;

        /// <summary>Multiplier for walk/move intensity now (1 = vanilla), eased over ~0.2 s.</summary>
        internal static float Factor()
        {
            if (_frame == Time.frameCount) return _factor;
            _frame = Time.frameCount;
            float target = 1f;
            int pct = Settings.AimWalkSwayReduction != null ? Settings.AimWalkSwayReduction.Value : 0;
            bool on = WeaponMotionSuppressionState.ShouldApply(true)
                      || (Settings.HeadBobReductionAlways.Value && Settings.ModEnabled.Value);
            if (pct > 0 && on)
                target = 1f - Mathf.Clamp(pct, 0, 100) / 100f;
            // Ease in/out so aiming in or out mid-step does not jerk the camera.
            // Real time since the last call, so a pause in walking does not leave a stale value.
            float now = Time.realtimeSinceStartup;
            _factor = Mathf.MoveTowards(_factor, target, Mathf.Clamp(now - _lastTime, 0f, 1f) * 5f);
            _lastTime = now;
            return _factor;
        }
    }

    internal sealed class AimWalkSwayWalkPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(WalkEffector), nameof(WalkEffector.Process), new[] { typeof(float) });

        [PatchPrefix]
        private static void Prefix(WalkEffector __instance, out Vector2 __state)
        {
            __state = new Vector2(float.NaN, 0f);
            float f = AimWalkSway.Factor();
            if (f >= 0.999f) return;
            __state = new Vector2(__instance.Intensity, __instance.Overweight);
            __instance.Intensity *= f;
            __instance.Overweight *= f;
        }

        [PatchFinalizer]
        private static void Finalizer(WalkEffector __instance, Vector2 __state)
        {
            if (float.IsNaN(__state.x)) return;
            __instance.Intensity = __state.x;
            __instance.Overweight = __state.y;
        }
    }

    internal sealed class AimWalkSwayMotionPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.Method(typeof(MotionEffector), nameof(MotionEffector.Process), new[] { typeof(float) });

        [PatchPrefix]
        private static void Prefix(MotionEffector __instance, out float __state)
        {
            __state = float.NaN;
            float f = AimWalkSway.Factor();
            if (f >= 0.999f) return;
            __state = __instance.Intensity;
            __instance.Intensity *= f;
        }

        [PatchFinalizer]
        private static void Finalizer(MotionEffector __instance, float __state)
        {
            if (float.IsNaN(__state)) return;
            __instance.Intensity = __state;
        }
    }

    internal sealed class AimHeadBobCameraPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod()
            => AccessTools.PropertyGetter(typeof(ProceduralWeaponAnimation), "FarAimPlane");

        [PatchPostfix]
        private static void Postfix(ref float __result)
        {
            float f = AimWalkSway.Factor();
            if (f < 0.999f) __result *= f;
        }
    }
}
