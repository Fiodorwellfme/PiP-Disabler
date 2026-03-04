# Tarkov Decompiled Assembly: Subsystem Behavior & Hooking Guide
This guide is a **behavioral explanation** of the decompiled assembly, generated after scanning every `.cs` file in `Tarkov Assembly decompiled.zip` (8,683 files).
It focuses on **how subsystems work**, **how they relate**, and **where practical hook points exist** for instrumentation/modding.

## How to read this guide
- Treat each namespace as a subsystem boundary unless explicitly split by folder-level domains.
- “Hook points” below are method families repeatedly present in that subsystem and are generally stable interception seams (lifecycle, init/dispose, process pipelines, trigger callbacks).
- Because this is decompiled source, symbol quality varies (including obfuscated classes). The guide prioritizes repeated structural signals over fragile one-off names.

## Global architecture
- Total C# files scanned: **8683**.
- Total namespaces (subsystems): **211**.
- Root/global namespace footprint: **6024 files**, indicating a large core model and glue layer outside explicit namespaces.

### Primary subsystem clusters
- `(global)` — 6024 files, 8723 type declarations. Interpreted role: Core gameplay/domain models (obfuscated + root-level glue).
- `EFT.UI` — 462 files, 948 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT` — 275 files, 829 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.Hideout` — 178 files, 288 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.InventoryLogic` — 114 files, 205 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.Interactive` — 97 files, 159 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `RootMotion.FinalIK` — 73 files, 121 type declarations. Interpreted role: Animation IK and rig logic.
- `EFT.Quests` — 68 files, 83 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `GPUInstancer` — 60 files, 80 type declarations. Interpreted role: Rendering/instancing optimization.
- `EFT.UI.DragAndDrop` — 53 files, 91 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `Koenigz.PerfectCulling.EFT` — 53 files, 91 type declarations. Interpreted role: Culling/performance pipeline.
- `ChartAndGraph` — 51 files, 77 type declarations. Interpreted role: UI graphing widgets.
- `EFT.UI.Ragfair` — 41 files, 82 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `Audio.AmbientSubsystem` — 38 files, 47 type declarations. Interpreted role: Audio simulation and emitters.
- `EFT.GameTriggers` — 34 files, 74 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.GlobalEvents` — 34 files, 39 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.UI.Matchmaker` — 29 files, 65 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.Visual` — 29 files, 37 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `Audio.SpatialSystem` — 24 files, 30 type declarations. Interpreted role: Audio simulation and emitters.
- `EFT.UI.Chat` — 24 files, 48 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `EFT.Animations` — 23 files, 33 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `Audio.AmbientSubsystem.Data` — 22 files, 24 type declarations. Interpreted role: Audio simulation and emitters.
- `EFT.Weather` — 20 files, 29 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).
- `AmplifyImpostors` — 18 files, 18 type declarations. Interpreted role: Impostor rendering.
- `EFT.Hideout.ShootingRange` — 18 files, 36 type declarations. Interpreted role: Escape from Tarkov gameplay stack (player, items, raid flow, UI).

## Cross-subsystem relation model
- **Dependency direction** is inferred from `using` statements: when subsystem A imports subsystem B heavily, A usually depends on B abstractions/services.
- **Execution direction** is inferred from hook families: Unity lifecycle (`Awake/Start/Update/...`) tends to be entry-flow; `Handle/Process/Execute` are mid-pipeline; `Dispose/OnDestroy` closes resources.
- **Data ownership** is commonly in model-heavy namespaces (`EFT`, `EFT.InventoryLogic`, global root), while UI/visual namespaces reactively consume and display that state.

### Strongest namespace-to-namespace dependencies (top 120)
- `(global)` -> `EFT` (`using` references: 1739)
- `(global)` -> `EFT.InventoryLogic` (`using` references: 755)
- `(global)` -> `EFT.NextObservedPlayer` (`using` references: 130)
- `(global)` -> `EFT.UI` (`using` references: 126)
- `EFT.UI` -> `EFT.InventoryLogic` (`using` references: 119)
- `(global)` -> `EFT.Interactive` (`using` references: 109)
- `(global)` -> `EFT.Quests` (`using` references: 98)
- `(global)` -> `EFT.Communications` (`using` references: 75)
- `(global)` -> `Audio.SpatialSystem` (`using` references: 63)
- `(global)` -> `EFT.HealthSystem` (`using` references: 52)
- `(global)` -> `BitPacking` (`using` references: 51)
- `(global)` -> `Diz.Binding` (`using` references: 49)
- `EFT.UI` -> `EFT.InputSystem` (`using` references: 48)
- `EFT.Hideout` -> `EFT.UI` (`using` references: 45)
- `EFT.UI` -> `EFT.UI.Screens` (`using` references: 45)
- `(global)` -> `EFT.Hideout` (`using` references: 44)
- `EFT.UI.DragAndDrop` -> `EFT.InventoryLogic` (`using` references: 44)
- `EFT` -> `EFT.InventoryLogic` (`using` references: 43)
- `(global)` -> `EFT.Game.Spawning` (`using` references: 40)
- `(global)` -> `EFT.InputSystem` (`using` references: 40)
- `EFT.Hideout` -> `EFT.InventoryLogic` (`using` references: 38)
- `EFT.UI` -> `EFT.UI.DragAndDrop` (`using` references: 38)
- `(global)` -> `EFT.Ballistics` (`using` references: 37)
- `(global)` -> `Diz.LanguageExtensions` (`using` references: 34)
- `(global)` -> `EFT.SynchronizableObjects` (`using` references: 32)
- `(global)` -> `EFT.Vaulting.Models` (`using` references: 32)
- `(global)` -> `ChatShared` (`using` references: 30)
- `(global)` -> `EFT.UI.Ragfair` (`using` references: 28)
- `(global)` -> `AnimationEventSystem` (`using` references: 28)
- `EFT.UI` -> `EFT.Quests` (`using` references: 28)
- `(global)` -> `EFT.AssetsManager` (`using` references: 27)
- `EFT.UI` -> `EFT.HealthSystem` (`using` references: 26)
- `(global)` -> `EFT.Weather` (`using` references: 24)
- `(global)` -> `EFT.UI.Screens` (`using` references: 24)
- `(global)` -> `JsonType` (`using` references: 22)
- `(global)` -> `EFT.Dialogs` (`using` references: 22)
- `(global)` -> `FlyingWormConsole3.LiteNetLib` (`using` references: 21)
- `(global)` -> `EFT.Animations` (`using` references: 21)
- `EFT` -> `EFT.Interactive` (`using` references: 21)
- `EFT` -> `EFT.UI` (`using` references: 21)
- `(global)` -> `EFT.Network` (`using` references: 19)
- `(global)` -> `EFT.Customization` (`using` references: 18)
- `(global)` -> `Audio.SpatialSystem.Data` (`using` references: 18)
- `(global)` -> `EFT.Vaulting` (`using` references: 17)
- `(global)` -> `EFT.CameraControl` (`using` references: 16)
- `(global)` -> `Koenigz.PerfectCulling.EFT` (`using` references: 16)
- `(global)` -> `MultiFlare` (`using` references: 16)
- `Audio.AmbientSubsystem` -> `EFT` (`using` references: 16)
- `EFT.UI.Ragfair` -> `EFT.InventoryLogic` (`using` references: 16)
- `(global)` -> `GPUInstancer` (`using` references: 15)
- `(global)` -> `EFT.EnvironmentEffect` (`using` references: 15)
- `EFT.Interactive` -> `EFT.InventoryLogic` (`using` references: 15)
- `(global)` -> `EFT.WeaponMounting` (`using` references: 14)
- `(global)` -> `EFT.GlobalEvents` (`using` references: 14)
- `(global)` -> `EFT.Settings.Graphics` (`using` references: 14)
- `(global)` -> `Systems.Effects` (`using` references: 14)
- `(global)` -> `Koenigz.PerfectCulling` (`using` references: 14)
- `EFT.UI.Chat` -> `ChatShared` (`using` references: 14)
- `(global)` -> `ChartAndGraph` (`using` references: 13)
- `(global)` -> `EFT.MovingPlatforms` (`using` references: 13)
- `(global)` -> `Bsg.GameSettings` (`using` references: 13)
- `(global)` -> `RootMotion.FinalIK` (`using` references: 13)
- `(global)` -> `EFT.NetworkPackets` (`using` references: 13)
- `(global)` -> `EFT.Vehicle` (`using` references: 12)
- `(global)` -> `EFT.Hideout.ShootingRange` (`using` references: 12)
- `(global)` -> `EFT.Vaulting.Debug.View` (`using` references: 12)
- `EFT.UI` -> `EFT.UI.Ragfair` (`using` references: 12)
- `EFT` -> `EFT.CameraControl` (`using` references: 11)
- `EFT` -> `EFT.HealthSystem` (`using` references: 11)
- `EFT.UI` -> `EFT.Communications` (`using` references: 11)
- `EFT.UI` -> `Diz.Binding` (`using` references: 11)
- `EFT.UI` -> `EFT.Hideout` (`using` references: 11)
- `EFT.Visual` -> `EFT.InventoryLogic` (`using` references: 11)
- `(global)` -> `EFT.Counters` (`using` references: 10)
- `(global)` -> `CommonAssets.Scripts.Audio` (`using` references: 10)
- `EFT` -> `EFT.AssetsManager` (`using` references: 10)
- `EFT` -> `EFT.Ballistics` (`using` references: 10)
- `UI.Hideout` -> `EFT.Hideout` (`using` references: 10)
- `(global)` -> `EFT.HandBook` (`using` references: 9)
- `EFT` -> `JsonType` (`using` references: 9)
- `EFT` -> `EFT.InputSystem` (`using` references: 9)
- `EFT.UI.Matchmaker` -> `EFT.InputSystem` (`using` references: 9)
- `EFT.UI.Matchmaker` -> `EFT.UI.Screens` (`using` references: 9)
- `(global)` -> `AmplifyMotion` (`using` references: 8)
- `(global)` -> `Audio.AmbientSubsystem` (`using` references: 8)
- `(global)` -> `RuntimeInspector` (`using` references: 8)
- `(global)` -> `Audio.Data` (`using` references: 8)
- `EFT` -> `EFT.GlobalEvents` (`using` references: 8)
- `EFT.UI` -> `EFT.UI.WeaponModding` (`using` references: 8)
- `EFT.UI.Gestures` -> `EFT.InputSystem` (`using` references: 8)
- `EFT.UI.Settings` -> `Bsg.GameSettings` (`using` references: 8)
- `EFT.Visual` -> `Diz.Skinning` (`using` references: 8)
- `UI.Hideout` -> `EFT.InventoryLogic` (`using` references: 8)
- `(global)` -> `Diz.Jobs` (`using` references: 7)
- `(global)` -> `EFT.Trading` (`using` references: 7)
- `(global)` -> `Interpolation` (`using` references: 7)
- `(global)` -> `EFT.RocketLauncher.Explosion` (`using` references: 7)
- `Audio.AmbientSubsystem` -> `Audio.AmbientSubsystem.Data` (`using` references: 7)
- `Audio.SpatialSystem` -> `EFT` (`using` references: 7)
- `Audio.SpatialSystem.Data` -> `Audio.Data` (`using` references: 7)
- `EFT` -> `EFT.Game.Spawning` (`using` references: 7)
- `EFT` -> `EFT.UI.Screens` (`using` references: 7)
- `EFT` -> `EFT.SynchronizableObjects` (`using` references: 7)
- `EFT.Hideout` -> `EFT.InputSystem` (`using` references: 7)
- `EFT.Interactive` -> `Audio.SpatialSystem` (`using` references: 7)
- `EFT.UI` -> `EFT.Customization` (`using` references: 7)
- `EFT.UI.Insurance` -> `EFT.UI.DragAndDrop` (`using` references: 7)
- `(global)` -> `EFT.Airdrop` (`using` references: 6)
- `(global)` -> `EFT.InventoryLogic.Operations` (`using` references: 6)
- `(global)` -> `EFT.UI.DragAndDrop` (`using` references: 6)
- `(global)` -> `EFT.Rendering.Clouds` (`using` references: 6)
- `(global)` -> `CommonAssets.Scripts.Audio.RadioSystem` (`using` references: 6)
- `(global)` -> `EFT.UI.Chat` (`using` references: 6)
- `Audio.AmbientSubsystem` -> `Audio.SpatialSystem` (`using` references: 6)
- `EFT` -> `EFT.MovingPlatforms` (`using` references: 6)
- `EFT` -> `Systems.Effects` (`using` references: 6)
- `EFT` -> `EFT.Communications` (`using` references: 6)
- `EFT` -> `AnimationEventSystem` (`using` references: 6)
- `EFT.HandBook` -> `EFT.UI` (`using` references: 6)
- `EFT.Hideout` -> `EFT.UI.Screens` (`using` references: 6)

## Subsystem-by-subsystem deep guide

### Subsystem: `(global)`
**Role hypothesis:** Core gameplay/domain models (obfuscated + root-level glue).

**Structure snapshot**
- Files: 6024.
- Declared types: 8723 (classes: 7137, structs: 669, interfaces: 550, enums: 367).
- Method declarations: 30220. Field declarations: 34049.
- Dominant folders: `(root)` (6023), `Properties` (1).

**How it works (operational pattern)**
- This layer behaves like a mixed kernel: data contracts, base classes, generated/obfuscated logic, and cross-cutting helpers live here. Most higher namespaces appear to lean on this layer for primitives and utility abstractions.
- Expect business rules and state containers to be anchored here, with feature namespaces acting as orchestrators/adapters on top.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Dispose` (591), `Update` (293), `Start` (220), `Awake` (170), `Init` (153), `OnDestroy` (131), `Execute` (119), `OnRenderImage` (82), `OnDisable` (64), `OnEnable` (60), `OnDrawGizmosSelected` (59), `OnDrawGizmos` (51), `Initialize` (51), `LateUpdate` (41), `OnValidate` (38), `Run` (35), `OnActivate` (28), `Apply` (26), `OnStateEnter` (25), `OnStateExit` (23).
- High-frequency methods worth tracing for behavior flow: `method_0` (2407), `method_1` (1183), `method_2` (777), `Dispose` (591), `method_3` (581), `method_4` (440), `ToString` (387), `method_5` (378), `smethod_0` (357), `method_6` (331), `Serialize` (302), `Update` (293).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `System.Linq`, `Comfort.Common`, `System.Threading.Tasks`, `System.Threading`, `Newtonsoft.Json`.

**Key elements in this subsystem**
- `ABossLogic`, `AbstractAchievementControllerClass`, `AbstractMaterialBumpedSpecularSMap`, `AbstractMaterialSpeedVertPaintShaderSolid`, `AbstractPrestigeControllerClass`, `Class2139`, `AbstractQuestClass`, `Class3540`, `Class3541`, `Class3542`, `AbstractQuestControllerClass`, `AbstractSkillClass`, `AbstractSuppressStationary`, `AchievementControllerClass`, `Class3615`, `AchievementDataClass`, `AchievementsBookClass`, `Class2137`, `Class2138`, `AchievementTaskClass`, `ActionsReturnClass`, `Class2737`, `ActionsTypesClass`, `ActionTrigger`, `ActorDataStruct`, `Class131`, `AdditionalHostilitySettings`, `ChancedEnemy`, `AdditionalHotObjects`, `AdditionalNavmeshBlock`, `AddNoteDescriptorClass`, `AddNoteOperationClass`, `AddViewListClass`, `Class3151`, `Class3152`, `AdvancedLight`, `LightTypeEnum`, `ShadingTypeEnum`, `AGSMachineryBones`, `AIBossPlayer`, ... (+8683 more).

### Subsystem: `AbsolutDecals`
**Role hypothesis:** AbsolutDecals subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 7 (classes: 4, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 55. Field declarations: 59.
- Dominant folders: `AbsolutDecals` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (3), `Update` (2), `Start` (2), `OnDrawGizmos` (1), `OnDrawGizmosSelected` (1), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `OnDestroy` (3), `BakeDecal` (3), `method_0` (3), `Update` (2), `Start` (2), `DrawGizmo` (1), `InitializeMesh` (1), `OnDrawGizmos` (1), `OnDrawGizmosSelected` (1), `BakeToMesh` (1), `BakeToUniqueMesh` (1), `ResetState` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.Ballistics`, `System.Collections.Generic`, `System.Collections`.

**Key elements in this subsystem**
- `DecalProjector`, `ProjectionDirections`, `ProjectorState`, `DecalSystem`, `BakeResult`, `SingleDecal`, `DecalTester`.

### Subsystem: `AchievementsSystem`
**Role hypothesis:** AchievementsSystem subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 3. Field declarations: 12.
- Dominant folders: `AchievementsSystem` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Show` (1), `method_0` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `EFT.UI`, `UnityEngine`, `UnityEngine.EventSystems`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `AchievementIconView`.

### Subsystem: `AmplifyImpostors`
**Role hypothesis:** Impostor rendering.

**Structure snapshot**
- Files: 18.
- Declared types: 18 (classes: 5, structs: 0, interfaces: 0, enums: 13).
- Method declarations: 16. Field declarations: 106.
- Dominant folders: `AmplifyImpostors` (18).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `RenderImpostor` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1), `GenerateAutomaticMesh` (1), `GenerateMesh` (1), `smethod_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `UnityEngine.Experimental.Rendering`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `AmplifyImpostor`, `AmplifyImpostorAsset`, `AmplifyImpostorBakePreset`, `CutMode`, `DeferredBuffers`, `FolderMode`, `ImageFormat`, `ImpostorType`, `LODReplacement`, `OverrideMask`, `PresetPipeline`, `RenderingMaps`, `RenderPipelineInUse`, `TextureChannels`, `TextureCompression`, `TextureOutput`, `TextureScale`, `VersionInfo`.

### Subsystem: `AmplifyMotion`
**Role hypothesis:** AmplifyMotion subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 6 (classes: 2, structs: 2, interfaces: 0, enums: 2).
- Method declarations: 17. Field declarations: 31.
- Dominant folders: `AmplifyMotion` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `vmethod_0` (1), `vmethod_1` (1), `vmethod_2` (1), `vmethod_3` (1), `vmethod_4` (1), `vmethod_5` (1), `method_0` (1), `smethod_0` (1), `smethod_1` (1), `smethod_2` (1), `smethod_3` (1), `GetRow` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `MotionState`, `Struct1313`, `Struct1314`, `ObjectType`, `Quality`, `VersionInfo`.

### Subsystem: `AnimationEventSystem`
**Role hypothesis:** AnimationEventSystem subsystem.

**Structure snapshot**
- Files: 14.
- Declared types: 22 (classes: 12, structs: 3, interfaces: 1, enums: 6).
- Method declarations: 74. Field declarations: 63.
- Dominant folders: `AnimationEventSystem` (14).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnStateEnter` (2), `OnStateUpdate` (2), `OnStateExit` (2), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `Equals` (6), `Clone` (4), `method_0` (4), `Serialize` (3), `Deserialize` (3), `ToString` (3), `method_3` (3), `GetHashCode` (2), `OnStateEnter` (2), `OnStateUpdate` (2), `OnStateExit` (2), `method_1` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`, `System.IO`, `JetBrains.Annotations`, `NLog`, `System.Linq`, `System.Globalization`.

**Key elements in this subsystem**
- `AnimationEvent`, `AnimationEventParameter`, `AnimationEventsContainer`, `EUpdateType`, `Class392`, `Struct253`, `AnimationEventsEmitter`, `GClass715`, `EEmitType`, `Struct254`, `AnimationEventsSequenceData`, `GStruct142`, `AnimationEventsStateBehaviour`, `AnimatorControllerStaticData`, `EAnimationEventParamType`, `EEventConditionModes`, `EEventConditionParamTypes`, `EventCondition`, `EConditionType`, `EventsCollection`, `IAnimatorEventParameter`, `LActionSetup`.

### Subsystem: `AnimationSystem.RootMotionTable`
**Role hypothesis:** AnimationSystem subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 6 (classes: 3, structs: 3, interfaces: 0, enums: 0).
- Method declarations: 8. Field declarations: 18.
- Dominant folders: `AnimationSystem/RootMotionTable` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `GetParameter` (2), `GetClipIndex` (1), `LoadNodes` (1), `IsValidStateToStoreRotation` (1), `IsValidClipForCurvesDP` (1), `GetValue` (1), `GetStep` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.IO`.

**Key elements in this subsystem**
- `CharacterClipsKeeper`, `LayerData`, `AnimationClipData`, `RootMotionBlendTable`, `ParameterSettings`, `ParameterRelatedCurve`.

### Subsystem: `Arena.UI`
**Role hypothesis:** Arena subsystem.

**Structure snapshot**
- Files: 6.
- Declared types: 8 (classes: 3, structs: 4, interfaces: 0, enums: 1).
- Method declarations: 19. Field declarations: 38.
- Dominant folders: `Arena/UI` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (2), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (2), `method_0` (2), `method_1` (2), `method_2` (2), `method_3` (2), `Awake` (1), `UpdateStatusLabel` (1), `NicknameForceSubmit` (1), `ValidationCallback` (1), `method_4` (1), `method_5` (1), `method_6` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `TMPro`, `UnityEngine.Events`, `UnityEngine.UI`, `System.Collections.Generic`, `EFT.InventoryLogic`, `PlayerIcons`, `System.Text.RegularExpressions`.

**Key elements in this subsystem**
- `ArenaToggleStateToImageColor`, `EToggleState`, `FaceCardColorNode`, `FaceCardColorTulpe`, `FaceCardView`, `NicknameField`, `in`, `Struct274`.

### Subsystem: `Audio`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 1, structs: 0, interfaces: 1, enums: 1).
- Method declarations: 6. Field declarations: 7.
- Dominant folders: `Audio` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `SetActive` (1), `CheckVolumeCoroutine` (1), `ResetPlugin` (1), `UpdateAudioData` (1), `UpdateTimeDelta` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections`, `UnityEngine`.

**Key elements in this subsystem**
- `EAudioLogLevel`, `ISettingsFromJson`, `MetaXRPluginErrorChecker`.

### Subsystem: `Audio.ActiveHeadphones.Debug`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 3, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 2. Field declarations: 28.
- Dominant folders: `Audio/ActiveHeadphones/Debug` (4).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Clone` (1), `CreateRealTemplate` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.ActiveHeadphones`.

**Key elements in this subsystem**
- `EditorHeadphonesTemplate`, `EditorHeadphonesTemplates`, `EHeadphonesType`, `HeadphonesTemplateStorage`.

### Subsystem: `Audio.AmbientSubsystem`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 38.
- Declared types: 47 (classes: 43, structs: 1, interfaces: 2, enums: 1).
- Method declarations: 216. Field declarations: 248.
- Dominant folders: `Audio/AmbientSubsystem` (38).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (15), `OnDestroy` (13), `OnPlay` (7), `Dispose` (6), `Init` (5), `Update` (2), `OnStop` (2), `Initialize` (1), `LateUpdate` (1), `Start` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (15), `method_0` (14), `OnDestroy` (13), `method_1` (11), `method_2` (11), `method_3` (8), `method_4` (8), `method_5` (7), `GetClip` (7), `OnPlay` (7), `ChangeSoundContent` (7), `method_6` (6).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT`, `UnityEngine.Audio`, `Audio.AmbientSubsystem.Data`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `Audio.SpatialSystem`, `Comfort.Common`, `System.Collections`, `EFT.Weather`.

**Key elements in this subsystem**
- `AmbientAudioSystem`, `AmbientSoundBlender`, `AmbientSoundPlayer`, `AmbientSoundPlayerGroup`, `Class814`, `AmbientSoundPlayerGroupController`, `BaseAmbientSoundPlayer`, `BaseRandomAmbientSoundPlayer`, `DayTimeAmbientBlender`, `DayTimeAmbientSeasonClips`, `DayTimeAmbientSoundContainer`, `EDayTime`, `EnvironmentSoundBlendSystem`, `EventAudioClipChanger`, `EventAudioClips`, `EventGameObjectChanger`, `EventGameObjects`, `EventLoopPlayer`, `EventRandomPlayer`, `EventSoundBankChanger`, `EventSoundBanks`, `EventSoundContentChangerAbstract`, `EventTimeDependentSoundChanger`, `IEnvironmentMixerParamsHandler`, `ISoundPlayer`, `LoopAmbientSoundPlayer`, `OneShotAmbientSoundPlayer`, `PrecipitationAmbientBlender`, `ReverbPresets`, `RoomAmbientData`, `RoomAmbientSoundPlayer`, `SeasonAmbientSoundPlayer`, `SeasonSoundBanks`, `SeasonRandomTimeRange`, `SeasonLoopAmbientSoundPlayer`, `SeasonSoundClips`, `SoundPlayerRandomPointComponent`, `SoundPlayerRoomObserverComponent`, `SoundPoint`, `SoundPointsManager`, ... (+7 more).

### Subsystem: `Audio.AmbientSubsystem.AmbientSplineEmitter`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 6.
- Declared types: 7 (classes: 6, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 65. Field declarations: 47.
- Dominant folders: `Audio/AmbientSubsystem/AmbientSplineEmitter` (6).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (5), `Init` (4), `OnAwake` (2), `Tick` (2), `OnTick` (2), `OnLateTick` (2), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `OnDestroy` (5), `Init` (4), `method_0` (3), `method_1` (3), `method_2` (3), `OnAwake` (2), `Translate` (2), `SetSpreadRange` (2), `UpdateSpread` (2), `UpdateSpatialBlend` (2), `ScaleMaxDistance` (2), `FadeOut` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `BezierSplineTools`, `Unity.Collections`, `Unity.Jobs`, `System.Collections`, `System.Collections.Generic`, `Audio.AmbientSubsystem.PathMoverStrategy`.

**Key elements in this subsystem**
- `AbstractSplineMappedEmitter`, `AmbientPlayerSplineMappedEmitter`, `AmbientSplineEmitterController`, `BaseSplineEmitterCalculator`, `SoundAmbientZoneCalculator`, `SplineEmitterPathMover`, `EMovementAction`.

### Subsystem: `Audio.AmbientSubsystem.AmbientSplineEmitter.SplineSoundEmitter`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 4.
- Declared types: 6 (classes: 5, structs: 1, interfaces: 0, enums: 0).
- Method declarations: 26. Field declarations: 30.
- Dominant folders: `Audio/AmbientSubsystem/AmbientSplineEmitter/SplineSoundEmitter` (4).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnAwake` (3), `OnDestroy` (2), `OnTriggered` (2), `Awake` (1), `Init` (1), `Tick` (1), `Execute` (1).
- High-frequency methods worth tracing for behavior flow: `OnAwake` (3), `method_0` (2), `method_1` (2), `method_2` (2), `OnDestroy` (2), `OnTriggered` (2), `Translate` (1), `SetSpreadRange` (1), `UpdateSpread` (1), `UpdateSpatialBlend` (1), `ScaleMaxDistance` (1), `FadeOut` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Linq`, `Unity.Burst`, `Unity.Collections`, `Unity.Jobs`, `Unity.Mathematics`.

**Key elements in this subsystem**
- `AmbientPlayerGroupSplineEmitter`, `SoundPlayerConfig`, `SoundPlayerSplineTrigger`, `SplineTriggerAbstract`, `SplineTriggerChecker`, `Struct231`.

### Subsystem: `Audio.AmbientSubsystem.Data`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 22.
- Declared types: 24 (classes: 21, structs: 0, interfaces: 1, enums: 2).
- Method declarations: 18. Field declarations: 35.
- Dominant folders: `Audio/AmbientSubsystem/Data` (22).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Apply` (4), `OnEnable` (1).
- High-frequency methods worth tracing for behavior flow: `Apply` (4), `method_0` (3), `TryGetClip` (3), `TryGetContent` (2), `GetClip` (1), `OnEnable` (1), `TryGetDayTimeSoundContainer` (1), `TryGetWindClip` (1), `TryGetPrecipitationClip` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Audio.SpatialSystem`, `UnityEngine.Audio`, `EFT.Weather`, `Audio.Data`, `EFT`.

**Key elements in this subsystem**
- `AudioClipByRainIntensity`, `DelayMixerDataSO`, `EDayTimeBlendState`, `EMixerParameterType`, `EnumSoundContentPreset`, `Content`, `EnvironmentSoundContainer`, `GenericVolumeParamsMixerDataSO`, `ISeasonSoundStorage`, `MixerEffectsDataSO`, `MixerEnvironmentParameter`, `MixerEnvironmentParametersByRoom`, `PrecipitationsByIntensity`, `PrecipitationSoundStorage`, `PrecipitationSoundStorageSO`, `ReverbMixerDataSO`, `SeasonAmbientSoundDataSO`, `SeasonClipsContentPreset`, `SeasonPrecipitations`, `SeasonSurfaceSetContainer`, `SeasonMovementSounds`, `SeasonWind`, `WindBySpeed`, `WindSoundStorage`.

### Subsystem: `Audio.AmbientSubsystem.GameEvents`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 3.
- Dominant folders: `Audio/AmbientSubsystem/GameEvents` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (1), `method_0` (1), `RunEvent` (1), `StopCurrentEvent` (1), `method_1` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Linq`, `EFT`, `UnityEngine`.

**Key elements in this subsystem**
- `AudioGameEventsController`.

### Subsystem: `Audio.AmbientSubsystem.PathMoverStrategy`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Audio/AmbientSubsystem/PathMoverStrategy` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EMovementStrategy`.

### Subsystem: `Audio.AudioCulling`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 1, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 10. Field declarations: 10.
- Dominant folders: `Audio/AudioCulling` (2).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnEnable` (1), `OnDisable` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `OnEnable` (1), `method_0` (1), `method_1` (1), `Play` (1), `method_2` (1), `method_3` (1), `Stop` (1), `OnDisable` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections`, `UnityEngine`, `UnityEngine.Audio`.

**Key elements in this subsystem**
- `EAudibleState`, `SyncLoopSoundPlayer`.

### Subsystem: `Audio.AudioWeatherSystem`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 1. Field declarations: 1.
- Dominant folders: `Audio/AudioWeatherSystem` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `TryGetDayTimeSoundContainer` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Audio.AmbientSubsystem`, `UnityEngine`.

**Key elements in this subsystem**
- `WeatherAmbientContainer`.

### Subsystem: `Audio.AutoPanner`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 5.
- Dominant folders: `Audio/AutoPanner` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `method_0` (1), `method_1` (1), `method_2` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Audio.AmbientSubsystem`, `UnityEngine`.

**Key elements in this subsystem**
- `AmbientPlayerAutoPanner`.

### Subsystem: `Audio.AuxiliaryAudioUtils`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Audio/AuxiliaryAudioUtils` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EAudioSourcePriority`.

### Subsystem: `Audio.BackendSettings`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 3, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 1. Field declarations: 9.
- Dominant folders: `Audio/BackendSettings` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `TryGetSettingsForGroup` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json.Linq`, `System.Collections.Generic`, `Newtonsoft.Json`, `Audio.SpatialSystem.Data`.

**Key elements in this subsystem**
- `AudioGroupOcclusionBackendSettings`, `ClientAudioOcclusionSettings`, `LocationOcclusionBackendSettings`.

### Subsystem: `Audio.ConfiguredAudioPlayer`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 2, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 3. Field declarations: 7.
- Dominant folders: `Audio/ConfiguredAudioPlayer` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Equals` (2), `GetHashCode` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.AnimationSequencePlayer`.

**Key elements in this subsystem**
- `AudioClipConfig`, `AudioClipDataConfigurator`, `EPlayerState`.

### Subsystem: `Audio.Data`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 11.
- Declared types: 12 (classes: 10, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 10. Field declarations: 12.
- Dominant folders: `Audio/Data` (11).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `TryGetBank` (2), `TryGetParameters` (1), `GetEnumerator` (1), `GetEnumerator_1` (1), `TryGetRandomClip` (1), `method_0` (1), `method_1` (1), `method_2` (1), `TryGetContent` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Audio.AmbientSubsystem.Data`, `System.Collections`, `System.Collections.Generic`, `EFT`.

**Key elements in this subsystem**
- `AudioClipWithSettings`, `AudioMixerParametersByType`, `AudioMixerParametersData`, `AudioMixerParamsContainer`, `BtrDriverSoundBankContainer`, `BtrDriverPhrasesByTrigger`, `EAudioQuality`, `EBtrDriverPhraseTrigger`, `SoundBankContainerBase`, `SoundBankWithSettings`, `SoundContainerBase`, `SurfaceSoundContainers`.

### Subsystem: `Audio.DebugTools`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Audio/DebugTools` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `ESelectionState`.

### Subsystem: `Audio.Effects`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 6 (classes: 6, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 22. Field declarations: 38.
- Dominant folders: `Audio/Effects` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnHighPassEnabled` (2), `OnLowPassEnabled` (2), `OnSetActive` (2), `Init` (1), `OnUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `InitializeComponents` (2), `ApplyHighPass` (2), `ApplyLowPass` (2), `OnHighPassEnabled` (2), `OnLowPassEnabled` (2), `OnSetActive` (2), `Init` (1), `SetTargetLowPassSettings` (1), `SetTargetHighPassSettings` (1), `method_0` (1), `ResetValues` (1), `SetActiveHighPass` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `AudioBuiltinEQFilter`, `AudioEQFilterBase`, `AudioOcclusionEQPreset`, `EnvironmentAudioThresholds`, `PositionEQThresholds`, `AudioFilterSettings`.

### Subsystem: `Audio.NPC`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 3. Field declarations: 2.
- Dominant folders: `Audio/NPC` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `method_0` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Comfort.Common`, `UnityEngine`.

**Key elements in this subsystem**
- `NPCAudioSourceSpatializeController`.

### Subsystem: `Audio.RadioSystem`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 15. Field declarations: 22.
- Dominant folders: `Audio/RadioSystem` (2).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (2), `OnEnable` (1), `OnDisable` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (2), `ChangeAudibleState` (2), `SetupSource` (2), `OnEnable` (1), `OnDisable` (1), `SyncBroadcast` (1), `method_0` (1), `Stop` (1), `Mute` (1), `ChangeStation` (1), `method_1` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Audio.AudioCulling`, `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `CommonAssets.Scripts.Audio.RadioSystem`, `UnityEngine.Audio`.

**Key elements in this subsystem**
- `ClientBroadcastPlayer`, `ClientSpatialBroadcastPlayer`.

### Subsystem: `Audio.ReverbSubsystem`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 3, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 23. Field declarations: 10.
- Dominant folders: `Audio/ReverbSubsystem` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (2), `OnStop` (2).
- High-frequency methods worth tracing for behavior flow: `Init` (2), `SetActive` (2), `SetPreset` (2), `SetPriority` (2), `SetMixerGroup` (2), `OnStop` (2), `Play` (1), `UpdateParameters` (1), `method_10` (1), `UpdateSourceVolume` (1), `method_12` (1), `PlayScheduled` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Audio.SpatialSystem`, `EFT`, `UnityEngine.Audio`.

**Key elements in this subsystem**
- `FakeReverbGeometry`, `ReverbSimpleSource`, `ReverbSuperSource`.

### Subsystem: `Audio.SpatialSystem`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 24.
- Declared types: 30 (classes: 19, structs: 6, interfaces: 2, enums: 3).
- Method declarations: 172. Field declarations: 169.
- Dominant folders: `Audio/SpatialSystem` (24).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (6), `OnAwake` (5), `Subscribe` (5), `Unsubscribe` (5), `OnDestroy` (4), `Init` (3), `OnValidate` (1), `Initialize` (1), `Update` (1), `LateUpdate` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (6), `SetActive` (6), `method_0` (6), `OnAwake` (5), `Subscribe` (5), `Unsubscribe` (5), `ManualUpdate` (4), `OnDestroy` (4), `SyncState` (4), `method_7` (4), `ResetFilter` (3), `SetFilterParams` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `EFT`, `System.Collections.Generic`, `System.Threading.Tasks`, `Comfort.Common`, `System.IO`, `System.Collections`, `Unity.Mathematics`, `System.Diagnostics`.

**Key elements in this subsystem**
- `AudioFilter`, `AudioFilterFrequencySettings`, `AudioGroupOcclusionSettings`, `AudioRouteData`, `AudioTriggerArea`, `BaseSpatialAudioPortal`, `PortalType`, `PortalState`, `BaseSpatialAudioSource`, `EAudioRoomTypeMask`, `FakeSpatialAudioSource`, `IIdentifiable`, `ISpatialAudioRoom`, `MetaSpatialAudioSource`, `MultiWindowPortal`, `WindowData`, `RoomPair`, `Route`, `AudioPortalData`, `Class789`, `RoomPairItem`, `RoutesAwareRoomItem`, `RoutesByRoomPairIDItem`, `SerializableAudioRoutesBakeData`, `SpatialAudioCrossSceneGroup`, `SpatialAudioPortal`, `SpatialAudioSystem`, `SpatialHighPassFilter`, `SpatialLowPassFilter`, `UniversalTriggerSpatialAudioPortal`.

### Subsystem: `Audio.SpatialSystem.Data`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 13.
- Declared types: 13 (classes: 8, structs: 4, interfaces: 0, enums: 1).
- Method declarations: 14. Field declarations: 73.
- Dominant folders: `Audio/SpatialSystem/Data` (13).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Apply` (6).
- High-frequency methods worth tracing for behavior flow: `Apply` (6), `GetRaysCountByQuality` (3), `GetPositionThresholdByQuality` (1), `Write` (1), `Read` (1), `Default` (1), `GetCompressionFactorByQuality` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Audio.Data`, `System.Collections.Generic`, `Audio.BackendSettings`, `System.IO`.

**Key elements in this subsystem**
- `AudioOcclusionSettings`, `CommonOcclusionSettings`, `DiffractionSettings`, `EPlayerRoomInteractionState`, `IndoorRoomsData`, `LocationBakeSettings`, `PortalData`, `PropagationSettings`, `QualityFloatValue`, `QualityIntValue`, `ReflectionSettings`, `SpatialAudioLocationInfo`, `TransmissionSettings`.

### Subsystem: `Audio.SpatialSystem.Editor.SpatialAudioTool`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Audio/SpatialSystem/Editor/SpatialAudioTool` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EDebugLayoutStyleType`.

### Subsystem: `Audio.SpatialSystem.SpatialAudioCalculator`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 0, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Audio/SpatialSystem/SpatialAudioCalculator` (3).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EOcclusionDebugMask`, `EPathEndStatus`, `EPathType`.

### Subsystem: `Audio.SpatialSystem.Utils`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 4.
- Dominant folders: `Audio/SpatialSystem/Utils` (1).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `SpatialAudioPoolsConfig`.

### Subsystem: `Audio.Vehicles`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 5.
- Declared types: 5 (classes: 4, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 37. Field declarations: 72.
- Dominant folders: `Audio/Vehicles` (5).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `Update` (2), `Init` (2), `OnDestroy` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (3), `Awake` (2), `Update` (2), `Init` (2), `SetEnvironment` (1), `GetImpactClip` (1), `CalculateVolume` (1), `GetRandomPitch` (1), `IsOutOfRange` (1), `UpdateImpactPlayers` (1), `TryPlayImpactSound` (1), `SetSpatialBlend` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Audio.Vehicles.BTR`, `UnityEngine.Audio`, `EFT.Vehicle.Vehicles`, `System.Collections.Generic`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `BtrTurretSoundPlayerController`, `EVehicleMovementStatus`, `SoundSuspensionController`, `SoundSuspensionImpactSoundPlayer`, `VehicleMovementSoundContext`.

### Subsystem: `Audio.Vehicles.BTR`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 4, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 36. Field declarations: 56.
- Dominant folders: `Audio/Vehicles/BTR` (4).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `OnDestroy` (2), `OnFilterEnd` (1), `Initialize` (1), `Update` (1), `Start` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (3), `method_1` (3), `method_2` (3), `Awake` (2), `OnDestroy` (2), `method_3` (2), `method_4` (2), `method_5` (2), `OnFilterEnd` (1), `SetEnvironment` (1), `Initialize` (1), `Update` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.Audio`, `EFT`, `System.Collections`, `System.Collections.Generic`, `Audio.Data`, `Audio.RadioSystem`, `Audio.SpatialSystem`, `Comfort.Common`, `EFT.GlobalEvents`.

**Key elements in this subsystem**
- `BtrDoorSoundHandler`, `BtrSoundController`, `VehicleMovementSoundContainer`, `VehicleRotationSoundPlayer`.

### Subsystem: `Audio.Weapons.Data`
**Role hypothesis:** Audio simulation and emitters.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 1. Field declarations: 1.
- Dominant folders: `Audio/Weapons/Data` (2).

**How it works (operational pattern)**
- Audio namespaces act as spatial/event audio routers, translating world/player/environment state into emitter behavior and mixer decisions.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `TryGetAimingBank` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Audio.Data`, `EFT.Weapons.Data`, `UnityEngine`.

**Key elements in this subsystem**
- `AimingSounds`, `WeaponAimingSoundsSO`.

### Subsystem: `BezierSplineTools`
**Role hypothesis:** BezierSplineTools subsystem.

**Structure snapshot**
- Files: 7.
- Declared types: 7 (classes: 5, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 36. Field declarations: 20.
- Dominant folders: `BezierSplineTools` (7).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `GetPoint` (2), `GetVelocity` (2), `GetDirection` (2), `Reset` (2), `AddCurve` (2), `GetControlPoint` (1), `ClosestTimeOnBezier` (1), `SetControlPoint` (1), `GetControlPointMode` (1), `SetControlPointMode` (1), `method_0` (1), `GetPointByOneSpeed` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Linq`, `EFT.Vehicle`.

**Key elements in this subsystem**
- `BezierControlPointMode`, `BezierCurve`, `BezierSpline`, `Line`, `SplineDecorator`, `SplineWalker`, `SplineWalkerMode`.

### Subsystem: `BitPacking`
**Role hypothesis:** BitPacking subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 0, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `BitPacking` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `BitPackingTag`, `EBitStreamMode`.

### Subsystem: `BSG.CameraEffects`
**Role hypothesis:** BSG subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 3 (classes: 3, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 23. Field declarations: 61.
- Dominant folders: `BSG/CameraEffects` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `OnDisable` (1), `Update` (1), `OnPreCull` (1), `OnPostRender` (1), `OnDestroy` (1), `OnEnable` (1), `OnValidate` (1), `OnRenderImage` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (3), `Awake` (2), `ApplySettings` (2), `SetMask` (1), `StartSwitch` (1), `FastForwardSwitch` (1), `OnDisable` (1), `Update` (1), `OnPreCull` (1), `OnPostRender` (1), `method_1` (1), `smethod_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`, `System.Linq`, `System.Runtime.CompilerServices`, `Comfort.Common`, `EFT.InventoryLogic`, `JetBrains.Annotations`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `NightVision`, `Class714`, `TextureMask`.

### Subsystem: `Bsg.GameSettings`
**Role hypothesis:** Bsg subsystem.

**Structure snapshot**
- Files: 5.
- Declared types: 7 (classes: 6, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 39. Field declarations: 10.
- Dominant folders: `BSG/GameSettings` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Subscribe` (3).
- High-frequency methods worth tracing for behavior flow: `HasSameValue` (4), `GetValue` (3), `SetValue` (3), `TakeValueFrom` (3), `Bind` (3), `Subscribe` (3), `BindWithoutValue` (3), `ResetToDefault` (3), `ForceApply` (3), `method_0` (3), `HasSameValue_1` (1), `TakeValueFrom_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Threading.Tasks`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `Diz.Binding`, `Newtonsoft.Json`, `UnityEngine`.

**Key elements in this subsystem**
- `EquatableGameSetting`, `GameSetting`, `Class770`, `IGameSetting`, `ListGameSetting`, `Class772`, `StateGameSetting`.

### Subsystem: `ChartAndGraph`
**Role hypothesis:** UI graphing widgets.

**Structure snapshot**
- Files: 51.
- Declared types: 77 (classes: 66, structs: 3, interfaces: 3, enums: 5).
- Method declarations: 366. Field declarations: 364.
- Dominant folders: `ChartAndGraph` (51).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (10), `Update` (9), `OnDisable` (4), `OnItemHoverted` (4), `OnBeforeSerialize` (4), `OnAfterDeserialize` (4), `OnDestroy` (4), `OnPopulateMesh` (4), `OnValidate` (3), `OnEnable` (3), `OnItemSelected` (3), `OnPropertyUpdated` (2), `OnLabelSettingChanged` (2), `OnAxisValuesChanged` (2), `OnLabelSettingsSet` (2), `LateUpdate` (2), `OnNonHoverted` (2), `OnItemLeave` (2), `OnMouseEnter` (2), `OnMouseExit` (2).
- High-frequency methods worth tracing for behavior flow: `method_0` (25), `method_1` (13), `method_2` (13), `Start` (10), `Update` (9), `method_3` (9), `method_4` (6), `method_6` (6), `method_5` (5), `method_7` (5), `Generator` (5), `OnDisable` (4).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `UnityEngine.UI`, `System.Threading`, `UnityEngine.Events`, `System.Linq`, `JetBrains.Annotations`, `UnityEngine.EventSystems`, `System.Text`.

**Key elements in this subsystem**
- `AbstractChartData`, `Slider`, `Class1060`, `Class1061`, `Class1062`, `AlignedItemLabels`, `AnyChart`, `AutoFloat`, `AxisBase`, `Class1063`, `Class1064`, `AxisChart`, `AxisFormat`, `BoxPathGenerator`, `CanvasLines`, `Struct271`, `Class1065`, `Class1066`, `CanvasLinesHover`, `CharItemEffectController`, `ChartAdvancedSettings`, `ChartDivisionAligment`, `ChartDivisionInfo`, `DivisionMessure`, `ChartDynamicMaterial`, `ChartItem`, `ChartItemEffect`, `ChartItemEvents`, `Event`, `ChartItemGrowEffect`, `ChartItemLerpEffect`, `ChartItemMaterialLerpEffect`, `ChartItemTextBlend`, `ChartMainDivisionInfo`, `ChartMaterialController`, `ChartOrientation`, `ChartOrientedSize`, `ChartSettingItemBase`, `ChartSubDivisionInfo`, `CylinderPathGenerator`, ... (+37 more).

### Subsystem: `ChartAndGraph.Axis`
**Role hypothesis:** UI graphing widgets.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 16. Field declarations: 23.
- Dominant folders: `ChartAndGraph/Axis` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (2), `Update` (2), `OnPopulateMesh` (2).
- High-frequency methods worth tracing for behavior flow: `OnDestroy` (2), `method_0` (2), `SetAxis` (2), `Update` (2), `This` (2), `GetGameObject` (2), `OnPopulateMesh` (2), `UpdateMaterial` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `AxisGenerator`, `CanvasAxisGenerator`.

### Subsystem: `ChatShared`
**Role hypothesis:** ChatShared subsystem.

**Structure snapshot**
- Files: 11.
- Declared types: 13 (classes: 7, structs: 0, interfaces: 3, enums: 3).
- Method declarations: 10. Field declarations: 29.
- Dominant folders: `ChatShared` (11).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `FindOrCreate` (1), `UpdateFromAnotherItem` (1), `UpdateFromChatMember` (1), `UpdateFromMerchant` (1), `UpdateFromTrader` (1), `SetIgnoreStatus` (1), `SetBanStatus` (1), `SetNickname` (1), `SetCategory` (1), `Compare` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT`, `Comfort.Common`, `Newtonsoft.Json`, `Comfort.Communication`, `Diz.Binding`, `EFT.UI.Ragfair`.

**Key elements in this subsystem**
- `ChatInfo`, `ChatRoomMember`, `MemberInfo`, `ChatRPC`, `EMessageParamsAction`, `EMessageType`, `EViewRule`, `IChatHandle`, `IChatMember`, `IChatsSession`, `Message`, `UpdatableChatMember`, `UpdatableChatMemberInfo`.

### Subsystem: `CommonAssets.Scripts`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `CommonAssets/Scripts` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `AlternativePropBone`.

### Subsystem: `CommonAssets.Scripts.ArtilleryShelling`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 6.
- Declared types: 6 (classes: 1, structs: 3, interfaces: 0, enums: 2).
- Method declarations: 0. Field declarations: 33.
- Dominant folders: `CommonAssets/Scripts/ArtilleryShelling` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Newtonsoft.Json`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `ArtilleryBrigade`, `ArtilleryGun`, `ArtilleryShellingMapConfiguration`, `ArtilleryShellingZone`, `EArtilleryProjectileState`, `EArtilleryProjectileType`.

### Subsystem: `CommonAssets.Scripts.ArtilleryShelling.Client`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 10. Field declarations: 10.
- Dominant folders: `CommonAssets/Scripts/ArtilleryShelling/Client` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `Init` (1), `Deinit` (1), `Update` (1), `SyncProjectileState` (1), `method_1` (1), `method_2` (1), `SetExplosiveItemParams` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `Comfort.Common`, `EFT`, `Systems.Effects`, `UnityEngine`.

**Key elements in this subsystem**
- `ArtilleryProjectileClient`, `Class1003`.

### Subsystem: `CommonAssets.Scripts.ArtilleryShelling.Client.Audio`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 7.
- Dominant folders: `CommonAssets/Scripts/ArtilleryShelling/Client/Audio` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT`, `UnityEngine`.

**Key elements in this subsystem**
- `ArtilleryShellingSoundsSO`.

### Subsystem: `CommonAssets.Scripts.Audio`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 0, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `CommonAssets/Scripts/Audio` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EAudioFadeType`, `EAudioMovementState`, `ELoudnessType`.

### Subsystem: `CommonAssets.Scripts.Audio.RadioSystem`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 9.
- Declared types: 14 (classes: 10, structs: 1, interfaces: 0, enums: 3).
- Method declarations: 25. Field declarations: 31.
- Dominant folders: `CommonAssets/Scripts/Audio/RadioSystem` (9).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (1), `OnValidate` (1), `OnDisable` (1), `Awake` (1), `Dispose` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (3), `method_1` (3), `TryGetBroadcastItemData` (1), `RebuildGrid` (1), `OnEnable` (1), `OnValidate` (1), `OnDisable` (1), `TryGetContent` (1), `TryGetRandomBroadcastData` (1), `Add` (1), `Remove` (1), `TryGetRandomData` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Collections`, `Comfort.Common`, `EFT`, `EFT.GlobalEvents.AudioEvents`.

**Key elements in this subsystem**
- `BroadcastDataPacket`, `BroadcastGrid`, `BroadcastItemData`, `BroadcastItemsContainer`, `BroadcastItemsData`, `BroadcastItemsDataContainer`, `BroadcastRule`, `CrossfadeDurationByType`, `EBroadcastItemType`, `EBroadcastState`, `ERadioStation`, `RadioBroadcastController`, `RadioStationsGridData`, `RadioStationGridData`.

### Subsystem: `CommonAssets.Scripts.Game`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 6 (classes: 5, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 16. Field declarations: 13.
- Dominant folders: `CommonAssets/Scripts/Game` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Run` (1), `Update` (1), `Start` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `method_1` (2), `method_2` (2), `Create` (1), `Run` (1), `Stop` (1), `Update` (1), `Start` (1), `ApplyState` (1), `TryRenameId` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading`, `UnityEngine`, `System.Linq`, `Comfort.Common`, `EFT`, `EFT.Counters`, `EFT.Interactive`, `Audio.AudioCulling`.

**Key elements in this subsystem**
- `EndByExitTrigerScenario`, `Class984`, `GInterface146`, `Class985`, `Class986`, `SyncableLoopSoundPlayer`.

### Subsystem: `CommonAssets.Scripts.Game.GameTriggers.Handlers`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 5 (classes: 4, structs: 1, interfaces: 0, enums: 0).
- Method declarations: 18. Field declarations: 26.
- Dominant folders: `CommonAssets/Scripts/Game/GameTriggers/Handlers` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (3), `Awake` (2), `OnDisable` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Start` (3), `method_0` (3), `Awake` (2), `ApplyState` (2), `method_1` (1), `method_2` (1), `method_3` (1), `OnDisable` (1), `OnDestroy` (1), `TryRenameId` (1), `method_4` (1), `method_5` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.GameTriggers`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Collections`, `System.Collections.Generic`, `Audio.SpatialSystem`, `Comfort.Common`, `EFT`, `UnityEngine.Audio`.

**Key elements in this subsystem**
- `HandlerAudioSourceMute`, `HandlerGameObjectState`, `HandlerPlayContinousSound`, `PlaySoundBankConfig`, `HandlerStateBase`.

### Subsystem: `CommonAssets.Scripts.Game.LabyrinthEvent`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 1, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 1.
- Dominant folders: `CommonAssets/Scripts/Game/LabyrinthEvent` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `ETrapType`, `TrapSyncable`.

### Subsystem: `CommonAssets.Scripts.Game.Syncable`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 2.
- Dominant folders: `CommonAssets/Scripts/Game/Syncable` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (1).
- High-frequency methods worth tracing for behavior flow: `Start` (1), `Serialize` (1), `Deserialize` (1), `method_1` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `CommonAssets.Scripts.Game.GameTriggers.Handlers`, `UnityEngine`.

**Key elements in this subsystem**
- `HandlerStateSync`.

### Subsystem: `CommonAssets.Scripts.RunddansEvent`
**Role hypothesis:** CommonAssets subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 15. Field declarations: 12.
- Dominant folders: `CommonAssets/Scripts/RunddansEvent` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `Start` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `Start` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1), `method_8` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `Audio.SpatialSystem`, `EFT`, `UnityEngine`.

**Key elements in this subsystem**
- `EventObjectSoundController`.

### Subsystem: `Communications`
**Role hypothesis:** Communications subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 10 (classes: 10, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 48. Field declarations: 24.
- Dominant folders: `Communications` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (2), `FixedUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (10), `method_1` (4), `method_2` (3), `method_3` (2), `method_4` (2), `method_5` (2), `method_6` (2), `method_7` (2), `method_8` (2), `OnDestroy` (2), `smethod_0` (1), `Reconnect` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Linq`, `System.Runtime.CompilerServices`, `System.Threading`, `ChatShared`, `Comfort.Common`, `UnityEngine`, `System.Collections`, `Comfort.Communication`, `Comfort.Net`.

**Key elements in this subsystem**
- `ChatClient`, `Class752`, `ChatController`, `Class755`, `Class756`, `Class757`, `Class758`, `Class759`, `Class760`, `Class761`.

### Subsystem: `CustomPlayerLoopSystem`
**Role hypothesis:** CustomPlayerLoopSystem subsystem.

**Structure snapshot**
- Files: 14.
- Declared types: 14 (classes: 1, structs: 13, interfaces: 0, enums: 0).
- Method declarations: 33. Field declarations: 1.
- Dominant folders: `CustomPlayerLoopSystem` (14).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnQuit` (1).
- High-frequency methods worth tracing for behavior flow: `GetNewSystem` (13), `UpdateFunction` (13), `Injection` (1), `MoveUNetUpdateSystemOnPreUpdate` (1), `InjectGlobalEventsOnPreUpdate` (1), `InjectDataProviderSyncUpdate` (1), `MoveUNetUpdateSystemOnFixedUpdate` (1), `InjectPostUNetUpdateSystem` (1), `OnQuit` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine.LowLevel`, `UnityEngine`, `UnityEngine.PlayerLoop`, `System.Threading`.

**Key elements in this subsystem**
- `CustomPlayerLoopSystemsInjector`, `DataProviderSyncUpdate`, `EndOfFixedUpdate`, `EndOfFrame`, `EndOfUpdate`, `FrameCounter`, `GlobalEventsApply`, `GlobalEventsClear`, `PostUNetUpdate`, `StartOfFixedUpdate`, `StartOfFrame`, `StartOfPostLateUpdate`, `StartOfUpdate`, `UNetUpdate`.

### Subsystem: `Cutscene`
**Role hypothesis:** Cutscene subsystem.

**Structure snapshot**
- Files: 13.
- Declared types: 20 (classes: 19, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 61. Field declarations: 99.
- Dominant folders: `Cutscene` (13).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (3), `Update` (2), `OnDestroy` (2), `OnStateEnter` (1), `OnStateUpdate` (1), `OnStateExit` (1), `OnDisable` (1), `Start` (1), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (10), `method_1` (5), `Play` (4), `method_2` (3), `Awake` (3), `Update` (2), `OnDestroy` (2), `method_3` (2), `method_4` (2), `method_5` (2), `GetBackedDataForPlay` (1), `OnStateEnter` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `uLipSync`, `UnityEngine.Timeline`, `System.Collections`, `System.Linq`, `System.Threading`, `EFT`, `System.Diagnostics`.

**Key elements in this subsystem**
- `AnimationEvent`, `AnimationTrack`, `AnimatorStateTimelineBehaviour`, `AnimatorStateTimelineData`, `BakedDataWithCurves`, `CurveData`, `BaseCutsceneTrigger`, `BlendingDirector`, `BlendType`, `Class745`, `Class746`, `Class747`, `CutsceneDirectorBlender`, `Class751`, `CutsceneFakePlayerSteps`, `InteractiveObjectCutsceneTrigger`, `StartCutsceneCondition`, `LighthouseKeeperExitCutsceneTrigger`, `LipSyncBackedDataRandomVariants`, `LipSyncPlayer`.

### Subsystem: `CW2`
**Role hypothesis:** CW2 subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 2. Field declarations: 3.
- Dominant folders: `CW2` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `FixedUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `FixedUpdate` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `AdditiveMeshBaker`.

### Subsystem: `CW2.Animations`
**Role hypothesis:** CW2 subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 15 (classes: 11, structs: 0, interfaces: 0, enums: 4).
- Method declarations: 32. Field declarations: 58.
- Dominant folders: `CW2/Animations` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (6), `Process` (5), `FixedUpdate` (2), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (6), `Process` (5), `Add` (2), `FixedUpdate` (2), `method_0` (2), `method_1` (2), `Awake` (1), `smethod_0` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `UnityEngine.Serialization`.

**Key elements in this subsystem**
- `PhysicsSimulator`, `RandomDevice`, `CurveDevice`, `InputDevice`, `ScriptValueDevice`, `UnityValueDevice`, `Values`, `Class740`, `Spring`, `Val`, `TargetType`, `ComponentType`, `OperationType`, `Class741`, `SmoothRandom`.

### Subsystem: `DefaultNamespace`
**Role hypothesis:** DefaultNamespace subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 4.
- Dominant folders: `DefaultNamespace` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnStateEnter` (1), `OnStateUpdate` (1), `OnStateExit` (1).
- High-frequency methods worth tracing for behavior flow: `OnStateEnter` (1), `OnStateUpdate` (1), `OnStateExit` (1), `method_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `LayerWeightStateController`.

### Subsystem: `DeferredDecals`
**Role hypothesis:** DeferredDecals subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 4 (classes: 4, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 31. Field declarations: 55.
- Dominant folders: `DeferredDecals` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnDisable` (1), `OnPreCullCameraRender` (1), `OnPreCameraRender` (1), `Update` (1), `OnDestroy` (1), `Dispose` (1), `Init` (1).
- High-frequency methods worth tracing for behavior flow: `DrawDecal` (2), `Awake` (1), `SetMaxStaticDecals` (1), `SetMaxDynamicDecals` (1), `EmitBloodOnEnvironment` (1), `EmitBleeding` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `EFT.Ballistics`, `UnityEngine`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `DeferredDecalRenderer`, `DeferredDecalMeshDataClass`, `SingleDecal`, `DeferredDecalBufferClass`.

### Subsystem: `Dissonance.Integrations.MirrorIgnorance`
**Role hypothesis:** Dissonance subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 9. Field declarations: 6.
- Dominant folders: `Dissonance/Integrations/MirrorIgnorance` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (1), `Initialize` (1).
- High-frequency methods worth tracing for behavior flow: `CreateServer` (1), `CreateClient` (1), `Update` (1), `Initialize` (1), `PreprocessPacketToClient` (1), `PreprocessPacketToServer` (1), `NullMessageReceivedHandler` (1), `CopyToArraySegment` (1), `CopyPacketToNetworkWriter` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Runtime.CompilerServices`, `Dissonance.Datastructures`, `Dissonance.Extensions`, `Dissonance.Networking`, `JetBrains.Annotations`, `UnityEngine`.

**Key elements in this subsystem**
- `MirrorIgnoranceCommsNetwork`, `Class902`.

### Subsystem: `Diz.Binding`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 5 (classes: 2, structs: 0, interfaces: 3, enums: 0).
- Method declarations: 4. Field declarations: 3.
- Dominant folders: `Diz/Binding` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Subscribe` (1).
- High-frequency methods worth tracing for behavior flow: `Subscribe` (1), `Bind` (1), `Invoke` (1), `method_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `System.Threading`.

**Key elements in this subsystem**
- `BindableEvent`, `Class1035`, `IBindable`, `IBindableEvent`, `IUpdatable`.

### Subsystem: `Diz.DependencyManager`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 6 (classes: 5, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 14. Field declarations: 16.
- Dominant folders: `Diz/DependencyManager` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Load` (2), `Start` (1), `Update` (1), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `Load` (2), `Start` (1), `Update` (1), `Awake` (1), `AddToken` (1), `Loading` (1), `Unload` (1), `SetRefCount` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `UnityEngine`, `System.Collections`, `System.Collections.Generic`, `System.Linq`, `System.Threading`, `UnityEngine.Events`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `ELoadState`, `SystemTester`, `Class1056`, `TestLoadable`, `Class1057`, `Class1058`.

### Subsystem: `Diz.Jobs`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 4 (classes: 3, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 15. Field declarations: 30.
- Dominant folders: `Diz/Jobs` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `Start` (1), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (1), `SetForceMode` (1), `SetTargetFrameRate` (1), `Start` (1), `LateUpdate` (1), `method_0` (1), `method_1` (1), `method_2` (1), `ForceExecuteContinuations` (1), `smethod_0` (1), `smethod_1` (1), `Yield` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections`, `System.Collections.Generic`, `System.Diagnostics`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `EJobPriority`, `JobScheduler`, `GClass1619`, `Utils`.

### Subsystem: `Diz.LanguageExtensions`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 1.
- Dominant folders: `Diz/LanguageExtensions` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `Error`.

### Subsystem: `Diz.Resources`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 3 (classes: 2, structs: 1, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 8.
- Dominant folders: `Diz/Resources` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `Create` (1), `Update` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `JetBrains.Annotations`, `UnityEngine`, `UnityEngine.Build.Pipeline`.

**Key elements in this subsystem**
- `EasyAssets`, `Struct265`, `Class1050`.

### Subsystem: `Diz.Skinning`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 4, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 10. Field declarations: 8.
- Dominant folders: `Diz/Skinning` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Unsubscribe` (1), `OnBeforeSerialize` (1), `OnAfterDeserialize` (1), `Init` (1).
- High-frequency methods worth tracing for behavior flow: `ApplySkin` (2), `Unskin` (2), `Unsubscribe` (1), `Plug` (1), `Unplug` (1), `OnBeforeSerialize` (1), `OnAfterDeserialize` (1), `Init` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `AbstractSkin`, `PluggableBone`, `Skeleton`, `Skin`.

### Subsystem: `Diz.Utils`
**Role hypothesis:** Diz subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 6 (classes: 6, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 14. Field declarations: 12.
- Dominant folders: `Diz/Utils` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (1), `Update` (1), `FixedUpdate` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (5), `Start` (1), `Update` (1), `FixedUpdate` (1), `OnDestroy` (1), `RunInMainTread` (1), `IsStopped` (1), `RunOnBackgroundThread` (1), `CheckIsMainThread` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `System.Threading`, `System.Threading.Tasks`, `UnityEngine`.

**Key elements in this subsystem**
- `AsyncWorker`, `Class1008`, `Class1009`, `Class1010`, `Class1011`, `Class1012`.

### Subsystem: `Editor_Tools.BallisticCalculatorTool`
**Role hypothesis:** Editor_Tools subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Editor_Tools/BallisticCalculatorTool` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `BallisticCalculatorTrajectoryDrawer`.

### Subsystem: `EFT`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 275.
- Declared types: 829 (classes: 668, structs: 28, interfaces: 27, enums: 106).
- Method declarations: 5356. Field declarations: 3696.
- Dominant folders: `EFT` (275).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (85), `Update` (54), `Awake` (28), `Init` (24), `Execute` (24), `OnDestroy` (23), `Dispose` (20), `OnAddAmmoInChamber` (20), `OnShellEjectEvent` (15), `OnOnOffBoltCatchEvent` (15), `LateUpdate` (12), `OnMagAppeared` (12), `OnBackpackDrop` (10), `OnMagPuttedToRig` (10), `OnIdleStartEvent` (10), `FixedUpdate` (9), `Apply` (9), `Run` (9), `OnFireEvent` (9), `OnEnd` (8).
- High-frequency methods worth tracing for behavior flow: `method_0` (356), `method_1` (160), `method_2` (109), `Start` (85), `Reset` (72), `method_3` (71), `method_5` (66), `method_4` (61), `SetInventoryOpened` (59), `Update` (54), `FastForward` (52), `method_6` (47).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `Comfort.Common`, `EFT.InventoryLogic`, `System.Threading.Tasks`, `System.Linq`, `Newtonsoft.Json`, `JetBrains.Annotations`, `System.Threading`.

**Key elements in this subsystem**
- `AbstractApplication`, `name`, `Class1097`, `AbstractGame`, `AbstractGameSession`, `Class1098`, `Class1099`, `Class1100`, `Class1101`, `Class1102`, `Class1103`, `Class1104`, `Class1105`, `Class1106`, `Class1107`, `AbstractSession`, `Class1108`, `Class1109`, `Class1110`, `AnimatorResetter`, `LayerState`, `ParameterDefault`, `Class1677`, `AudioArray`, `Class1528`, `Class1529`, `Class1530`, `AudioGroupPreset`, `AcousticSettings`, `AudioGroupAcousticSettings`, `AudioListenerConsistencyManager`, `AudioMultipleClipContainer`, `AudioSequence`, `GStruct283`, `AudioSingleClipContainer`, `BarterScheme`, `BarterVariant`, `BaseLocalGame`, `Class1630`, `Class1631`, ... (+789 more).

### Subsystem: `EFT.Achievements`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 9. Field declarations: 18.
- Dominant folders: `EFT/Achievements` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `Show` (1), `method_0` (1), `method_1` (1), `method_2` (1), `ShowRewardsTooltip` (1), `HideRewardsTooltip` (1), `ShowConditionsTooltip` (1), `HideConditionsTooltip` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Globalization`, `System.Linq`, `System.Runtime.CompilerServices`, `AchievementsSystem`, `EFT.Quests`, `EFT.UI`, `TMPro`, `UnityEngine`, `UnityEngine.EventSystems`.

**Key elements in this subsystem**
- `AchievementView`.

### Subsystem: `EFT.ActiveHeadphones`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 3.
- Dominant folders: `EFT/ActiveHeadphones` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EQBand`.

### Subsystem: `EFT.Airdrop`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 6 (classes: 5, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 12. Field declarations: 12.
- Dominant folders: `EFT/Airdrop` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (2), `Update` (2), `OnDisable` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (2), `Update` (2), `method_3` (1), `method_4` (1), `method_5` (1), `OnDisable` (1), `method_0` (1), `method_1` (1), `method_2` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.EnvironmentEffect`, `Audio.SpatialSystem`.

**Key elements in this subsystem**
- `AirdropPoint`, `AirdropSounds`, `AirdropSurfaceSet`, `EAirdropType`, `FireworkFlarePatronSound`, `FlarePatronSound`.

### Subsystem: `EFT.Animals`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 5 (classes: 5, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 18. Field declarations: 53.
- Dominant folders: `EFT/Animals` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (3), `Init` (2), `Start` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `Spawn` (4), `Awake` (3), `Init` (2), `method_0` (2), `SetDirection` (1), `Start` (1), `Update` (1), `CalculateVelocity` (1), `UpdateBirdDirection` (1), `RecalculateSpeed` (1), `ManualUpdate` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections`, `BezierSplineTools`.

**Key elements in this subsystem**
- `Bird`, `BirdBoidBrain`, `BirdBoidsSpawner`, `BirdCurveBrain`, `BirdsSpawner`.

### Subsystem: `EFT.AnimatedInteractionsSubsystem`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/AnimatedInteractionsSubsystem` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `IAnimatedInteractions`.

### Subsystem: `EFT.AnimatedInteractionsSubsystem.States`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 2. Field declarations: 2.
- Dominant folders: `EFT/AnimatedInteractionsSubsystem/States` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnStateEnter` (1), `OnStateExit` (1).
- High-frequency methods worth tracing for behavior flow: `OnStateEnter` (1), `OnStateExit` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `UnityEngine`.

**Key elements in this subsystem**
- `InteractionStateContainer`.

### Subsystem: `EFT.Animations`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 23.
- Declared types: 33 (classes: 27, structs: 0, interfaces: 1, enums: 5).
- Method declarations: 185. Field declarations: 380.
- Dominant folders: `EFT/Animations` (23).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnRotation` (3), `Initialize` (2), `Process` (2), `FixedUpdate` (2), `Start` (2), `OnShot` (1), `OnStop` (1), `OnDestroy` (1), `OnScopesModeUpdated` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (11), `method_1` (6), `Reset` (4), `method_2` (4), `method_3` (4), `AddAccelerationLimitless` (4), `SetTransforms` (3), `UpdateJoints` (3), `OnRotation` (3), `CalculateRecoil` (3), `Initialize` (2), `SetShotEffector` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Linq`, `System.Runtime.CompilerServices`, `Comfort.Common`, `CW2.Animations`, `System.Collections.Generic`, `EFT.InventoryLogic`, `UnityEngine.Serialization`, `EFT.Animations.Recoil`, `System.Collections`.

**Key elements in this subsystem**
- `AGSMachinery`, `AimingZone`, `AnimVal`, `BreathEffector`, `Class2151`, `ComponentType`, `EProceduralAnimationMask`, `HandShakeEffector`, `Class2152`, `IEffector`, `NewRotationRecoilProcess`, `ReturnTrajectorySideType`, `OldRecoilShotEffect`, `OldRotationRecoilProcess`, `OneOffWeaponSettings`, `PlayerSpring`, `ProceduralWeaponAnimation`, `GClass2791`, `SightNBone`, `Class2153`, `Class2154`, `Class2155`, `Class2156`, `RecoilProcessBase`, `RotationRecoilProcessBase`, `Spring`, `VecComponent`, `Target`, `UtesMachinery`, `Val`, `WeaponMachinery`, `WeaponPositionRecoilProcess`, `WeaponRotationRecoilProcess`.

### Subsystem: `EFT.Animations.Audio`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 64. Field declarations: 7.
- Dominant folders: `EFT/Animations/Audio` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `OnDestroy` (1), `OnUseProp` (1), `OnAddAmmoInChamber` (1), `OnAddAmmoInMag` (1), `OnArm` (1), `OnCook` (1), `OnDelAmmoChamber` (1), `OnDelAmmoFromMag` (1), `OnDisarm` (1), `OnFireEnd` (1), `OnFiringBullet` (1), `OnFoldOff` (1), `OnFoldOn` (1), `OnIdleStart` (1), `OnMalfunctionOff` (1), `OnMagHide` (1), `OnMagIn` (1), `OnMagOut` (1), `OnMagShow` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1), `method_8` (1), `method_9` (1), `Clear` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections`, `Audio.Data`, `Audio.SpatialSystem`, `Comfort.Common`, `EFT.Ballistics`, `EFT.WeaponMounting`, `UnityEngine`.

**Key elements in this subsystem**
- `BipodAudioController`.

### Subsystem: `EFT.Animations.NewRecoil`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 13. Field declarations: 25.
- Dominant folders: `EFT/Animations/NewRecoil` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `FixedUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `FixedUpdate` (1), `AddRecoilForce` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `CalculateBaseRecoilParameters` (1), `RecalculateRecoilParamsOnChangeWeapon` (1), `method_4` (1), `method_5` (1), `GetHandRotationRecoil` (1), `GetHandPositionRecoil` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `EFT.Animations.Recoil`, `EFT.InventoryLogic`, `UnityEngine`.

**Key elements in this subsystem**
- `NewRecoilShotEffect`.

### Subsystem: `EFT.Animations.Recoil`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Animations/Recoil` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `EFT.InventoryLogic`, `UnityEngine`.

**Key elements in this subsystem**
- `IRecoilShotEffect`.

### Subsystem: `EFT.AnimationSequencePlayer`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 10.
- Declared types: 10 (classes: 10, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 20. Field declarations: 30.
- Dominant folders: `EFT/AnimationSequencePlayer` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (3), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (3), `GetKeysWithMinDurations` (2), `method_0` (2), `Play` (2), `InitializeDictionary` (1), `Add` (1), `Set` (1), `Remove` (1), `TryGetValue` (1), `ContainsKey` (1), `GetAllKeys` (1), `GetAllValues` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `uLipSync`, `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `Newtonsoft.Json`, `System.Collections`, `TMPro`.

**Key elements in this subsystem**
- `AnimationDictionary`, `AnimationElement`, `LipSyncDictionary`, `LipSyncElement`, `SecondaryAnimationDictionary`, `SequenceReader`, `SerializableKeyValuePair`, `SerializedDictionary`, `SubtitleElement`, `SubtitleHandler`.

### Subsystem: `EFT.AssetsManager`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 9.
- Declared types: 12 (classes: 12, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 51. Field declarations: 46.
- Dominant folders: `EFT/AssetsManager` (9).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnGetFromPool` (3), `OnCreatePoolRoleModel` (3), `Update` (1), `Init` (1), `OnDestroy` (1), `OnCreatePoolObject` (1), `OnReturnToPool` (1).
- High-frequency methods worth tracing for behavior flow: `ReturnToPool` (6), `method_1` (3), `OnGetFromPool` (3), `method_0` (3), `OnCreatePoolRoleModel` (3), `method_2` (2), `Reset` (2), `SetupAnimator` (2), `EnablePhysics` (1), `SetUsed` (1), `StartAutoDestroyCountDown` (1), `Update` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `JetBrains.Annotations`, `System.Runtime.CompilerServices`, `System.Linq`, `Comfort.Common`, `Microsoft.Extensions.ObjectPool`, `RootMotion.FinalIK`, `EFT.CameraControl`.

**Key elements in this subsystem**
- `AmmoPoolObject`, `AssetPoolObject`, `Class3528`, `GClass3969`, `ObservedPlayerPoolObject`, `PlayerPoolObject`, `Class3527`, `PlayerRigidbodySleepHierarchy`, `PlayerZombiePoolObject`, `PoolContainerObject`, `PoolSafeMonoBehaviour`, `WeaponModPoolObject`.

### Subsystem: `EFT.BackEnd`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/BackEnd` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EDiagnosisSource`.

### Subsystem: `EFT.Ballistics`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 7 (classes: 5, structs: 1, interfaces: 0, enums: 1).
- Method declarations: 36. Field declarations: 31.
- Dominant folders: `EFT/Ballistics` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (1), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Shoot_1` (3), `SimulateShot` (2), `method_0` (2), `UnsubscribeHitAction` (2), `FindPresetIndex` (2), `Shoot` (2), `OnDestroy` (1), `Awake` (1), `Get` (1), `ApplyHit` (1), `IsUnsetup` (1), `Deflects` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `System.Threading`, `EFT.NetworkPackets`, `System.Collections.Generic`, `System.Diagnostics`, `Comfort.Common`, `EFT.AssetsManager`.

**Key elements in this subsystem**
- `BallisticCalculatorPrewarmer`, `Class2721`, `BallisticCollider`, `BallisticColliderComposer`, `BallisticsCalculator`, `Struct976`, `MaterialType`.

### Subsystem: `EFT.Bots`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 0, structs: 1, interfaces: 0, enums: 3).
- Method declarations: 0. Field declarations: 4.
- Dominant folders: `EFT/Bots` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json`.

**Key elements in this subsystem**
- `BotControllerSettings`, `EBossType`, `EBotAmount`, `EBotDifficulty`.

### Subsystem: `EFT.BufferZone`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 7 (classes: 5, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 39. Field declarations: 30.
- Dominant folders: `EFT/BufferZone` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `OnDestroy` (2), `Update` (1), `OnTriggerEnter` (1), `OnTriggerExit` (1), `Start` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `Awake` (2), `OnDestroy` (2), `IsPlayerInZone` (2), `IsPlayerDyingInZone` (2), `Update` (1), `SetUpReferences` (1), `IsPlayerHaveAccess` (1), `ChangePlayerAccessStatus` (1), `method_1` (1), `method_2` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Comfort.Common`, `EFT.Interactive`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Threading.Tasks`, `CommonAssets.Scripts.Game`.

**Key elements in this subsystem**
- `BufferAccessStatusType`, `BufferInnerZone`, `Class2644`, `BufferOuterBattleZone`, `BufferZoneContainer`, `BufferZoneDataReciever`, `EBufferZoneData`.

### Subsystem: `EFT.Builds`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Builds` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EEquipmentBuildType`.

### Subsystem: `EFT.CameraControl`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 13.
- Declared types: 15 (classes: 13, structs: 1, interfaces: 0, enums: 1).
- Method declarations: 42. Field declarations: 134.
- Dominant folders: `EFT/CameraControl` (13).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (4), `OnDestroy` (3), `OnPreCull` (2), `LateUpdate` (2), `OnDisable` (2), `OnPostRender` (1), `OnEnable` (1), `FixedUpdate` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (4), `method_0` (4), `OnDestroy` (3), `OnPreCull` (2), `LateUpdate` (2), `OnDisable` (2), `method_1` (2), `SetMaxFov` (1), `SetBiasByFov` (1), `OnPostRender` (1), `SetPivot` (1), `CopyComponentFromOptic` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `BSG.CameraEffects`, `UnityStandardAssets.ImageEffects`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `EFT.PostEffects`, `UnityEngine.Rendering.PostProcessing`, `UnityEngine.Rendering`, `System.Collections.Generic`, `System.Threading`.

**Key elements in this subsystem**
- `CameraLodBiasController`, `ECameraType`, `OpticComponentUpdater`, `OpticRetrice`, `OpticSight`, `GStruct432`, `PlayerCameraController`, `Class2641`, `ScopeCameraData`, `ScopeData`, `ScopeEffectsData`, `ScopeNightVisionData`, `ScopeReticle`, `ScopeSmoothCameraData`, `ScopeThermalVisionData`.

### Subsystem: `EFT.Character.Data`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 3.
- Dominant folders: `EFT/Character/Data` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `GetVolumeDB` (1), `GetLowpassFreq` (1), `GetReverbLevel` (1), `method_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `FirstPersonPlayerHearingSettings`.

### Subsystem: `EFT.ClientItems.ClientSpecItems`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 9. Field declarations: 8.
- Dominant folders: `EFT/ClientItems/ClientSpecItems` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (1), `OnDisable` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Initialiaze` (1), `InitializeOnlyView` (1), `UpdateStatus` (1), `Disable` (1), `method_0` (1), `method_1` (1), `OnEnable` (1), `OnDisable` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Comfort.Common`, `EFT.InventoryLogic`, `UnityEngine`.

**Key elements in this subsystem**
- `RadioTransmitterView`.

### Subsystem: `EFT.Communications`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 8 (classes: 1, structs: 0, interfaces: 0, enums: 7).
- Method declarations: 2. Field declarations: 6.
- Dominant folders: `EFT/Communications` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Redeem` (1), `Restore` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `Newtonsoft.Json`.

**Key elements in this subsystem**
- `EHideoutNotificationType`, `ENotificationDurationType`, `ENotificationIconType`, `ENotificationRequirements`, `ENotificationType`, `ProfileChangeEvent`, `is`, `EAuxiliaryTypes`.

### Subsystem: `EFT.Console.Commands`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Console/Commands` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `CutsceneCommands`.

### Subsystem: `EFT.Counters`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 0, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Counters` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `CounterTag`, `CounterValueType`, `EFenceStandingSource`.

### Subsystem: `EFT.Customization`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 5 (classes: 4, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 22.
- Dominant folders: `EFT/Customization` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json`.

**Key elements in this subsystem**
- `CustomizationOffer`, `ECustomizationItemCategory`, `ItemRequirements`, `OfferRequirements`, `SkillRequirements`.

### Subsystem: `EFT.DataProviding`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/DataProviding` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EDataLifeTime`.

### Subsystem: `EFT.Development`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 11. Field declarations: 13.
- Dominant folders: `EFT/Development` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (1), `OnDisable` (1), `OnGUI` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `OnDisable` (1), `OnGUI` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `EFT.InventoryLogic`, `UnityEngine`.

**Key elements in this subsystem**
- `RadioTransmitterDebug`.

### Subsystem: `EFT.Dialogs`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 2, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 0. Field declarations: 2.
- Dominant folders: `EFT/Dialogs` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json`.

**Key elements in this subsystem**
- `EDialogConditionType`, `EDialogSide`, `TraderDialogsBackendDTO`, `TraderDialogsDTO`.

### Subsystem: `EFT.EnvironmentEffect`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 5 (classes: 5, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 27. Field declarations: 40.
- Dominant folders: `EFT/EnvironmentEffect` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDrawGizmosSelected` (3), `OnDrawGizmos` (2), `OnValidate` (2), `Awake` (2), `Init` (1), `Update` (1), `OnDestroy` (1), `OnTriggerEnter` (1), `OnTriggerStay` (1), `OnTriggerExit` (1).
- High-frequency methods worth tracing for behavior flow: `OnDrawGizmosSelected` (3), `OnDrawGizmos` (2), `OnValidate` (2), `Awake` (2), `Reinit` (2), `Check` (2), `Init` (1), `GetPlayerCurrentEnvironmentType` (1), `GetEnvironmentByPos` (1), `UpdateEnvironmentForPlayer` (1), `SetTriggerForPlayer` (1), `TryFindTriggerByPos` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `JetBrains.Annotations`, `System.Collections.Generic`, `System.Threading`, `Comfort.Common`, `System.Linq`.

**Key elements in this subsystem**
- `DryPlane`, `EnvironmentManager`, `EnvironmentSwitcherTrigger`, `IndoorTrigger`, `TriggerGroup`.

### Subsystem: `EFT.Game.Spawning`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 12.
- Declared types: 15 (classes: 4, structs: 4, interfaces: 4, enums: 3).
- Method declarations: 24. Field declarations: 37.
- Dominant folders: `EFT/Game/Spawning` (12).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Dispose` (1), `Start` (1), `OnDrawGizmos` (1), `OnDrawGizmosSelected` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `method_1` (2), `Create` (2), `CalcMultiSpawnDelay` (1), `Dispose` (1), `IsNotCollidedArtillery` (1), `IsInPlayersIndividualLimits` (1), `IncreaseUsedPlayerSpawnsForNearestPlayer` (1), `Start` (1), `Contains` (1), `DebugInfo` (1), `CreateSpawnPointParams` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Collections`, `Comfort.Common`, `System.Linq`, `System.Runtime.CompilerServices`, `System.Runtime.InteropServices`.

**Key elements in this subsystem**
- `ActionIfNotEnoughPoints`, `ESpawnCategory`, `ESpawnCategoryMask`, `IPlayersCollection`, `ISpawnColliderParams`, `ISpawnPoint`, `ISpawnPointCollider`, `SpawnBoxParams`, `SpawnPoint`, `SpawnPointMarker`, `Class2540`, `Struct948`, `Class2541`, `SpawnPointParams`, `SpawnSphereParams`.

### Subsystem: `EFT.GameRandoms`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 2.
- Dominant folders: `EFT/GameRandoms` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `GetRandomFloat` (1), `GetNextRandom` (1), `Serialize` (1), `Deserialize` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `MalfunctionRandom`.

### Subsystem: `EFT.GameTriggers`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 34.
- Declared types: 74 (classes: 40, structs: 29, interfaces: 2, enums: 3).
- Method declarations: 189. Field declarations: 198.
- Dominant folders: `EFT/GameTriggers` (34).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (23), `Apply` (20), `OnValidate` (4), `OnRename` (3), `Awake` (3), `OnDestroy` (3), `Dispose` (2), `Execute` (1), `OnAudioSystemInit` (1), `OnLoopSourceFadeOut` (1), `OnEndSoundPlayed` (1), `OnLookAt` (1), `OnTriggerEnter` (1), `OnTriggerExit` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (30), `Start` (23), `Apply` (20), `TryRenameId` (17), `method_1` (13), `method_2` (10), `method_3` (7), `OnValidate` (4), `PlaySound` (4), `Interact` (4), `OnRename` (3), `Awake` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `Comfort.Common`, `System.Runtime.CompilerServices`, `System.Collections`, `EFT.Interactive`, `EFT.InventoryLogic`, `System.Linq`, `EFT.HealthSystem`, `Systems.Effects`.

**Key elements in this subsystem**
- `BaseTriggerHandler`, `DamageData`, `HandlerAnd`, `Class2563`, `HandlerAnimator`, `AnimatorAction`, `AnimatorActionTrigger`, `AnimatorActionBool`, `AnimatorActionInt`, `AnimatorActionFloat`, `Class2565`, `Class2566`, `Class2567`, `HandlerBotsEvent`, `HandlerCompleteQuestCondition`, `HandlerDamage`, `HandlerDelay`, `HandlerEffect`, `IEffectApplier`, `PanicMassFireApplier`, `HeavyBleedingApplier`, `LightBleedingApplier`, `ChronicStaminaFatigueApplier`, `ContusionApplier`, `DisorientationApplier`, `EnduranceApplier`, `FractureApplier`, `RestoreFullHealthsApplier`, `HealthBoostApplier`, `ImmunityPreventedNegativeEffectApplier`, `IntoxicationApplier`, `MisfireEffectHealth`, `OverEncumberedApplier`, `PainKillerApplier`, `RadExposureApplier`, `SandingScreenApplier`, `AddStaminaZeroEffectApplier`, `DoWoundApplier`, `RemoveAllEffects`, `EffectsSet`, ... (+34 more).

### Subsystem: `EFT.GlobalEvents`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 34.
- Declared types: 39 (classes: 36, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 112. Field declarations: 34.
- Dominant folders: `EFT/GlobalEvents` (34).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `OnDestroy` (2), `Apply` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `Serialize` (25), `Deserialize` (25), `Reset` (23), `Invoke` (20), `IsFilterPassed` (4), `Awake` (2), `OnDestroy` (2), `method_0` (2), `Dispatch` (2), `Add` (1), `Create` (1), `TryGet` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `EFT.Interactive`, `System.Runtime.CompilerServices`, `System.Threading`, `EFT.Vehicle`, `System.Reflection`, `Newtonsoft.Json`, `Cutscene`.

**Key elements in this subsystem**
- `BaseEventFilter`, `BaseReconnectEvent`, `BotsCrowdSpawnEvent`, `BtrIncomingToDestinationGlobalEvent`, `BtrNotificationInteractionMessageEvent`, `BtrPauseMoveEvent`, `BtrReadyToDepartureEvent`, `BtrServicePurchaseEvent`, `BtrSpawnOnThePathEvent`, `BtrViewReadyEvent`, `CommonEventData`, `Class2531`, `DoorInteractingEventFilter`, `EventFilterAnimationRelation`, `HalloweenSummonStartedEvent`, `HalloweenSyncExitsEvent`, `HalloweenSyncStateEvent`, `InteractiveObjectInteractionResultEvent`, `InteractWithKeeperZoneEventFilter`, `NotifyEvent`, `EMessageType`, `NPCGlobalEventsReacting`, `AnimationInt`, `PlayerInteractionWithBufferZoneEventFilter`, `ReactionOnEvent`, `SeasonsReconnectEvent`, `StormStartedEvent`, `SyncClientEventState`, `SyncEvent`, `SyncEventFromClient`, `SyncEventFromServer`, `TransitGroupSizeEvent`, `TransitGroupTimerEvent`, `TransitInitEvent`, `TransitInteractionEvent`, `EType`, `TransitMessagesEvent`, `EType`, `TransitUpdateEvent`.

### Subsystem: `EFT.GlobalEvents.ArtilleryShellingEcents`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 8.
- Declared types: 8 (classes: 8, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 32. Field declarations: 0.
- Dominant folders: `EFT/GlobalEvents/ArtilleryShellingEcents` (8).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Invoke` (8), `Reset` (8), `Serialize` (8), `Deserialize` (8).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `ChangePlayerShellingAlertStateEvent`, `FirstShellingExplosionInRoundEvent`, `FirstShellingExplosionZoneEvent`, `InitShellingProjectileFlyEvent`, `LastShellingExplosionInRoundEvent`, `LastShellingExplosionZoneEvent`, `ShellingNotifyEvent`, `ShellingProjectileExplosionEvent`.

### Subsystem: `EFT.GlobalEvents.AudioEvents`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 8. Field declarations: 0.
- Dominant folders: `EFT/GlobalEvents/AudioEvents` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Invoke` (2), `Serialize` (2), `Deserialize` (2), `Reset` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `CommonAssets.Scripts.Audio.RadioSystem`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `BroadcastItemChangedEvent`, `RadioStationsSyncEvent`.

### Subsystem: `EFT.HandBook`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 10.
- Declared types: 14 (classes: 13, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 54. Field declarations: 60.
- Dominant folders: `EFT/HandBook` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (3), `OnPointerEnter` (1), `OnPointerExit` (1), `OnPointerClick` (1).
- High-frequency methods worth tracing for behavior flow: `Show` (8), `method_0` (7), `method_1` (5), `Close` (5), `method_2` (4), `method_3` (4), `Awake` (3), `method_4` (3), `ClearEntity` (2), `ShowEntity` (1), `AddToFilteredList` (1), `OnPointerEnter` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.UI`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `System.Collections.Generic`, `EFT.UI.Ragfair`, `UnityEngine.EventSystems`, `UnityEngine.UI`, `System.Linq`, `UnityEngine.Events`.

**Key elements in this subsystem**
- `BaseHandbookData`, `ENodeType`, `EntitiesPanel`, `EntityIcon`, `EntityListElement`, `Class2517`, `Class2518`, `HandbookCategoriesPanel`, `Class2524`, `HandbookCategoryView`, `HandbookData`, `HandbookItemPreview`, `HandbookScreen`, `GClass3862`.

### Subsystem: `EFT.HealthSystem`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 8.
- Declared types: 70 (classes: 65, structs: 1, interfaces: 1, enums: 3).
- Method declarations: 246. Field declarations: 129.
- Dominant folders: `EFT/HealthSystem` (8).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (4).
- High-frequency methods worth tracing for behavior flow: `Started` (22), `RegularUpdate` (16), `method_0` (15), `method_5` (8), `Residue` (7), `method_6` (7), `Removed` (6), `method_1` (6), `Added` (5), `method_7` (5), `Store` (4), `Serialize` (4).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`, `Comfort.Common`, `EFT.InventoryLogic`, `System.IO`, `System.Linq`, `System.Runtime.CompilerServices`, `JetBrains.Annotations`.

**Key elements in this subsystem**
- `ActiveHealthController`, `Effect`, `GClass3008`, `Berserk`, `LightBleeding`, `HeavyBleeding`, `Bleeding`, `BodyTemperature`, `ChronicStaminaFatigue`, `Contusion`, `DamageModifier`, `Dehydration`, `Disorientation`, `Encumbered`, `Endurance`, `Exhaustion`, `Existence`, `Flash`, `Fracture`, `Frostbite`, `FullHealthRegenerationEffect`, `HalloweenBuff`, `HealthBoost`, `ImmunityPreventedNegativeEffect`, `Intoxication`, `LethalIntoxication`, `LowEdgeHealth`, `MedEffect`, `Class2221`, `MisfireEffect`, `MildMusclePain`, `SevereMusclePain`, `MusclePain`, `OverEncumbered`, `Pain`, `PainKiller`, `PanicEffect`, `RadExposure`, `Regeneration`, `SandingScreen`, ... (+30 more).

### Subsystem: `EFT.Hideout`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 178.
- Declared types: 288 (classes: 260, structs: 6, interfaces: 1, enums: 21).
- Method declarations: 1020. Field declarations: 1037.
- Dominant folders: `EFT/Hideout` (178).

**How it works (operational pattern)**
- Hideout modules appear to be simulation loops plus progression/economy gates. Components synchronize production state, user actions, and visual feedback.
- Typical flow is timer/production tick processing, requirement checks, then UI/event updates.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (23), `Awake` (17), `Update` (13), `OnEnable` (7), `OnDestroy` (6), `Dispose` (5), `OnDisable` (5), `OnPointerClick` (3), `OnInitializeItemInserted` (3), `Start` (2), `OnTriggerEnter` (2), `OnTriggerExit` (2), `OnStartDrag` (1), `OnPointerEnter` (1), `OnPointerExit` (1), `OnInitializePotentialDrag` (1), `OnBeginDrag` (1), `OnDrag` (1), `OnEndDrag` (1), `OnItemRemoved` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (121), `method_1` (82), `method_2` (55), `method_3` (44), `Show` (39), `method_4` (35), `method_5` (26), `Close` (26), `Init` (23), `method_6` (23), `method_7` (22), `method_8` (20).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `EFT.UI`, `System.Threading.Tasks`, `EFT.InventoryLogic`, `System.Linq`, `Comfort.Common`, `Newtonsoft.Json`, `TMPro`.

**Key elements in this subsystem**
- `AbstractPanel`, `AirFilteringUnitBehaviour`, `AmbianceController`, `AmbianceObject`, `AreaData`, `Class1963`, `Class1964`, `Class1965`, `AreaDetails`, `DetailsDisplaySettings`, `AreaIcon`, `Class1987`, `Class1988`, `Class1989`, `AreaLevelAudio`, `AreaPanel`, `AreaPanelSettings`, `AreaRequirement`, `AreaRequirementIcon`, `AreaRequirementPanel`, `Class2000`, `AreaScreenSubstrate`, `Class1968`, `Class1969`, `Class1970`, `Class1971`, `Class1972`, `AreasPanel`, `Class1973`, `Class1974`, `Class1975`, `AreasScrollRect`, `AreaSubstrateSettings`, `AreaTemplate`, `AreaWorldPanel`, `Class2001`, `Struct689`, `AudioAmbiance`, `MuteSettings`, `SimpleAudioSettings`, ... (+248 more).

### Subsystem: `EFT.Hideout.ShootingRange`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 18.
- Declared types: 36 (classes: 30, structs: 0, interfaces: 0, enums: 6).
- Method declarations: 216. Field declarations: 165.
- Dominant folders: `EFT/Hideout/ShootingRange` (18).

**How it works (operational pattern)**
- Hideout modules appear to be simulation loops plus progression/economy gates. Components synchronize production state, user actions, and visual feedback.
- Typical flow is timer/production tick processing, requirement checks, then UI/event updates.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (5), `OnEnable` (5), `OnDisable` (5), `Start` (3), `OnStartCommon` (2), `OnCompleteFold` (2), `OnCompleteUnfold` (2), `FixedUpdate` (1), `OnDrawGizmos` (1), `Update` (1), `OnDestroy` (1), `Run` (1), `OnTriggerEnter` (1), `OnTriggerExit` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (22), `method_1` (13), `Fold` (8), `Unfold` (8), `method_2` (7), `method_3` (6), `method_4` (6), `Awake` (5), `InteractionStates` (5), `OnEnable` (5), `OnDisable` (5), `method_5` (5).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Threading.Tasks`, `System.Linq`, `DG.Tweening`, `DG.Tweening.Core`, `DG.Tweening.Plugins.Options`, `System.Collections.Generic`, `Comfort.Common`.

**Key elements in this subsystem**
- `FoldingPopperTarget`, `Class2029`, `Class2030`, `FoldingTarget`, `Class2031`, `Class2032`, `FoldingTargetState`, `HideoutTargetBallisticCollider`, `InteractiveShootingRange`, `PaperTarget`, `PaperTargetControl`, `Class2022`, `PopperTarget`, `PopperTargetControl`, `Class2023`, `Class2024`, `PopperTargets`, `Class2033`, `Class2034`, `Class2035`, `RailTarget`, `MovementSpeed`, `HorizontalDirection`, `VerticalDirection`, `MovementType`, `RailTargetControl`, `Class2025`, `RailTargets`, `Class2036`, `Class2037`, `Class2038`, `SharedTargetControl`, `ShootingAreaTrigger`, `ShootingScoreInterface`, `TargetColliderType`, `Turnstile`.

### Subsystem: `EFT.Impostors`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 7.
- Declared types: 11 (classes: 7, structs: 3, interfaces: 0, enums: 1).
- Method declarations: 43. Field declarations: 57.
- Dominant folders: `EFT/Impostors` (7).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `OnEnable` (2), `OnDisable` (2), `OnDestroy` (1), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (6), `method_1` (3), `smethod_0` (2), `method_2` (2), `method_3` (2), `Awake` (2), `OnEnable` (2), `OnDisable` (2), `smethod_1` (1), `smethod_2` (1), `smethod_3` (1), `smethod_4` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Collections.Generic`, `System.Linq`, `System.IO`, `System.Threading.Tasks`, `AmplifyImpostors`, `JetBrains.Annotations`, `Sirenix.OdinInspector`.

**Key elements in this subsystem**
- `AmplifyImpostorsArray`, `Class2094`, `Class2095`, `Class2096`, `AmplifyImpostorsArrayElement`, `EImpostorsShadowMode`, `ImpostorBounds`, `ImpostorPropBlock`, `ImpostorsRenderer`, `Class2111`, `ImpostorVertex`.

### Subsystem: `EFT.InputSystem`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 16.
- Declared types: 22 (classes: 14, structs: 0, interfaces: 0, enums: 8).
- Method declarations: 69. Field declarations: 43.
- Dominant folders: `EFT/InputSystem` (16).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `Start` (1), `Update` (1), `FixedUpdate` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (6), `Equals` (5), `Clone` (4), `GetHashCode` (3), `TranslateCommand` (3), `ShouldLockCursor` (3), `TranslateAxes` (3), `method_1` (3), `EqualityCheck` (2), `CopyItem` (2), `Create` (2), `smethod_0` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Runtime.CompilerServices`, `UnityEngine`, `Newtonsoft.Json`, `System.Linq`, `System.ComponentModel`, `Sirenix.OdinInspector`, `System.Text`, `Comfort.Common`, `EFT.UI`.

**Key elements in this subsystem**
- `AxisGroup`, `AxisPair`, `Class1861`, `EAxis`, `ECommand`, `ECursorResult`, `EGameKey`, `EKeyPress`, `EmptyInputNode`, `EPressType`, `InputManager`, `Class1883`, `Class1884`, `InputNode`, `ETranslateResult`, `InputNodeAbstract`, `InputSource`, `InputTree`, `KeyGroup`, `Class1887`, `MouseAxisUpdateType`, `UIInputRoot`.

### Subsystem: `EFT.Interactive`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 97.
- Declared types: 159 (classes: 128, structs: 7, interfaces: 2, enums: 22).
- Method declarations: 680. Field declarations: 821.
- Dominant folders: `EFT/Interactive` (97).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (20), `OnDestroy` (13), `OnTriggerEnter` (11), `OnTriggerExit` (11), `OnValidate` (10), `Update` (8), `Start` (8), `Init` (5), `OnEnable` (4), `OnTriggerStay` (3), `OnStatusChangedHandler` (3), `OnDrawGizmosSelected` (3), `Dispose` (2), `OnItemTransferred` (2), `OnReturnToPool` (2), `OnAfterDeserialize` (1), `OnBeforeSerialize` (1), `LateUpdate` (1), `OnPlayerExit` (1), `OnPlayerEnter` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (53), `method_1` (25), `Awake` (20), `method_2` (17), `method_3` (14), `OnDestroy` (13), `method_4` (13), `method_5` (12), `OnTriggerEnter` (11), `OnTriggerExit` (11), `OnValidate` (10), `method_8` (10).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `Comfort.Common`, `System.Linq`, `System.Collections`, `EFT.InventoryLogic`, `System.Threading`, `System.Diagnostics`, `Audio.SpatialSystem`.

**Key elements in this subsystem**
- `Appliance`, `BarbedWire`, `BaseRestrictableZone`, `BeaconPlacer`, `BetterPropagationGroups`, `Volumes`, `Class2712`, `BetterPropagationVolume`, `BorderZone`, `BrokenWindowPieceCollider`, `BrokenWindowPieceTemplate`, `BufferGates`, `BufferGateSwitcher`, `CandleSwitcher`, `CarExtraction`, `ColliderReporter`, `CommonTransportee`, `Corpse`, `Class2668`, `Class2669`, `DamageTrigger`, `Door`, `DoorAmbuent`, `DoorSwitch`, `EDoorState`, `EExfiltrationStatus`, `EExfiltrationType`, `EHingeOrientation`, `ELootableContainerSpawnType`, `ENodeStatus`, `ERequirementState`, `ESpecificInteractionContext`, `EventObjectInteractive`, `EventObjectTrigger`, `EVolumeRelations`, `EVolumeRelationsMask`, `ExfiltrationDoor`, `DoorTransform`, `ExfiltrationPoint`, `Class2649`, ... (+119 more).

### Subsystem: `EFT.Interactive.SecretExfiltrations`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 3, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 16. Field declarations: 10.
- Dominant folders: `EFT/Interactive/SecretExfiltrations` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnItemTransferred` (1).
- High-frequency methods worth tracing for behavior flow: `Reset` (2), `Serialize` (2), `Deserialize` (2), `Invoke` (2), `LoadSettings` (1), `InitSecretExfilPoint` (1), `method_5` (1), `ExternalDiscoverOfPoint` (1), `InfiltrationMatch` (1), `OnItemTransferred` (1), `Proceed` (1), `TransferExitItem` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.GlobalEvents`, `System.Linq`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`.

**Key elements in this subsystem**
- `SecretExfiltrationPoint`, `SecretExfiltrationPointFoundShareEvent`, `SecretExfiltrationPointFoundSyncEvent`.

### Subsystem: `EFT.InventoryLogic`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 114.
- Declared types: 205 (classes: 148, structs: 3, interfaces: 9, enums: 45).
- Method declarations: 667. Field declarations: 658.
- Dominant folders: `EFT/InventoryLogic` (114).

**How it works (operational pattern)**
- InventoryLogic acts as transactional domain logic: validation, move/swap constraints, item class relationships, and rule enforcement.
- It often forms the “source of truth” pipeline that UI and interaction layers call into.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Apply` (4), `OnInit` (2), `OnAmmoLoadedCall` (1), `OnAmmoUnloadedCall` (1), `OnMagazineCheckCall` (1), `OnShot` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (83), `method_1` (49), `method_2` (30), `method_3` (22), `method_4` (20), `ToString` (19), `method_5` (17), `method_6` (14), `GetLocalizedDescription` (13), `TryFindItem` (11), `method_7` (10), `GetHashSum` (8).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Linq`, `UnityEngine`, `JetBrains.Annotations`, `Comfort.Common`, `Diz.Binding`, `Newtonsoft.Json`, `Diz.LanguageExtensions`, `JsonType`.

**Key elements in this subsystem**
- `AmmoBox`, `AmmoBoxTemplate`, `AmmoCaliber`, `AmmoTemplate`, `Class2332`, `AmmoType`, `AnimationVariantsComponent`, `ArmorComponent`, `Class2278`, `Class2279`, `ArmorHolderComponent`, `EArmorPlateFiltering`, `Class2282`, `Class2283`, `BackendIdGenerator`, `BarrelComponent`, `BuffComponent`, `Class2286`, `Class2287`, `CantPutIntoDuringRaidComponent`, `CantRemoveFromSlotsDuringRaidComponent`, `CommandStatus`, `CompositeArmorComponent`, `CompoundItem`, `Class2341`, `Class2342`, `CurveMultiplier`, `DogtagComponent`, `Class2288`, `EArmorMaterial`, `EArmorType`, `EBoundItem`, `ECommandResult`, `ECurrencyType`, `EDamageEffectType`, `EDeafStrength`, `EDogtagExchangeSide`, `EEditBuildItemAvailability`, `EErrorHandlingType`, `EHighlightScope`, ... (+165 more).

### Subsystem: `EFT.InventoryLogic.Operations`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 1.
- Dominant folders: `EFT/InventoryLogic/Operations` (1).

**How it works (operational pattern)**
- InventoryLogic acts as transactional domain logic: validation, move/swap constraints, item class relationships, and rule enforcement.
- It often forms the “source of truth” pipeline that UI and interaction layers call into.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `method_3` (1), `Terminate` (1), `ToString` (1), `Dispose` (1), `ToDescriptor` (1), `ToBaseInventoryCommand` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `SearchContentOperation`.

### Subsystem: `EFT.ItemGameSounds`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 2. Field declarations: 7.
- Dominant folders: `EFT/ItemGameSounds` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `GetSoundBank` (1), `GetBank` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `JsonType`, `System.Collections.Generic`, `UnityEngine`.

**Key elements in this subsystem**
- `ItemDropSounds`, `ItemDropSurfaceSet`.

### Subsystem: `EFT.ItemInHandSubsystem`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/ItemInHandSubsystem` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.InventoryLogic`.

**Key elements in this subsystem**
- `ILeftHandController`.

### Subsystem: `EFT.MovingPlatforms`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 3.
- Declared types: 12 (classes: 9, structs: 0, interfaces: 1, enums: 2).
- Method declarations: 54. Field declarations: 82.
- Dominant folders: `EFT/MovingPlatforms` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (3), `OnRouteFinished` (2), `OnDrawGizmosSelected` (2), `LateUpdate` (1), `Update` (1), `FixedUpdate` (1), `OnTriggerEnter` (1), `OnTriggerExit` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (5), `Init` (3), `Move` (3), `method_1` (3), `PlaceAtStartPosition` (3), `PlaceAtEndPosition` (3), `method_2` (3), `method_3` (2), `OnRouteFinished` (2), `OnDrawGizmosSelected` (2), `CalculateLength` (2), `GetPosition` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Linq`, `System.Runtime.CompilerServices`, `BezierSplineTools`, `System.Collections.Generic`, `System.Collections`, `Comfort.Common`, `UnityEngine.Serialization`.

**Key elements in this subsystem**
- `Carriage`, `CarriageMoveType`, `Class2605`, `Locomotive`, `SoundEvent`, `ETravelState`, `Class2606`, `Class2607`, `MovingPlatform`, `GInterface459`, `GClass3596`, `PreciseInterpolator`.

### Subsystem: `EFT.Network`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 3.
- Declared types: 4 (classes: 2, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 5. Field declarations: 7.
- Dominant folders: `EFT/Network` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDrawGizmos` (1).
- High-frequency methods worth tracing for behavior flow: `AddValue` (2), `OnDrawGizmos` (1), `method_0` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `System.Runtime.CompilerServices`, `UnityEngine`.

**Key elements in this subsystem**
- `NetworkChannel`, `NetworkSystemMessageType`, `ServerDebugGraph`, `Class2270`.

### Subsystem: `EFT.Network.Transport`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Network/Transport` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `SystemReasonDisconnection`.

### Subsystem: `EFT.NetworkPackets`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 0, structs: 0, interfaces: 0, enums: 4).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/NetworkPackets` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EHandsTypePacket`, `EHitSpecial`, `EHitType`, `SyncPositionReason`.

### Subsystem: `EFT.NextObservedPlayer`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 15.
- Declared types: 16 (classes: 14, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 238. Field declarations: 184.
- Dominant folders: `EFT/NextObservedPlayer` (15).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (3), `Execute` (3), `Dispose` (3), `OnFireEnd` (2), `OnWeapIn` (2), `Initialize` (2), `LateUpdate` (2), `Update` (1), `OnAddAmmoInChamber` (1), `OnAddAmmoInMag` (1), `OnCook` (1), `OnDelAmmoChamber` (1), `OnDelAmmoFromMag` (1), `OnArm` (1), `OnDisarm` (1), `OnFiringBullet` (1), `OnFoldOff` (1), `OnFoldOn` (1), `OnIdleStart` (1), `OnLauncherAppeared` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (5), `method_1` (4), `method_2` (4), `OnDestroy` (3), `method_3` (3), `method_4` (3), `Execute` (3), `method_5` (3), `method_6` (3), `Dispose` (3), `OnFireEnd` (2), `OnWeapIn` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `Comfort.Common`, `AnimationEventSystem`, `Audio.SpatialSystem`, `EFT.InventoryLogic`, `EFT.Vaulting`, `System.Threading.Tasks`.

**Key elements in this subsystem**
- `BaseAnimationHandsController`, `CommandMessageAutoSenderCoroutineController`, `CommandMessageType`, `EGrenadeAttackVariation`, `EmptyAnimationHandsController`, `FirearmAnimationHandsController`, `GrenadeAnimationHandsController`, `KnifeAnimationHandsController`, `MedsAnimationHandsController`, `ObservedPlayerAudioController`, `ObservedPlayerMovementContext`, `ObservedPlayerView`, `ObservedPlayerVoIP`, `Class2200`, `QuickUseItemAnimationHandsController`, `UsableItemAnimationHandsController`.

### Subsystem: `EFT.NextObserver.ObservedPlayerScene`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/NextObserver/ObservedPlayerScene` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `ObservedPlayerSceneController`.

### Subsystem: `EFT.NPC`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 10 (classes: 8, structs: 1, interfaces: 0, enums: 1).
- Method declarations: 88. Field declarations: 48.
- Dominant folders: `EFT/NPC` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (6), `OnDestroy` (5), `Initialize` (1), `OnCurrentAnimStateEnded` (1), `OnSetActiveObject` (1), `OnDeactivateObject` (1), `OnSound` (1), `OnAddAmmoInChamber` (1), `OnAddAmmoInMag` (1), `OnArm` (1), `OnCook` (1), `OnDelAmmoChamber` (1), `OnDelAmmoFromMag` (1), `OnDisarm` (1), `OnFireEnd` (1), `OnFiringBullet` (1), `OnFoldOff` (1), `OnFoldOn` (1), `OnIdleStart` (1), `OnMagHide` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (7), `Awake` (6), `OnDestroy` (5), `method_1` (4), `method_2` (3), `method_3` (2), `method_4` (2), `method_5` (2), `Initialize` (1), `OnCurrentAnimStateEnded` (1), `OnSetActiveObject` (1), `OnDeactivateObject` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Collections`, `EFT.GlobalEvents`, `System.Runtime.CompilerServices`, `Comfort.Common`, `System.Threading`, `AnimationEventSystem`, `Cutscene`.

**Key elements in this subsystem**
- `AnimationIntRandomizerByTimer`, `AnimatorByEventsToggler`, `NPCAdditionalSoundPlayer`, `AudioClipWithID`, `NPCAnimationsEventReceiver`, `NPCFootStepsSoundPlayer`, `NPCReactionController`, `EProcessQueueState`, `ObjectWithID`, `Class1858`.

### Subsystem: `EFT.ObstacleCollision`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 1, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/ObstacleCollision` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `IObstacleCollisionFacade`.

### Subsystem: `EFT.Particles`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 1. Field declarations: 2.
- Dominant folders: `EFT/Particles` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Emit` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `BasicParticleSystemMediator`.

### Subsystem: `EFT.PostEffects`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 3. Field declarations: 9.
- Dominant folders: `EFT/PostEffects` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `OnDestroy` (1), `UpdateParameters` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `OpticCullingMask`.

### Subsystem: `EFT.PrefabSettings`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 2, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 1. Field declarations: 14.
- Dominant folders: `EFT/PrefabSettings` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `SwitchMeshRenderersActive` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `FlareCartridgeSettings`, `FlareColorType`, `TripwireVisual`.

### Subsystem: `EFT.ProfileEditor.UI`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 13 (classes: 13, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 34. Field declarations: 39.
- Dominant folders: `EFT/ProfileEditor/UI` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnPointerEnter` (1), `OnPointerExit` (1), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (11), `method_1` (7), `Show` (5), `method_2` (3), `smethod_0` (2), `Equip` (2), `SetEquipped` (1), `OnPointerEnter` (1), `OnPointerExit` (1), `Awake` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `EFT.UI`, `UnityEngine`, `UnityEngine.Events`, `EFT.InventoryLogic`, `UnityEngine.UI`, `System.Collections.Generic`, `System.Linq`, `Comfort.Common`, `JetBrains.Annotations`.

**Key elements in this subsystem**
- `DressItemPanel`, `Class2084`, `Class2085`, `Class2086`, `DressRoomEquipmentItemView`, `Class2087`, `EquipmentItemPanel`, `Class2088`, `ProfileButton`, `SimpleEquipmentItemSelector`, `Class2089`, `Class2090`, `Class2091`.

### Subsystem: `EFT.Quests`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 68.
- Declared types: 83 (classes: 74, structs: 0, interfaces: 1, enums: 8).
- Method declarations: 53. Field declarations: 96.
- Dominant folders: `EFT/Quests` (68).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDeserialized` (4), `OnDeserializedMethod` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `GenerateFormattedDescription` (12), `IdentityFields` (6), `OnDeserialized` (4), `ToString` (3), `method_0` (3), `Test` (3), `HasIdentity` (2), `CalculateIdentity` (2), `Compare` (1), `UpdateFromAnotherItem` (1), `OnDeserializedMethod` (1), `LocalizeDescription` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Linq`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `Newtonsoft.Json`, `System.Text`, `System.Runtime.Serialization`, `System.Text.RegularExpressions`, `Comfort.Common`, `System.ComponentModel`, `Diz.Binding`.

**Key elements in this subsystem**
- `Condition`, `Class3552`, `ConditionAchievementUnlocked`, `ConditionArenaBattlePassProgressionLevel`, `ConditionArenaBattlePassUnlockedItems`, `ConditionArenaDeathCount`, `ConditionArenaEnemyPreset`, `ConditionArenaGameMode`, `ConditionArenaMatchPlace`, `ConditionArenaPlayerAction`, `Class3548`, `ConditionArenaPlayerInTeamPlace`, `ConditionArenaPlayerPreset`, `ConditionArenaPreset`, `EPresetTagTypes`, `Class3549`, `ConditionArenaRankingMode`, `ConditionArenaRatingPointsCount`, `ConditionArenaRoundCount`, `ConditionArenaRoundPlace`, `ConditionArenaRoundResult`, `Class3550`, `ConditionBlock`, `ConditionCompleteCondition`, `ConditionCounterCreator`, `ConditionCounterTemplate`, `ConditionCounterManager`, `Class3557`, `Class3558`, `ConditionEquipment`, `Class3560`, `ConditionExamineItem`, `ConditionExitName`, `ConditionExitStatus`, `ConditionExperience`, `ConditionFindItem`, `Class3561`, `ConditionGlobalVariableValue`, `ConditionHandoverItem`, `ETagHandoverTypes`, ... (+43 more).

### Subsystem: `EFT.Rendering.Clouds`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 14.
- Declared types: 18 (classes: 9, structs: 5, interfaces: 1, enums: 3).
- Method declarations: 31. Field declarations: 82.
- Dominant folders: `EFT/Rendering/Clouds` (14).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (2), `LateUpdate` (2), `Awake` (2), `Init` (2), `OnDisable` (1), `OnPreRenderCamera` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `OnEnable` (2), `LateUpdate` (2), `method_1` (2), `smethod_0` (2), `smethod_1` (2), `GetHashCode` (2), `Awake` (2), `Init` (2), `Relay` (2), `OnDisable` (1), `OnPreRenderCamera` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `EFT.Weather`, `UnityEngine.Rendering`, `System.Globalization`.

**Key elements in this subsystem**
- `AnimationBridge`, `CloudController`, `CloudDataDefaultResources`, `CloudLayer`, `CloudMap`, `CloudLayerAnimationBridge`, `CloudProxy`, `CloudMapProxy`, `CloudRemapData`, `Opacity`, `CloudResolution`, `CloudSettings`, `CloudShadowsResolution`, `IRelay`, `WeatherDebugAnimationBridge`, `WeatherDebugProxy`, `WindOrientation`, `WindOverrideMode`.

### Subsystem: `EFT.RocketLauncher`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 17. Field declarations: 16.
- Dominant folders: `EFT/RocketLauncher` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (1), `Update` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (1), `Launch` (1), `Update` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1), `method_8` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections`, `System.Runtime.CompilerServices`, `System.Threading`, `Comfort.Common`, `EFT.Ballistics`, `EFT.Interactive`, `EFT.InventoryLogic`, `EFT.NetworkPackets`, `UnityEngine`.

**Key elements in this subsystem**
- `RocketProjectile`.

### Subsystem: `EFT.RocketLauncher.Explosion`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 3 (classes: 2, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 5. Field declarations: 12.
- Dominant folders: `EFT/RocketLauncher/Explosion` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (1).
- High-frequency methods worth tracing for behavior flow: `Update` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `UnityEngine`.

**Key elements in this subsystem**
- `ComputableUnitType`, `ExplosionTester`, `Class3535`.

### Subsystem: `EFT.Settings.Graphics`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 12.
- Declared types: 14 (classes: 2, structs: 2, interfaces: 0, enums: 10).
- Method declarations: 10. Field declarations: 4.
- Dominant folders: `EFT/Settings/Graphics` (12).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `Equals` (4), `ToString` (2), `GetHashCode` (2), `Compare` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json`, `System.Collections.Generic`, `UnityEngine`.

**Key elements in this subsystem**
- `AspectRatio`, `CloudsMode`, `EAntialiasingMode`, `EDLSSMode`, `EFSR2Mode`, `EFSR3Mode`, `EFSRMode`, `EftResolution`, `Class1830`, `Class1831`, `ENvidiaReflexMode`, `ESamplingMode`, `ESSAOMode`, `ESSRMode`.

### Subsystem: `EFT.SpeedTree`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 6 (classes: 3, structs: 3, interfaces: 0, enums: 0).
- Method declarations: 27. Field declarations: 103.
- Dominant folders: `EFT/SpeedTree` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1).
- High-frequency methods worth tracing for behavior flow: `Equals` (5), `GetHashCode` (3), `SetParams` (2), `RecordTick` (1), `SaveAsMinWindData` (1), `SaveAsMaxWindData` (1), `method_0` (1), `method_1` (1), `method_2` (1), `ExportToFile` (1), `method_3` (1), `method_4` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`, `System.IO`, `System.Linq`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `SpeedTreeWindStatistic`, `TreeWind`, `BaseTreeData`, `FactorTreeData`, `Settings`, `Class2093`.

### Subsystem: `EFT.StreamingAnimatorSystem`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 12. Field declarations: 6.
- Dominant folders: `EFT/StreamingAnimatorSystem` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (1), `OnDisable` (1).
- High-frequency methods worth tracing for behavior flow: `OnEnable` (1), `OnDisable` (1), `SetDonorAnimator` (1), `ClearData` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `method_5` (1), `method_6` (1), `method_7` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `UnityEngine`.

**Key elements in this subsystem**
- `StreamingAnimator`.

### Subsystem: `EFT.SynchronizableObjects`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 11.
- Declared types: 14 (classes: 9, structs: 1, interfaces: 0, enums: 4).
- Method declarations: 49. Field declarations: 74.
- Dominant folders: `EFT/SynchronizableObjects` (11).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `Awake` (1), `OnTriggerEnter` (1), `Initialize` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (4), `Deserialize` (3), `method_1` (3), `method_2` (3), `ApplyAirdropViewType` (1), `SetActive` (1), `Init` (1), `InitStaticObject` (1), `SetLogic` (1), `UpdateSyncObjectData` (1), `ManualUpdate` (1), `CollisionEnter` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `Comfort.Common`, `System.Collections.Generic`, `System.Threading`, `EFT.Interactive`, `System.Linq`, `EFT.Airdrop`, `EFT.InventoryLogic`, `Sirenix.OdinInspector`.

**Key elements in this subsystem**
- `AirdropSynchronizableObject`, `EAirdropViewType`, `AirdropViewData`, `Class2123`, `AirplaneSynchronizableObject`, `AirplaneView`, `ESynchronizableObjectState`, `ETripwireState`, `SynchronizableObject`, `SynchronizableObjectType`, `SyncObjectCollisionChecker`, `TripwireInteractionTrigger`, `TripwireProceduralMesh`, `TripwireSynchronizableObject`.

### Subsystem: `EFT.Test`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 2. Field declarations: 2.
- Dominant folders: `EFT/Test` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (1).
- High-frequency methods worth tracing for behavior flow: `SetOverload` (1), `Update` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Threading`, `UnityEngine`.

**Key elements in this subsystem**
- `CPUOverload`.

### Subsystem: `EFT.Trading`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 1, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 3.
- Dominant folders: `EFT/Trading` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.InventoryLogic`, `Newtonsoft.Json`.

**Key elements in this subsystem**
- `TradingItemReference`.

### Subsystem: `EFT.Tripwire`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 7.
- Dominant folders: `EFT/Tripwire` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `GetDefuseLoopSound` (1), `GetDefuseEndSound` (1), `GetGrenadePinSound` (1), `GetFuzeSound` (1), `GetDefuseCollisionSound` (1), `TryGetPlantSound` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Audio.Data`, `UnityEngine`.

**Key elements in this subsystem**
- `TripwireSoundStorageSO`.

### Subsystem: `EFT.UI`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 462.
- Declared types: 948 (classes: 880, structs: 19, interfaces: 2, enums: 47).
- Method declarations: 3848. Field declarations: 4669.
- Dominant folders: `EFT/UI` (462).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (126), `Update` (60), `Init` (38), `OnPointerExit` (36), `OnPointerEnter` (34), `OnPointerClick` (27), `OnDisable` (26), `OnEnable` (20), `OnDestroy` (20), `OnPointerDown` (12), `Start` (10), `OnPointerUp` (9), `LateUpdate` (6), `OnApplicationFocus` (6), `OnDrag` (4), `OnBeginDrag` (4), `OnItemRemoved` (4), `OnScroll` (4), `OnAnswerToggleValueChanged` (3), `OnEmptyViewClickHandler` (2).
- High-frequency methods worth tracing for behavior flow: `method_0` (491), `Show` (322), `method_1` (255), `method_2` (166), `method_3` (150), `Close` (133), `Awake` (126), `method_4` (119), `method_5` (95), `method_6` (75), `method_7` (62), `Update` (60).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `UnityEngine.UI`, `System.Collections.Generic`, `TMPro`, `JetBrains.Annotations`, `EFT.InventoryLogic`, `UnityEngine.Events`, `System.Linq`, `UnityEngine.EventSystems`.

**Key elements in this subsystem**
- `AcceptQuestChangeWindow`, `AccountSideSelectionScreen`, `AchievementGlobalProgressTooltip`, `AchievementNotificationView`, `AchievementObjectiveView`, `AchievementsObjectivesView`, `Class3082`, `AchievementsScreen`, `AchievementsTabController`, `Class2739`, `Class2740`, `AchievementsSortPanel`, `AchievementsSortToButtonDictionary`, `ActionPanel`, `Class2779`, `AmmoCountPanel`, `AmmoSelector`, `Class2749`, `Class2750`, `Class2751`, `AnimatedTextPanel`, `AnimatedToggle`, `AnswerRawView`, `AnswersContainerView`, `Class2754`, `ArenaEftItemTransferGridView`, `ArenaEftTransferServiceView`, `Class2755`, `AssembleBuildWindow`, `Class2756`, `AssembleModPanel`, `Class2757`, `Class2758`, `AzimuthPanel`, `BanTimeWindow`, `BarterSchemePanel`, `Class3079`, `BaseDialogScreen`, `BaseDropDownBox`, `Struct1160`, ... (+908 more).

### Subsystem: `EFT.UI.BattleTimer`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 9 (classes: 7, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 35. Field declarations: 60.
- Dominant folders: `EFT/UI/BattleTimer` (6).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (5), `Show` (3), `UpdateTimer` (3), `SetTimerText` (3), `SetTimerCaption` (1), `SetVisitedStatus` (1), `RedrawCurrentVisitedStatus` (1), `ForceLockedStatus` (1), `UpdateVisitedStatus` (1), `method_1` (1), `SetTimerColor` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `TMPro`, `UnityEngine`, `System.Text`, `System.Collections`, `EFT.Interactive`, `System.Linq`, `System.Runtime.CompilerServices`, `Comfort.Common`, `EFT.Interactive.SecretExfiltrations`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `CustomTimerPanel`, `EMainTimerState`, `ExitTimerPanel`, `EVisitedStatus`, `Class3272`, `Class3273`, `MainTimerPanel`, `TimerPanel`, `TransitTimerPanel`.

### Subsystem: `EFT.UI.Builds`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 7.
- Declared types: 22 (classes: 21, structs: 1, interfaces: 0, enums: 0).
- Method declarations: 120. Field declarations: 122.
- Dominant folders: `EFT/UI/Builds` (7).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnPointerUp` (1), `OnPointerDown` (1), `OnPointerClick` (1), `OnPointerEnter` (1), `OnPointerExit` (1), `OnDisable` (1), `Awake` (1), `Dispose` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (12), `method_2` (9), `method_1` (8), `Show` (7), `method_3` (7), `method_4` (6), `method_5` (6), `method_6` (6), `method_7` (6), `method_8` (4), `Close` (3), `method_9` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `JetBrains.Annotations`, `TMPro`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `EFT.InventoryLogic`, `UnityEngine.Events`, `System.Linq`, `Diz.Binding`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `AmmoPresetView`, `EditBuildNameWindow`, `EquipmentBuildListView`, `MagPresetEditor`, `Class3497`, `Class3498`, `MagPresetsListView`, `in`, `in`, `in`, `in`, `Class2831`, `Class3500`, `Class3501`, `MagPresetsWindow`, `CompositionGroup`, `Class3492`, `Class3493`, `Class3494`, `Class3495`, `Class3496`, `MagPresetView`.

### Subsystem: `EFT.UI.Chat`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 24.
- Declared types: 48 (classes: 43, structs: 0, interfaces: 0, enums: 5).
- Method declarations: 200. Field declarations: 283.
- Dominant folders: `EFT/UI/Chat` (24).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (9), `OnPointerClick` (8), `Update` (7), `OnPointerEnter` (3), `OnPointerExit` (3), `OnEnable` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (35), `Show` (20), `method_1` (18), `method_2` (16), `method_3` (14), `Close` (10), `Awake` (9), `method_4` (9), `OnPointerClick` (8), `Update` (7), `method_5` (7), `method_6` (5).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `ChatShared`, `UnityEngine.UI`, `UnityEngine.Events`, `JetBrains.Annotations`, `UnityEngine.EventSystems`, `TMPro`, `System.Collections.Generic`, `System.Linq`.

**Key elements in this subsystem**
- `AttachmentMessageView`, `ChatCreateDialoguePanel`, `Class3365`, `Class3366`, `Class3367`, `Class3368`, `ChatFriendsListElement`, `ChatFriendsListPanel`, `Class3381`, `Class3382`, `ChatFriendsPanel`, `ChatFriendsRequestsPanel`, `Class3383`, `Class3384`, `ChatInvitePlayersPanel`, `Class3369`, `Class3370`, `Class3371`, `ChatMember`, `ChatMembersPanel`, `Class3372`, `Class3373`, `Class3374`, `ChatMessageSendBlock`, `Class3375`, `ChatScreen`, `Class3376`, `DialoguesContainer`, `EDialogType`, `Class3377`, `Class3378`, `Class3379`, `DialogueView`, `Class3380`, `EDialogueInteractionButton`, `EFriendInteractionButton`, `EMessageContextInteraction`, `EMessageViewType`, `FriendsInvitationView`, `GlobalChatButton`, ... (+8 more).

### Subsystem: `EFT.UI.DragAndDrop`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 53.
- Declared types: 91 (classes: 85, structs: 1, interfaces: 0, enums: 5).
- Method declarations: 615. Field declarations: 426.
- Dominant folders: `EFT/UI/DragAndDrop` (53).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnClick` (12), `OnPointerEnter` (12), `OnItemAdded` (8), `OnItemRemoved` (7), `OnPointerExit` (6), `OnEndDrag` (6), `OnDrag` (5), `OnBeingExaminedChanged` (5), `OnRefreshItem` (4), `OnDestroy` (4), `OnSetInHands` (4), `OnRemoveFromHands` (4), `OnPointerDown` (4), `Awake` (4), `OnBeginDrag` (4), `OnBindItem` (2), `OnUnbindItem` (2), `Update` (2), `Init` (2), `OnPointerClick` (2).
- High-frequency methods worth tracing for behavior flow: `method_0` (48), `Show` (26), `method_1` (22), `Create` (19), `method_2` (16), `method_3` (14), `OnClick` (12), `OnPointerEnter` (12), `UpdateInfo` (10), `method_37` (9), `method_4` (9), `CanAccept` (8).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.InventoryLogic`, `UnityEngine`, `System.Runtime.CompilerServices`, `UnityEngine.EventSystems`, `JetBrains.Annotations`, `UnityEngine.UI`, `TMPro`, `Comfort.Common`, `System.Linq`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `AssembleBuildItemView`, `AutoResizableGridView`, `BaseSelectableItemView`, `BindPanel`, `BoundItemView`, `BoundSlotView`, `CaptchaGridItemView`, `ComplexStashPanel`, `ContainedGridsView`, `DraggedItemView`, `DropdownSelectableItemView`, `EModLockedState`, `EmptyItemView`, `ETradingItemViewType`, `ETradingSide`, `FastAccessGrenadeGridItemView`, `FastAccessGrenadeItemView`, `Class3328`, `Class3329`, `Class3330`, `GeneratedGridsView`, `GridItemView`, `EItemValueFormat`, `Class3348`, `Class3349`, `Class3350`, `GridSortPanel`, `Class3347`, `GridView`, `Class3331`, `Class3332`, `Class3333`, `Class3334`, `Class3335`, `Class3336`, `Class3337`, `HideoutItemView`, `ItemView`, `Class3352`, `Class3353`, ... (+51 more).

### Subsystem: `EFT.UI.Gestures`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 14.
- Declared types: 29 (classes: 25, structs: 2, interfaces: 1, enums: 1).
- Method declarations: 138. Field declarations: 141.
- Dominant folders: `EFT/UI/Gestures` (14).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (5), `OnPointerEnter` (2), `OnPointerExit` (2), `Init` (2), `OnDisable` (1), `OnPointerClick` (1), `Start` (1), `Update` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (19), `method_1` (13), `Show` (8), `method_2` (8), `Awake` (5), `Close` (5), `BindUpdatedHandler` (4), `UnderPointerChanged` (4), `method_3` (4), `AlignToCenter` (3), `AlignToLeft` (3), `AlignToRight` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.InputSystem`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Linq`, `UnityEngine.UI`, `UnityEngine.EventSystems`, `Comfort.Common`, `TMPro`, `JetBrains.Annotations`.

**Key elements in this subsystem**
- `GestureAudioBindAlignment`, `GestureBaseItem`, `GStruct449`, `GestureItemBindAlignment`, `GesturesAudioItem`, `Class3394`, `GesturesAudioSubItem`, `GesturesBindAlignment`, `GesturesBindItem`, `GesturesBindPanel`, `GesturesDropdownPanel`, `GesturesMenu`, `GStruct450`, `Class3395`, `Class3396`, `Class3397`, `Class3398`, `GesturesMenuItem`, `GesturesQuickPanel`, `Class3399`, `Class3400`, `Class3401`, `Class3402`, `GesturesVoipPanel`, `Class3403`, `PredefinedLayoutGroup`, `SpecialAlignmentNode`, `EAlignment`, `GInterface500`.

### Subsystem: `EFT.UI.Health`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 8.
- Declared types: 11 (classes: 11, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 41. Field declarations: 82.
- Dominant folders: `EFT/UI/Health` (8).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (5), `Update` (2), `OnEnable` (1), `OnPointerEnter` (1), `OnDrop` (1), `OnPointerExit` (1), `OnPointerClick` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (7), `Show` (6), `Awake` (5), `method_1` (4), `Update` (2), `method_2` (2), `SetEffectsFilter` (1), `SetBuffValue` (1), `DisableBuff` (1), `OnEnable` (1), `Close` (1), `OnPointerEnter` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.HealthSystem`, `System.Collections.Generic`, `UnityEngine.UI`, `System.Runtime.CompilerServices`, `TMPro`, `UnityEngine.EventSystems`, `JetBrains.Annotations`, `EFT.InventoryLogic`, `DG.Tweening`.

**Key elements in this subsystem**
- `BodyPartView`, `BuffableHealthParameterPanel`, `DamageIcon`, `DamagePanel`, `Class3391`, `Class3392`, `Class3393`, `HealthBarButton`, `HealthParameterPanel`, `HealthParametersPanel`, `InventoryScreenHealthPanel`.

### Subsystem: `EFT.UI.Insurance`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 9.
- Declared types: 18 (classes: 18, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 91. Field declarations: 99.
- Dominant folders: `EFT/UI/Insurance` (9).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (4), `OnPointerEnter` (3), `OnPointerExit` (3), `OnClick` (2), `OnItemAdded` (2), `OnItemRemoved` (2), `OnDisable` (1), `OnRefreshItem` (1), `Update` (1), `OnPointerClick` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (13), `method_2` (8), `method_1` (7), `Show` (6), `method_3` (6), `Awake` (4), `method_4` (4), `OnPointerEnter` (3), `OnPointerExit` (3), `method_5` (3), `Close` (3), `Create` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.UI.DragAndDrop`, `EFT.InventoryLogic`, `TMPro`, `UnityEngine.UI`, `System.Runtime.CompilerServices`, `JetBrains.Annotations`, `UnityEngine.EventSystems`, `System.Collections.Generic`, `System.Linq`.

**Key elements in this subsystem**
- `InsuranceItemView`, `InsuranceSlotItemView`, `InsuranceSlotView`, `InsuranceWindow`, `Class3479`, `Class3480`, `Class3481`, `InsuredItemPanel`, `InsuredItemsScreen`, `Class3482`, `InsurerParametersPanel`, `Class3478`, `ItemsToInsureScreen`, `Class3483`, `Class3484`, `ItemToInsurePanel`, `Class3485`, `Class3486`.

### Subsystem: `EFT.UI.Map`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 10.
- Declared types: 19 (classes: 19, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 71. Field declarations: 112.
- Dominant folders: `EFT/UI/Map` (10).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (3), `Awake` (3), `OnPointerClick` (2), `Load` (2), `OnPointerEnter` (1), `OnPointerExit` (1), `Init` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (10), `Show` (8), `Close` (4), `Update` (3), `method_1` (3), `method_2` (3), `Awake` (3), `Select` (2), `OnPointerClick` (2), `Load` (2), `Disable` (1), `Deselect` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.UI`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `JetBrains.Annotations`, `System.Linq`, `EFT.InventoryLogic`, `UnityEngine.EventSystems`, `UnityEngine.Events`, `EFT.Interactive`.

**Key elements in this subsystem**
- `EntryPoint`, `EntryPointView`, `ExtractionPoint`, `ExtractionPointView`, `MapPoints`, `Class3502`, `Class3503`, `MapPointsManager`, `Class3505`, `Class3506`, `Class3507`, `MapScreen`, `GClass3805`, `Class3504`, `PocketMap`, `SelectEntryPointPanel`, `SimplePocketMap`, `JsonMapConfig`, `Class3512`.

### Subsystem: `EFT.UI.Matchmaker`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 29.
- Declared types: 65 (classes: 62, structs: 0, interfaces: 0, enums: 3).
- Method declarations: 332. Field declarations: 390.
- Dominant folders: `EFT/UI/Matchmaker` (29).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (11), `Update` (6), `OnPointerEnter` (3), `OnPointerExit` (3), `OnPointerClick` (2), `OnPointerUp` (2), `OnPointerDown` (2), `Init` (2), `Start` (2), `Dispose` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (39), `Show` (31), `method_1` (21), `method_2` (14), `method_3` (13), `method_5` (13), `method_6` (13), `Close` (12), `Awake` (11), `method_7` (11), `method_8` (11), `method_9` (10).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `UnityEngine.Events`, `UnityEngine.UI`, `System.Collections.Generic`, `System.Linq`, `TMPro`, `System.Threading`, `Comfort.Common`, `EFT.InputSystem`.

**Key elements in this subsystem**
- `AbstractGroupPlayerPanel`, `BannerPageToggle`, `ComradeView`, `CoopSettingsWindow`, `ECoopBlock`, `GroupPlayerPanel`, `Class3303`, `GroupPlayersList`, `Class3304`, `Class3305`, `Class3306`, `LocationConditionsPanel`, `LocationConditionsPanelFactory`, `LocationPath`, `MatchMakerAcceptScreen`, `GClass3914`, `Class3317`, `Class3318`, `Class3319`, `Class3320`, `Class3321`, `MatchmakerBanner`, `MatchmakerBannersPanel`, `Class3322`, `MatchmakerFinalCountdown`, `FinalCountdownScreenClass`, `MatchMakerGroupPreview`, `MatchmakerInsuranceScreen`, `EInsuranceTab`, `GClass3913`, `Class3315`, `Class3316`, `MatchmakerKeyAccessScreen`, `MatchmakerMapPointsScreen`, `GClass3916`, `MatchmakerOfflineRaidScreen`, `CreateRaidSettingsForProfileClass`, `MatchMakerPlayerPreview`, `Class3307`, `MatchmakerRaidSettingView`, ... (+25 more).

### Subsystem: `EFT.UI.Prestige`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 7.
- Declared types: 19 (classes: 19, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 33. Field declarations: 94.
- Dominant folders: `EFT/UI/Prestige` (7).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnPointerClick` (2).
- High-frequency methods worth tracing for behavior flow: `method_0` (10), `Show` (8), `OnPointerClick` (2), `SetCurrentLevel` (1), `method_1` (1), `method_2` (1), `Close` (1), `SetCurrentReward` (1), `SelectNextPrestige` (1), `CreateLevels` (1), `SelectPrestige` (1), `UpdateClaimButton` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `JetBrains.Annotations`, `UnityEngine`, `TMPro`, `UnityEngine.UI`, `EFT.Quests`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `Diz.Binding`, `UnityEngine.EventSystems`, `Comfort.Common`.

**Key elements in this subsystem**
- `PrestigeConditionCounterView`, `Class3254`, `PrestigeConditionView`, `Class3255`, `PrestigeLevelView`, `PrestigeRewardDetailsView`, `PrestigeRewardView`, `PrestigeScreen`, `InventoryPrestigeTabController`, `Class3256`, `Class3257`, `Class3258`, `Class3259`, `Class3260`, `Class3261`, `Class3262`, `Class3263`, `Class3264`, `RewardGameModeView`.

### Subsystem: `EFT.UI.Ragfair`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 41.
- Declared types: 82 (classes: 71, structs: 2, interfaces: 0, enums: 9).
- Method declarations: 401. Field declarations: 565.
- Dominant folders: `EFT/UI/Ragfair` (41).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (15), `Update` (4), `OnPointerEnter` (3), `OnPointerExit` (3), `OnPointerClick` (3), `OnItemAdded` (1), `OnItemRemoved` (1), `OnPointerUp` (1), `OnPointerDown` (1), `Start` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (54), `Show` (29), `method_1` (26), `method_2` (22), `method_3` (21), `method_4` (19), `method_5` (16), `Awake` (15), `method_6` (14), `method_7` (12), `Close` (12), `method_8` (11).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `UnityEngine.UI`, `System.Collections.Generic`, `TMPro`, `UnityEngine.Events`, `EFT.InventoryLogic`, `System.Linq`, `Comfort.Common`, `JetBrains.Annotations`.

**Key elements in this subsystem**
- `AddOfferWindow`, `EAddOfferWarning`, `Class3450`, `Class3451`, `Class3452`, `Class3453`, `BuildItemSearchValue`, `CancellableFilterPanel`, `CancellableFiltersPanel`, `Class3443`, `CategoryView`, `CombinedView`, `Class3449`, `EExchangeableWindowType`, `EFilterType`, `EOfferOwnerType`, `ERagFairOfferDataType`, `ESortType`, `EViewListType`, `EWindowType`, `FilterRule`, `FiltersPanel`, `Class3447`, `HandoverExchangeableItemsWindow`, `Class3424`, `Class3425`, `Class3426`, `Class3427`, `Class3428`, `Class3429`, `Class3430`, `Class3431`, `HandoverRagfairMoneyWindow`, `Class3432`, `Class3433`, `Class3434`, `Class3435`, `Class3436`, `Class3437`, `Struct1239`, ... (+42 more).

### Subsystem: `EFT.UI.Screens`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 12.
- Declared types: 15 (classes: 10, structs: 0, interfaces: 0, enums: 5).
- Method declarations: 40. Field declarations: 22.
- Dominant folders: `EFT/UI/Screens` (12).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (2), `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Show` (5), `Close` (4), `method_3` (3), `method_0` (3), `method_2` (2), `OnDestroy` (2), `method_4` (2), `TranslateCommand` (2), `ShowAsync` (1), `InviteAcceptedHandler` (1), `MatchingTypeUpdateHandler` (1), `Awake` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `UnityEngine`, `EFT.InputSystem`, `System.Threading.Tasks`, `TMPro`, `EFT.UI.Settings`, `UnityEngine.Events`, `System.Collections.Generic`, `Comfort.Common`.

**Key elements in this subsystem**
- `BaseScreen`, `EEftScreenType`, `EftAsyncScreen`, `EftScreen`, `EScreenOrder`, `EScreenState`, `EShadingStateSwitcher`, `EStateSwitcher`, `MatchmakerEftScreen`, `PostFXPreviewScreen`, `StatedEftScreen`, `UIScreen`, `Class410`, `Class3252`, `Class3253`.

### Subsystem: `EFT.UI.SessionEnd`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 8.
- Declared types: 19 (classes: 19, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 49. Field declarations: 108.
- Dominant folders: `EFT/UI/SessionEnd` (8).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (6), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Show` (11), `Awake` (6), `method_0` (6), `TranslateCommand` (5), `method_3` (5), `method_4` (5), `Close` (2), `ShowNextScreen` (2), `IsAvailable` (1), `method_5` (1), `CloseScreenInterruption` (1), `method_11` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.UI.Screens`, `System.Runtime.CompilerServices`, `EFT.InputSystem`, `UnityEngine.Events`, `EFT.InventoryLogic`, `Comfort.Common`, `System.Threading`, `System.Diagnostics`, `System.Threading.Tasks`.

**Key elements in this subsystem**
- `HealthTreatmentScreen`, `GClass3898`, `Class3244`, `KillListVictim`, `SessionEndUI`, `SessionExperiencePanel`, `SessionResultExitStatus`, `GClass3899`, `Class3245`, `SessionResultExperienceCount`, `GClass3909`, `Class3246`, `SessionResultKillList`, `GClass3910`, `Class3248`, `Class3249`, `SessionResultStatistics`, `GClass3911`, `GClass3857`.

### Subsystem: `EFT.UI.Settings`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 16.
- Declared types: 71 (classes: 61, structs: 6, interfaces: 1, enums: 3).
- Method declarations: 321. Field declarations: 385.
- Dominant folders: `EFT/UI/Settings` (16).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (4), `OnTabSelected` (3), `Update` (2), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (50), `method_1` (28), `method_2` (20), `method_3` (15), `method_4` (13), `method_5` (10), `Show` (9), `method_6` (9), `method_11` (9), `method_7` (8), `method_8` (8), `method_9` (8).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `Bsg.GameSettings`, `System.Collections.Generic`, `System.Linq`, `EFT.InputSystem`, `UnityEngine.Events`, `Comfort.Common`, `System.Diagnostics`.

**Key elements in this subsystem**
- `CommandAxisPair`, `CommandKeyPair`, `Class3199`, `Class3200`, `ControlSettingsTab`, `Class3209`, `Class3210`, `EHealthColorScheme`, `EVisibilityMode`, `GameSettingsTab`, `Class3211`, `layout`, `Class3212`, `Struct1180`, `Class3213`, `Class3214`, `Class3215`, `GraphicsSettingsTab`, `Class3216`, `Class3217`, `Class3218`, `Class3219`, `Class3220`, `Class3221`, `Class3222`, `Class3223`, `Class3224`, `Class3225`, `Class3226`, `Class3227`, `Struct1183`, `PostFXSettingsTab`, `Class3229`, `Struct1185`, `SettingControl`, `SettingDropDown`, `Class3202`, `Class3203`, `Class3204`, `Struct1177`, ... (+31 more).

### Subsystem: `EFT.UI.Tutorial`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 7.
- Declared types: 11 (classes: 11, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 16. Field declarations: 30.
- Dominant folders: `EFT/UI/Tutorial` (7).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `method_0` (8), `method_1` (3), `method_2` (3), `GetKeyBindingBanner` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.InputSystem`, `System.Collections.Generic`, `System.Linq`, `System.Runtime.CompilerServices`, `Comfort.Common`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `KeyAxis`, `KeyBanner`, `KeyBannerGenerator`, `Class3188`, `Class3189`, `Class3190`, `Class3191`, `KeyBindingBannerView`, `KeyBindingView`, `KeyCombination`, `KeyView`.

### Subsystem: `EFT.UI.UI.Matchmaker`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 9.
- Dominant folders: `EFT/UI/UI/Matchmaker` (1).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnPointerClick` (1).
- High-frequency methods worth tracing for behavior flow: `Show` (1), `SetEyeButtonAvailability` (1), `PointerEnterHandler` (1), `PointerExitHandler` (1), `OnPointerClick` (1), `method_1` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `System.Threading`, `UnityEngine`, `UnityEngine.EventSystems`.

**Key elements in this subsystem**
- `PartyPlayerItem`.

### Subsystem: `EFT.UI.Utilities.LightScroller`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 15 (classes: 11, structs: 0, interfaces: 1, enums: 3).
- Method declarations: 36. Field declarations: 44.
- Dominant folders: `EFT/UI/Utilities/LightScroller` (1).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Dispose` (2), `OnEnable` (1), `OnScroll` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (9), `method_1` (6), `method_3` (2), `Dispose` (2), `OnEnable` (1), `OnScroll` (1), `SetScrollPosition` (1), `Update` (1), `method_4` (1), `method_5` (1), `Close` (1), `method_6` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `System.Runtime.CompilerServices`, `UnityEngine`, `UnityEngine.Events`, `UnityEngine.EventSystems`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `LightScroller`, `EScrollDirection`, `EScrollOrder`, `EScrollbarVisibilityEnum`, `Interface22`, `Class3178`, `Class3179`, `Class3180`, `Class3181`, `Class3182`, `Class3183`, `Class3184`, `Class3185`, `Class3186`, `Class3187`.

### Subsystem: `EFT.UI.WeaponModding`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 4.
- Declared types: 12 (classes: 12, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 68. Field declarations: 80.
- Dominant folders: `EFT/UI/WeaponModding` (4).

**How it works (operational pattern)**
- UI subsystems are event-driven consumers. They read gameplay/profile/raid state, then transform it into view state, interaction widgets, and modal flows.
- Flow is generally: initialize presenter/view-model -> bind widgets/listeners -> refresh from model -> react to user actions -> teardown/unsubscribe.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `Update` (1), `Start` (1), `Init` (1), `OnDisable` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (10), `method_1` (6), `Show` (4), `method_2` (4), `method_3` (4), `method_4` (3), `Close` (2), `method_5` (2), `method_7` (2), `method_8` (2), `method_9` (2), `Awake` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `UnityEngine`, `System.Collections.Generic`, `System.Linq`, `System.Threading`, `EFT.UI.DragAndDrop`, `Comfort.Common`, `TMPro`, `UnityEngine.Events`.

**Key elements in this subsystem**
- `DropDownMenu`, `Class3265`, `ModdingScreenSlotView`, `Class3266`, `Class3267`, `Class3268`, `Class3269`, `WeaponModdingScreen`, `GClass3922`, `WeaponPreview`, `Class3270`, `Class3271`.

### Subsystem: `EFT.Utilities`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 6.
- Declared types: 9 (classes: 5, structs: 1, interfaces: 0, enums: 3).
- Method declarations: 14. Field declarations: 34.
- Dominant folders: `EFT/Utilities` (6).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnDestroy` (1), `OnEnable` (1), `Init` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (3), `Awake` (1), `Setup` (1), `ManualUpdate` (1), `Clear` (1), `method_1` (1), `OnDestroy` (1), `method_2` (1), `OnEnable` (1), `Init` (1), `Rotate` (1), `SetRotation` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Linq`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading`, `Cinemachine`, `CustomPlayerLoopSystem`, `EFT.UI`, `Comfort.Common`.

**Key elements in this subsystem**
- `AutoCameraController`, `EAction`, `PointAction`, `Class2121`, `EClientMetrics`, `EMissingEventActionType`, `EventObject`, `RandomBetweenFloats`, `XCoordRotation`.

### Subsystem: `EFT.Vaulting`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 18.
- Declared types: 19 (classes: 3, structs: 0, interfaces: 13, enums: 3).
- Method declarations: 1. Field declarations: 6.
- Dominant folders: `EFT/Vaulting` (18).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `TryGetSoundElement` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `EFT.Vaulting.Controllers`, `EFT.Vaulting.Models`, `System.Collections.Generic`, `UnityEngine.Serialization`.

**Key elements in this subsystem**
- `EVaultingSoundState`, `EVaultingSoundType`, `EVaultingStrategy`, `IAutoMoveRestrictions`, `IBaseMoveSettings`, `IClimbSettings`, `IGridSettings`, `IMoveRestrictions`, `IMovesSettings`, `IVaultingComponent`, `IVaultingComponentDebug`, `IVaultingGameplayRestrictions`, `IVaultingParameters`, `IVaultingRestrictions`, `IVaultingSettings`, `IVaultSettings`, `VaultingSoundSet`, `VaultingSoundElement`, `VolumeRange`.

### Subsystem: `EFT.Vaulting.Controllers`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 9.
- Dominant folders: `EFT/Vaulting/Controllers` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Tick` (2).
- High-frequency methods worth tracing for behavior flow: `Tick` (2), `method_0` (2), `UpdateGridParameters` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `GridPointsController`, `VaultingController`.

### Subsystem: `EFT.Vaulting.Debug`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 3.
- Dominant folders: `EFT/Vaulting/Debug` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (1), `method_0` (1), `method_1` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.Vaulting.Debug.View`, `UnityEngine`.

**Key elements in this subsystem**
- `VaultingDebugTool`.

### Subsystem: `EFT.Vaulting.Debug.View`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 5.
- Declared types: 5 (classes: 5, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 18. Field declarations: 18.
- Dominant folders: `EFT/Vaulting/Debug/View` (5).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (3), `OnDestroy` (2), `Update` (1), `OnDrawGizmos` (1), `OnDisable` (1), `OnGUI` (1).
- High-frequency methods worth tracing for behavior flow: `OnEnable` (3), `method_0` (2), `method_1` (2), `method_2` (2), `OnDestroy` (2), `ShowApproximatedLine` (1), `Update` (1), `OnDrawGizmos` (1), `UpdateDataModel` (1), `OnDisable` (1), `OnGUI` (1), `SetCurrentPage` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `EFT.Vaulting.Models`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `EditorApproximationView`, `EditorGridView`, `EditorView`, `PageViewer`, `VaultingDebugToolView`.

### Subsystem: `EFT.Vaulting.Enums`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Vaulting/Enums` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `SurfaceType`.

### Subsystem: `EFT.Vaulting.Models`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 11.
- Declared types: 11 (classes: 0, structs: 1, interfaces: 10, enums: 0).
- Method declarations: 0. Field declarations: 5.
- Dominant folders: `EFT/Vaulting/Models` (11).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`.

**Key elements in this subsystem**
- `IAutomaticVaultingModelDebug`, `IGridPointsModelDebug`, `IGridRootMoverModelDebug`, `IGridSettingsModelDebug`, `IObstacleCalculatorModelDebug`, `ISurfaceApproximatorModelDebug`, `IVaultingModelDebug`, `IVaultingRestrictionsModelDebug`, `IVaultingStateModelDebug`, `IWeightCalculatorModelDebug`, `VaultingPoint`.

### Subsystem: `EFT.Vehicle`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 18.
- Declared types: 21 (classes: 12, structs: 4, interfaces: 0, enums: 5).
- Method declarations: 167. Field declarations: 157.
- Dominant folders: `EFT/Vehicle` (18).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (5), `Update` (4), `Init` (2), `OnDestroy` (2), `LateUpdate` (2), `FixedUpdate` (2), `Dispose` (1), `OnEnd` (1), `OnValidate` (1), `OnIncomingToDestination` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (5), `Start` (5), `method_1` (4), `method_2` (4), `method_3` (4), `method_4` (4), `method_5` (4), `method_6` (4), `method_7` (4), `Update` (4), `method_8` (3), `method_9` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Collections.Generic`, `System.Threading.Tasks`, `EFT.NextObservedPlayer`, `Comfort.Common`, `System.Threading`, `EFT.Ballistics`, `EFT.GlobalEvents`, `BezierSplineTools`.

**Key elements in this subsystem**
- `BTRDoor`, `BTRMapPath`, `BTRPassenger`, `BTRPlace`, `EState`, `BTRSide`, `EState`, `Struct937`, `BTRTurretServer`, `BTRTurretView`, `BTRVehicle`, `BTRView`, `EBtrInteractionStatus`, `EBtrSide`, `MapPathConfig`, `PathDestination`, `PathPartBase`, `PathReverseData`, `PathReversePart`, `PathSpline`, `ReverseMoveType`.

### Subsystem: `EFT.Vehicle.Vehicles`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 13. Field declarations: 42.
- Dominant folders: `EFT/Vehicle/Vehicles` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (1), `FixedUpdate` (1), `Awake` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `Start` (1), `CalculateBaseParams` (1), `SetOffset` (1), `FixedUpdate` (1), `Awake` (1), `Update` (1), `method_0` (1), `SetWheelSettings` (1), `RotateWheel` (1), `method_1` (1), `method_2` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `VehicleSuspensionSpring`, `VehicleWheelsBase`.

### Subsystem: `EFT.Visual`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 29.
- Declared types: 37 (classes: 31, structs: 4, interfaces: 0, enums: 2).
- Method declarations: 105. Field declarations: 96.
- Dominant folders: `EFT/Visual` (29).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (15), `Awake` (5), `OnSkin` (4), `OnDestroy` (2), `OnEnable` (2), `OnDisable` (2), `OnItemRemoved` (2), `OnItemAdded` (2), `OnValidate` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (15), `method_0` (15), `Unskin` (10), `Deinit` (7), `GetRenderers` (5), `Awake` (5), `OnSkin` (4), `method_1` (3), `ManualUpdate` (3), `ApplySkin` (2), `Skin` (2), `AdjustShadowMode` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `EFT.InventoryLogic`, `Diz.Skinning`, `System.Collections.Generic`, `System.Linq`, `UnityEngine.Rendering`, `JetBrains.Annotations`, `MultiFlare`, `UnityEngine.Serialization`.

**Key elements in this subsystem**
- `ArmBandView`, `MeshCustomizationIdPair`, `Class1850`, `BackpackSkin`, `Class1854`, `BackpackSkinData`, `CustomSkin`, `Dress`, `EmissionFlicker`, `EWidth`, `Flicker`, `ECurveType`, `FlickerSystem`, `FoldableStockView`, `BonePosition`, `GunShadowDisabler`, `HeadSkin`, `HeadSkinData`, `HelmetDress`, `HoodedDress`, `IkLight`, `JackOLantern`, `LegsView`, `LightFlicker`, `LoddedSkin`, `Class1852`, `NightVisionDevice`, `NightVisionMount`, `RetractableStockView`, `BonePosition`, `SkinDress`, `ThermalVisionDevice`, `TorsoSkin`, `VestlikeArmor`, `VestSkin`, `Class1855`, `VestSkinData`.

### Subsystem: `EFT.WeaponMounting`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 9.
- Declared types: 9 (classes: 5, structs: 0, interfaces: 3, enums: 1).
- Method declarations: 7. Field declarations: 5.
- Dominant folders: `EFT/WeaponMounting` (9).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `SetData` (1), `ClearData` (1), `SetCurrentInteractive` (1), `method_0` (1), `SetMovingPlatform` (1), `method_1` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Newtonsoft.Json`, `EFT.Interactive`, `EFT.MovingPlatforms`.

**Key elements in this subsystem**
- `EMountSideDirection`, `IMountingMovementSettings`, `IMountingPointDetectionSettings`, `IWeaponMountingComponent`, `MountingMovementSettings`, `MountingPointDetectionSettings`, `MountPointData`, `PlayerMountingPointData`, `WeaponMountingView`.

### Subsystem: `EFT.WeaponMounting.Debug`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 2.
- Dominant folders: `EFT/WeaponMounting/Debug` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Initialize` (1), `OnGUI` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Initialize` (1), `method_0` (1), `OnGUI` (1), `OnDestroy` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `MountingDebugTool`.

### Subsystem: `EFT.Weapons.Data`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/Weapons/Data` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `ESoundWeaponType`.

### Subsystem: `EFT.Weather`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 20.
- Declared types: 29 (classes: 15, structs: 6, interfaces: 1, enums: 7).
- Method declarations: 108. Field declarations: 226.
- Dominant folders: `EFT/Weather` (20).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (4), `OnDestroy` (4), `Dispose` (3), `Update` (3), `OnValidate` (2), `LateUpdate` (2), `Execute` (1), `Initialize` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (9), `method_1` (5), `Awake` (4), `OnDestroy` (4), `Dispose` (3), `vmethod_1` (3), `smethod_0` (3), `smethod_1` (3), `Update` (3), `method_2` (3), `method_3` (3), `method_4` (3).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `Comfort.Common`, `WaterSSR`, `System.Threading`, `UnityEngine.Rendering`, `System.Linq`, `System.Collections.Generic`, `Unity.Jobs`, `EFT.Rendering.Clouds`.

**Key elements in this subsystem**
- `ECloudinessType`, `EFogType`, `ERainType`, `EWeatherType`, `EWindSpeed`, `FactoryWinterController`, `EStatus`, `Class2113`, `Class2114`, `Class2115`, `FogRemapData`, `FogRemapDataV2`, `FogRemapRecord`, `FogRemapRecordV2`, `IWeatherCurve`, `SphericalHarmonicsSettings`, `ToDController`, `GStruct306`, `Struct797`, `TodLightSetter`, `TODSkySimple`, `Struct798`, `WeatherController`, `WeatherCurve`, `WeatherDebug`, `Direction`, `WindController`, `Class2118`, `WiresController`.

### Subsystem: `EFT.ZombieEventsConsumers`
**Role hypothesis:** Escape from Tarkov gameplay stack (player, items, raid flow, UI).

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 0, structs: 0, interfaces: 2, enums: 0).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `EFT/ZombieEventsConsumers` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `AnimationEventSystem`.

**Key elements in this subsystem**
- `IZombieFireBulletEvents`, `IZombieFireEndEvents`.

### Subsystem: `FastAnimatorSystem`
**Role hypothesis:** FastAnimatorSystem subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 1, structs: 1, interfaces: 0, enums: 1).
- Method declarations: 13. Field declarations: 14.
- Dominant folders: `FastAnimatorSystem` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1), `Process` (1), `OnAnimatorMove` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `Init` (1), `Play` (1), `Stop` (1), `Process` (1), `SetLayerWeight` (1), `GetLayerWeight` (1), `RaiseImmediateTransitionHappened` (1), `SetCuller` (1), `GetLayerProcessor` (1), `GetClipByIndex` (1), `method_0` (1), `OnAnimatorMove` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `AnimationSystem.RootMotionTable`, `UnityEngine.Animations`, `UnityEngine.Playables`.

**Key elements in this subsystem**
- `ETransitionInterruptionSource`, `InitialLayerInfo`, `PlayableAnimator`.

### Subsystem: `FastAnimatorSystem.TestAnimatorEnvironment`
**Role hypothesis:** FastAnimatorSystem subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 4 (classes: 4, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 15. Field declarations: 76.
- Dominant folders: `FastAnimatorSystem/TestAnimatorEnvironment` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (3), `Start` (2), `Init` (1), `Process` (1).
- High-frequency methods worth tracing for behavior flow: `Update` (3), `method_0` (3), `Start` (2), `method_1` (1), `method_2` (1), `Init` (1), `Process` (1), `GetCurrentState` (1), `GetNextState` (1), `GetDeltaPosition` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `EFT`, `UnityEngine.UI`, `AnimationSystem.RootMotionTable`.

**Key elements in this subsystem**
- `TestAnimatorBenchmarkMover`, `TestAnimatorMover`, `TestAnimatorSkeleton`, `TestWeaponAnimatorSkeleton`.

### Subsystem: `FitstPersonAnimations.WeaponAnimation.Effectors.Recoil`
**Role hypothesis:** FitstPersonAnimations subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `FitstPersonAnimations/WeaponAnimation/Effectors/Recoil` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `RecoilPipelineType`.

### Subsystem: `FlyingWormConsole3`
**Role hypothesis:** Console/network utility.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 1. Field declarations: 2.
- Dominant folders: `FlyingWormConsole3` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `ConsoleProRemoteServer`.

### Subsystem: `FlyingWormConsole3.LiteNetLib`
**Role hypothesis:** Console/network utility.

**Structure snapshot**
- Files: 13.
- Declared types: 13 (classes: 0, structs: 0, interfaces: 0, enums: 13).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `FlyingWormConsole3/LiteNetLib` (13).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `ConnectionRequestResult`, `ConnectionState`, `ConnectRequestResult`, `DeliveryMethod`, `DisconnectReason`, `DisconnectResult`, `IPv6Mode`, `LocalAddrType`, `NatAddressType`, `NetLogLevel`, `PacketProperty`, `ShutdownResult`, `UnconnectedMessageType`.

### Subsystem: `FlyingWormConsole3.LiteNetLib.Utils`
**Role hypothesis:** Console/network utility.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 0, structs: 0, interfaces: 0, enums: 2).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `FlyingWormConsole3/LiteNetLib/Utils` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `NtpLeapIndicator`, `NtpMode`.

### Subsystem: `GPUInstancer`
**Role hypothesis:** Rendering/instancing optimization.

**Structure snapshot**
- Files: 60.
- Declared types: 80 (classes: 70, structs: 0, interfaces: 0, enums: 10).
- Method declarations: 361. Field declarations: 649.
- Dominant folders: `GPUInstancer` (60).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (19), `Update` (14), `Start` (13), `OnDisable` (9), `OnEnable` (7), `FixedUpdate` (5), `OnDestroy` (5), `OnControllerColliderHit` (1), `OnGUI` (1), `Initialize` (1), `OnResolutionChangeStatic` (1), `LateUpdate` (1), `OnTriggerEnter` (1), `OnValidate` (1), `OnCollisionEnter` (1), `OnDrag` (1), `OnPointerDown` (1), `OnPointerUp` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (36), `Awake` (19), `method_1` (18), `Update` (14), `Start` (13), `method_2` (11), `OnDisable` (9), `method_3` (7), `method_4` (7), `OnEnable` (7), `method_5` (6), `Reset` (6).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Collections`, `UnityEngine.UI`, `System.Runtime.CompilerServices`, `System.Linq`, `System.Threading`, `System.Globalization`, `UnityEngine.Rendering`, `UnityEngine.XR`.

**Key elements in this subsystem**
- `AddRemoveInstances`, `AddRuntimeCreatedGameObjects`, `AstroidGenerator`, `BillboardAtlasBinding`, `BillboardQuality`, `ColorPicker`, `Class872`, `ColorVariations`, `DetailDemoSceneController`, `CameraModes`, `QualityMode`, `FlyCamera`, `FPController`, `FPS`, `GPUIComputeThreadCount`, `GPUIMatrixHandlingType`, `GPUInstancerBillboard`, `GPUInstancerBillboardAtlasBindings`, `GPUInstancerCameraData`, `GPUInstancerCameraHandler`, `GPUInstancerDetailManager`, `GPUInstancerDetailPrototype`, `GPUInstancerDrawCallColorDebugger`, `GPUInstancerEventType`, `GPUInstancerFloatingOriginHandler`, `GPUInstancerGUIInfo`, `GPUInstancerHiZOcclusionGenerator`, `GPUInstancerInstanceRemover`, `GPUInstancerLODColorDebugger`, `GPUInstancerManager`, `GClass1261`, `Class875`, `GPUInstancerMapMagicIntegration`, `GPUInstancerModificationCollider`, `GPUInstancerPrefab`, `GPUInstancerPrefabListRuntimeHandler`, `Class891`, `GPUInstancerPrefabManager`, `Class892`, `Class893`, ... (+40 more).

### Subsystem: `Interpolation`
**Role hypothesis:** Interpolation subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 0, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 0. Field declarations: 0.
- Dominant folders: `Interpolation` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Imports are minimal or mostly global/root-level; this subsystem may be foundational or self-contained.

**Key elements in this subsystem**
- `EBoundType`.

### Subsystem: `JsonType`
**Role hypothesis:** JsonType subsystem.

**Structure snapshot**
- Files: 10.
- Declared types: 10 (classes: 4, structs: 1, interfaces: 0, enums: 5).
- Method declarations: 1. Field declarations: 13.
- Dominant folders: `JsonType` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `CreateDefault` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `Newtonsoft.Json`, `System.Collections.Generic`, `EFT`.

**Key elements in this subsystem**
- `BTRServerSettings`, `EDateTime`, `EGame`, `EItemDropSoundType`, `ELootRarity`, `KeepAliveResponse`, `LocalSettings`, `LocationWeatherTime`, `TaxonomyColor`, `TraderServerSettings`.

### Subsystem: `Koenigz.PerfectCulling`
**Role hypothesis:** Culling/performance pipeline.

**Structure snapshot**
- Files: 11.
- Declared types: 20 (classes: 15, structs: 4, interfaces: 0, enums: 1).
- Method declarations: 82. Field declarations: 116.
- Dominant folders: `Koenigz/PerfectCulling` (11).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (2), `OnDisable` (2), `Init` (1), `Start` (1), `Awake` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (8), `SetRawData` (4), `GetRawData` (2), `SetRawDataMT` (2), `SampleAtIndex` (2), `PrepareForBake` (2), `CompleteBake` (2), `DrawInspectorGUI` (2), `method_1` (2), `method_2` (2), `method_3` (2), `method_4` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `UnityEngine.Rendering`, `Koenigz.PerfectCulling.EFT`, `Unity.Jobs`, `Koenigz.PerfectCulling.EFT.Autotest`, `System.Diagnostics`, `System.Linq`, `System.Runtime.InteropServices`.

**Key elements in this subsystem**
- `PerfectCullingBakeData`, `PerfectCullingBakeGroup`, `RuntimeGroupContent`, `GroupType`, `Class825`, `PerfectCullingBakingBehaviour`, `PerfectCullingCamera`, `PerfectCullingColorTable`, `PerfectCullingExcludeVolume`, `PerfectCullingResourcesLocator`, `PerfectCullingSettings`, `PerfectCullingVolumeBakeData`, `VisibilitySet`, `RawData`, `Class835`, `Class836`, `Class837`, `Struct234`, `SamplingProviderBase`, `TerrainToMeshUtility`.

### Subsystem: `Koenigz.PerfectCulling.EFT`
**Role hypothesis:** Culling/performance pipeline.

**Structure snapshot**
- Files: 53.
- Declared types: 91 (classes: 69, structs: 10, interfaces: 2, enums: 10).
- Method declarations: 263. Field declarations: 302.
- Dominant folders: `Koenigz/PerfectCulling/EFT` (53).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Execute` (6), `OnDestroy` (5), `Awake` (4), `Update` (4), `OnEndContentCollect` (3), `Start` (3), `OnBakedLODVisbilityChanged` (2), `Apply` (1), `Initialize` (1), `OnPostLevelLoaded` (1), `OnDrawGizmos` (1), `OnBeginContentCollect` (1), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (28), `method_1` (11), `GetBakeHash` (10), `method_2` (7), `PrepareRuntimeContent` (6), `Execute` (6), `method_3` (5), `method_4` (5), `method_5` (5), `OnDestroy` (5), `smethod_0` (5), `GetBakeGroups` (4).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.IO`, `System.Threading`, `System.Threading.Tasks`, `System.Diagnostics`, `System.Text`, `Unity.Collections`, `Unity.Jobs`.

**Key elements in this subsystem**
- `AdaptiveGridGenerationParams`, `AdaptiveGridProbe`, `AutocullLODGroupCell`, `BakeBatch`, `BakedLodContent`, `BakedLodPreProcess`, `Class844`, `BVH`, `Node`, `CrossSceneContentPortals`, `CrossSceneCullingGroupPreProcess`, `Class845`, `CrossSceneCullingTreePreProcess`, `Class846`, `CullingCellData`, `CullingGridCellContent`, `CullingGridContent`, `CullingGridPreProcess`, `CullingGroupData`, `EOccludeBehaviour`, `EOccludeMode`, `EPickBehaviour`, `EShadowMode`, `ESharedOccluderLODMode`, `ETransparencyMode`, `ExcludeBorderSamplingProvider`, `ExpandingBoxProbe`, `IAutocullAutomated`, `ISpatialItem`, `LightMeshQuality`, `LightVolumeSettings`, `LightSettings`, `SerializedMesh`, `Class840`, `MultisceneSharedOccluder`, `OrientedBounds`, `OrientedPoint`, `PerfectCullingAdaptiveGrid`, `Class851`, `Class852`, ... (+51 more).

### Subsystem: `Koenigz.PerfectCulling.EFT.Autotest`
**Role hypothesis:** Culling/performance pipeline.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 1.
- Dominant folders: `Koenigz/PerfectCulling/EFT/Autotest` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `PerfectCullingAutoTestCamera`, `PerfectCullingAutoTestController`.

### Subsystem: `Koenigz.PerfectCulling.SamplingProviders`
**Role hypothesis:** Culling/performance pipeline.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 7. Field declarations: 10.
- Dominant folders: `Koenigz/PerfectCulling/SamplingProviders` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnValidate` (1).
- High-frequency methods worth tracing for behavior flow: `InitializeSamplingProvider` (2), `IsSamplingPositionActive` (2), `OnValidate` (1), `RefreshInnerVolumes` (1), `IsSamplingPositionActiveMT` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `Koenigz.PerfectCulling.EFT`.

**Key elements in this subsystem**
- `ExcludeFloatingSamplingProvider`, `ExcludeInnerVolumeSamplingProvider`.

### Subsystem: `MirzaBeig.Scripting.Effects`
**Role hypothesis:** MirzaBeig subsystem.

**Structure snapshot**
- Files: 15.
- Declared types: 20 (classes: 15, structs: 3, interfaces: 0, enums: 2).
- Method declarations: 109. Field declarations: 165.
- Dominant folders: `MirzaBeig/Scripting/Effects` (15).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (13), `LateUpdate` (13), `Awake` (11), `Update` (9), `OnDrawGizmosSelected` (9), `OnApplicationQuit` (4), `OnDisable` (3), `OnBecameVisible` (2), `OnBecameInvisible` (2).
- High-frequency methods worth tracing for behavior flow: `Start` (13), `LateUpdate` (13), `Awake` (11), `Update` (9), `OnDrawGizmosSelected` (9), `GetForce` (8), `method_0` (5), `PerParticleSystemSetup` (4), `OnApplicationQuit` (4), `OnDisable` (3), `smethod_0` (2), `smethod_1` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Threading`.

**Key elements in this subsystem**
- `AttractionParticleAffector`, `AttractionParticleForceField`, `Noise`, `Noise2`, `ParticleAffector`, `GetForceParameters`, `ParticleAffectorMT`, `ParticleFlocking`, `GStruct118`, `ParticleForceField`, `GetForceParameters`, `ParticleLights`, `ParticlePlexus`, `TurbulenceParticleAffector`, `NoiseType`, `TurbulenceParticleAffectorMT`, `TurbulenceParticleForceField`, `NoiseType`, `VortexParticleAffector`, `VortexParticleForceField`.

### Subsystem: `MirzaBeig.Shaders.ImageEffects`
**Role hypothesis:** MirzaBeig subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 10. Field declarations: 6.
- Dominant folders: `MirzaBeig/Shaders/ImageEffects` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (2), `Start` (2), `Update` (2), `OnRenderImage` (2), `OnDisable` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (2), `Start` (2), `Update` (2), `OnRenderImage` (2), `blit` (1), `OnDisable` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `IEBase`, `Sharpen`.

### Subsystem: `MultiFlare`
**Role hypothesis:** MultiFlare subsystem.

**Structure snapshot**
- Files: 10.
- Declared types: 11 (classes: 9, structs: 1, interfaces: 0, enums: 1).
- Method declarations: 20. Field declarations: 33.
- Dominant folders: `MultiFlare` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (3), `Awake` (2), `Update` (2), `OnEnable` (1), `OnDisable` (1), `OnDrawGizmos` (1), `Start` (1), `LateUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `OnDestroy` (3), `SetBlindnessProtectionFactor` (2), `Awake` (2), `Update` (2), `SetAlpha` (1), `SetScale` (1), `OnEnable` (1), `OnDisable` (1), `OnDrawGizmos` (1), `Start` (1), `method_0` (1), `smethod_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.Serialization`, `System.Collections.Generic`, `System.Collections`, `Comfort.Common`, `EFT`.

**Key elements in this subsystem**
- `BatchSettings`, `Flare`, `FlareLight`, `FlareOverlapSettings`, `FlareSceneSettings`, `FlareScheduler`, `FlareSettings`, `FlareType`, `MultiFlare`, `ProFlareAtlas`, `Container`.

### Subsystem: `PlayerIcons`
**Role hypothesis:** PlayerIcons subsystem.

**Structure snapshot**
- Files: 4.
- Declared types: 5 (classes: 4, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 8. Field declarations: 36.
- Dominant folders: `PlayerIcons` (4).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `SetPresetIcon` (3), `method_0` (2), `SaveLastRequest` (1), `CheckForcePause` (1), `TryForceRenderLastIco` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Comfort.Common`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `System.Threading.Tasks`, `EFT`, `EFT.InventoryLogic`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `MeshMaterialSetting`, `MeshType`, `PlayerIconCreatorSettings`, `Class738`, `PlayerIconImage`.

### Subsystem: `PostEffects`
**Role hypothesis:** PostEffects subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 14. Field declarations: 29.
- Dominant folders: `PostEffects` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnValidate` (1), `OnDestroy` (1), `OnPreCull` (1), `OnPreRender` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `GetTexture` (2), `Awake` (1), `OnValidate` (1), `method_0` (1), `method_1` (1), `OnDestroy` (1), `OnPreCull` (1), `OnPreRender` (1), `Update` (1), `smethod_0` (1), `method_2` (1), `method_3` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `ContactShadows`, `NoiseTextureSet`.

### Subsystem: `Prism.Demo`
**Role hypothesis:** Prism subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 15.
- Dominant folders: `Prism/Demo` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (2), `Update` (2).
- High-frequency methods worth tracing for behavior flow: `Start` (2), `Update` (2), `smethod_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `Prism.Utils`.

**Key elements in this subsystem**
- `PrismLerpPresetExample`, `PrismLightFlicker`.

### Subsystem: `Prism.Utils`
**Role hypothesis:** Prism subsystem.

**Structure snapshot**
- Files: 10.
- Declared types: 10 (classes: 2, structs: 0, interfaces: 0, enums: 8).
- Method declarations: 1. Field declarations: 92.
- Dominant folders: `Prism/Utils` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `method_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`.

**Key elements in this subsystem**
- `AberrationType`, `AOBlurType`, `BloomType`, `DoFSamples`, `NoiseType`, `PrismAnimCurveCreator`, `PrismPreset`, `PrismPresetType`, `SampleCount`, `TonemapType`.

### Subsystem: `RootMotion`
**Role hypothesis:** Animation IK and rig logic.

**Structure snapshot**
- Files: 11.
- Declared types: 14 (classes: 10, structs: 1, interfaces: 0, enums: 3).
- Method declarations: 49. Field declarations: 70.
- Dominant folders: `RootMotion` (11).

**How it works (operational pattern)**
- Animation IK subsystems refine transforms after core animation state is known, applying pose corrections and target constraints.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (3), `LateUpdate` (3), `Update` (2), `FixedUpdate` (2), `OnGUI` (1), `OnDisable` (1), `Start` (1), `OnTriggerEnter` (1), `OnTriggerStay` (1), `OnTriggerExit` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (3), `LateUpdate` (3), `method_0` (3), `Update` (2), `FixedUpdate` (2), `UpdateTransform` (2), `IsEmpty` (1), `Contains` (1), `AutoDetectReferences` (1), `DetectReferencesByNaming` (1), `AssignHumanoidReferences` (1), `SetupError` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `Axis`, `BipedLimbOrientations`, `LimbOrientation`, `BipedReferences`, `GStruct144`, `CameraController`, `UpdateMode`, `CameraControllerFPS`, `Comments`, `DemoGUIMessage`, `InterpolationMode`, `Singleton`, `SolverManager`, `TriggerEventBroadcaster`.

### Subsystem: `RootMotion.Demos`
**Role hypothesis:** Animation IK and rig logic.

**Structure snapshot**
- Files: 9.
- Declared types: 13 (classes: 9, structs: 2, interfaces: 0, enums: 2).
- Method declarations: 46. Field declarations: 113.
- Dominant folders: `RootMotion/Demos` (9).

**How it works (operational pattern)**
- Animation IK subsystems refine transforms after core animation state is known, applying pose corrections and target constraints.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (7), `Update` (7), `LateUpdate` (3), `OnAnimatorMove` (2), `FixedUpdate` (1).
- High-frequency methods worth tracing for behavior flow: `Start` (7), `Update` (7), `GetPivotPoint` (3), `LateUpdate` (3), `method_0` (3), `GetAngleFromForward` (2), `OnAnimatorMove` (2), `Move` (2), `method_1` (2), `method_2` (2), `method_3` (2), `GetSpherecastHit` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`.

**Key elements in this subsystem**
- `CharacterAnimationBase`, `CharacterAnimationSimple`, `CharacterAnimationThirdPerson`, `CharacterBase`, `CharacterThirdPerson`, `MoveMode`, `GStruct145`, `SimpleLocomotion`, `RotationMode`, `SlowMo`, `UserControlAI`, `UserControlThirdPerson`, `GStruct146`.

### Subsystem: `RootMotion.FinalIK`
**Role hypothesis:** Animation IK and rig logic.

**Structure snapshot**
- Files: 73.
- Declared types: 121 (classes: 111, structs: 2, interfaces: 0, enums: 8).
- Method declarations: 750. Field declarations: 775.
- Dominant folders: `RootMotion/FinalIK` (73).

**How it works (operational pattern)**
- Animation IK subsystems refine transforms after core animation state is known, applying pose corrections and target constraints.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Update` (17), `Start` (12), `OnDestroy` (11), `Apply` (10), `LateUpdate` (9), `OnInitiate` (9), `OnUpdate` (9), `OnModifyOffset` (6), `OnDisable` (4), `Awake` (3), `FixedUpdate` (3), `OnApply` (3), `OnPreSolve` (2), `OnDrawGizmosSelected` (2), `OnEnable` (2), `Process` (2), `OnInitiateVirtual` (2), `OnUpdateVirtual` (2), `OnPostSolveVirtual` (2), `OnPostWrite` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (31), `method_3` (30), `method_1` (29), `method_2` (27), `method_4` (25), `Initiate` (21), `IsValid` (20), `Update` (17), `FixTransforms` (17), `method_5` (15), `OpenUserManual` (14), `OpenScriptReference` (14).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Collections`, `System.Runtime.CompilerServices`, `UnityEngine.Serialization`.

**Key elements in this subsystem**
- `AimIK`, `AimPoser`, `Pose`, `Amplifier`, `Body`, `EffectorLink`, `BipedIK`, `BipedIKSolvers`, `BodyTilt`, `CCDIK`, `Constraint`, `ConstraintPosition`, `ConstraintPositionOffset`, `ConstraintRotation`, `ConstraintRotationOffset`, `Constraints`, `FABRIK`, `FABRIKChain`, `FABRIKRoot`, `FBBIKArmBending`, `FBBIKHeadEffector`, `BendBone`, `FBIKChain`, `ChildConstraint`, `Smoothing`, `Finger`, `FingerRig`, `FullBodyBipedChain`, `FullBodyBipedEffector`, `FullBodyBipedIK`, `GenericPoser`, `Map`, `Grounder`, `GrounderBipedIK`, `GrounderFBBIK`, `SpineEffector`, `GrounderIK`, `GrounderQuadruped`, `GStruct147`, `Grounding`, ... (+81 more).

### Subsystem: `RuntimeInspector`
**Role hypothesis:** RuntimeInspector subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 11. Field declarations: 27.
- Dominant folders: `RuntimeInspector` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `OnValidate` (1), `Update` (1), `OnGUI` (1).
- High-frequency methods worth tracing for behavior flow: `Awake` (1), `method_0` (1), `method_1` (1), `method_2` (1), `OnValidate` (1), `LastRectClick` (1), `Update` (1), `OnGUI` (1), `Open` (1), `Close` (1), `smethod_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Reflection`, `EFT.Weather`, `UnityEngine`.

**Key elements in this subsystem**
- `Debugger`.

### Subsystem: `Systems.Effects`
**Role hypothesis:** Systems subsystem.

**Structure snapshot**
- Files: 7.
- Declared types: 16 (classes: 12, structs: 3, interfaces: 0, enums: 1).
- Method declarations: 59. Field declarations: 128.
- Dominant folders: `Systems/Effects` (7).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnDestroy` (3), `Update` (3), `Awake` (2), `Start` (2), `OnValidate` (1), `Dispose` (1), `LateUpdate` (1), `OnEnable` (1), `OnDisable` (1), `OnRenderImage` (1), `OnBecameVisible` (1), `OnBecameInvisible` (1).
- High-frequency methods worth tracing for behavior flow: `Emit` (4), `OnDestroy` (3), `Update` (3), `Awake` (2), `PlayerMeshesHit` (2), `UpdateMBOITIndoorAttenuation` (2), `PlayKnifeHitEffect` (2), `SetFlareEffect` (2), `Start` (2), `OnValidate` (1), `InitDictionaryAndNames` (1), `AddEffectEmit` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `Comfort.Common`, `EFT`, `EFT.Ballistics`, `EFT.PrefabSettings`, `System.Linq`, `Audio.SpatialSystem`, `DeferredDecals`.

**Key elements in this subsystem**
- `Effects`, `MuzzleEffectContainer`, `EmissionEffect`, `GStruct73`, `Effect`, `ParticleSys`, `Type`, `Class710`, `Class711`, `EffectsCommutator`, `FireworkEffectSelector`, `FlareShotEffectAnimator`, `FlareShotEffectSelector`, `FlareParameters`, `ScreenColorBlender`, `TreePlaneLod`.

### Subsystem: `TerrainStitch`
**Role hypothesis:** TerrainStitch subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 2. Field declarations: 2.
- Dominant folders: `TerrainStitch` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (1).
- High-frequency methods worth tracing for behavior flow: `Start` (1), `CreateNeighbours` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `UnityEngine`.

**Key elements in this subsystem**
- `TerrainNeighbours`.

### Subsystem: `UI`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 2.
- Dominant folders: `UI` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.UI`, `UnityEngine`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `LocalizedFilterButton`.

### Subsystem: `UI.BattleUI.Gestures`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 5. Field declarations: 4.
- Dominant folders: `UI/BattleUI/Gestures` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `SetChild` (2), `CleanUp` (2), `method_0` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `BaseTransformSolver`, `MiddleAlignmentGridTransformSolver`.

### Subsystem: `UI.DragAndDrop.ItemViews`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 8. Field declarations: 7.
- Dominant folders: `UI/DragAndDrop/ItemViews` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `Show` (1), `SetExamined` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Runtime.CompilerServices`, `EFT`, `EFT.UI`, `UnityEngine`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `WishlistGridView`, `Class735`.

### Subsystem: `UI.Hideout`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 10.
- Declared types: 28 (classes: 27, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 82. Field declarations: 100.
- Dominant folders: `UI/Hideout` (10).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnCellSelected` (3), `Init` (2), `Awake` (1), `OnPointerEnter` (1), `OnPointerExit` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (13), `Show` (11), `method_1` (6), `Close` (5), `method_3` (4), `method_4` (3), `method_5` (3), `method_2` (3), `OnCellSelected` (3), `UpdateTabGroup` (2), `UpdateAreaStashView` (2), `TranslateCommand` (2).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.Hideout`, `UnityEngine`, `EFT.InventoryLogic`, `System.Collections.Generic`, `System.Runtime.CompilerServices`, `EFT.UI`, `System.Threading.Tasks`, `EFT.UI.Screens`, `UnityEngine.Events`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `AbstractHideoutAreaTransferItemsScreen`, `Class725`, `BaseHideoutAreaTransferItemsScreen`, `TransferScreenSwitcherTabController`, `GClass3901`, `HideoutAreaTransferItemsScreen`, `GClass3902`, `HideoutCustomizationCell`, `Class726`, `HideoutCustomizationOptionsPanel`, `Class3062`, `HideoutCustomizationOptionsWithSlotsPanel`, `Class727`, `Class728`, `Class729`, `Class730`, `HideoutCustomizationScreen`, `Class3063`, `HideoutCustomizationSimpleOptionsPanel`, `Class731`, `HideoutCustomizationSlotsGroupView`, `Class732`, `Class733`, `Class734`, `QteHandleData`, `EPropsTarget`, `PropsVariantData`, `PropsData`.

### Subsystem: `UI.InfoWindow`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 5.
- Dominant folders: `UI/InfoWindow` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnValidate` (1), `Start` (1), `OnDestroy` (1).
- High-frequency methods worth tracing for behavior flow: `OnValidate` (1), `Start` (1), `OnDestroy` (1), `method_0` (1), `method_1` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `EFT.UI`, `UnityEngine`, `UnityEngine.UI`.

**Key elements in this subsystem**
- `StretchWindow`.

### Subsystem: `UI.Matchmaker.Group`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 6. Field declarations: 6.
- Dominant folders: `UI/Matchmaker/Group` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `Show` (1), `method_1` (1), `RequestContextMenuForPlayer` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`, `System.Linq`, `System.Runtime.CompilerServices`, `ChatShared`, `EFT.UI`, `EFT.UI.Matchmaker`, `UnityEngine`.

**Key elements in this subsystem**
- `FriendListInvitePlayerPanel`, `Class724`.

### Subsystem: `UI.Trading_UI.Ragfair.NodeView`
**Role hypothesis:** UI subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 1 (classes: 1, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 0. Field declarations: 2.
- Dominant folders: `UI/Trading_UI/Ragfair/NodeView` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- No strong callback-family concentration was detected; prefer constructor/init methods and call-site patching on high-frequency domain methods.
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `EFT.HandBook`, `UnityEngine`.

**Key elements in this subsystem**
- `WishlistCategoryView`.

### Subsystem: `UnityDiagnostics`
**Role hypothesis:** UnityDiagnostics subsystem.

**Structure snapshot**
- Files: 1.
- Declared types: 3 (classes: 2, structs: 0, interfaces: 0, enums: 1).
- Method declarations: 13. Field declarations: 14.
- Dominant folders: `UnityDiagnostics` (1).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Awake` (1), `Start` (1), `Update` (1), `FixedUpdate` (1), `OnGUI` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `method_1` (2), `Awake` (1), `Start` (1), `Update` (1), `FixedUpdate` (1), `OnGUI` (1), `Add` (1), `Remove` (1), `MakeLag` (1), `CheckToggleInput` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Diagnostics`, `System.Threading`, `Comfort.Common`, `UnityEngine`.

**Key elements in this subsystem**
- `CPULagSimulator`, `ELoadType`, `LagSimulator`.

### Subsystem: `UnityEngine.UI.Extensions`
**Role hypothesis:** Unity integration layer.

**Structure snapshot**
- Files: 2.
- Declared types: 2 (classes: 2, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 13. Field declarations: 38.
- Dominant folders: `UnityEngine/UI/Extensions` (2).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Start` (1), `Update` (1).
- High-frequency methods worth tracing for behavior flow: `method_0` (2), `CalculateLayoutInputHorizontal` (1), `SetLayoutHorizontal` (1), `SetLayoutVertical` (1), `CalculateLayoutInputVertical` (1), `SetLayout` (1), `LayoutRow` (1), `GetGreatestMinimumChildWidth` (1), `Start` (1), `method_1` (1), `Update` (1), `method_2` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `System.Collections.Generic`.

**Key elements in this subsystem**
- `FlowLayoutGroup`, `SoftMask`.

### Subsystem: `WaterSSR`
**Role hypothesis:** WaterSSR subsystem.

**Structure snapshot**
- Files: 3.
- Declared types: 3 (classes: 3, structs: 0, interfaces: 0, enums: 0).
- Method declarations: 15. Field declarations: 21.
- Dominant folders: `WaterSSR` (3).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `OnEnable` (2), `OnDisable` (2), `Awake` (1), `OnValidate` (1).
- High-frequency methods worth tracing for behavior flow: `OnEnable` (2), `OnDisable` (2), `Awake` (1), `OnValidate` (1), `GetSettingsBlock` (1), `method_0` (1), `method_1` (1), `method_2` (1), `method_3` (1), `method_4` (1), `smethod_0` (1), `DisableSnowMask` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Runtime.CompilerServices`, `System.Threading`, `System.Collections.Generic`, `UnityEngine.Rendering`.

**Key elements in this subsystem**
- `WaterForSSRv3`, `WaterObject`, `WaterRendererv3`.

### Subsystem: `WindowsManagerUtilities`
**Role hypothesis:** WindowsManagerUtilities subsystem.

**Structure snapshot**
- Files: 8.
- Declared types: 9 (classes: 3, structs: 6, interfaces: 0, enums: 0).
- Method declarations: 4. Field declarations: 31.
- Dominant folders: `WindowsManagerUtilities` (8).

**How it works (operational pattern)**
- This subsystem follows a standard Unity/C# component pattern: setup/init methods create runtime state, processing methods execute behavior, callbacks/events react to engine or gameplay changes, and disposal methods clean resources.

**Hook points (practical interception seams)**
- Frequently observed callback-style methods: `Init` (1).
- High-frequency methods worth tracing for behavior flow: `Clear` (2), `AddTextureOffsets` (1), `Init` (1).
- Typical hook strategy: pre-hook validation (`Init/Load/Register`), mid-hook transformation (`Handle/Process/Execute/Update`), post-hook cleanup (`Dispose/OnDisable/OnDestroy`).

**Relations to other subsystems**
- Most imported namespaces: `UnityEngine`, `System.Collections.Generic`.

**Key elements in this subsystem**
- `GeometryBuffers`, `Struct178`, `InstancesBuffers`, `LightProperties`, `MaterialParameters`, `MeshOffsets`, `SceneDataContainer`, `TexturesOffsets`, `WindowVertex`.

## Hooking playbook by lifecycle phase
1. **Bootstrap hooks:** `Awake`, `Start`, `Init`, `Initialize`, `Load`, `Register`. Use for dependency replacement and service wrapping.
2. **Frame/tick hooks:** `Update`, `LateUpdate`, `FixedUpdate`, `Tick`, `Process`, `Handle`, `Execute`. Use for simulation overrides or telemetry.
3. **Interaction hooks:** `OnTriggerEnter/Exit`, `OnCollisionEnter/Exit`, `OnEnable/Disable`. Use for proximity, activation, and state machine transitions.
4. **Shutdown hooks:** `Dispose`, `OnDestroy`, `Unregister`, `Save`. Use to prevent leaks and persist instrumentation output.

## Coverage appendix
- Full per-file extracted inventory remains in `docs/tarkov-assembly-elements.csv`.
- This document contains one subsection for **every discovered namespace subsystem**, each with operational behavior, hooks, and relationships.
