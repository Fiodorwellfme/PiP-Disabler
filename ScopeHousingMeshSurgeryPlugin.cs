using BepInEx;
using BepInEx.Configuration;
using EFT.CameraControl;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    [BepInPlugin("com.example.scopehousingmeshsurgery", "Scope Housing Mesh Surgery", "4.7.0")]
    public sealed class ScopeHousingMeshSurgeryPlugin : BaseUnityPlugin
    {
        internal static ScopeHousingMeshSurgeryPlugin Instance;

        // --- Logging helpers ---
        internal static void LogInfo(string msg) { if (Instance != null) Instance.Logger.LogInfo(msg); }
        internal static void LogWarn(string msg) { if (Instance != null) Instance.Logger.LogWarning(msg); }
        internal static void LogError(string msg) { if (Instance != null) Instance.Logger.LogError(msg); }
        internal static void LogVerbose(string msg)
        {
            if (Instance != null && VerboseLogging != null && VerboseLogging.Value)
                Instance.Logger.LogInfo("[V] " + msg);
        }

        /// <summary>
        /// Returns the main FPS camera via CameraClass.Instance.Camera.
        /// Unlike Camera.main (which does FindObjectWithTag every call and
        /// can flicker to optic/UI cameras during ADS transitions),
        /// CameraClass is EFT's own singleton and always points to the
        /// correct main FPS camera.  Falls back to Camera.main if
        /// CameraClass isn't initialized yet (menus, loading).
        /// </summary>
        internal static Camera GetMainCamera()
        {
            try
            {
                if (CameraClass.Exist)
                {
                    var cam = CameraClass.Instance.Camera;
                    if (cam != null) return cam;
                }
            }
            catch { }
            return Camera.main;
        }

        // --- 0. Global ---
        internal static ConfigEntry<bool> ModEnabled;
        internal static ConfigEntry<KeyCode> ModToggleKey;

        // --- General ---
        internal static ConfigEntry<bool> DisablePiP;
        internal static ConfigEntry<bool> AutoDisableForHighMagnificationScopes;
        internal static ConfigEntry<KeyCode> DisablePiPToggleKey;
        internal static ConfigEntry<bool> MakeLensesTransparent;
        internal static ConfigEntry<KeyCode> LensesTransparentToggleKey;

        // --- Mesh Surgery ---
        internal static ConfigEntry<bool> EnableMeshSurgery;
        internal static ConfigEntry<KeyCode> MeshSurgeryToggleKey;
        internal static ConfigEntry<bool> RestoreOnUnscope;
        internal static ConfigEntry<float> PlaneOffsetMeters;
        internal static ConfigEntry<string> PlaneNormalAxis;
        internal static ConfigEntry<float> CutRadius;
        internal static ConfigEntry<bool> ShowCutPlane;
        internal static ConfigEntry<bool> ShowCutVolume;
        internal static ConfigEntry<float> CutVolumeOpacity;
        internal static ConfigEntry<string> CutMode;
        internal static ConfigEntry<float> CylinderRadius;
        internal static ConfigEntry<float> MidCylinderRadius;
        internal static ConfigEntry<float> MidCylinderPosition;
        internal static ConfigEntry<float> FarCylinderRadius;
        internal static ConfigEntry<float> Plane1OffsetMeters;
        internal static ConfigEntry<float> Plane2Position;
        internal static ConfigEntry<float> Plane2Radius;
        internal static ConfigEntry<float> Plane3Position;
        internal static ConfigEntry<float> Plane3Radius;
        internal static ConfigEntry<float> Plane4Position;
        internal static ConfigEntry<float> Plane4Radius;
        internal static ConfigEntry<float> CutStartOffset;
        internal static ConfigEntry<float> CutLength;
        internal static ConfigEntry<float> NearPreserveDepth;
        internal static ConfigEntry<bool> ShowReticle;
        internal static ConfigEntry<float> ReticleBaseSize;
        internal static ConfigEntry<int> ReticleSmoothingFrames;
        internal static ConfigEntry<float> ReticleJitterThreshold;
        internal static ConfigEntry<bool> ReticleFlipHorizontal;
        internal static ConfigEntry<float> ReticleMipBias;
        internal static ConfigEntry<float> AdsSettledThreshold;
        internal static ConfigEntry<bool> ReticleOverlayCamera;
        internal static ConfigEntry<bool> RemoveCameraSide;
        internal static ConfigEntry<bool> ForceManualKeepSide;
        internal static ConfigEntry<bool> ManualKeepPositive;
        internal static ConfigEntry<string> ExcludeNameContainsCsv;

        // --- Scope Effects ---
        internal static ConfigEntry<bool>  VignetteEnabled;
        internal static ConfigEntry<float> VignetteOpacity;
        internal static ConfigEntry<float> VignetteSizeMult;
        internal static ConfigEntry<float> VignetteSoftness;
        internal static ConfigEntry<bool>  ScopeShadowEnabled;
        internal static ConfigEntry<float> ScopeShadowOpacity;
        internal static ConfigEntry<float> ScopeShadowRadius;
        internal static ConfigEntry<float> ScopeShadowSoftness;

        // --- Diagnostics ---
        internal static ConfigEntry<KeyCode> DiagnosticsKey;
        internal static ConfigEntry<string>  ScopeBlacklist;

        // --- Zoom / FOV ---
        internal static ConfigEntry<bool> EnableZoom;
        internal static ConfigEntry<bool> EnableShaderZoom;
        internal static ConfigEntry<float> DefaultZoom;
        internal static ConfigEntry<bool> AutoFovFromScope;
        internal static ConfigEntry<float> ScopedFov;
        internal static ConfigEntry<float> FovAnimationDuration;
        internal static ConfigEntry<KeyCode> ZoomToggleKey;
        internal static ConfigEntry<bool> EnableScrollZoom;
        internal static ConfigEntry<float> ScrollZoomSensitivity;
        internal static ConfigEntry<KeyCode> ScrollZoomModifierKey;
        internal static ConfigEntry<float> ScrollZoomMin;
        internal static ConfigEntry<float> ScrollZoomMax;

        // --- 4. Zeroing ---
        internal static ConfigEntry<bool> EnableZeroing;
        internal static ConfigEntry<KeyCode> ZeroingUpKey;
        internal static ConfigEntry<KeyCode> ZeroingDownKey;

        // --- Debug ---
        internal static ConfigEntry<bool> VerboseLogging;

        private void Awake()
        {
            Instance = this;

            // --- 0. Global ---
            ModEnabled = Config.Bind("0. Global", "ModEnabled", true,
                "Master ON/OFF switch for the entire mod. When OFF, all effects are " +
                "cleaned up and the game behaves as if the mod is not installed.");
            ModToggleKey = Config.Bind("0. Global", "ModToggleKey", KeyCode.F12,
                "Toggle key for master mod enable/disable.");

            // --- General ---
            DisablePiP = Config.Bind("1. General", "DisablePiP", true,
                "Disable Picture-in-Picture optic rendering (No-PiP mode). " +
                "Core feature — gives identical perf between hip-fire and ADS.");
            AutoDisableForHighMagnificationScopes = Config.Bind("1. General", "AutoDisableForHighMagnificationScopes", false,
                "Automatically disable all mod effects while scoped with optics whose maximum magnification exceeds 10x.");
            DisablePiPToggleKey = Config.Bind("1. General", "DisablePiPToggleKey", KeyCode.F10,
                "Toggle key for PiP disable.");

            MakeLensesTransparent = Config.Bind("1. General", "MakeLensesTransparent", true,
                "Hide lens surfaces (linza/backLens) while scoped so you see through the tube.");
            LensesTransparentToggleKey = Config.Bind("1. General", "LensesTransparentToggleKey", KeyCode.F11,
                "Toggle key for lens transparency.");

            // --- Zoom ---
            EnableZoom = Config.Bind("2. Zoom", "EnableZoom", true,
                "Enable scope magnification (either shader zoom or FOV zoom fallback).");
            EnableShaderZoom = Config.Bind("2. Zoom", "EnableShaderZoom", true,
                "Use GrabPass shader zoom on the lens surface (best quality, weapon stays normal size). " +
                "Requires scopezoom.bundle in assets/ folder. Falls back to FOV zoom if not available.");
            DefaultZoom = Config.Bind("2. Zoom", "DefaultZoom", 4f,
                new ConfigDescription(
                    "Default magnification when auto-detection fails (e.g. fixed scopes without zoom data).",
                    new AcceptableValueRange<float>(1f, 16f)));
            AutoFovFromScope = Config.Bind("2. Zoom", "AutoFovFromScope", true,
                "Auto-detect magnification from the scope's zoom data (ScopeZoomHandler). " +
                "Works for variable-zoom scopes. Falls back to DefaultZoom for fixed scopes.");
            ScopedFov = Config.Bind("2. Zoom", "ScopedFov", 15f,
                new ConfigDescription(
                    "FOV (degrees) for FOV zoom fallback mode. Lower = more zoom. " +
                    "Only used when shader zoom is unavailable.",
                    new AcceptableValueRange<float>(5f, 75f)));
            FovAnimationDuration = Config.Bind("2. Zoom", "FovAnimationDuration", 0.25f,
                new ConfigDescription(
                    "Duration (seconds) of the FOV zoom-in animation when entering ADS.\n" +
                    "0 = instant snap. 0.25 = smooth quarter-second transition.\n" +
                    "Scope exit always restores FOV instantly to avoid sluggish feel.",
                    new AcceptableValueRange<float>(0f, 2f)));

            // --- Scroll Zoom ---
            EnableScrollZoom = Config.Bind("2. Zoom", "EnableScrollZoom", true,
                "Allow scroll wheel to adjust magnification while ADS.\n" +
                "Scroll up = zoom in, scroll down = zoom out.\n" +
                "Zoom range is clamped to the scope's native min/max magnification.\n" +
                "Resets to the scope's current magnification on scope exit.");
            ScrollZoomSensitivity = Config.Bind("2. Zoom", "ScrollZoomSensitivity", 0.15f,
                new ConfigDescription(
                    "How fast scroll wheel changes magnification.\n" +
                    "Each scroll tick multiplies/divides magnification by (1 + sensitivity).\n" +
                    "0.1 = slow, 0.25 = fast.",
                    new AcceptableValueRange<float>(0.01f, 1f)));
            ScrollZoomModifierKey = Config.Bind("2. Zoom", "ScrollZoomModifierKey", KeyCode.LeftAlt,
                "Hold this key while scrolling to change magnification.\n" +
                "Set to None to use plain scroll (no modifier required).\n" +
                "Default: LeftAlt (Alt+Scroll).");
            ScrollZoomMin = Config.Bind("2. Zoom", "ScrollZoomMin", 0f,
                new ConfigDescription(
                    "Override minimum magnification for scroll zoom.\n" +
                    "0 = auto-detect from the scope's native zoom range.\n" +
                    ">0 = force this as the minimum (e.g. 1).",
                    new AcceptableValueRange<float>(0f, 20f)));
            ScrollZoomMax = Config.Bind("2. Zoom", "ScrollZoomMax", 0f,
                new ConfigDescription(
                    "Override maximum magnification for scroll zoom.\n" +
                    "0 = auto-detect from the scope's native zoom range.\n" +
                    ">0 = force this as the maximum (e.g. 12).",
                    new AcceptableValueRange<float>(0f, 100f)));
            ZoomToggleKey = Config.Bind("2. Zoom", "ZoomToggleKey", KeyCode.None,
                "Toggle key for zoom (None = always on when EnableZoom is true).");

            // --- 4. Zeroing ---
            EnableZeroing = Config.Bind("4. Zeroing", "EnableZeroing", true,
                "Enable optic zeroing (calibration distance adjustment) via keyboard.\n" +
                "Uses the proper EFT pathway (works with Fika).");
            ZeroingUpKey = Config.Bind("4. Zeroing", "ZeroingUpKey", KeyCode.PageUp,
                "Key to increase zeroing distance.");
            ZeroingDownKey = Config.Bind("4. Zeroing", "ZeroingDownKey", KeyCode.PageDown,
                "Key to decrease zeroing distance.");

            // --- Mesh Surgery (ON by default, Cylinder mode) ---
            EnableMeshSurgery = Config.Bind("3. Mesh Surgery", "EnableMeshSurgery", true,
                "Enable runtime mesh cutting to bore a hole through the scope housing.");
            MeshSurgeryToggleKey = Config.Bind("3. Mesh Surgery", "MeshSurgeryToggleKey", KeyCode.F9,
                "Toggle key for mesh surgery.");
            RestoreOnUnscope = Config.Bind("3. Mesh Surgery", "RestoreOnUnscope", true,
                "Restore original meshes when leaving scope.");
            PlaneOffsetMeters = Config.Bind("3. Mesh Surgery", "PlaneOffsetMeters", 0.001f,
                "Offset applied along plane normal (meters).");
            PlaneNormalAxis = Config.Bind("3. Mesh Surgery", "PlaneNormalAxis", "-Y",
                new ConfigDescription(
                    "Which local axis to use as the cut plane normal.\n" +
                    "Auto = use backLens.forward (game default).\n" +
                    "X/Y/Z = force that local axis as the plane normal.\n" +
                    "-X/-Y/-Z = force the negative of that axis.\n" +
                    "If the cut is horizontal when it should be vertical, try Z or Y.",
                    new AcceptableValueList<string>("Auto", "X", "Y", "Z", "-X", "-Y", "-Z")));
            CutRadius = Config.Bind("3. Mesh Surgery", "CutRadius", 0f,
                new ConfigDescription(
                    "Max distance (meters) from scope center to cut. 0 = unlimited (cut all geometry).\n" +
                    "Set to e.g. 0.05 to only cut geometry near the lens opening.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ShowCutPlane = Config.Bind("3. Mesh Surgery", "ShowCutPlane", false,
                "Show green/red semi-transparent circles at the near/far cut plane positions.\n" +
                "Use this to visualize the cut endpoints.");
            ShowCutVolume = Config.Bind("3. Mesh Surgery", "ShowCutVolume", false,
                "Show a semi-transparent 3D tube representing the full cut volume.\n" +
                "Visualizes the near→mid→far radius profile so you can see exactly what gets removed.");
            CutVolumeOpacity = Config.Bind("3. Mesh Surgery", "CutVolumeOpacity", 0.49f,
                new ConfigDescription(
                    "Opacity of the 3D cut volume visualizer (0 = invisible, 1 = opaque).",
                    new AcceptableValueRange<float>(0.05f, 0.8f)));
            CutMode = Config.Bind("3. Mesh Surgery", "CutMode", "Cylinder",
                new ConfigDescription(
                    "Plane = flat infinite cut. Cylinder = cylindrical bore cut centered on the lens axis.\n" +
                    "Cylinder removes geometry inside a cylinder of CylinderRadius around the lens center.",
                    new AcceptableValueList<string>("Plane", "Cylinder")));
            CylinderRadius = Config.Bind("3. Mesh Surgery", "CylinderRadius", 0.015f,
                new ConfigDescription(
                    "Near radius (meters) of the cylindrical/cone cut (camera side).\n" +
                    "Typical scope lens radius is ~0.01-0.02m.",
                    new AcceptableValueRange<float>(0.001f, 0.1f)));
            MidCylinderRadius = Config.Bind("3. Mesh Surgery", "MidCylinderRadius", 0.013f,
                new ConfigDescription(
                    "Intermediate radius (meters) at MidCylinderPosition along the bore.\n" +
                    "0 = disabled (linear near→far interpolation).\n" +
                    ">0 = two-segment interpolation: near→mid, then mid→far.\n" +
                    "Set smaller than near/far to create a waist (hourglass). Set larger for a bulge.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            MidCylinderPosition = Config.Bind("3. Mesh Surgery", "MidCylinderPosition", 0.28f,
                new ConfigDescription(
                    "Position of the mid-radius control point along the cut length (0=near, 1=far).\n" +
                    "0.5 = midpoint. 0.3 = closer to camera. 0.7 = closer to objective.",
                    new AcceptableValueRange<float>(0.01f, 0.99f)));
            FarCylinderRadius = Config.Bind("3. Mesh Surgery", "FarCylinderRadius", 0.12f,
                new ConfigDescription(
                    "Far radius (meters) of the cone cut (objective side).\n" +
                    "0 = same as CylinderRadius (pure cylinder). >0 creates a cone/frustum shape.\n" +
                    "Set larger than CylinderRadius to widen the bore toward the objective lens.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane1OffsetMeters = Config.Bind("3. Mesh Surgery", "Plane1OffsetMeters", 0f,
                new ConfigDescription(
                    "Offset (meters) for plane 1 from linza/backLens origin along bore axis.\n" +
                    "Plane 1 radius is always CylinderRadius.",
                    new AcceptableValueRange<float>(-0.02f, 0.02f)));
            Plane2Position = Config.Bind("3. Mesh Surgery", "Plane2Position", 0.05751174f,
                new ConfigDescription(
                    "Normalized depth of plane 2 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane2Radius = Config.Bind("3. Mesh Surgery", "Plane2Radius", 0.013f,
                new ConfigDescription(
                    "Radius (meters) at plane 2.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane3Position = Config.Bind("3. Mesh Surgery", "Plane3Position", 0.55f,
                new ConfigDescription(
                    "Normalized depth of plane 3 from near (0) to far (1).",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane3Radius = Config.Bind("3. Mesh Surgery", "Plane3Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 3.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            Plane4Position = Config.Bind("3. Mesh Surgery", "Plane4Position", 1f,
                new ConfigDescription(
                    "Normalized depth of plane 4 from near (0) to far (1). Usually 1.",
                    new AcceptableValueRange<float>(0f, 1f)));
            Plane4Radius = Config.Bind("3. Mesh Surgery", "Plane4Radius", 0.2f,
                new ConfigDescription(
                    "Radius (meters) at plane 4 (away from player).",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            CutStartOffset = Config.Bind("3. Mesh Surgery", "CutStartOffset", 0.04084507f,
                new ConfigDescription(
                    "How far behind the backLens (toward the camera) the near cut plane starts.\n" +
                    "The near plane is fixed at: backLens - (offset × boreAxis).\n" +
                    "0 = starts exactly at the backLens. 0.05 = 5cm behind it (catches interior tube geometry).\n" +
                    "Changing CutLength does NOT move this plane.",
                    new AcceptableValueRange<float>(0f, 0.3f)));
            CutLength = Config.Bind("3. Mesh Surgery", "CutLength", 0.755493f,
                new ConfigDescription(
                    "How far forward from the near plane the cut extends (toward the objective).\n" +
                    "The far plane is at: nearPlane + (length × boreAxis).\n" +
                    "Only the far plane moves when you change this value.",
                    new AcceptableValueRange<float>(0.01f, 1f)));
            NearPreserveDepth = Config.Bind("3. Mesh Surgery", "NearPreserveDepth", 0.03042253f,
                new ConfigDescription(
                    "Depth (meters) from the near cut plane where NO geometry is cut.\n" +
                    "Preserves the eyepiece housing closest to the camera so you\n" +
                    "keep a thin ring framing the reticle while ADSing.\n" +
                    "0.01 = 1cm ring (default). 0.02-0.04 = thicker ring.\n" +
                    "0 = disabled (cut starts at the near plane — removes everything).",
                    new AcceptableValueRange<float>(0f, 0.15f)));
            ShowReticle = Config.Bind("3. Mesh Surgery", "ShowReticle", true,
                "Render the scope reticle texture as a glowing overlay where the lens was.\n" +
                "Uses alpha blending so the reticle's own alpha channel controls transparency.");
            ReticleBaseSize = Config.Bind("3. Mesh Surgery", "ReticleBaseSize", 0.030f,
                new ConfigDescription(
                    "Physical diameter (meters) of the reticle quad at 1x magnification.\n" +
                    "The quad is scaled by 1/magnification so screen-pixel coverage stays constant\n" +
                    "across all zoom levels.  Typical scope lens diameter is 0.02-0.04 m.\n" +
                    "Set to 0 to fall back to the legacy CylinderRadius x2 value.",
                    new AcceptableValueRange<float>(0f, 0.2f)));
            ReticleSmoothingFrames = Config.Bind("3. Mesh Surgery", "ReticleSmoothingFrames", 1,
                new ConfigDescription(
                    "[DEPRECATED — reticle is now parented to the lens transform.]\n" +
                    "Was: Number of frames to average the reticle world-position over.",
                    new AcceptableValueRange<int>(1, 60)));
            ReticleJitterThreshold = Config.Bind("3. Mesh Surgery", "ReticleJitterThreshold", 0.0002f,
                new ConfigDescription(
                    "[DEPRECATED — reticle is now parented to the lens transform.]\n" +
                    "Was: Minimum world-space displacement before the reticle updates.",
                    new AcceptableValueRange<float>(0f, 0.05f)));
            ReticleFlipHorizontal = Config.Bind("3. Mesh Surgery", "ReticleFlipHorizontal", false,
                "Flip the reticle texture along the Y axis (left-right mirror).\n" +
                "Enable if the reticle appears mirrored compared to the original PiP view.");
            ReticleMipBias = Config.Bind("3. Mesh Surgery", "ReticleMipBias", -1.5f,
                new ConfigDescription(
                    "Mip map bias for the reticle texture. Negative = sharper.\n" +
                    "0 = Unity default.  -1 = subtle sharpening.  -2 = very crisp at the cost\n" +
                    "of slight shimmering.  Adjust to taste with your scope.",
                    new AcceptableValueRange<float>(-4f, 0f)));
            AdsSettledThreshold = Config.Bind("3. Mesh Surgery", "AdsSettledThreshold", 0.006244131f,
                new ConfigDescription(
                    "Lens movement threshold (units/frame) below which the weapon is\n" +
                    "considered settled after ADS-in.  The reticle/vignette/shadow are\n" +
                    "hidden until the lens stops moving to avoid jitter during the ADS\n" +
                    "transition animation.  Lower = stricter (waits longer).  0 = disabled.",
                    new AcceptableValueRange<float>(0f, 0.01f)));
            ReticleOverlayCamera = Config.Bind("3. Mesh Surgery", "ReticleOverlayCamera", true,
                "[DEPRECATED — reticle now uses a CommandBuffer with nonJitteredProjectionMatrix.\n" +
                "The overlay camera has been removed. This setting has no effect.]");
            RemoveCameraSide = Config.Bind("3. Mesh Surgery", "RemoveCameraSide", true,
                "Remove geometry on the camera side of the lens plane.");
            ForceManualKeepSide = Config.Bind("3. Mesh Surgery", "ForceManualKeepSide", false,
                "If true, ignores auto keep-side selection.");
            ManualKeepPositive = Config.Bind("3. Mesh Surgery", "ManualKeepPositive", true,
                "Only used when ForceManualKeepSide=true.");
            ExcludeNameContainsCsv = Config.Bind("3. Mesh Surgery", "ExcludeNameContainsCsv",
                "linza,lens,glass,reticle,collider,trigger,shadow,backlens",
                "Comma-separated substrings to exclude from mesh cutting.");

            // --- Scope Effects ---
            VignetteEnabled = Config.Bind("4. Scope Effects", "VignetteEnabled", true,
                "Render a circular vignette ring around the scope aperture.\n" +
                "A world-space quad at the lens position fading from transparent centre to black edge.");
            VignetteOpacity = Config.Bind("4. Scope Effects", "VignetteOpacity", 0.5823944f,
                new ConfigDescription("Maximum opacity of the lens vignette ring (0=invisible, 1=full black).",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSizeMult = Config.Bind("4. Scope Effects", "VignetteSizeMult", 0.2730047f,
                new ConfigDescription(
                    "Vignette quad diameter as a multiplier of ReticleBaseSize.\n" +
                    "1.0 = same size as reticle.  1.5 gives a visible border ring.\n" +
                    "Higher values (5-15) may be needed for high-magnification scopes.",
                    new AcceptableValueRange<float>(0f, 1f)));
            VignetteSoftness = Config.Bind("4. Scope Effects", "VignetteSoftness", 1f,
                new ConfigDescription(
                    "Fraction of the vignette radius used for the gradient falloff (0=hard edge, 1=full gradient).",
                    new AcceptableValueRange<float>(0f, 1f)));

            ScopeShadowEnabled = Config.Bind("4. Scope Effects", "ScopeShadowEnabled", false,
                "Overlay a fullscreen scope-tube shadow: black everywhere except a transparent\n" +
                "circular window in the centre.  Simulates looking down a scope tube.");
            ScopeShadowOpacity = Config.Bind("4. Scope Effects", "ScopeShadowOpacity", 0.92f,
                new ConfigDescription("Maximum opacity of the scope shadow overlay.",
                    new AcceptableValueRange<float>(0f, 1f)));
            ScopeShadowRadius = Config.Bind("4. Scope Effects", "ScopeShadowRadius", 0.18f,
                new ConfigDescription(
                    "Radius of the transparent centre window as a fraction of the half-screen (0.0-0.5).\n" +
                    "0.18 = window fills roughly 36% of screen width.  Increase for wider aperture.",
                    new AcceptableValueRange<float>(0.02f, 0.5f)));
            ScopeShadowSoftness = Config.Bind("4. Scope Effects", "ScopeShadowSoftness", 0.06f,
                new ConfigDescription(
                    "Width of the gradient edge between the clear window and the black shadow (fraction of screen).\n" +
                    "0 = hard edge.  0.05-0.1 is a natural-looking falloff.",
                    new AcceptableValueRange<float>(0f, 0.3f)));

            // --- Diagnostics ---
            DiagnosticsKey = Config.Bind("5. Diagnostics", "DiagnosticsKey", KeyCode.F8,
                "Press to log full diagnostics for the currently active scope: name, hierarchy,\n" +
                "magnification, cut-plane config, target mesh list, blacklist hint.");
            ScopeBlacklist = Config.Bind("5. Diagnostics", "ScopeBlacklist", "",
                "Comma-separated list of scope root names to exclude from mesh surgery and reticle.\n" +
                "Use the diagnostics key (F8) to find the root name and copy the hint at the bottom\n" +
                "of the log output.  Match is case-insensitive substring: e.g. 'elcan' matches\n" +
                "any scope whose root name contains 'elcan'.");

            // --- Debug ---
            VerboseLogging = Config.Bind("6. Debug", "VerboseLogging", false,
                "Enable detailed logging. Turn on to diagnose lens/zoom issues.");

            Patches.Patcher.Enable();

            // Initialize scope detection via PWA reflection
            ScopeLifecycle.Init();

            // Load shader zoom AssetBundle (optional — falls back to FOV zoom if missing)
            ZoomController.LoadShader();

            Logger.LogInfo("ScopeHousingMeshSurgery v4.7.0 loaded.");
            Logger.LogInfo($"  ModEnabled={ModEnabled.Value}  DisablePiP={DisablePiP.Value}  MakeLensesTransparent={MakeLensesTransparent.Value}");
            Logger.LogInfo($"  EnableZoom={EnableZoom.Value}  ShaderZoom={EnableShaderZoom.Value} (available={ZoomController.ShaderAvailable})");
            Logger.LogInfo($"  AutoFov={AutoFovFromScope.Value}  DefaultZoom={DefaultZoom.Value}  FovAnimDur={FovAnimationDuration.Value}s");
            Logger.LogInfo($"  ScrollZoom={EnableScrollZoom.Value}  ScrollSens={ScrollZoomSensitivity.Value}  ModifierKey={ScrollZoomModifierKey.Value}  Min={ScrollZoomMin.Value}  Max={ScrollZoomMax.Value}");
            Logger.LogInfo($"  EnableMeshSurgery={EnableMeshSurgery.Value}  CutMode={CutMode.Value}  CutLen={CutLength.Value}  NearPreserve={NearPreserveDepth.Value}  ShowReticle={ShowReticle.Value}");
        }

        private void Update()
        {
            // --- Global mod toggle (always active, even when mod is OFF) ---
            if (ModToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ModToggleKey.Value))
            {
                ModEnabled.Value = !ModEnabled.Value;
                Logger.LogInfo($"[Global] Mod {(ModEnabled.Value ? "ENABLED" : "DISABLED")}");
                if (!ModEnabled.Value)
                {
                    // Exit scoped state (restore lenses, meshes, FOV)
                    ScopeLifecycle.ForceExit();
                    // CRITICAL: also restore PiP cameras — the LateUpdate/LensFade patches check
                    // DisablePiP.Value independently, so they keep blocking even when ModEnabled=false.
                    // We explicitly re-enable the optic cameras here so the scope renders normally.
                    PiPDisabler.RestoreAllCameras();
                }
                else
                {
                    // Re-enabled: immediately sync lifecycle state in case player is already scoped.
                    // Without this, the mod stays dark until the next scope enter/exit event.
                    ScopeLifecycle.SyncState();
                }
            }

            // When mod is disabled, skip ALL per-frame logic
            if (!ModEnabled.Value) return;

            // --- Feature toggle keys ---
            if (InputProxy.GetKeyDown(MeshSurgeryToggleKey.Value))
            {
                EnableMeshSurgery.Value = !EnableMeshSurgery.Value;
                Logger.LogInfo($"Mesh surgery toggled: {EnableMeshSurgery.Value}");
                if (!EnableMeshSurgery.Value)
                    MeshSurgeryManager.RestoreAll();
            }

            if (InputProxy.GetKeyDown(DisablePiPToggleKey.Value))
            {
                DisablePiP.Value = !DisablePiP.Value;
                Logger.LogInfo($"Disable PiP toggled: {DisablePiP.Value}");
                if (!DisablePiP.Value)
                    PiPDisabler.RestoreAllCameras();
            }

            if (InputProxy.GetKeyDown(LensesTransparentToggleKey.Value))
            {
                MakeLensesTransparent.Value = !MakeLensesTransparent.Value;
                Logger.LogInfo($"Lens transparency toggled: {MakeLensesTransparent.Value}");
                if (!MakeLensesTransparent.Value)
                    LensTransparency.RestoreAll();
            }

            if (ZoomToggleKey.Value != KeyCode.None && InputProxy.GetKeyDown(ZoomToggleKey.Value))
            {
                EnableZoom.Value = !EnableZoom.Value;
                Logger.LogInfo($"Zoom toggled: {EnableZoom.Value}");
                if (!EnableZoom.Value)
                    ZoomController.Restore();
            }

            // --- Diagnostics dump ---
            if (DiagnosticsKey.Value != KeyCode.None && InputProxy.GetKeyDown(DiagnosticsKey.Value))
                ScopeDiagnostics.Dump(ScopeLifecycle.ActiveOptic);

            // --- Scroll wheel zoom (only while scoped + modifier held) ---
            if (ScopeLifecycle.IsScoped && EnableScrollZoom.Value)
            {
                var modKey = ScrollZoomModifierKey.Value;
                bool modHeld = modKey == KeyCode.None || InputProxy.GetKey(modKey);
                if (modHeld)
                {
                    float scroll = InputProxy.GetScrollDelta();
                    if (ZoomController.HandleScrollZoom(scroll))
                    {
                        ScopeLifecycle.ReapplyFov();
                    }
                }
            }

            // --- Per-frame logic ---
            PiPDisabler.TickBaseOpticCamera();

            // Safety-net: re-check scope state every frame in case we missed an event.
            ScopeLifecycle.CheckAndUpdate();

            // Per-frame maintenance (ensure lens hidden, update variable zoom, etc.)
            ScopeLifecycle.Tick();
        }
    }

    internal static class InputProxy
    {
        private static System.Type _inputType;
        private static System.Reflection.MethodInfo _getKeyDown;
        private static System.Reflection.MethodInfo _getKey;
        private static System.Reflection.PropertyInfo _mouseScrollDelta;

        static InputProxy()
        {
            _inputType = System.Type.GetType("UnityEngine.Input, UnityEngine.InputLegacyModule")
                      ?? System.Type.GetType("UnityEngine.Input, UnityEngine");
            if (_inputType != null)
            {
                _getKeyDown = _inputType.GetMethod("GetKeyDown", new[] { typeof(KeyCode) });
                _getKey = _inputType.GetMethod("GetKey", new[] { typeof(KeyCode) });
                _mouseScrollDelta = _inputType.GetProperty("mouseScrollDelta",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            }
        }

        public static bool GetKeyDown(KeyCode key)
        {
            try
            {
                if (_getKeyDown == null) return false;
                return (bool)_getKeyDown.Invoke(null, new object[] { key });
            }
            catch { return false; }
        }

        /// <summary>Returns true while the key is held down.</summary>
        public static bool GetKey(KeyCode key)
        {
            try
            {
                if (_getKey == null) return false;
                return (bool)_getKey.Invoke(null, new object[] { key });
            }
            catch { return false; }
        }

        /// <summary>
        /// Returns the Y component of Input.mouseScrollDelta.
        /// Positive = scroll up, negative = scroll down.
        /// </summary>
        public static float GetScrollDelta()
        {
            try
            {
                if (_mouseScrollDelta == null) return 0f;
                var vec = (Vector2)_mouseScrollDelta.GetValue(null);
                return vec.y;
            }
            catch { return 0f; }
        }
    }
}
