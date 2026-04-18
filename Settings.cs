using BepInEx.Configuration;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PiPDisabler
{
    public static class Settings
    {

        // --- 0. Global ---
        public static ConfigEntry<bool> ModEnabled;
        public static ConfigEntry<KeyCode> ModToggleKey;
        public static ConfigEntry<bool> AutoSwitchReticleRenderForNvg;

        // --- General ---
        public static ConfigEntry<bool> AutoDisableForVariableScopes;
        public static ConfigEntry<string> AutoBypassNameContains;
        public static ConfigEntry<string> ScopeWhitelistNames;
        public static ConfigEntry<KeyCode> ScopeWhitelistToggleEntryKey;

        // --- Optimization ---
        public static ConfigEntry<float> AutoLodBiasMultiplier;

        // --- Mesh Surgery ---
        public static ConfigEntry<bool> EnableMeshSurgery;
        public static ConfigEntry<KeyCode> MeshSurgeryToggleKey;
        public static ConfigEntry<float> ReloadMeshResumeDelaySeconds;
        public static ConfigEntry<float> PlaneOffsetMeters;
        public static ConfigEntry<string> PlaneNormalAxis;
        public static ConfigEntry<float> Plane1Radius;
        public static ConfigEntry<float> Plane1OffsetMeters;
        public static ConfigEntry<float> Plane2Position;
        public static ConfigEntry<float> Plane2Radius;
        public static ConfigEntry<float> Plane3Position;
        public static ConfigEntry<float> Plane3Radius;
        public static ConfigEntry<float> Plane4Position;
        public static ConfigEntry<float> Plane4Radius;
        public static ConfigEntry<float> CutStartOffset;
        public static ConfigEntry<float> CutLength;
        public static ConfigEntry<float> NearPreserveDepth;
        public static ConfigEntry<float> ReticleBaseSize;
        public static ConfigEntry<bool> ExpandSearchToWeaponRoot;
        public static ConfigEntry<bool> DebugShowHousingMask;
        public static ConfigEntry<bool> StencilIncludeWeaponMeshes;

        // --- Custom Mesh Surgery settings (per-scope authoring) ---
        public static ConfigEntry<KeyCode> SaveCustomMeshSurgerySettingsKey;
        public static ConfigEntry<KeyCode> DeleteCustomMeshSurgerySettingsKey;
        public static ConfigEntry<float> CustomPlaneOffsetMeters;
        public static ConfigEntry<float> CustomCutRadius;
        public static ConfigEntry<float> CustomPlane1Radius;
        public static ConfigEntry<float> CustomPlane1OffsetMeters;
        public static ConfigEntry<float> CustomPlane2Position;
        public static ConfigEntry<float> CustomPlane2Radius;
        public static ConfigEntry<float> CustomPlane3Position;
        public static ConfigEntry<float> CustomPlane3Radius;
        public static ConfigEntry<float> CustomPlane4Position;
        public static ConfigEntry<float> CustomPlane4Radius;
        public static ConfigEntry<float> CustomCutStartOffset;
        public static ConfigEntry<float> CustomCutLength;
        public static ConfigEntry<float> CustomNearPreserveDepth;
        public static ConfigEntry<float> CustomReticleBaseSize;
        public static ConfigEntry<bool> CustomExpandSearchToWeaponRoot;

        // --- Scope Effects ---
        public static ConfigEntry<bool>  VignetteEnabled;
        public static ConfigEntry<float> VignetteOpacity;
        public static ConfigEntry<float> VignetteSizeMult;
        public static ConfigEntry<float> VignetteSoftness;
        public static ConfigEntry<bool>  ScopeShadowEnabled;
        public static ConfigEntry<bool>  DebugShowScopeShadowMask;
        public static ConfigEntry<bool>  ScopeShadowPersistOnUnscope;
        public static ConfigEntry<float> ScopeShadowOpacity;
        public static ConfigEntry<float> ScopeShadowRadius;
        public static ConfigEntry<float> ScopeShadowSoftness;

        // --- Weapon Scaling ---
        public static ConfigEntry<bool> EnableWeaponScaling;
        public static ConfigEntry<float> BaselineWeaponScale;
        public static ConfigEntry<float> WeaponScaleStrength;
        // --- Zoom / FOV ---
        public static ConfigEntry<bool> EnableZoom;
        public static ConfigEntry<float> DefaultZoom;
        public static ConfigEntry<float> BaselineFOV;
        public static ConfigEntry<bool> AutoFovFromScope;
        public static ConfigEntry<float> ScopedFov;
        public static ConfigEntry<float> FovAnimationDuration;
        public static ConfigEntry<KeyCode> ZoomToggleKey;
        public static ConfigEntry<float> ManualLodBias;
        public static ConfigEntry<int> ManualMaximumLodLevel;
        public static ConfigEntry<float> ManualCullingMultiplier;
        public static Dictionary<string, ConfigEntry<float>> MapManualLodBias;

        // --- Debug ---
        public static ConfigEntry<bool> DebugReticleAfterEverything;


        public static void Init(ConfigFile config)
        {
            // --- 0. Global ---
            ModEnabled = config.Bind("Global", "Mod Enabled", true,
                new ConfigDescription(
                    "Master ON/OFF switch for the entire mod.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false, Order = 2 }));
            ModToggleKey = config.Bind("Global", "Mod Toggle Key", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for master mod enable/disable.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false, Order = 1 }));
            AutoSwitchReticleRenderForNvg = config.Bind("General", "Auto Switch Reticle Render For NVG", true,
                new ConfigDescription(
                    "Automatically switch reticle CommandBuffer event based on NVG state.\n" +
                "ON: use AfterForwardAlpha while NVG are active and AfterEverything otherwise.\n" +
                "OFF: keep the normal path (AfterForwardAlpha) unless debug override is enabled.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true, Order = 0 }));

            // --- General ---
            AutoDisableForVariableScopes = config.Bind("General", "Auto Disable For Variable Scopes", true,
                new ConfigDescription(
                    "Automatically disable all mod effects while scoped with unsupported scopes",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            AutoBypassNameContains = config.Bind("General", "Auto Bypass Name Contains", "npz, PU, vomz, d-evo",
                new ConfigDescription(
                    "Comma-separated list of substrings. Any scope whose object name or scope key contains one of these ",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeWhitelistNames = config.Bind("General", "Scope Whitelist Names", "",
                new ConfigDescription(
                    "Comma/semicolon/newline separated list of allowed scope keys.\n" +
                "Primary key is derived from the object under mod_scope that does not contain mount (case-insensitive).\n" +
                "Fallbacks: template _name, template _id, then optic object name. Empty list = whitelist ignored.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeWhitelistToggleEntryKey = config.Bind("General", "Scope Whitelist Toggle Entry Key", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, add/remove the current scope key in ScopeWhitelistNames (derived from mod_scope non-mount object).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            // --- Weapon Scaling ---
            EnableWeaponScaling = config.Bind("General", "Enable Weapon Scaling", true,
                new ConfigDescription(
                    "Compensate weapon/arms model scale across magnification levels.\n" +
                "Without this, zooming in (lower FOV) makes the weapon appear larger on screen.\n" +
                "With this enabled, the weapon shrinks proportionally as you zoom in so it\n" +
                "always occupies the same screen space at every magnification level.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BaselineWeaponScale = config.Bind("General", "Baseline Weapon Scale", 0.8873239f,
                new ConfigDescription(
                    "Base weapon scale applied at all FOV values before compensation.\n" +
                    "1.00 = default EFT visual scale.",
                    new AcceptableValueRange<float>(0.00f, 2.00f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            WeaponScaleStrength = config.Bind("General", "Weapon Scale Strength", 0.2723005f,
                new ConfigDescription(
                    "Blends between no compensation and full inverse-FOV compensation.\n" +
                    "0.00 = no compensation, 1.00 = full compensation, values outside [0,1] over/under-compensate.",
                    new AcceptableValueRange<float>(-2.00f, 2.00f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Zoom ---
            EnableZoom = config.Bind("General", "Enable Zoom", true,
                new ConfigDescription(
                    "Enable scope magnification via FOV zoom.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DefaultZoom = config.Bind("General", "Default Zoom", 4f,
                new ConfigDescription(
                    "Default magnification when auto-detection fails (e.g. fixed scopes without zoom data).",
                    new AcceptableValueRange<float>(1f, 16f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            BaselineFOV = config.Bind("General", "Baseline FOV", 50f,
                new ConfigDescription(
                    "What to use when calculating magnified FOV (if set to 50 then 2x will be 25°).\n" +
                    "Be aware that 1x is always forced to 35°.",
                    new AcceptableValueRange<float>(20f, 50f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            FovAnimationDuration = config.Bind("General", "FOV Animation Duration", 0.15f,
                new ConfigDescription(
                    "Duration of the FOV transitions during magnification changes.",
                    new AcceptableValueRange<float>(0f, 10f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            ManualLodBias = config.Bind("Optimization", "Manual LOD Bias", 0f,
                new ConfigDescription(
                    "Manual LOD bias while scoped.\n" +
                    "0 = auto (Magnification * Auto LOD bias multiplier).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            AutoLodBiasMultiplier = config.Bind("Optimization", "Auto LOD bias multiplier", 2f,
                new ConfigDescription(
                    "Self explanatory",
                    new AcceptableValueRange<float>(0.01f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ManualCullingMultiplier = config.Bind("Optimization", "ManualCullingMultiplier", 0.8f,
                new ConfigDescription(
                    "Manual multiplier for Camera.layerCullDistances while scoped.\n" +
                    "0 = auto (use magnification).\n" +
                    ">0 = force this multiplier (e.g. 2.0 doubles cull distances).",
                    new AcceptableValueRange<float>(0f, 20f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Mesh Surgery (ON by default, Cylinder mode) --- //Needs global cleanup, config name standardization.
            EnableMeshSurgery = config.Bind("Global Mesh Surgery settings", "EnableMeshSurgery", true,
                new ConfigDescription(
                    "Enable runtime mesh cutting to bore a hole through the scope housing.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            MeshSurgeryToggleKey = config.Bind("Global Mesh Surgery settings", "MeshSurgeryToggleKey", KeyCode.None,
                new ConfigDescription(
                    "Toggle key for mesh surgery.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ReloadMeshResumeDelaySeconds = config.Bind("Global Mesh Surgery settings", "ReloadMeshResumeDelaySeconds", 0.12f, //Needs renaming, should be in general
                new ConfigDescription(
                    "Extra delay in seconds after reload ends before mesh surgery re-applies/reticle displays etc....",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false, ShowRangeAsPercent = false }));
            PlaneOffsetMeters = config.Bind("Global Mesh Surgery settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            PlaneNormalAxis = config.Bind("Global Mesh Surgery settings", "PlaneNormalAxis", "-Y", //Should be hard set to -Y
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z"),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1Radius = config.Bind("Global Mesh Surgery settings", "Plane1Radius", 0.011f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane1OffsetMeters = config.Bind("Global Mesh Surgery settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Position = config.Bind("Global Mesh Surgery settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Plane 2 profile position (0..1) anchored from the near side.\n" +
                    "Changing CutLength keeps this plane at the same world-space depth from near.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane2Radius = config.Bind("Global Mesh Surgery settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Radius (meters) at plane 2.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Position = config.Bind("Global Mesh Surgery settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Normalized depth of plane 3 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane3Radius = config.Bind("Global Mesh Surgery settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 3.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Position = config.Bind("Global Mesh Surgery settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Normalized depth of plane 4 from near (0) to far (1). Usually 1.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            Plane4Radius = config.Bind("Global Mesh Surgery settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 4 (away from player).",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutStartOffset = config.Bind("Global Mesh Surgery settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "How far behind the backLens (toward the camera) the near cut plane starts.\n" +
                    "The near plane is fixed at: backLens - (offset × boreAxis).\n" +
                    "0 = starts exactly at the backLens. 0.05 = 5cm behind it (catches interior tube geometry).\n" +
                    "Changing CutLength does NOT move this plane.",
                    new AcceptableValueRange<float>(0f, 0.3f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CutLength = config.Bind("Global Mesh Surgery settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "How far forward from the near plane the cut extends (toward the objective).\n" +
                    "The far plane is at: nearPlane + (length × boreAxis).\n" +
                    "Only the far plane moves when you change this value.",
                    new AcceptableValueRange<float>(0.01f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            NearPreserveDepth = config.Bind("Global Mesh Surgery settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Depth (meters) from the near cut plane where NO geometry is cut.\n" +
                    "Preserves the eyepiece housing closest to the camera so you\n" +
                    "keep a thin ring framing the reticle while ADSing.\n" +
                    "0.01 = 1cm ring (default). 0.02-0.04 = thicker ring.\n" +
                    "0 = disabled (cut starts at the near plane — removes everything).",
                    new AcceptableValueRange<float>(0f, 0.15f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ReticleBaseSize = config.Bind("Global Mesh Surgery settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ExpandSearchToWeaponRoot = config.Bind("Global Mesh Surgery settings", "ExpandSearchToWeaponRoot", true, //Needs to be hardset
                new ConfigDescription(
                    "Expand the mesh surgery search root all the way up to the Weapon_root node.\n" +
                "When enabled, meshes on the weapon body under Weapon_root are also candidates\n" +
                "for cutting — not just those in the scope sub-hierarchy.\n" +
                "Use this when scope geometry blends into the weapon receiver and you need to cut\n" +
                "the underlying weapon meshes as well.\n" +
                "Example path: Weapon_root/Weapon_root_anim/weapon/mod_scope/...",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DebugShowHousingMask = config.Bind("Global Mesh Surgery settings", "DebugShowHousingMask", false,
                new ConfigDescription(
                    "Render a red overlay wherever the lens stencil mask is.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            StencilIncludeWeaponMeshes = config.Bind("Global Mesh Surgery settings", "StencilIncludeWeaponMeshes", true, //Really useful ? We're only rendering the reticle on the lens mask area
                new ConfigDescription(
                    "Include weapon body renderers (found under the 'weapon' transform) in the\n" +
                "stencil mask alongside the scope housing.  Prevents the reticle from\n" +
                "bleeding through the weapon mesh at screen centre.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Custom Mesh Surgery settings ---
            SaveCustomMeshSurgerySettingsKey = config.Bind("Per scope settings", "SaveCustomMeshSurgerySettingsKey", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, save all values from this category for the active scope key into custom_mesh_surgery_settings.json.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            DeleteCustomMeshSurgerySettingsKey = config.Bind("Per scope settings", "DeleteCustomMeshSurgerySettingsKey", KeyCode.None,
                new ConfigDescription(
                    "When pressed while scoped, delete custom mesh surgery settings for the active scope key from custom_mesh_surgery_settings.json.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlaneOffsetMeters = config.Bind("Per scope settings", "PlaneOffsetMeters", 0.001f,
                new ConfigDescription(
                    "Custom per-scope plane offset applied along plane normal (meters).",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane1Radius = config.Bind("Per scope settings", "Plane1Radius", 0.011f,
                new ConfigDescription(
                    "Custom per-scope near radius in meters.",
                    new AcceptableValueRange<float>(0.001f, 0.1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane1OffsetMeters = config.Bind("Per scope settings", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Custom per-scope plane 1 offset in meters.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Position = config.Bind("Per scope settings", "Plane2Position", 0.1138498f,
                new ConfigDescription(
                    "Custom per-scope plane 2 position (0..1), anchored from near when CutLength changes.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane2Radius = config.Bind("Per scope settings", "Plane2Radius", 0.0186338f,
                new ConfigDescription(
                    "Custom per-scope plane 2 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Position = config.Bind("Per scope settings", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Custom per-scope plane 3 depth (0..1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane3Radius = config.Bind("Per scope settings", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Custom per-scope plane 3 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Position = config.Bind("Per scope settings", "Plane4Position", 1f,
                new ConfigDescription(
                    "Custom per-scope plane 4 depth (0..1).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomPlane4Radius = config.Bind("Per scope settings", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Custom per-scope plane 4 radius in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutStartOffset = config.Bind("Per scope settings", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "Custom per-scope cut start offset in meters.",
                    new AcceptableValueRange<float>(-0.2f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomCutLength = config.Bind("Per scope settings", "CutLength", 0.755493f,
                new ConfigDescription(
                    "Custom per-scope cut length in meters.",
                    new AcceptableValueRange<float>(0.01f, 4f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomNearPreserveDepth = config.Bind("Per scope settings", "NearPreserveDepth", 0.02549295f,
                new ConfigDescription(
                    "Custom per-scope near preserve depth in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            CustomReticleBaseSize = config.Bind("Per scope settings", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Custom per-scope reticle base diameter in meters.",
                    new AcceptableValueRange<float>(0f, 0.2f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            CustomExpandSearchToWeaponRoot = config.Bind("Per scope settings", "ExpandSearchToWeaponRoot", true,
                new ConfigDescription(
                    "Custom per-scope search root expansion to Weapon_root.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));

            // --- Scope Effects ---
            VignetteEnabled = config.Bind("Scope Effects", "Vignette", false,
                new ConfigDescription(
                    "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteOpacity = config.Bind("Scope Effects", "Vignette Opacity", 0.78f,
                new ConfigDescription(
                    "Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSizeMult = config.Bind("Scope Effects", "Vignette Size Multiplier", 0.41f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            VignetteSoftness = config.Bind("Scope Effects", "Vignette Softness", 1f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            ScopeShadowEnabled = config.Bind("Scope Effects", "ScopeShadow", false,
                new ConfigDescription(
                    "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            DebugShowScopeShadowMask = config.Bind("Scope Effects", "DebugShowScopeShadowMask", false,
                new ConfigDescription(
                    "Render the shadow lens mask as a green overlay for debugging.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = true }));
            ScopeShadowPersistOnUnscope = config.Bind("Scope Effects", "ScopeShadow Persist On Unscope", false,
                new ConfigDescription(
                    "Keep the scope shadow visible after leaving ADS until the FOV restore finishes, useful when FOV animation set to 0.",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowOpacity = config.Bind("Scope Effects", "ScopeShadow Opacity", 0.82f,
                new ConfigDescription(
                    "Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowRadius = config.Bind("Scope Effects", "ScopeShadow Radius", 0.05f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
            ScopeShadowSoftness = config.Bind("Scope Effects", "ScopeShadow Softness", 0.08f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f),
                    new ConfigurationManagerAttributes { IsAdvanced = false }));

            // --- Debug ---
            DebugReticleAfterEverything = config.Bind("General", "Draw reticle after everything", false,
                new ConfigDescription(
                    "When enabled, reticle is always clear but doesn't get tinted by NVGs",
                    null,
                    new ConfigurationManagerAttributes { IsAdvanced = false }));
        }
    }
}
