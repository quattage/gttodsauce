using BepInEx;
using GTTODSauce.impl;
using HarmonyLib;
using UnityEngine;

namespace GTTODSauce;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class GTTODSauce : BaseUnityPlugin {

    private static GTTODSauce? _modSingleton;
    private Harmony? _harmony;
    private MovementManager? _manager;

    private void OnEnable() {
        if(_modSingleton != null && _modSingleton != this) {
            Logger.LogError($"Redundant re-registration of {MyPluginInfo.PLUGIN_GUID} singleton - skipped harmony injections");
            Destroy(this);
            return;
        }
        if(_harmony == null)
            _harmony = Harmony.CreateAndPatchAll(typeof(GTTODSauce), MyPluginInfo.PLUGIN_GUID);
        else _harmony.PatchAll(typeof(GTTODSauce));
        Logger.LogInfo($"Loaded [{MyPluginInfo.PLUGIN_GUID} {MyPluginInfo.PLUGIN_VERSION}]");
        _modSingleton = this;

    }

    private void OnDestroy() {
        _harmony?.UnpatchSelf();
        _harmony = null;
        _manager?.Revert();
        _manager = null;
        Logger.LogInfo($"Unloaded {MyPluginInfo.PLUGIN_NAME}");
        _modSingleton = null;
    }

    public void Log(string message) {
        Logger.LogInfo(message);
    }

    public void LogDebug(string message) {
        Logger.LogDebug(message);
    }

    public void LogError(string message) {
        Logger.LogError(message);
    }

    public MovementManager CreateManager(ac_CharacterController controller) {
        if(_manager != null) {
            if(controller == _manager.Controller)
                return _manager;
            _manager.Revert();
            _manager = new MovementManager(this, controller);
            _manager.Apply();
            return _manager;
        }
        _manager = new MovementManager(this, controller);
        _manager.Apply();
        return _manager;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "Update"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_Update(ac_CharacterController __instance) {
        _modSingleton.CreateManager(__instance).Update();
        return false;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "FixedUpdate"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_FixedUpdate(ac_CharacterController __instance) {
        _modSingleton.CreateManager(__instance).FixedUpdate();
        if(Input.GetKeyDown(KeyCode.F4)) {
            _modSingleton.enabled = !_modSingleton.enabled;
            GameManager.GM.GetComponent<GTTOD_HUD>().BigTextPopUp($"{(_modSingleton.enabled ? "Unfucked" : "Re-fucked")} GTTOD", 0);
        }
        return false;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "Start"), HarmonyPostfix]
    public static void gttoduf_ac_CharacterController_Start(ac_CharacterController __instance) {
        _modSingleton?._manager?.Apply();
        __instance.ControllerUpdate();
        __instance.UpdateBasicMovement();
        __instance.CameraUpdate();
        __instance.ColliderUpdate();
        __instance.UpdateAdvancedMovement();
        __instance.UpdateMovingPlatform();
    }

    [HarmonyPatch(typeof(ac_CharacterController), "LateUpdate"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_LateUpdate(ac_CharacterController __instance) {
        // the mod doesn't need this
        return false;
    }
}