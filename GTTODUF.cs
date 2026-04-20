using BepInEx;
using gttoduf.impl;
using HarmonyLib;
using UnityEngine;

namespace gttoduf;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class GTTODUF : BaseUnityPlugin {

    public static string ID => MyPluginInfo.PLUGIN_GUID;
    public static string Name => MyPluginInfo.PLUGIN_NAME;
    public static string Version => MyPluginInfo.PLUGIN_VERSION;
    private static GTTODUF? _modSingleton;
    private Harmony? _harmony;
    private MovementManager? _manager;

    private void OnEnable() {
        if(_modSingleton != null && _modSingleton != this) {
            Logger.LogError($"Redundant re-registration of {Name} singleton - skipped harmony injections");
            Destroy(this);
            return;
        }
        if(_harmony == null)
            _harmony = Harmony.CreateAndPatchAll(typeof(GTTODUF), ID);
        else _harmony.PatchAll(typeof(GTTODUF));
        Logger.LogInfo($"Loaded [{Name} {Version}]");
        _modSingleton = this;

    }

    private void OnDestroy() {
        _harmony?.UnpatchSelf();
        _harmony = null;
        _manager?.Revert();
        _manager = null;
        Logger.LogInfo($"Destroyed {Name}");
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
    public static bool gttoduf_UpdatePatch(ac_CharacterController __instance) {
        _modSingleton.CreateManager(__instance).Update();
        return false;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "FixedUpdate"), HarmonyPrefix]
    public static bool gttoduf_FixedUpdatePatch(ac_CharacterController __instance) {
        _modSingleton.CreateManager(__instance).FixedUpdate();
        if(Input.GetKeyDown(KeyCode.F4)) {
            _modSingleton.enabled = !_modSingleton.enabled;
            GameManager.GM.GetComponent<GTTOD_HUD>().BigTextPopUp($"{(_modSingleton.enabled ? "Unfucked" : "Re-fucked")} GTTOD", 0);
        }
        return false;
    }
}