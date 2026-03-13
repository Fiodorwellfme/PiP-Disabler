using System;
using System.Reflection;
using EFT.CameraControl;
using HarmonyLib;
using SPT.Reflection.Patching;
using UnityEngine;

namespace PiPDisabler.Patches
{
    internal static class UpscalerFastSwitch
    {
        public static bool TryFastSwitchDlss(object cameraClass, object targetMode)
        {
            if (cameraClass == null || targetMode == null) return false;
            if (ToEnumInt(targetMode) <= 0) return false;

            var ssaaObj = GetMemberValue(cameraClass, "Ssaa_0", "SSAA", "ssaa_0");
            var ssaaImpl = GetMemberValue(cameraClass, "Ssaaimpl_0", "ssaaimpl_0");
            if (ssaaObj == null || ssaaImpl == null) return false;
            if (!InvokeBool(ssaaObj, "UsesDLSSUpscaler")) return false;

            int quality = MapDlssQuality(targetMode);
            if (quality < 0) return false;

            SetMemberValue(ssaaImpl, quality, "DLSSQualityNext");
            SetMemberValue(ssaaImpl, true, "EnableDLSS");

            int outW = InvokeInt(ssaaObj, "GetOutputWidth");
            int outH = InvokeInt(ssaaObj, "GetOutputHeight");
            if (outW <= 0 || outH <= 0) return false;

            float ratio = InvokeFloat(ssaaImpl, "ComputeOptimalResamplingFactor", outW, outH, quality);
            if (ratio <= 0f) return false;

            SetMemberValue(ssaaObj, true, "UseJitter");
            InvokeVoid(ssaaObj, "Switch", ratio);
            UpdateDlssShaderParams(ratio);
            InvokeVoid(cameraClass, "method_3");
            return true;
        }

        public static bool TryFastSwitchFsr2(object cameraClass, object targetMode)
            => TryFastSwitchFsr(cameraClass, targetMode, "UsesFSR2Upscaler", "EnableFSR2", "EnableFSR3", GetFsrRatio(targetMode));

        public static bool TryFastSwitchFsr3(object cameraClass, object targetMode)
            => TryFastSwitchFsr(cameraClass, targetMode, "UsesFSR3Upscaler", "EnableFSR3", "EnableFSR2", GetFsrRatio(targetMode));

        private static bool TryFastSwitchFsr(object cameraClass, object targetMode, string familyCheckMethod, string enableThis, string disableOther, float ratio)
        {
            if (cameraClass == null || targetMode == null) return false;
            if (ToEnumInt(targetMode) <= 0 || ratio <= 0f) return false;

            var ssaaObj = GetMemberValue(cameraClass, "Ssaa_0", "SSAA", "ssaa_0");
            var ssaaImpl = GetMemberValue(cameraClass, "Ssaaimpl_0", "ssaaimpl_0");
            if (ssaaObj == null || ssaaImpl == null) return false;
            if (!InvokeBool(ssaaObj, familyCheckMethod)) return false;

            SetHalfVolumetricLights(cameraClass);

            SetMemberValue(ssaaImpl, false, "EnableDLSS");
            SetMemberValue(ssaaImpl, false, "EnableFSR");
            SetMemberValue(ssaaImpl, false, disableOther);
            SetMemberValue(ssaaImpl, true, enableThis);

            SetMemberValue(ssaaObj, true, "UseJitter");
            InvokeVoid(ssaaObj, "Switch", ratio);
            InvokeVoid(cameraClass, "method_3");
            return true;
        }

        private static void UpdateDlssShaderParams(float ratio)
        {
            try
            {
                var int0 = AccessTools.Field(typeof(CameraClass), "Int_0")?.GetValue(null);
                if (int0 is int shaderId)
                {
                    float x = Mathf.Log(ratio, 2f) - 1f;
                    Shader.SetGlobalVector(shaderId, new Vector4(x, 1f, 2f, 3f));
                }
            }
            catch { }
        }

        private static void SetHalfVolumetricLights(object cameraClass)
        {
            try
            {
                var vol = GetMemberValue(cameraClass, "VolumetricLightRenderer_0", "volumetricLightRenderer_0");
                if (vol == null) return;

                var member = AccessTools.Property(vol.GetType(), "Resolution") as MemberInfo
                             ?? AccessTools.Field(vol.GetType(), "Resolution");
                if (member == null) return;

                var enumType = member is PropertyInfo pi ? pi.PropertyType : ((FieldInfo)member).FieldType;
                var halfValue = Enum.Parse(enumType, "Half", true);

                if (member is PropertyInfo prop) prop.SetValue(vol, halfValue, null);
                else if (member is FieldInfo field) field.SetValue(vol, halfValue);
            }
            catch { }
        }

        private static float GetFsrRatio(object targetMode)
        {
            if (targetMode == null) return -1f;
            switch (targetMode.ToString())
            {
                case "Quality": return 0.6666667f;
                case "Balanced": return 0.58823526f;
                case "Performance": return 0.5f;
                case "UltraPerformance": return 0.33333334f;
                default: return -1f;
            }
        }

        private static int MapDlssQuality(object targetMode)
        {
            if (targetMode == null) return -1;
            switch (targetMode.ToString())
            {
                case "Performance": return 0;
                case "Balanced": return 1;
                case "Quality": return 2;
                case "UltraPerformance": return 3;
                default: return -1;
            }
        }

        private static int ToEnumInt(object value)
        {
            try { return Convert.ToInt32(value); }
            catch { return -1; }
        }

        private static object GetMemberValue(object instance, params string[] names)
        {
            if (instance == null) return null;
            var type = instance.GetType();
            foreach (var name in names)
            {
                var prop = AccessTools.Property(type, name);
                if (prop != null) return prop.GetValue(instance, null);

                var field = AccessTools.Field(type, name);
                if (field != null) return field.GetValue(instance);
            }
            return null;
        }

        private static void SetMemberValue(object instance, object value, params string[] names)
        {
            if (instance == null) return;
            var type = instance.GetType();
            foreach (var name in names)
            {
                var prop = AccessTools.Property(type, name);
                if (prop != null && prop.CanWrite)
                {
                    prop.SetValue(instance, value, null);
                    return;
                }

                var field = AccessTools.Field(type, name);
                if (field != null)
                {
                    field.SetValue(instance, value);
                    return;
                }
            }
        }

        private static bool InvokeBool(object instance, string method)
        {
            try
            {
                var mi = AccessTools.Method(instance.GetType(), method);
                if (mi == null) return false;
                return mi.Invoke(instance, null) is bool b && b;
            }
            catch { return false; }
        }

        private static int InvokeInt(object instance, string method)
        {
            try
            {
                var mi = AccessTools.Method(instance.GetType(), method);
                if (mi == null) return 0;
                var value = mi.Invoke(instance, null);
                return Convert.ToInt32(value);
            }
            catch { return 0; }
        }

        private static float InvokeFloat(object instance, string method, params object[] args)
        {
            try
            {
                var mi = AccessTools.Method(instance.GetType(), method, new[] { typeof(int), typeof(int), typeof(int) });
                if (mi == null) return -1f;
                var value = mi.Invoke(instance, args);
                return Convert.ToSingle(value);
            }
            catch { return -1f; }
        }

        private static void InvokeVoid(object instance, string method, params object[] args)
        {
            try
            {
                var argTypes = new Type[args.Length];
                for (int i = 0; i < args.Length; i++) argTypes[i] = args[i].GetType();
                var mi = AccessTools.Method(instance.GetType(), method, argTypes) ?? AccessTools.Method(instance.GetType(), method);
                mi?.Invoke(instance, args);
            }
            catch { }
        }
    }

    internal static class UpscalerPatchHelpers
    {
        public static object GetCameraClass(object instance)
        {
            if (instance == null) return null;
            return AccessTools.Property(instance.GetType(), "CameraClass")?.GetValue(instance, null)
                   ?? AccessTools.Field(instance.GetType(), "cameraClass_0")?.GetValue(instance);
        }

        public static bool IsUsingFamily(object cameraClass, string method)
        {
            if (cameraClass == null) return false;
            var ssaaObj = AccessTools.Property(cameraClass.GetType(), "Ssaa_0")?.GetValue(cameraClass, null)
                          ?? AccessTools.Property(cameraClass.GetType(), "SSAA")?.GetValue(cameraClass, null)
                          ?? AccessTools.Field(cameraClass.GetType(), "ssaa_0")?.GetValue(cameraClass);
            if (ssaaObj == null) return false;

            try
            {
                var mi = AccessTools.Method(ssaaObj.GetType(), method);
                return mi != null && mi.Invoke(ssaaObj, null) is bool b && b;
            }
            catch { return false; }
        }

        public static bool IsModeOn(object mode) => mode != null && Convert.ToInt32(mode) > 0;
    }

    internal sealed class GClass1074DlssModePatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method("GClass1074:method_6");

        [PatchPrefix]
        private static bool Prefix(object __instance, object x)
        {
            var cam = UpscalerPatchHelpers.GetCameraClass(__instance);
            if (cam != null && UpscalerPatchHelpers.IsUsingFamily(cam, "UsesDLSSUpscaler") && UpscalerPatchHelpers.IsModeOn(x))
            {
                return !UpscalerFastSwitch.TryFastSwitchDlss(cam, x);
            }
            return true;
        }
    }

    internal sealed class GClass1074Fsr2AntiAliasingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method("GClass1074:method_7");

        [PatchPrefix]
        private static bool Prefix(object __instance, object x)
        {
            var cam = UpscalerPatchHelpers.GetCameraClass(__instance);
            if (cam != null && UpscalerPatchHelpers.IsUsingFamily(cam, "UsesFSR2Upscaler") && UpscalerPatchHelpers.IsModeOn(x))
            {
                return false;
            }
            return true;
        }
    }

    internal sealed class GClass1074Fsr2SetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method("GClass1074:method_9");

        [PatchPrefix]
        private static bool Prefix(object __instance, object x)
        {
            var cam = UpscalerPatchHelpers.GetCameraClass(__instance);
            if (cam != null && UpscalerPatchHelpers.IsUsingFamily(cam, "UsesFSR2Upscaler") && UpscalerPatchHelpers.IsModeOn(x))
            {
                UpscalerFastSwitch.TryFastSwitchFsr2(cam, x);
                return false;
            }
            return true;
        }
    }

    internal sealed class GClass1074Fsr3AntiAliasingPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method("GClass1074:method_8");

        [PatchPrefix]
        private static bool Prefix(object __instance, object x)
        {
            var cam = UpscalerPatchHelpers.GetCameraClass(__instance);
            if (cam != null && UpscalerPatchHelpers.IsUsingFamily(cam, "UsesFSR3Upscaler") && UpscalerPatchHelpers.IsModeOn(x))
            {
                return false;
            }
            return true;
        }
    }

    internal sealed class GClass1074Fsr3SetPatch : ModulePatch
    {
        protected override MethodBase GetTargetMethod() => AccessTools.Method("GClass1074:method_10");

        [PatchPrefix]
        private static bool Prefix(object __instance, object x)
        {
            var cam = UpscalerPatchHelpers.GetCameraClass(__instance);
            if (cam != null && UpscalerPatchHelpers.IsUsingFamily(cam, "UsesFSR3Upscaler") && UpscalerPatchHelpers.IsModeOn(x))
            {
                UpscalerFastSwitch.TryFastSwitchFsr3(cam, x);
                return false;
            }
            return true;
        }
    }
}
