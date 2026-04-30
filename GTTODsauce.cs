using System;
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
    public Intention Applied = new(true);

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

    public void Toggle(ac_CharacterController controller) {
        Applied.SetDoing(!Applied.Doing);
        if(!Applied) {
            _manager.Revert();
            controller.ActivateCharacter();
        } else {
            controller.ActivateCharacter();
            _manager.Apply(this, controller);
        }
    }

    [HarmonyPatch(typeof(ac_CharacterController), "Update"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_Update(ac_CharacterController __instance) {
        if(_modSingleton == null || !_modSingleton.Applied) return true;
        if(_modSingleton._manager == null) {
            _modSingleton._manager = new();
            _modSingleton._manager.Apply(_modSingleton, __instance);
        }
        _modSingleton._manager.Update();
        return false;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "FixedUpdate"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_FixedUpdate(ac_CharacterController __instance) {
        if(_modSingleton == null) return true;
        if(_modSingleton.Applied.Ticks <= 0) _modSingleton.Applied.Tick();
        if(Input.GetKey(KeyCode.F4) && _modSingleton.Applied.Ticks >= 0) {
            _modSingleton.Applied.Tick(-60);
            _modSingleton.Toggle(__instance);
            GameManager.GM.HUD.ObjectivePopUp($"{(_modSingleton.Applied ? "sauced" : "de-sauced")}", "");
        }
        if(!_modSingleton.Applied) return true;
        if(_modSingleton._manager == null) {
            _modSingleton._manager = new();
            _modSingleton._manager.Apply(_modSingleton, __instance);
        }
        _modSingleton._manager.FixedUpdate();
        return false;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "LateUpdate"), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_LateUpdate(ac_CharacterController __instance) {
        // the mod doesn't need this
        return _modSingleton != null && !_modSingleton.Applied;
    }

    [HarmonyPatch(typeof(ac_CharacterController), "Swim", typeof(bool)), HarmonyPrefix]
    public static bool GTTODSauce_ac_CharacterController_Swim(ac_CharacterController __instance, bool SwimmingState) {
        if(_modSingleton == null || !_modSingleton.Applied) return true;
        _modSingleton._manager.Swimming = SwimmingState && __instance.Movement.CanSwim;
        return true;
    }

    [HarmonyPatch(typeof(LandCannon), "Launch"), HarmonyPrefix]
    public static bool GTTODSauce_LandCannon_Launch(LandCannon __instance) {
        if(_modSingleton == null || !_modSingleton.Applied) return true;
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

    [HarmonyPatch(typeof(GTTOD_BalancePole), "LateUpdate"), HarmonyPostfix]
    public static void GTTODSauce_BalancePole_LateUpdate(GTTOD_BalancePole __instance) {
        if(_modSingleton == null || !_modSingleton.Applied || !__instance.Launching) return;
        _modSingleton?._manager?.RefundAirjump();
        _modSingleton?._manager?.RefundDashes();
    }



    // miscellaneous patches just to remove camera shake from areas that I can't control in the character controller itself

    [HarmonyPatch(typeof(GTTOD_PlayerBody), "Footstep"), HarmonyPrefix]
    public static bool GTTODSauce_GTTOD_PlayerBody_Footstep(GTTOD_PlayerBody __instance) {
        if(_modSingleton == null || !_modSingleton.Applied) return true;
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
        if(_modSingleton == null || !_modSingleton.Applied) return true;
        __instance.SuddenStop.GetComponent<AudioRange>().PlayLocalAudioRange();
        __instance.SuddenStop.Play();
        return false;
    }


    // these rigidbody patches fix the issue where any amount of external force will throw
    // doorguy so far out of the map that you softlock

    [HarmonyPatch(typeof(Rigidbody), "AddForce", [typeof(Vector3), typeof(ForceMode)]), HarmonyPrefix]
    public static bool GTTODSauce_Rigidbody_AddForce_A(Rigidbody __instance, Vector3 force, ForceMode mode) {
        if(_modSingleton == null || !_modSingleton.Applied) return true;
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
        if(_modSingleton == null || !_modSingleton.Applied) return true;
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