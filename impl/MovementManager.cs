
using System;
using System.Collections.Generic;
using EZCameraShake;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.UIElements;
using UnityEngine.UIElements.Experimental;
using static ac_CharacterController;

namespace gttoduf.impl;

public class MovementManager(GTTODUF mod, ac_CharacterController controller) {

    private const float _slopeAngle = 65f;
    private const float _dashDistance = 15f;

    public ac_CharacterController Controller { get; private set; } = controller;
    private GTTODUF _mod = mod;

    private Vector3 _wishdir;
    private Vector3 _wishdirRotated;
    private Vector3 _velocity;
    private Vector3 _dashEndpoint;
    private bool _jumpLockout = false;
    public bool HasAirjump = true;


    public Rigidbody RB => Controller.PlayerPhysics;
    public Vector3 CenterMass => Controller.PlayerCollider.transform.position + Controller.PlayerCollider.center;
    public Vector3 BottomSurface => Controller.PlayerCollider.transform.position + new Vector3(0, -Controller.PlayerCollider.height / 1.9f, 0);
    public Vector3 FloorExtents => new(Controller.PlayerCollider.radius, Controller.PlayerCollider.radius, Controller.PlayerCollider.radius);
    public Vector3 BodyExtents => new(Controller.PlayerCollider.radius, Controller.PlayerCollider.height / 2f, Controller.PlayerCollider.radius);
    public float FloorCastDistance => Controller.PlayerCollider.height / 2f;

    public Intention Grounded = new();
    public Intention Jumping = new();
    public Intention Sliding = new();
    public Intention Crouching = new();
    public Intention Dashing = new();

    public float Speed => _velocity.magnitude;
    public float XZSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
    public float YSpeed => _velocity.y;

    private Vector3 _cameraZ; // x = stand, y = crouch, z = impulse

    public void Apply() {
        _cameraZ = new(Controller.CameraParent.localPosition.y, Controller.CameraParent.localPosition.y - 1f, 1);
        GameObject playerObjects = Controller.transform.parent.gameObject;
        Transform fuckYou = playerObjects?.transform.Find("WallrunObjects");
        fuckYou?.gameObject.SetActive(false);
        Controller.DashCount = (int)GameManager.GM.StoredVariables.PlayerManager.FindModifier("AGILITY", "THRUSTER COIL").GetCurrentModifier();
        Controller.CameraManager.ActivateCameras(Controller.transform);
        Controller.CameraParent = Controller.CameraAnimation.transform;
        Controller.PlayerPhysics = Controller.GetComponent<Rigidbody>();
        Controller.Obstacle = Controller.GetComponent<NavMeshObstacle>();
        Controller.PlayerCollider = Controller.GetComponent<CapsuleCollider>();
        UnityEngine.Cursor.visible = false;
        UnityEngine.Cursor.lockState = CursorLockMode.Locked;
        Controller.Active = true;
    }

    public void Revert() {
        GameObject playerObjects = Controller.transform.parent.gameObject;
        Transform fuckYou = playerObjects?.transform.Find("WallrunObjects");
        fuckYou?.gameObject.SetActive(true);
        Controller = null;
        _mod = null;
    }

    private void OnHitGround() {
        JumpMovementShake(6.5f);
        Controller.PlayGlobalSoundEffect(2);
        RefundGroundedState(true);
    }

    private void OnSlideEnter() {
        Controller.PlayGlobalSoundEffect(5);
        Controller.PlayerBody.Recoil(new Vector3(0f, 0.2f, -0.2f), Vector3.zero, 5f, 20f);
    }

    private void OnSlideExit() {
    }

    public void Update() {
        TryReset();
        // dubious controller support since wishdir is normalized
        _wishdir = new Vector3(-GetLeftRight(), 0, GetForwardBack()).normalized;

        // ugh
        Controller.XCameraRotation = Mathf.Clamp(Controller.XCameraRotation -= (Input.GetAxis("Mouse Y") + Controller.GetProcessedLookInput("Controller Y", ref Controller.verticalTimer)) * (GameManager.GM.Settings.SavedSettings.VerticalSensitivity * Controller.SensitivityModifier) * (float)((!Controller.InvertLookAxis) ? 1 : (-1)), -90, 90);
        Controller.YCameraRotation += (Input.GetAxis("Mouse X") + Controller.GetProcessedLookInput("Controller X", ref Controller.horizontalTimer)) * (GameManager.GM.Settings.SavedSettings.HorizontalSensitivity * Controller.SensitivityModifier) * (float)((!(Controller.XCameraRotation > 180f) && !(Controller.XCameraRotation < -180f)) ? 1 : (-1));
        Controller.CameraParent.localRotation = Quaternion.Euler(Controller.XCameraRotation, 0f, Controller.ZCameraRotation);
        Controller.transform.rotation = Quaternion.Euler(Controller.transform.rotation.x, Controller.YCameraRotation, Controller.transform.rotation.z);

        _wishdirRotated = Controller.transform.rotation * _wishdir;
        if(!_jumpLockout) Jumping.SetTryingIfNotDoing(KeyBindingManager.ActionPressed(KeyAction.Jump));
        else Jumping.SetTryingIfNotDoing(false);
        Crouching.SetTrying(KeyBindingManager.ActionPressed(KeyAction.Crouch));
        Dashing.SetTrying(!Crouching.IsExpected() && KeyBindingManager.ActionPressed(KeyAction.Dash));
        UpdateHeadZ(Crouching.Doing ? 1.6f : 0, Sliding ? 20f : 8f);
        PatchVanillaCC();
    }

    public void FixedUpdate() {
        /**
            By ingesting the velocity from the previous rigidbody update, we can basically just pretend 
            that we're doing the kinematic collisions since the velocity is being altered by the rigidbody
            when collision events are handled by it.
        **/
        if(!RB.isKinematic)
            _velocity = RB.velocity;
        FixupRigidbody();
        InspectGround();
        if(Dashing.StateTicks >= 0 && Dashing.IsExpected()) {
            if(EvaluateDashing()) return;
        } else if(Dashing.StateTicks < 0) Dashing.TickUp();
        if(Grounded) MoveOnGround();
        else MoveInAir();
        Jumping.SetTrying(false);
        Crouching.SetTrying(false);
        if(Jumping) Jumping.TickUp();
        else Jumping.ResetTicks();
        RB.velocity = _velocity;
        DebugAll();
    }

    /// <summary>
    /// Ensures that when the character is grounded, they're actually touching the ground and basically glued to it
    /// so you don't get weird ramping behaviours (usually felt as randomly speeding up or slowing down slightly) 
    /// when walking slowly up/down things or near edges.
    /// Critically, this is done with a box collider the size of the capsule's radius, and not the capsule itself, 
    /// so that you can't slowly slide off of thin stuff. It makes walking along edges much more predictable. 
    /// The consequence of this is that you can get quite far off the edges of things before you're no longer 
    /// considered to be standing on them, since the box that's cast is axis-aligned.
    /// </summary>
    private void InspectGround() {
        if(!Grounded && YSpeed > 0.1f) return;
        bool hit = Physics.BoxCast(CenterMass, FloorExtents, Vector3.down, out Controller.GroundCheck, Quaternion.identity, FloorCastDistance);
        float distance = (Vector3.Distance(CenterMass, BottomSurface)) * 0.90f;
        if(!hit || Controller.GroundCheck.distance > distance || Vector3.Angle(Vector3.up, Controller.GroundCheck.normal) > _slopeAngle) {
            Grounded.SetDoing(false);
            return;
        }
        if(!Grounded) OnHitGround();
        Grounded.SetDoing(true);
        if(Jumping.IsExpected()) return;
        if(Grounded.StateTicks > -100 && Grounded.StateTicks < 0) return;
        Vector3 diff = Controller.GroundCheck.point - BottomSurface;
        // we project the positional offset along the gravity normal (in this case, just down) to remove any 
        // adverse translation when hitting mesh colliders or slightly sloped surfaces, since the ground
        // contact is not likely to be perfectly aligned directly underneath the player
        Vector3 projected = Vector3.Dot(diff, Vector3.down) * Vector3.down;
        float fraction = Controller.GroundCheck.distance / FloorCastDistance;
        if((fraction > 0f && fraction < 1f)) RB.position += projected;
        RB.velocity = new(RB.velocity.x, 0, RB.velocity.z);
        _velocity.y = 0;
    }

    private void MoveOnGround() {
        HasAirjump = true;
        Jumping.SetDoing(false);
        if(Grounded.StateTicks < 0)
            Grounded.ResetTicks();
        else Grounded.TickUp();
        if(Jumping.IsTryingButNotDoing()) {
            _velocity.y = 20;
            Jumping.SetDoing(true);
            Jumping.SetTrying(false);
            Grounded.SetTryingAndDoing(false);
            JumpMovementShake(7f);
            Controller.PlayGlobalSoundEffect(0);
            Controller.CurrentDashCount = Controller.DashCount;
            return;
        }
        if(Crouching.IsTryingButNotDoing()) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 3f * Controller.BodyVariables.SizeModifier);
            Controller.CameraAnimation.SetTrigger("Crouch");
            Controller.PlayGlobalSoundEffect(3);
            Controller.PlayerBody.Recoil(new Vector3(-0.1f, 0.1f, 0f), Vector3.zero, 3f, 8f);
        } else if(Crouching.IsDoingButNotTrying()) {
            JumpMovementShake(7f, 0.4f);
            Crouching.SetDoing(false);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
            Controller.PlayGlobalSoundEffect(3);
            Controller.PlayerBody.Recoil(new Vector3(-0.1f, 0f, 0f), Vector3.zero, 3f, 8f);
        }
        if(Sliding && Sliding.StateTicks >= 0) {
            EvaluateSliding();
            return;
        } else if(Sliding.StateTicks < 0) Sliding.TickUp();
        if(Crouching.Trying && Speed > 15)
            Sliding.SetTryingAndDoing(true);
        if(Crouching) ApplyAcceleration(15, 6);
        else ApplyAcceleration(35, 8);
        ApplyFrictionXZ(14f);
    }

    private void MoveInAir() {
        Jumping.SetDoing(false);
        Sliding.ResetTicks();
        if(Grounded.StateTicks > 0)
            Grounded.ResetTicks();
        else Grounded.TickDown();
        if(Grounded.StateTicks < -25 && Jumping.Trying && HasAirjump) {
            _velocity.y = 23;
            Jumping.SetDoing(true);
            Jumping.SetTrying(false);
            Grounded.SetTryingAndDoing(false);
            HasAirjump = false;
            JumpMovementShake(13f);
            Controller.PlayGlobalSoundEffect(1);
        }
        if(Crouching.IsTryingButNotDoing()) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 3f * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.IsDoingButNotTrying()) {
            Crouching.SetDoing(false);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
        }
        ApplyAcceleration(20, Crouching ? 4 : 3);
        if(XZSpeed > 30 && _wishdir.magnitude < 0.3)
            ApplyFrictionXZ(0.05f);
        else ApplyFrictionXZ(0.5f);
        ApplyGravity(40);
    }

    private void EvaluateSliding() {
        if(Sliding.StateTicks <= 0) OnSlideEnter();
        Sliding.TickUp();
        ApplyAcceleration(30, 0.01f);
        if(RB.velocity.y > 0.25f) {
            Sliding.TickUp(15);
            ApplyFrictionXZ(Sliding.StateTicks * 0.5f);
        } else if(YSpeed < -0.25) {
            Sliding.TickDown(Sliding.StateTicks - 15);
            ApplyFrictionXZ(0.01f);
        } else ApplyFrictionXZ(1f);
        if(Sliding.StateTicks < 4 && XZSpeed < 60)
            _velocity += (_velocity.normalized * 8f);
        if(!Crouching.IsExpected() || Speed < 15f) {
            Sliding.SetDoing(false);
            Sliding.Reset();
            Sliding.TickDown(30);
            OnSlideExit();
        }
    }

    private bool EvaluateDashing() {
        if(Dashing.IsTryingButNotDoing()) {
            if(Grounded.StateTicks > -15 || Controller.CurrentDashCount <= 0) {
                Dashing.SetTryingAndDoing(false);
                Dashing.ResetTicks();
                Dashing.TickDown(15);
                return false;
            }
            ForceWishdirForward();
            bool hit = Physics.BoxCast(CenterMass - _wishdirRotated, FloorExtents * 0.7f, _wishdirRotated, out Controller.DashCheck, Quaternion.identity, _dashDistance, ~(1 << 8));
            if(hit) {
                if(Controller.DashCheck.distance < Controller.PlayerCollider.radius * 4f) {
                    Dashing.SetTryingAndDoing(false);
                    Dashing.ResetTicks();
                    Dashing.TickDown(15);
                    return false;
                }
                _dashEndpoint = Vector3.Lerp(CenterMass, Controller.DashCheck.point, 0.87f);
            } else _dashEndpoint = RB.position + (_wishdirRotated * _dashDistance);
            float mag = _velocity.magnitude;
            _velocity = _wishdirRotated * mag;
            if(XZSpeed < 60) _velocity += (_velocity.normalized * 8f);
            RefundGroundedState();
            Controller.CurrentDashCount--;
            PlayDashEffects();
        }
        Controller.PlayerPhysics.isKinematic = true;
        Dashing.TickUp();
        Dashing.SetDoing();
        RB.position = Vector3.MoveTowards(RB.position, _dashEndpoint, Time.fixedDeltaTime * 250);
        if(Vector3.Distance(RB.position, _dashEndpoint) <= Controller.PlayerCollider.radius) {
            Dashing.SetTryingAndDoing(false);
            Dashing.ResetTicks();
            Dashing.TickDown(15);
            Controller.PlayerPhysics.isKinematic = false;
            _velocity.y = 0;
            RB.velocity = _velocity;
        }
        return true;
    }

    /// <summary>
    /// Callable by other movement objects (like people cannons, monkey bars, poles, dash points, etc)
    /// to reset airjumps, dashes, and sliding frames.
    /// </summary>
    public void RefundGroundedState(bool includeDashes = false) {
        HasAirjump = true;
        if(Sliding.StateTicks > 0) Sliding.ResetTicks();
        if(includeDashes) Controller.CurrentDashCount = Controller.DashCount;
    }

    /// <summary>
    /// Assigns the wishdir to the forward direction only if
    /// the wishdir is zero. This is useful for movement mechanics
    /// (mostly dashing) that should default to forward if the player
    /// isn't providing any directional input
    /// </summary>
    private void ForceWishdirForward() {
        if(_wishdir.magnitude > 0.4f) return;
        _wishdir = new(0, 0, 1);
        _wishdirRotated = Controller.transform.rotation * _wishdir;
    }

    private void ApplyAcceleration(in float wishspeed, in float accel) {
        if(_wishdirRotated.magnitude <= 0.001f) return;
        float projected = Vector3.Dot(_velocity, _wishdirRotated);
        float addedspeed = wishspeed - projected;
        if(addedspeed <= 0) return;
        float accelspeed = accel * Time.fixedDeltaTime * wishspeed;
        if(accelspeed > addedspeed) accelspeed = addedspeed;
        _velocity += _wishdirRotated * accelspeed;
    }

    private void ApplyGravity(float strength) {
        _velocity = new Vector3(_velocity.x, _velocity.y - (strength * Time.fixedDeltaTime), _velocity.z);
    }

    private void ApplyFrictionXZ(in float strength) {
        float y = _velocity.y;
        ApplyFriction(strength);
        _velocity.y = y;
    }


    private void ApplyFriction(in float strength) {
        float speed = _velocity.magnitude;
        if(speed < 0.0005f) {
            _velocity = Vector3.zero;
            return;
        }
        float friction = speed * Time.fixedDeltaTime * strength;
        float diff = Mathf.Max(0, speed - friction);
        if(diff != speed) {
            diff /= speed;
            _velocity *= diff;
        }
    }

    /// <summary>
    /// Updates variables to the actual ac_CharacterController
    /// so that other aspects of the game (like viewmodel animations) 
    /// respond properly to locomotion changes
    /// </summary>
    private void PatchVanillaCC() {
        Controller.Crouching = Crouching.Doing;
        Controller.Walking = Grounded && _wishdir.magnitude > 0.1;
        if(Grounded) {
            Controller.CharacterGroundState = GroundState.SteadyGround;
        } else Controller.CharacterGroundState = GroundState.InAir;
    }

    /// <summary>
    /// Messes with the cc's standard rigidbody so that the kinematics can be applied without the game fucking with stuff.
    /// Some of this probably doesn't need to be called each frame but I don't know what objects may attempt to
    /// update these values elsewhere.
    /// </summary>
    private void FixupRigidbody() {
        RB.mass = 0; RB.drag = 0; RB.angularDrag = 0; RB.useGravity = false;
        RB.angularVelocity = Vector3.zero;
        RB.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        RB.interpolation = RigidbodyInterpolation.Interpolate;
        RB.constraints = RigidbodyConstraints.FreezeRotation;
        // unfortunate but i dont want to write my own collision detection from scratch again
        RB.isKinematic = false;
    }

    private void UpdateHeadZ(float target, float speed) {
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
        float currentHeight = Vector3.Dot(Controller.CameraParent.localPosition, Vector3.up);
        _cameraZ.z = Mathf.Approximately(_cameraZ.z, 0) ? 0 : Mathf.Lerp(_cameraZ.z, 0f, t);
        float targetZ = (_cameraZ.x - _cameraZ.y) * (1 - target) + _cameraZ.z;
        float newHeight = Mathf.Lerp(currentHeight, targetZ, t);
        Controller.CameraParent.localPosition = new Vector3(0, newHeight, 0);
    }

    private void ResizeCollider(float newHeight) {
        // Vector3 center = Controller.PlayerCollider.center;
        // float prevHeight = Controller.PlayerCollider.height;
        // float delta = newHeight - prevHeight;
        // center.y += delta * 0.5f;
        // Controller.PlayerCollider.center = center;
        // Controller.PlayerCollider.height = newHeight;
    }

    private void VerySmallMovementShake() {
        CameraShaker.Instance.DefaultPosInfluence = Vector3.zero;
        CameraShaker.Instance.DefaultRotInfluence = new Vector3(-10, 0, 0);
        CameraShaker.Instance.ShakeOnce(6f, 0.5f, 0.1f, 1f);
        CameraShaker.Instance.ResetCamera();
    }

    private void JumpMovementShake(in float mag, in float inTime = 0.2f) {
        CameraShaker.Instance.DefaultPosInfluence = Vector3.zero;
        CameraShaker.Instance.DefaultRotInfluence = new Vector3(-10, 0, 0);
        CameraShaker.Instance.ShakeOnce(mag, 0.7f, inTime, 1f);
        CameraShaker.Instance.ResetCamera();
        Controller.PlayerBody.Recoil(new Vector3(0f, 0.05f, 0f), Vector3.zero, 5f, 8f);
        Controller.PlayerBody.Recoil(Vector3.zero, new Vector3(4, 6, 0), 2f, 6f);
    }

    private void PlayDashEffects() {
        Controller.PlayGlobalSoundEffect(Controller.WorldStep ? 9 : 6);
        Controller.Effects.BumpDistortion(-25f, 150f);
        Controller.Health.SetInvulnerability(0.15f);
        Controller.Effects.BumpDash(1f, 25f);
        Controller.MovementShake();
        Controller.PlayerBody.Recoil(new Vector3(_wishdir.x * 0.08f, 0.03f, _wishdir.z * 0.2f), Vector3.zero, 5f, 8f);
    }

    private void DebugAll() {
        _mod.Log("________________________________");
        _mod.Log("Vel: " + _velocity);
        _mod.Log("XZ: " + XZSpeed + " Y: " + YSpeed);
        _mod.Log("WISHDIR: " + _wishdir);
        _mod.Log("WISHDIR_ROT: " + _wishdirRotated);
        _mod.Log("Grounded: " + Grounded + ", Plane: " + Controller.GroundCheck.normal);
        _mod.Log("Jumping: " + Jumping + " (lockout: " + _jumpLockout + ")");
        _mod.Log("HasAirjump: " + HasAirjump);
        _mod.Log("Crouching: " + Crouching);
        _mod.Log("Sliding: " + Sliding);
        _mod.Log("Dashing: " + Dashing);
        _mod.Log("BS: " + BottomSurface);
    }

    private float GetForwardBack() {
        return Input.GetAxis("Vertical") + (KeyBindingManager.ActionPressed(KeyAction.WalkForward) ? 1
            : (KeyBindingManager.ActionPressed(KeyAction.WalkBackward) ? (-1) : 0));
    }

    private float GetLeftRight() {
        return -Input.GetAxis("Horizontal") + (KeyBindingManager.ActionPressed(KeyAction.StrafeLeft) ? 1
            : (KeyBindingManager.ActionPressed(KeyAction.StrafeRight) ? (-1) : 0));
    }

    private void TryReset() {
        if(Input.GetKey(KeyCode.F2)) {
            _velocity = Vector3.zero;
            RB.velocity = Vector3.zero;
            Controller.transform.position = new Vector3(0, 0, 0);
        }
    }
}
