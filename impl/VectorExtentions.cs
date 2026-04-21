


using UnityEngine;

namespace gttoduf.impl;

/// <summary>
/// A series of helper methods that have been pulled out of the MovementManager
/// class to make it easier to navigate and so that I can put some dense comments
/// here.
/// </summary>
public static class VectorExtentions {

    /// <summary>
    /// Damps the provided velocity vector by an aribtrary strength amount and
    /// returns a modified copy. The damping strength is scaled by fixed delta
    /// automatically.
    /// </summary>
    public static Vector3 ApplyFriction(this in Vector3 vector, in float strength) {
        float speed = vector.magnitude;
        if(speed < 0.0005f) return Vector3.zero;
        float friction = speed * Time.fixedDeltaTime * strength;
        float diff = Mathf.Max(0, speed - friction);
        if(diff != speed) {
            diff /= speed;
            return vector * diff;
        }
        return vector;
    }

    /// <summary>
    /// Damps the provided velocity vector by an aribtrary strength amount and
    /// returns a modified copy. The damping strength is scaled by fixed delta
    /// automatically. This method maintains the original Y value of the input
    /// vector so that friction is only applied to velocity along a horizontal
    /// plane. This is useful for me since it makes tuning air drag much easier 
    /// since gravity won't be damped by air friction.
    /// </summary>
    public static Vector3 ApplyFrictionXZ(this in Vector3 vector, in float strength) {
        float y = vector.y;
        Vector3 output = ApplyFriction(vector, strength);
        output.y = y;
        return output;
    }

    /// <summary>
    /// Reduces the Y value of the velocity vector by an arbitrary amount normalized against time.
    /// I have no idea 
    /// </summary>
    public static Vector3 ApplyGravity(this in Vector3 vector, float strength = 40) {
        return new Vector3(vector.x, vector.y - (strength * Time.fixedDeltaTime), vector.z);
    }

    /// <summary>
    /// Acceleration projection method which retains the classic over-correction bug from quake and source:
    /// https://github.com/ValveSoftware/source-sdk-2013/blob/master/mp/src/game/shared/gamemovement.cpp
    /// This is what makes airstrafing produce additional speed. Everyone say thank you to John Carmack.
    /// </summary>
    public static Vector3 ApplyAcceleration(this in Vector3 vector, Vector3 wishdir, in float wishspeed, in float accel) {
        if(wishdir.magnitude <= 0.001f) return vector;
        float projected = Vector3.Dot(vector, wishdir);
        float addedspeed = wishspeed - projected;
        if(addedspeed <= 0) return vector;
        float accelspeed = accel * Time.fixedDeltaTime * wishspeed;
        if(accelspeed > addedspeed) accelspeed = addedspeed;
        return vector + wishdir * accelspeed;
    }

    public static Vector3 MakeWishdir() {
        // dubious controller support since wishdir is normalized
        return new Vector3(-GetLeftRight(), 0, GetForwardBack()).normalized;
    }

    public static float GetForwardBack() {
        return Input.GetAxis("Vertical") + (KeyBindingManager.ActionPressed(KeyAction.WalkForward) ? 1
            : (KeyBindingManager.ActionPressed(KeyAction.WalkBackward) ? (-1) : 0));
    }

    public static float GetLeftRight() {
        return -Input.GetAxis("Horizontal") + (KeyBindingManager.ActionPressed(KeyAction.StrafeLeft) ? 1
            : (KeyBindingManager.ActionPressed(KeyAction.StrafeRight) ? (-1) : 0));
    }

}