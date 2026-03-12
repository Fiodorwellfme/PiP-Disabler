using System;
using System.Collections.Generic;
using System.Reflection;
using EFT;
using EFT.Animations;
using EFT.CameraControl;
using EFT.InventoryLogic;
using Comfort.Common;
using HarmonyLib;
using UnityEngine;

namespace ScopeHousingMeshSurgery
{
    /// <summary>
    /// Handles optic zeroing (calibration distance) via the proper EFT pathway.
    ///
    /// Listens for PageUp/PageDown input while scoped, then calls
    /// IFirearmHandsController.OpticCalibrationSwitchUp/Down with a correctly
    /// built FirearmScopeStateStruct[].  This works offline and with Fika
    /// because it uses the same code path as EFT's native input handling.
    ///
    /// The overlay reticle does NOT need to move — EFT adjusts weapon alignment
    /// internally when zeroing changes.  We just need to trigger the change and
    /// read back the current distance for display/logging.
    /// </summary>
    internal static class ZeroingController
    {
        // ── Reflection cache ────────────────────────────────────────────────
        private static bool _reflectionAttempted;
        private static bool _reflectionReady;

        // Methods on Player.FirearmController (implements IFirearmHandsController)
        private static MethodInfo _opticCalibUpMethod;
        private static MethodInfo _opticCalibDownMethod;

        // FirearmScopeStateStruct type + fields
        private static Type _scopeStateStructType;
        private static FieldInfo _scopeStateId;
        private static FieldInfo _scopeStateScopeIndex;
        private static FieldInfo _scopeStateScopeMode;
        private static FieldInfo _scopeStateCalibIndex;

        // Weapon.AimIndex (reactive property)
        private static PropertyInfo _aimIndexProp;

        // Cooldown to prevent rapid-fire zeroing
        private static float _lastZeroingTime;
        private const  float ZEROING_COOLDOWN = 0.2f;

        // Current zeroing distance (updated after each change)
        public static int CurrentZeroingMeters { get; private set; }

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Call from ScopeLifecycle.Tick() each frame while scoped.
        /// Polls zeroing input and applies changes through the proper EFT pathway.
        /// </summary>
        public static void Tick()
        {
            if (!ScopeHousingMeshSurgeryPlugin.EnableZeroing.Value) return;
            if (!ScopeLifecycle.IsScoped) return;

            // Cooldown
            if (Time.unscaledTime - _lastZeroingTime < ZEROING_COOLDOWN) return;

            // Poll input
            KeyCode upKey   = ScopeHousingMeshSurgeryPlugin.ZeroingUpKey.Value;
            KeyCode downKey = ScopeHousingMeshSurgeryPlugin.ZeroingDownKey.Value;

            bool wantsUp   = UnityEngine.Input.GetKeyDown(upKey);
            bool wantsDown = UnityEngine.Input.GetKeyDown(downKey);

            if (!wantsUp && !wantsDown) return;

            // Ensure reflection is ready
            if (!EnsureReflection()) return;

            try
            {
                var player = GetLocalPlayer();
                if (player == null) return;

                var fc = player.HandsController as Player.FirearmController;
                if (fc == null) return;

                // Build scope state array from the weapon's current state
                var scopeStates = BuildScopeStates(fc);
                if (scopeStates == null) return;

                // Call the proper EFT method
                if (wantsUp)
                {
                    _opticCalibUpMethod.Invoke(fc, new object[] { scopeStates });
                    ScopeHousingMeshSurgeryPlugin.LogInfo("[Zeroing] OpticCalibrationSwitchUp called");
                }
                else
                {
                    _opticCalibDownMethod.Invoke(fc, new object[] { scopeStates });
                    ScopeHousingMeshSurgeryPlugin.LogInfo("[Zeroing] OpticCalibrationSwitchDown called");
                }

                _lastZeroingTime = Time.unscaledTime;

                // Read back the new zeroing distance
                ReadCurrentZeroing(player);
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Zeroing] Tick failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Read and cache the current zeroing distance.  Called after zeroing
        /// changes and on scope enter for initial state.
        /// </summary>
        public static void ReadCurrentZeroing(Player player = null)
        {
            try
            {
                if (player == null) player = GetLocalPlayer();
                if (player == null) return;

                var pwa = player.ProceduralWeaponAnimation;
                if (pwa == null) return;

                // pwa.CurrentAimingMod is the active SightComponent
                var sight = pwa.CurrentAimingMod;
                if (sight == null) return;

                // SightComponent.GetCurrentOpticCalibrationDistance()
                var method = sight.GetType().GetMethod("GetCurrentOpticCalibrationDistance",
                    BindingFlags.Public | BindingFlags.Instance);

                if (method != null)
                {
                    int meters = (int)method.Invoke(sight, null);
                    if (meters != CurrentZeroingMeters)
                    {
                        CurrentZeroingMeters = meters;
                        ScopeHousingMeshSurgeryPlugin.LogInfo(
                            $"[Zeroing] Current distance: {meters}m");
                    }
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[Zeroing] ReadCurrentZeroing failed: {ex.Message}");
            }
        }

        /// <summary>Reset state on scope exit.</summary>
        public static void Reset()
        {
            CurrentZeroingMeters = 0;
        }

        // ── Reflection setup ────────────────────────────────────────────────

        private static bool EnsureReflection()
        {
            if (_reflectionReady) return true;
            if (_reflectionAttempted) return false;
            _reflectionAttempted = true;

            try
            {
                var fcType = typeof(Player.FirearmController);

                // Find OpticCalibrationSwitchUp/Down methods
                // These accept a single parameter: an array of FirearmScopeStateStruct
                foreach (var method in fcType.GetMethods(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (method.Name == "OpticCalibrationSwitchUp" || method.Name == "OpticCalibrationSwitchDown")
                    {
                        var parms = method.GetParameters();
                        if (parms.Length == 1 && parms[0].ParameterType.IsArray)
                        {
                            if (method.Name == "OpticCalibrationSwitchUp")
                                _opticCalibUpMethod = method;
                            else
                                _opticCalibDownMethod = method;

                            // Extract the struct type from the array parameter
                            if (_scopeStateStructType == null)
                                _scopeStateStructType = parms[0].ParameterType.GetElementType();
                        }
                    }
                }

                // Fallback: search interface methods if direct search failed
                if (_opticCalibUpMethod == null || _opticCalibDownMethod == null)
                {
                    foreach (var iface in fcType.GetInterfaces())
                    {
                        foreach (var method in iface.GetMethods())
                        {
                            if (method.Name.Contains("OpticCalibrationSwitchUp") ||
                                method.Name.Contains("OpticCalibrationSwitch") && method.Name.Contains("Up"))
                            {
                                var parms = method.GetParameters();
                                if (parms.Length == 1 && parms[0].ParameterType.IsArray)
                                {
                                    if (_opticCalibUpMethod == null)
                                        _opticCalibUpMethod = method;
                                    if (_scopeStateStructType == null)
                                        _scopeStateStructType = parms[0].ParameterType.GetElementType();
                                }
                            }
                            if (method.Name.Contains("OpticCalibrationSwitchDown") ||
                                method.Name.Contains("OpticCalibrationSwitch") && method.Name.Contains("Down"))
                            {
                                var parms = method.GetParameters();
                                if (parms.Length == 1 && parms[0].ParameterType.IsArray)
                                {
                                    if (_opticCalibDownMethod == null)
                                        _opticCalibDownMethod = method;
                                }
                            }
                        }
                    }
                }

                if (_opticCalibUpMethod == null || _opticCalibDownMethod == null)
                {
                    ScopeHousingMeshSurgeryPlugin.LogError(
                        "[Zeroing] Could not find OpticCalibrationSwitchUp/Down methods");
                    return false;
                }

                // Discover FirearmScopeStateStruct fields
                if (_scopeStateStructType != null)
                {
                    _scopeStateId         = FindField(_scopeStateStructType, "Id", typeof(string));
                    _scopeStateScopeIndex = FindField(_scopeStateStructType, "ScopeIndexInsideSight", typeof(int));
                    _scopeStateScopeMode  = FindField(_scopeStateStructType, "ScopeMode", typeof(int));
                    _scopeStateCalibIndex = FindField(_scopeStateStructType, "ScopeCalibrationIndex", typeof(int));

                    if (_scopeStateId == null || _scopeStateScopeIndex == null ||
                        _scopeStateScopeMode == null || _scopeStateCalibIndex == null)
                    {
                        ScopeHousingMeshSurgeryPlugin.LogWarn(
                            $"[Zeroing] FirearmScopeStateStruct fields incomplete. " +
                            $"Id={_scopeStateId != null} ScopeIndex={_scopeStateScopeIndex != null} " +
                            $"Mode={_scopeStateScopeMode != null} CalibIdx={_scopeStateCalibIndex != null}. " +
                            $"Attempting fallback by field order.");

                        // Fallback: assign fields by order (common for obfuscated structs)
                        var fields = _scopeStateStructType.GetFields(BindingFlags.Public | BindingFlags.Instance);
                        LogStructFields(fields);

                        if (fields.Length >= 4)
                        {
                            // Typical order: Id (string), ScopeIndexInsideSight (int), ScopeMode (int), ScopeCalibrationIndex (int)
                            foreach (var f in fields)
                            {
                                if (f.FieldType == typeof(string) && _scopeStateId == null)
                                    _scopeStateId = f;
                                else if (f.FieldType == typeof(int))
                                {
                                    if (_scopeStateScopeIndex == null) _scopeStateScopeIndex = f;
                                    else if (_scopeStateScopeMode == null) _scopeStateScopeMode = f;
                                    else if (_scopeStateCalibIndex == null) _scopeStateCalibIndex = f;
                                }
                            }
                        }
                    }

                    ScopeHousingMeshSurgeryPlugin.LogInfo(
                        $"[Zeroing] Struct type: {_scopeStateStructType.Name} " +
                        $"Id={_scopeStateId?.Name} ScopeIndex={_scopeStateScopeIndex?.Name} " +
                        $"Mode={_scopeStateScopeMode?.Name} CalibIdx={_scopeStateCalibIndex?.Name}");
                }

                // AimIndex on weapon
                // Weapon.AimIndex is a BindableState<int> / ReactiveProperty
                // We'll resolve it dynamically when building scope states
                var weaponType = typeof(Weapon);
                _aimIndexProp = weaponType.GetProperty("AimIndex",
                    BindingFlags.Public | BindingFlags.Instance);

                _reflectionReady = _opticCalibUpMethod != null &&
                                   _opticCalibDownMethod != null &&
                                   _scopeStateStructType != null;

                ScopeHousingMeshSurgeryPlugin.LogInfo(
                    $"[Zeroing] Reflection ready={_reflectionReady} " +
                    $"Up={_opticCalibUpMethod?.Name} Down={_opticCalibDownMethod?.Name}");

                return _reflectionReady;
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Zeroing] Reflection setup failed: {ex.Message}");
                return false;
            }
        }

        // ── Build scope state array ─────────────────────────────────────────

        /// <summary>
        /// Build FirearmScopeStateStruct[] for all sights on the weapon.
        /// Mirrors EFT's internal scope state serialization.
        /// </summary>
        private static Array BuildScopeStates(Player.FirearmController fc)
        {
            try
            {
                var weapon = fc.Item as Weapon;
                if (weapon == null) return null;

                // Get current aim index
                int currentAimIndex = GetAimIndex(weapon);

                // Collect all SightComponent on the weapon
                var sights = new List<SightComponentInfo>();
                CollectSights(weapon, sights);

                if (sights.Count == 0)
                {
                    ScopeHousingMeshSurgeryPlugin.LogVerbose("[Zeroing] No sights found on weapon");
                    return null;
                }

                // Build the array
                var stateArray = Array.CreateInstance(_scopeStateStructType, sights.Count);

                for (int i = 0; i < sights.Count; i++)
                {
                    var info = sights[i];
                    var state = Activator.CreateInstance(_scopeStateStructType);

                    if (_scopeStateId != null)
                        _scopeStateId.SetValue(state, info.ItemId);
                    if (_scopeStateScopeIndex != null)
                        _scopeStateScopeIndex.SetValue(state, info.ScopeIndex);
                    if (_scopeStateScopeMode != null)
                        _scopeStateScopeMode.SetValue(state, info.CurrentMode);
                    if (_scopeStateCalibIndex != null)
                        _scopeStateCalibIndex.SetValue(state, info.CurrentCalibIndex);

                    stateArray.SetValue(state, i);
                }

                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[Zeroing] Built {sights.Count} scope states, aimIndex={currentAimIndex}");

                return stateArray;
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Zeroing] BuildScopeStates failed: {ex.Message}");
                return null;
            }
        }

        // ── Sight collection ────────────────────────────────────────────────

        private struct SightComponentInfo
        {
            public string ItemId;
            public int ScopeIndex;
            public int CurrentMode;
            public int CurrentCalibIndex;
        }

        /// <summary>
        /// Collect all SightComponent instances from the weapon, including mods.
        /// Uses reflection to access SightComponent properties.
        /// </summary>
        private static void CollectSights(Weapon weapon, List<SightComponentInfo> output)
        {
            try
            {
                // weapon.AllSlots → iterate all mods → find items with SightComponent
                // Simpler: use weapon.GetComponentsInChildren or iterate Mods
                // EFT approach: weapon iterates over all mod slots recursively

                // Try the Sight enumeration that EFT uses
                // Weapon has a method/property to get all sights
                var getAllSights = weapon.GetType().GetMethod("GetAllSightMods",
                    BindingFlags.Public | BindingFlags.Instance);

                // Fallback: use Mods property
                IEnumerable<Item> mods = null;
                var modsProp = weapon.GetType().GetProperty("Mods",
                    BindingFlags.Public | BindingFlags.Instance);
                if (modsProp != null)
                    mods = modsProp.GetValue(weapon) as IEnumerable<Item>;

                if (mods == null)
                {
                    // Last resort: try GetAllItems
                    var getAllItems = weapon.GetType().GetMethod("GetAllItems",
                        BindingFlags.Public | BindingFlags.Instance);
                    if (getAllItems != null)
                        mods = getAllItems.Invoke(weapon, null) as IEnumerable<Item>;
                }

                if (mods == null) return;

                foreach (var mod in mods)
                {
                    if (mod == null) continue;

                    // Check if this item has sight functionality
                    // SightComponent is accessed via item.GetItemComponent<SightComponent>()
                    var sightComp = GetSightComponent(mod);
                    if (sightComp == null) continue;

                    // Read current mode and calibration index for each scope index
                    int scopeCount = GetScopeCount(sightComp);
                    for (int si = 0; si < scopeCount; si++)
                    {
                        output.Add(new SightComponentInfo
                        {
                            ItemId           = mod.Id,
                            ScopeIndex       = si,
                            CurrentMode      = GetSelectedMode(sightComp, si),
                            CurrentCalibIndex = GetCalibrationIndex(sightComp, si)
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                ScopeHousingMeshSurgeryPlugin.LogError($"[Zeroing] CollectSights failed: {ex.Message}");
            }
        }

        // ── SightComponent accessors (reflection) ───────────────────────────

        private static object GetSightComponent(Item item)
        {
            try
            {
                // item.GetItemComponent<SightComponent>()
                // SightComponent might be in EFT.InventoryLogic
                var method = item.GetType().GetMethod("GetItemComponent",
                    BindingFlags.Public | BindingFlags.Instance);
                if (method == null) return null;

                // Find the SightComponent type
                Type sightCompType = null;
                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    sightCompType = asm.GetType("EFT.InventoryLogic.SightComponent");
                    if (sightCompType != null) break;
                }
                if (sightCompType == null)
                {
                    // Try without namespace
                    foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                    {
                        foreach (var t in asm.GetTypes())
                        {
                            if (t.Name == "SightComponent")
                            { sightCompType = t; break; }
                        }
                        if (sightCompType != null) break;
                    }
                }
                if (sightCompType == null) return null;

                var generic = method.MakeGenericMethod(sightCompType);
                return generic.Invoke(item, null);
            }
            catch { return null; }
        }

        private static int GetScopeCount(object sightComp)
        {
            try
            {
                // SightComponent.ScopesCount or Template.Zooms.Length
                var prop = sightComp.GetType().GetProperty("ScopesCount",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null) return (int)prop.GetValue(sightComp);

                // Fallback: ScopesCurrentCalibPointIndexes.Length
                var calibProp = sightComp.GetType().GetProperty("ScopesCurrentCalibPointIndexes",
                    BindingFlags.Public | BindingFlags.Instance);
                if (calibProp != null)
                {
                    var arr = calibProp.GetValue(sightComp) as int[];
                    if (arr != null) return arr.Length;
                }

                return 1;
            }
            catch { return 1; }
        }

        private static int GetSelectedMode(object sightComp, int scopeIndex)
        {
            try
            {
                // SightComponent.SelectedScopeMode or ScopesSelectedModes[scopeIndex]
                var prop = sightComp.GetType().GetProperty("ScopesSelectedModes",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var arr = prop.GetValue(sightComp) as int[];
                    if (arr != null && scopeIndex < arr.Length)
                        return arr[scopeIndex];
                }

                // Fallback: SelectedScopeMode
                var modeProp = sightComp.GetType().GetProperty("SelectedScopeMode",
                    BindingFlags.Public | BindingFlags.Instance);
                if (modeProp != null) return (int)modeProp.GetValue(sightComp);

                return 0;
            }
            catch { return 0; }
        }

        private static int GetCalibrationIndex(object sightComp, int scopeIndex)
        {
            try
            {
                // SightComponent.ScopesCurrentCalibPointIndexes[scopeIndex]
                var prop = sightComp.GetType().GetProperty("ScopesCurrentCalibPointIndexes",
                    BindingFlags.Public | BindingFlags.Instance);
                if (prop != null)
                {
                    var arr = prop.GetValue(sightComp) as int[];
                    if (arr != null && scopeIndex < arr.Length)
                        return arr[scopeIndex];
                }
                return 0;
            }
            catch { return 0; }
        }

        private static int GetAimIndex(Weapon weapon)
        {
            try
            {
                if (_aimIndexProp != null)
                {
                    var reactive = _aimIndexProp.GetValue(weapon);
                    if (reactive != null)
                    {
                        var valueProp = reactive.GetType().GetProperty("Value",
                            BindingFlags.Public | BindingFlags.Instance);
                        if (valueProp != null)
                            return (int)valueProp.GetValue(reactive);
                    }
                }
                return 0;
            }
            catch { return 0; }
        }

        // ── Helpers ─────────────────────────────────────────────────────────

        private static FieldInfo FindField(Type type, string name, Type fieldType)
        {
            var field = type.GetField(name, BindingFlags.Public | BindingFlags.Instance);
            if (field != null && field.FieldType == fieldType) return field;

            // Try case-insensitive
            foreach (var f in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && f.FieldType == fieldType)
                    return f;
            }
            return null;
        }

        private static void LogStructFields(FieldInfo[] fields)
        {
            foreach (var f in fields)
            {
                ScopeHousingMeshSurgeryPlugin.LogVerbose(
                    $"[Zeroing] Struct field: {f.Name} type={f.FieldType.Name}");
            }
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
    }
}
