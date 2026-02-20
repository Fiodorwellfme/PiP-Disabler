# Tarkov Assembly-CSharp Decompilation Reference for SPT 4.0 Modding

> **Source**: `Assembly-CSharp.dll` — ~8,700 .cs files decompiled
> **Obfuscation note**: Many classes use GClass/GInterface/GStruct naming (e.g., `GClass3687`, `GInterface466`). Field names are often obfuscated (`int_0`, `float_1`, `bool_0`, etc.). Events/delegates follow `action_0`, `action_1` patterns.

---

## 1. Architecture Overview

### 1.1 Namespace Structure

| Namespace | Purpose |
|-----------|---------|
| `EFT` | Core game classes — Player, GameWorld, AbstractGame |
| `EFT.CameraControl` | Scope/optic camera pipeline, ScopeData, OpticSight |
| `EFT.InventoryLogic` | Items, SightComponent, IAdjustableOpticData, operations |
| `EFT.Animations` | ProceduralWeaponAnimation, weapon animation effectors |
| `EFT.Ballistics` | Bullet physics, penetration, ricochet |
| `EFT.Interactive` | Doors, switches, exfils, interactive objects |
| `EFT.HealthSystem` | Health, effects, damage model |
| `EFT.Weapons` / `EFT.Weapons.Data` | Weapon data models |
| `EFT.Settings` / `EFT.Settings.Graphics` | Game settings, graphics options |
| `EFT.PostEffects` | OpticCullingMask, SSAA, visual effects |
| `EFT.UI` | UI screens, menus, HUD |
| `EFT.Bots` | AI behavior, bot logic |
| `EFT.BackEnd` | Backend communication |
| `EFT.Hideout` | Hideout logic, shooting range |
| `EFT.NetworkPackets` | Multiplayer packet structures |
| `EFT.NextObservedPlayer` | Observed/third-person player handling |
| `EFT.Game.Spawning` | Spawn system |
| `EFT.RocketLauncher` | GL/rocket launcher subsystem |
| `EFT.WeaponMounting` | Weapon mounting system |
| `EFT.Vaulting` | Vaulting mechanics |
| `FitstPersonAnimations` *(sic)* | First-person weapon animation effectors, recoil pipeline |
| `Comfort.Common` | Singleton<T> pattern and utility classes |
| `Audio.*` | Spatial audio, ambient, weapon sounds |

### 1.2 Singleton Pattern

The game uses `Singleton<T>` (from `Comfort.Common`) extensively. Key singletons:

```csharp
Singleton<GameWorld>.Instance           // The active GameWorld
Singleton<BackendConfigSettingsClass>.Instance  // Server/backend config
Singleton<SharedGameSettingsClass>.Instance     // Player game settings
Singleton<PoolManagerClass>.Instance    // Object pooling/factory
Singleton<Effects>.Instance             // Visual effects system
Singleton<BotEventHandler>.Instance     // Bot event system
Singleton<GUISounds>.Instance           // UI audio
Singleton<IEasyAssets>.Instance         // Asset loading
MonoBehaviourSingleton<BetterAudio>.Instance   // Audio system
MonoBehaviourSingleton<SpatialAudioSystem>.Instance  // Spatial audio
```

**CameraClass** uses its own singleton pattern (not `Singleton<T>`):
```csharp
CameraClass.Instance  // Static property, lazily initialized
CameraClass.Exist     // Check if initialized
```

---

## 2. Game Lifecycle

### 2.1 AbstractGame

```csharp
// EFT.AbstractGame : MonoBehaviour, IGame, IDisposable
// Base for all game modes

public static TGame Create<TGame>(...) // Factory method
public virtual void Dispose()
public virtual void FixedUpdate()
public virtual void LateFixedUpdate()
public virtual void LateUpdate()

// Game types determined by subclass:
//   HideoutGame  → EGameType.Hideout
//   LocalGame    → EGameType.Offline   (SPT uses this)
//   EftNetworkGame → EGameType.Online
//   NarrateGame  → EGameType.Narrate

public static event Action<EGameType> OnGameTypeSetted;
public event Action<string, float?> OnMatchingStatusChanged;
```

Key subclasses for SPT: `LocalGame`, `BaseLocalGame`, `ClientLocalGameWorld`, `ClientNetworkGameWorld`.

### 2.2 GameWorld

```csharp
// EFT.GameWorld : MonoBehaviour, GInterface169, IPlayersCollection, IDisposable
// Accessed via Singleton<GameWorld>.Instance

public static TGameWorld Create<TGameWorld>(GameObject, PoolManagerClass, EUpdateQueue, MongoID?)
public static event Action OnDispose;
public event Action AfterGameStarted;
public event Action<IPlayer> OnPersonAdd;
public event Action<float> OnBeforeWorldTick;
public event Action<float> OnLateUpdate;
public event Action<IKillableLootItem> OnLootItemDestroyed;

// Key properties
public PoolManagerClass ObjectsFactory;
public ExfiltrationControllerClass ExfiltrationController;
public BufferZoneControllerClass BufferZoneController;
public MongoID? CurrentProfileId;
```

---

## 3. Player System

### 3.1 Player Class Hierarchy

```
EFT.Player : MonoBehaviour, IPlayer, IOnItemAdded, GInterface179, IOnItemRemoved, IOnSetInHands, IDissonancePlayer
  ├── EFT.ClientPlayer
  ├── EFT.LocalPlayer
  └── (Bot players via AI subsystem)
```

### 3.2 Key Player Properties & Events

```csharp
// Movement & State
public MovementContext MovementContext { get; set; }
public ICharacterController CharacterController { get; }
public EPlayerState CurrentState;

// Animations
public ProceduralWeaponAnimation ProceduralWeaponAnimation { get; set; }
public IAnimator BodyAnimatorCommon { get; }
public IAnimator ArmsAnimatorCommon { get; }

// Hands/Weapons
public IHandsController HandsController;
// Cast to Player.FirearmController for weapon-specific logic

// Inventory
public InventoryController InventoryController;

// Health
public IHealthController HealthController;

// Sight/Scope Events (critical for scope modding)
public event Action<SightComponent> OnSightChangedEvent;
public event Action<SightComponent, ESmoothScopeState> OnSmoothSightChange;
public event Action<bool> OnTacticalInteractionChanged;
public event Action<float, float, int> OnSpeedChangedEvent;

// Vision
public EPointOfView PointOfView;
// FOV: Singleton<SharedGameSettingsClass>.Instance.Game.Settings.FieldOfView

// Key inner classes:
//   Player.FirearmController — weapon handling in raid
//   Player.Class1353 — connects PlayerState to ProceduralWeaponAnimation
```

### 3.3 ProceduralWeaponAnimation (PWA)

Located at `EFT.Animations.ProceduralWeaponAnimation`. Accessed via `Player.ProceduralWeaponAnimation`.

Key members referenced from Player:
```csharp
PWA.CurrentScope         // Current scope info (.IsOptic, etc.)
PWA.HandsContainer       // .CameraTransform, .WeaponRootAnim
PWA.PointOfView          // EPointOfView
PWA.WalkEffectorEnabled
PWA.DrawEffectorEnabled
PWA.TiltBlender.Target
PWA.SetStrategy(pointOfView)
PWA.SetHeadRotation(rotation)
PWA.ProcessEffectors(time, fixedFrames, motion, velocity)
```

---

## 4. Optics & Scope System (Complete Pipeline)

This is the core rendering pipeline for scoped weapons. It consists of several interconnected classes.

### 4.1 Class Relationship Diagram

```
SightsItemClass (Inventory Item)
  └── has SightComponent (runtime state: selected scope, mode, zoom value)
       └── references SightsTemplateClass/GInterface404 (static data: zooms, FOVs, calibration)

WeaponManagerClass (on weapon prefab)
  └── event OnSmoothScopeStateChanged
  └── event OnSmoothSensetivityChange

SightModVisualControllers (MonoBehaviour on scope prefab)
  ├── SightComponent SightMod (linked to inventory SightComponent)
  ├── ScopePrefabCache (scope mode management, OpticSight references)
  └── ScopeZoomHandler (variable zoom logic)

ScopePrefabCache (MonoBehaviour)
  ├── ScopeModeInfo[] — array of modes, each containing:
  │     ├── GameObject ModeGameObject
  │     ├── CollimatorSight (red dot / holo)
  │     ├── OpticSight (magnified scope)
  │     └── bool IgnoreOpticsForCameraPlane
  ├── HasOptics / HasCollimators
  ├── CurrentModeId / SetMode(int)
  └── DistaneAngle[] AngleByRange (range-based angle adjustment)

OpticSight (MonoBehaviour — on scope prefab, one per magnified mode)
  ├── Renderer LensRenderer (the scope lens mesh)
  ├── ScopeData ScopeData
  │     ├── ScopeReticle Reticle (Mesh, Material, Position, Rotation, Scale)
  │     ├── ScopeNightVisionData
  │     ├── ScopeThermalVisionData
  │     └── ScopeEffectsData (chromatic aberration, bloom, vignette, etc.)
  ├── GInterface466 CameraData (ScopeCameraData or ScopeSmoothCameraData)
  ├── float DistanceToCamera
  ├── Transform ScopeTransform
  ├── static BindableStateClass<GStruct432> OpticSightState (global optic state)
  └── Shader property: "_SwitchToSight" (lens fade control)

CameraClass.Instance (Singleton)
  ├── Camera Camera (main game camera)
  ├── GClass3687 OpticCameraManager
  │     ├── Camera Camera (the optic/PiP camera)
  │     ├── OpticComponentUpdater Updater
  │     ├── OpticSight CurrentOpticSight
  │     ├── OpticRetrice OpticRetrice (reticle renderer)
  │     ├── int OpticRenderResolution / OpticNextRenderResolution
  │     ├── RenderTexture RenderTexture_0
  │     ├── event OnOpticEnabled
  │     ├── event OnOpticDisabled
  │     └── event OnOpticTexturesAreChanged(Texture, Texture)
  ├── NightVision
  ├── ThermalVision
  ├── VisorEffect
  ├── EffectsController
  ├── event OnFovChanged(float)
  └── event OnCameraChanged
```

### 4.2 OpticComponentUpdater — The PiP Camera Controller

```csharp
// EFT.CameraControl.OpticComponentUpdater : MonoBehaviour [ExecuteAlways]
// Attached to the secondary "optic camera" that renders the scope view

public Camera MainCamera => CameraClass.Instance.Camera;

void Awake()
  // Grabs all post-effect components on the optic camera:
  // TOD_Scattering, MBOIT_Scattering, Undithering, PostProcessLayer,
  // VolumetricLightRenderer, OpticCullingMask, ChromaticAberration,
  // BloomOptimized, ThermalVision, CC_FastVignette, UltimateBloom,
  // Tonemapping, NightVision, Fisheye, CameraLodBiasController
  // Subscribes to CameraClass.Instance.OnCameraChanged

void CopyComponentFromOptic(OpticSight opticSight)
  // Called when a scope becomes active. Configures:
  //   - FOV (from ScopeCameraData.FieldOfView or ScopeZoomHandler.FiledOfView)
  //   - Near/Far clip planes
  //   - Optic culling mask
  //   - LOD bias
  //   - Chromatic aberration (Aniso, Shift)
  //   - Bloom (Intensity, Threshold, BlurSize)
  //   - Thermal vision (full config)
  //   - Night vision (Intensity, MaskSize, NoiseIntensity, NoiseScale, Color)
  //   - Vignette, Fisheye, Tonemapping
  //   - For adjustable optics: stores ScopeZoomHandler reference

void LateUpdate()
  // Every frame:
  //   1. Copies position/rotation from scope transform pivot
  //   2. Syncs occlusion culling from main camera
  //   3. Copies Undithering, VolumetricLightRenderer, TOD_Scattering,
  //      MBOIT_Scattering (with optic scatter reduction), PostProcessLayer from main camera
  //   4. For adjustable optics: calls ScopeZoomHandler.UpdateScope() and updates FOV

public float OpticScatterReductionStrength = 1f; // Reduces fog/scatter in scope
```

### 4.3 OpticRetrice — Reticle Rendering

```csharp
// EFT.CameraControl.OpticRetrice : MonoBehaviour
// Renders the scope reticle via CommandBuffer on the optic camera

private SkinnedMeshRenderer _renderer;
private CameraEvent _cameraEvent = CameraEvent.BeforeImageEffectsOpaque;

void SetOpticSight(OpticSight opticSight)
  // Configures reticle: mesh, material from ScopeData.Reticle
  // null opticSight = disable

void UpdateTransform(OpticSight opticSight)
  // Positions reticle using ScopeReticle.Position, .Rotation
  // Scale = ScopeReticle.Scale * 0.1f

void OnPreCull()
  // Clears and rebuilds command buffer each frame:
  //   1. Sets _Scale global shader float
  //   2. Sets _NonJitteredProj global matrix
  //   3. DrawRenderer with reticle material

// Shader globals set:
//   _Scale       → reticle scale
//   _NonJitteredProj → non-jittered projection matrix
```

### 4.4 ScopeMaskRenderer — Scope Mask (Lens/Collimator Stencil)

```csharp
// ScopeMaskRenderer : MonoBehaviour [ExecuteInEditMode]
// Creates a mask texture identifying scope lens vs collimator areas

// Uses shaders:
//   "Hidden/ScopeMask"   — draws lens/collimator geometry in distinct colors
//   "CW FX/Collimator"   — collimator dot rendering
// Sets global texture: "_ScopeMask"

// Color coding in mask:
//   Red   → Optic lens (drawn from OpticSight.LensRenderer)
//   Blue  → Collimator body (from CollimatorSight.CollimatorMeshRenderer)
//   Green → Active collimator dot (with CollimatorMaterial properties)

// Subscribes to:
//   CollimatorSight.OnCollimatorEnabled
//   CollimatorSight.OnCollimatorDisabled
//   CollimatorSight.OnCollimatorUpdated

// Renders via CommandBuffer on CameraEvent.BeforeGBuffer
// Uses GClass1001 for SSAA-aware rendering
```

### 4.5 ScopeZoomHandler — Variable Zoom Scopes

```csharp
// ScopeZoomHandler : MonoBehaviour
// Manages smooth zoom on adjustable (variable-power) scopes

// Key Properties:
float FiledOfView          // Current FOV (note: BSG typo "Filed")
float BlendFactor          // Interpolation factor for reticle blending
float Single_0             // = IAdjustableOpticData.MinMaxFov.x (max FOV = min zoom)
float Single_1             // = IAdjustableOpticData.MinMaxFov.y (min FOV = max zoom)
float Single_2             // = IAdjustableOpticData.MinMaxFov.z (step or default)

// Components (on same GameObject):
ScopePrefabCache ScopeCache
ScopeSmoothCameraData CameraData

// Events:
event Action<ESmoothScopeState> OnSmoothScopeStateChanged;
event Action<float> OnSmoothSensetivityChange;

void Init(SightComponent sightComponent)
  // Reads IAdjustableOpticData, clamps zoom, sets initial state

void UpdateScope()
  // Called every frame from OpticComponentUpdater.LateUpdate()
  // Interpolates FOV toward target zoom
```

### 4.6 Key Camera/Scope Data Classes

```csharp
// GInterface466 — Common interface for scope camera data
bool IsAdjustableOptic { get; }
float NearClipPlane { get; }
float FarClipPlane { get; }
bool OpticCullingMask { get; }
float OpticCullingMaskScale { get; }
bool CameraLodBiasController { get; }
float LodBiasFactor { get; }

// ScopeCameraData : MonoBehaviour, GInterface466
//   Fixed-zoom optic. IsAdjustableOptic = false
float FieldOfView;

// ScopeSmoothCameraData : MonoBehaviour, GInterface466, IAdjustableOpticData
//   Variable-zoom optic. IsAdjustableOptic = true
Vector3 MinMaxFieldOfView;        // x=maxFOV(minZoom), y=minFOV(maxZoom), z=step
AnimationCurve FieldOfViewCurve;
AnimationCurve ReticleBlendCurve;
float ZoomSensitivity;
float AdjustableOpticSensitivity;
// + all GInterface466 fields

// ScopeData : MonoBehaviour
ScopeReticle Reticle;
ScopeNightVisionData NightVisionData;
ScopeThermalVisionData ThermalVisionData;
ScopeEffectsData PostEffectsData;

// ScopeReticle : MonoBehaviour
Mesh Mesh;
Material Material;
Vector3 Position;
Vector3 Rotation;
float Scale;

// ScopeEffectsData : MonoBehaviour
bool ChromaticAberration;
int ChromaticAberrationAniso;
float ChromaticAberrationShift;
bool BloomOptimized; float BloomOptimizedIntensity, BloomOptimizedThreshold, BloomOptimizedBlurSize;
bool FastVignette, UltimateBloom, Fisheye;
bool Tonemapping; float White, AdaptionSpeed, ExposureAdjustment, MiddleGrey;
// + AdaptiveTextureSize, TonemapperType

// ScopeNightVisionData : MonoBehaviour
bool NightVision;
float Intensity, MaskSize, NoiseIntensity, NoiseScale;
Color Color;

// ScopeThermalVisionData : MonoBehaviour
bool ThermalVision, ThermalVisionIsGlitch, ThermalVisionIsPixelated;
bool ThermalVisionIsNoisy, ThermalVisionIsMotionBlurred, ThermalVisionIsFpsStuck;
ThermalVisionUtilities, StuckFPSUtilities, MotionBlurUtilities, GlitchUtilities, PixelationUtilities;
float ChromaticAberrationThermalShift, UnsharpBias, UnsharpRadiusBlur;
```

### 4.7 CollimatorSight — Red Dots / Holos

```csharp
// CollimatorSight : MonoBehaviour
// Red dot / holographic sight rendering

MeshRenderer CollimatorMeshRenderer;
Material CollimatorMaterial;

// Static events (global):
static event Action<CollimatorSight> OnCollimatorEnabled;
static event Action<CollimatorSight> OnCollimatorDisabled;
static event Action<CollimatorSight> OnCollimatorUpdated;

void LookAt(Vector3 point, Vector3 worldUp)
  // Rotates toward aim point with -90° X correction
  // Fires OnCollimatorUpdated

// Used by ScopeMaskRenderer to draw collimator areas in the scope mask
```

### 4.8 OpticCullingMask

```csharp
// EFT.PostEffects.OpticCullingMask : MonoBehaviour [RequireComponent(Camera)]
// Renders a vignette/culling mask on the optic camera to simulate scope tube

Shader: "Hidden/OpticCullingMask"
float _maskScale = 1f;  // Controls mask radius
// Draws a quad mesh via CommandBuffer at CameraEvent.BeforeGBuffer
```

---

## 5. Inventory & Item System

### 5.1 Sight Item Class Hierarchy

```
FunctionalModItemClass
  └── SightsItemClass (has SightComponent)
       ├── AssaultScopeItemClass
       ├── OpticScopeItemClass
       ├── SpecialScopeItemClass
       └── IronSightItemClass
```

Template counterparts: `SightsTemplateClass`, `AssaultScopeTemplateClass`, `OpticScopeTemplateClass`, etc.

### 5.2 SightComponent — Runtime Sight State

```csharp
// EFT.InventoryLogic.SightComponent : GClass3379

GInterface404 Template;          // SightsTemplateClass (static data)
int SelectedScope;               // Active scope index
int[] ScopesSelectedModes;       // Per-scope active mode index
int[] ScopesCurrentCalibPointIndexes;  // Per-scope zeroing index
float ScopeZoomValue;            // Smooth zoom value for adjustable optics
Vector3[][] OpticCalibrationPoints;    // Per-scope calibration point arrays

// Properties
int ScopesCount => Template.ScopesCount;
string CustomAimPlane => Template.CustomAimPlane;
IAdjustableOpticData AdjustableOpticData => Template;

int SelectedScopeIndex { get; set; }   // Wraps around ScopesCount
int SelectedScopeMode { get; set; }    // Wraps around ModesCount

float GetCurrentSensitivity  // from Template.AimSensitivity[scope][mode]
float GetCurrentOpticZoom()  // from Template.Zooms[scope][mode]
float GetMinOpticZoom()
float GetMaxOpticZoom()
bool HasCurrentZoomGreaterThenOne()

int[] GetScopeCalibrationDistances(int scopeIndex)
Vector3 GetCurrentOpticCalibrationPoint()
int GetCurrentOpticCalibrationDistance()
bool OpticCalibrationPointUp()   // Returns true if index actually changed
bool OpticCalibrationPointDown()
```

### 5.3 SightsTemplateClass — Static Sight Data

```csharp
// SightsTemplateClass : FunctionalModTemplateClass, GInterface404, IAdjustableOpticData

int ScopesCount;
int[] ModesCount;              // Per-scope number of modes
float[][] Zooms;               // [scopeIndex][modeIndex] = zoom multiplier
float[][] AimSensitivity;      // [scopeIndex][modeIndex] = sensitivity
int[][] CalibrationDistances;  // [scopeIndex][calibIndex] = meters
string CustomAimPlane;

// IAdjustableOpticData fields (for variable zoom scopes):
bool IsAdjustableOptic;
Vector3 MinMaxFov;             // x=maxFOV, y=minFOV, z=step
float ZoomSensitivity;
float AdjustableOpticSensitivity;
float AdjustableOpticSensitivityMax;
```

### 5.4 IAdjustableOpticData Interface

```csharp
// EFT.InventoryLogic.IAdjustableOpticData
bool IsAdjustableOptic { get; }
Vector3 MinMaxFov { get; }
float ZoomSensitivity { get; }
float AdjustableOpticSensitivity { get; }
float AdjustableOpticSensitivityMax { get; }
```

### 5.5 FirearmScopeStateStruct

```csharp
// Used for networking/syncing scope state
string Id;
int ScopeMode;
int ScopeIndexInsideSight;
int ScopeCalibrationIndex;

static bool IsScopeStatesDifferent(stateA, stateB)
```

---

## 6. Weapon System

### 6.1 FirearmHandsControllerClass

```csharp
// FirearmHandsControllerClass : GClass2963
// Manages the visual state of a held firearm

Weapon Weapon_0;
WeaponPrefab WeaponPrefab_0;
WeaponManagerClass WeaponManagerClass_1;

// Sight tracking
Dictionary<string, SightComponent> Dictionary_1;  // ID → SightComponent
SightModVisualControllers[] SightModVisualControllers_0;

void UpdateTacticalComboVisualControllers()
  // Scans weapon hierarchy for TacticalComboVisualController and SightModVisualControllers
  // Populates Dictionary_0 (lights) and Dictionary_1 (sights)
```

### 6.2 WeaponManagerClass

```csharp
// WeaponManagerClass : GClass2086, GInterface210
// High-level weapon management, scope state changes

event Action<ESmoothScopeState> OnSmoothScopeStateChanged;
event Action<float> OnSmoothSensetivityChange;

// References ScopeZoomHandler internally for adjustable optics
```

### 6.3 SightModVisualControllers

```csharp
// SightModVisualControllers : MonoBehaviour
// Bridges inventory SightComponent to visual scope prefab

SightComponent SightMod { get; set; }  // Setting triggers UpdateSightMode
ScopePrefabCache (private)
ScopeZoomHandler (private)

void UpdateSightMode(bool setupZeroModeAnyway)
  // Syncs ScopePrefabCache.SetMode() with SightComponent.ScopesSelectedModes

bool TryGetZoomHandler(out ScopeZoomHandler zoomHandler)
void ForceChangeScopeState()
```

---

## 7. Key Shader Properties & Global Textures

| Property Name | Type | Where Used |
|---------------|------|------------|
| `_SwitchToSight` | float | OpticSight lens fade (0 = visible, 0.97 = hidden) |
| `_ScopeMask` | Texture | ScopeMaskRenderer global mask texture |
| `_Color` | Color | ScopeMaskRenderer mask colors |
| `_Scale` | float | OpticRetrice reticle scale |
| `_NonJitteredProj` | Matrix4x4 | OpticRetrice non-jittered projection |
| `_MaskScale` | float | OpticCullingMask scope tube radius |

Key shaders:
- `"Hidden/ScopeMask"` — Scope mask stencil rendering
- `"CW FX/Collimator"` — Collimator dot rendering
- `"Hidden/OpticCullingMask"` — Scope tube vignette

Shader lookups use `GClass872.Find(shaderName)` instead of `Shader.Find()`.

---

## 8. Enums

### ESmoothScopeState
```csharp
Min,          // Minimum zoom
SmoothValue,  // Intermediate zoom
Max           // Maximum zoom
```

### EPlayerState (partial — for animation context)
```
Idle, Run, ProneMove, ProneIdle, Stationary, ...
```

---

## 9. Common Patterns for SPT Modding

### 9.1 Harmony Patching Targets

**Scope enable/disable flow:**
1. `OpticSight.OnEnable()` → Sets `OpticSightState.Value`, clears reticle, fades lens
2. `OpticSight.OnDisable()` → Reverse
3. `GClass3687` (OpticCameraManager) → `OnOpticEnabled` / `OnOpticDisabled` events
4. `OpticComponentUpdater.CopyComponentFromOptic(OpticSight)` → Configures the PiP camera

**Reticle rendering:**
1. `OpticRetrice.SetOpticSight(OpticSight)` → Sets mesh + material from `ScopeData.Reticle`
2. `OpticRetrice.UpdateTransform(OpticSight)` → Positions with `ScopeReticle.Position/Rotation/Scale`
3. `OpticRetrice.OnPreCull()` → Rebuilds CommandBuffer every frame with `_Scale` + `_NonJitteredProj`

**Scope mask:**
1. `ScopeMaskRenderer.method_4(Camera)` → Pre-cull callback, rebuilds mask
2. `ScopeMaskRenderer.method_8(CommandBuffer)` → Draws optic lens (red)
3. `ScopeMaskRenderer.method_9(CommandBuffer, Camera)` → Draws collimators (blue)
4. `ScopeMaskRenderer.method_10(CommandBuffer)` → Draws active collimator dot (green)

**Variable zoom:**
1. `ScopeZoomHandler.Init(SightComponent)` → Sets up zoom range from `IAdjustableOpticData`
2. `ScopeZoomHandler.UpdateScope()` → Called in `OpticComponentUpdater.LateUpdate()`
3. `ScopeZoomHandler.OnSmoothScopeStateChanged` event
4. `ScopeZoomHandler.FiledOfView` property

**FOV / Aiming:**
1. `Player.OnSightChangedEvent` — fires when player switches sights
2. `Player.OnSmoothSightChange` — fires when adjustable scope zoom state changes
3. `CameraClass.OnFovChanged` — fires when camera FOV changes
4. `Singleton<SharedGameSettingsClass>.Instance.Game.Settings.FieldOfView` — user FOV setting

### 9.2 Accessing the Optic Camera

```csharp
// The optic camera manager
var opticManager = CameraClass.Instance.OpticCameraManager; // GClass3687

// The optic PiP camera
Camera opticCamera = opticManager.Camera;

// The component updater (handles post-effects sync)
OpticComponentUpdater updater = opticManager.Updater;

// Currently active OpticSight
OpticSight currentSight = opticManager.CurrentOpticSight;

// Reticle renderer
OpticRetrice reticle = opticManager.OpticRetrice;

// Render texture
RenderTexture rt = opticManager.RenderTexture_0;

// Events
opticManager.OnOpticEnabled += () => { /* scope activated */ };
opticManager.OnOpticDisabled += () => { /* scope deactivated */ };
opticManager.OnOpticTexturesAreChanged += (tex, depthTex) => { /* textures swapped */ };
```

### 9.3 Accessing Scope Data from an OpticSight

```csharp
OpticSight sight = ...; // e.g., from ScopePrefabCache.CurrentModOpticSight

// Camera data (FOV, clip planes)
GInterface466 cameraData = sight.CameraData;
bool isAdjustable = cameraData.IsAdjustableOptic;

// If adjustable:
ScopeSmoothCameraData smoothData = (ScopeSmoothCameraData)cameraData;
ScopeZoomHandler zoomHandler = smoothData.ScopeZoomHandler;

// Visual data
ScopeData scopeData = sight.ScopeData;
ScopeReticle reticle = scopeData.Reticle;        // Mesh, Material, Position, Rotation, Scale
ScopeEffectsData effects = scopeData.PostEffectsData;  // Chromatic aberration, bloom, etc.
ScopeNightVisionData nv = scopeData.NightVisionData;
ScopeThermalVisionData thermal = scopeData.ThermalVisionData;

// Lens renderer (for fade/transparency)
Renderer lens = sight.LensRenderer;
// Use _SwitchToSight shader property: 0 = visible, 0.97 = hidden

// Distance to camera (for PiP sizing)
float dist = sight.DistanceToCamera;
```

### 9.4 Scope Hierarchy on a Weapon

```
Weapon GameObject
  └── [mod attachments...]
       └── Sight Mod GameObject
            ├── SightModVisualControllers (bridges inventory ↔ visual)
            ├── ScopePrefabCache (manages modes)
            ├── ScopeZoomHandler (if adjustable zoom)
            └── mode_0 / mode_1 / ... (child GameObjects, toggled by ScopePrefabCache.SetMode)
                 ├── CollimatorSight (if red dot mode)
                 │    └── MeshRenderer (collimator dot mesh)
                 └── OpticSight (if magnified mode)
                      ├── Renderer LensRenderer
                      └── ScopeData (child, SetActive(false) by default)
                           ├── ScopeReticle
                           ├── ScopeEffectsData
                           ├── ScopeNightVisionData
                           └── ScopeThermalVisionData
```

### 9.5 Hooking Into the Scope Lifecycle (Typical Mod Pattern)

```csharp
// 1. Detect when a scope activates
// Patch: OpticSight.OnEnable() or subscribe to OpticSight.OpticSightState

// 2. Modify the PiP camera (e.g., disable PiP, change FOV)
// Patch: OpticComponentUpdater.CopyComponentFromOptic() or .LateUpdate()

// 3. Modify the reticle
// Patch: OpticRetrice.SetOpticSight() or .UpdateTransform() or .OnPreCull()

// 4. Modify the scope mask
// Patch: ScopeMaskRenderer.method_5() (main render method) or individual method_7-10

// 5. Modify zoom behavior
// Patch: ScopeZoomHandler.UpdateScope() or .Init()

// 6. Access scope housing meshes
// Navigate: OpticSight.gameObject → find child MeshRenderers/MeshFilters
// The lens is at OpticSight.LensRenderer
// Housing mesh is typically sibling/parent of OpticSight

// 7. Player-level scope events
// Player.OnSightChangedEvent — sight was switched
// Player.OnSmoothSightChange — adjustable zoom state changed
```

---

## 10. Important Obfuscated Class Names

| GClass/GInterface | Likely Purpose |
|-------------------|----------------|
| `GClass3687` | OpticCameraManager (on CameraClass) |
| `GInterface466` | Scope camera data interface (FOV, clip planes, culling) |
| `GInterface404` | Sight template data interface (zooms, modes, calibration) |
| `GClass3379` | Base item component class (SightComponent inherits from this) |
| `GClass2963` | Base firearm hands controller |
| `GClass2086` | Base weapon manager |
| `GClass872` | Shader utility (has `Find(string)` method) |
| `GClass1001` | SSAA-aware CommandBuffer helper |
| `GStruct432` | OpticSight state struct (OpticSight + IsEnabled) |
| `BindableStateClass<T>` | Observable state wrapper with `.Value` |
| `ESmoothScopeState` | Enum: Min, SmoothValue, Max |

---

## 11. Post-Processing & Visual Effects

Accessed via `CameraClass.Instance`:

| Property | Type | Notes |
|----------|------|-------|
| `.NightVision` | NightVision | NV goggles effect |
| `.ThermalVision` | ThermalVision | Thermal vision effect |
| `.VisorEffect` | VisorEffect | Helmet visor rendering |
| `.EffectsController` | EffectsController | General effects management |
| `.PostFX` | GradingPostFX | Color grading |

On the optic camera (`OpticComponentUpdater`):
- `TOD_Scattering` / `MBOIT_Scattering` — atmospheric scattering (synced from main camera)
- `PostProcessLayer` — Unity post-processing (TAA, etc., synced from main camera)
- `VolumetricLightRenderer` — volumetric lighting
- `Undithering` — dithering cleanup
- `ChromaticAberration` — per-scope chromatic aberration
- `BloomOptimized` — per-scope bloom
- `ThermalVision` / `NightVision` — per-scope NV/thermal
- `CC_FastVignette`, `UltimateBloom`, `Fisheye`, `Tonemapping` — per-scope effects

---

## 12. Summary of Key Files by Category

### Scope/Optics (read these first for scope modding)
- `EFT/CameraControl/OpticComponentUpdater.cs` — PiP camera controller
- `EFT/CameraControl/OpticSight.cs` — Core magnified sight component
- `EFT/CameraControl/OpticRetrice.cs` — Reticle CommandBuffer renderer
- `EFT/CameraControl/ScopeData.cs` — Scope data container
- `EFT/CameraControl/ScopeReticle.cs` — Reticle mesh/material/transform
- `EFT/CameraControl/ScopeCameraData.cs` — Fixed-zoom camera data
- `EFT/CameraControl/ScopeSmoothCameraData.cs` — Variable-zoom camera data
- `EFT/CameraControl/ScopeEffectsData.cs` — Per-scope post effects
- `EFT/CameraControl/ScopeNightVisionData.cs` — Scope NV settings
- `EFT/CameraControl/ScopeThermalVisionData.cs` — Scope thermal settings
- `ScopeZoomHandler.cs` — Variable zoom interpolation
- `ScopePrefabCache.cs` — Scope mode management
- `ScopeMaskRenderer.cs` — Scope stencil mask
- `CollimatorSight.cs` — Red dot/holo rendering
- `SightModVisualControllers.cs` — Inventory ↔ visual bridge
- `EFT/InventoryLogic/SightComponent.cs` — Runtime sight state
- `SightsItemClass.cs` / `SightsTemplateClass.cs` — Inventory item/template
- `EFT/InventoryLogic/IAdjustableOpticData.cs` — Variable zoom interface
- `EFT/PostEffects/OpticCullingMask.cs` — Scope tube vignette
- `GClass3687.cs` — OpticCameraManager

### Core Game
- `CameraClass.cs` — Camera singleton, post-effects, optic manager
- `EFT/Player.cs` — Player class (massive, ~2500+ lines)
- `EFT/LocalPlayer.cs` / `EFT/ClientPlayer.cs` — Player subclasses
- `EFT/GameWorld.cs` — World management, events
- `EFT/AbstractGame.cs` — Game lifecycle base
- `EFT/BaseLocalGame.cs` — SPT game mode base

### Weapons
- `FirearmHandsControllerClass.cs` — Held weapon management
- `WeaponManagerClass.cs` — Weapon state + scope events
- `EFT/Animations/ProceduralWeaponAnimation.cs` — Weapon animation system

### Items
- `AssaultScopeItemClass.cs`, `OpticScopeItemClass.cs`, `SpecialScopeItemClass.cs`, `IronSightItemClass.cs` — Sight item subtypes
- `FirearmScopeStateStruct.cs` — Scope state for networking
