using System;
using BepInEx;
using EZCameraShake;
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

    [HarmonyPatch(typeof(ac_CharacterController), "ActivateCharacter"), HarmonyPostfix]
    public static void gttoduf_ac_CharacterController_ActivateCharacter(ac_CharacterController __instance) {
        _modSingleton.CreateManager(__instance);
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

    [HarmonyPatch(typeof(ac_CharacterController), "LateUpdate"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_LateUpdate(ac_CharacterController __instance) {
        // the mod doesn't need this
        return false;
    }

    [HarmonyPatch(typeof(LandCannon), "Launch"), HarmonyPrefix]
    public static bool GTTODSauce_LandCannon_Launch(LandCannon __instance) {
        _modSingleton._manager.EnsureAirtime();
        _modSingleton._manager.JumpMovementShake(10f);
        _modSingleton._manager.RefundAirjump();
        _modSingleton._manager.RefundDashes();
        _modSingleton._manager.RefundWallruns(true);
        _modSingleton._manager.Grounded.SetTryingAndDoing(false);
        _modSingleton._manager.ApplyImpulse(
            __instance.transform.up * __instance.UpForce +
            __instance.transform.forward * __instance.ForwardForce,
            !__instance.KillMomentum, true
        );
        __instance.Audio.PlayLocalAudioRange();
        __instance.StartCoroutine(__instance.LaunchCooldown());
        return false;
    }

    // miscellaneous patches just to remove camera shake from areas that I can't control in the character controller itself

    [HarmonyPatch(typeof(GTTOD_PlayerBody), "Footstep"), HarmonyPrefix]
    public static bool GTTODSauce_GTTOD_PlayerBody_Footstep(GTTOD_PlayerBody __instance) {
        if(__instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.InAir
            || (__instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.Swimming
            || __instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.Climbing
            || ((__instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.SteadyGround
            || __instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.Grounded)
            && !__instance.CharacterController.Walking)))
            return false;
        // the same implementation as vanilla but I've removed the camera shaking stuff here
        int index = 0;
        string text = (Physics.Raycast(__instance.transform.position, Vector3.down, out RaycastHit hitInfo, __instance.CharacterController.BodyVariables.ColliderHeight, __instance.FootstepLayerMask) ? hitInfo.collider.tag : (Physics.Raycast(__instance.transform.position, Vector3.down, out RaycastHit hitInfo2, __instance.CharacterController.BodyVariables.ColliderHeight, __instance.DefaultLayerMask) ? hitInfo2.collider.tag : "Default"));
        foreach(FootstepsCategory footstep in __instance.Footsteps) {
            if(text == footstep.FootstepName) {
                index = __instance.Footsteps.IndexOf(footstep);
                break;
            }
        }

        AnimationSFX audioToPlay = (((__instance.CharacterController.Crouching || __instance.CharacterController.AuraFarming || __instance.CharacterController.CharacterGroundState == ac_CharacterController.GroundState.Onwall) && __instance.Footsteps[index].HasSoftFootsteps) ? __instance.Footsteps[index].SoftFootstepSFX : __instance.Footsteps[index].FootstepSFX);
        __instance.Audio.SetAudioRange(audioToPlay);
        return false;
    }

    [HarmonyPatch(typeof(PlayerEffects), "PlaySuddenStop"), HarmonyPrefix]
    public static bool GTTODSauce_GTTOD_PlayerEffects_PlaySuddenStop(PlayerEffects __instance) {
        __instance.SuddenStop.GetComponent<AudioRange>().PlayLocalAudioRange();
        __instance.SuddenStop.Play();
        return false;
    }


    // these rigidbody patches fix the issue where any amount of external force will throw
    // doorguy so far out of the map that you softlock

    [HarmonyPatch(typeof(Rigidbody), "AddForce", [typeof(Vector3), typeof(ForceMode)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddForce_A(Rigidbody __instance, Vector3 force, ForceMode mode) {
        if(!__instance.Equals(_modSingleton?._manager?.RB)) return true;
        _modSingleton._manager.ApplyImpulse(force * 0.001f, true, false);
        return false;
    }

    [HarmonyPatch(typeof(Rigidbody), "AddForce", [typeof(Vector3)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddForce_B(Rigidbody __instance, Vector3 force) {
        return GTTODSauce_Rigidbody_AddForce_A(__instance, force, ForceMode.Force);
    }

    [HarmonyPatch(typeof(Rigidbody), "AddForce", [typeof(float), typeof(float), typeof(float), typeof(ForceMode)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddForce_C(Rigidbody __instance, float x, float y, float z, ForceMode mode) {
        return GTTODSauce_Rigidbody_AddForce_A(__instance, new Vector3(x, y, z), ForceMode.Force);
    }

    [HarmonyPatch(typeof(Rigidbody), "AddForce", [typeof(float), typeof(float), typeof(float), typeof(ForceMode)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddForce_D(Rigidbody __instance, float x, float y, float z) {
        return GTTODSauce_Rigidbody_AddForce_C(__instance, x, y, z, ForceMode.Force);
    }

    [HarmonyPatch(typeof(Rigidbody), "AddExplosionForce", [typeof(float), typeof(Vector3), typeof(float), typeof(float), typeof(ForceMode)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddExplosionForce_A(Rigidbody __instance, float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier, ForceMode mode) {
        if(!__instance.Equals(_modSingleton?._manager?.RB)) return true;
        MovementManager body = _modSingleton._manager;
        if(body == null) return false;
        Vector3 forceDir = (body.CenterMass - explosionPosition);
        float distance = forceDir.magnitude;
        forceDir.y += upwardsModifier;
        forceDir = (forceDir.normalized / Mathf.Max(0.1f, distance)) * explosionForce;
        body.ApplyImpulse(forceDir.normalized, true, false);
        return false;
    }

    [HarmonyPatch(typeof(Rigidbody), "AddExplosionForce", [typeof(float), typeof(Vector3), typeof(float), typeof(float)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddExplosionForce_B(Rigidbody __instance, float explosionForce, Vector3 explosionPosition, float explosionRadius, float upwardsModifier) {
        return GTTODSauce_Rigidbody_AddExplosionForce_A(__instance, explosionForce, explosionPosition, explosionRadius, upwardsModifier, ForceMode.Force);
    }

    [HarmonyPatch(typeof(Rigidbody), "AddExplosionForce", [typeof(float), typeof(Vector3), typeof(float)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddExplosionForce_C(Rigidbody __instance, float explosionForce, Vector3 explosionPosition, float explosionRadius) {
        return GTTODSauce_Rigidbody_AddExplosionForce_A(__instance, explosionForce, explosionPosition, explosionRadius, 0, ForceMode.Force);
    }
}