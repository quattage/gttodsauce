
using System;
using EZCameraShake;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.TextCore.Text;
using UnityEngine.UIElements;
using static ac_CharacterController;

namespace gttoduf.impl;

public class MovementManager(GTTODUF mod, ac_CharacterController controller) {

    private const float _slopeAngle = 65f;
    private const float _wallAngle = 42f;
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
    public Vector3 BottomSurface => CenterMass + new Vector3(0, -Controller.PlayerCollider.height / 1.9f, 0);
    public Vector3 TopSurface => CenterMass + new Vector3(0, Controller.PlayerCollider.height / 2f, 0);
    public Vector3 FloorExtents => new(Controller.PlayerCollider.radius, Controller.PlayerCollider.radius, Controller.PlayerCollider.radius);
    public Vector3 BodyExtents => new(Controller.PlayerCollider.radius, Controller.PlayerCollider.height / 2f, Controller.PlayerCollider.radius);
    public float FloorCastDistance => Controller.PlayerCollider.height / 2f;

    public Intention Grounded = new();
    public Intention Jumping = new();
    public Intention Sliding = new();
    public Intention Crouching = new();
    public Intention Dashing = new();
    public Intention Wallrunning = new();

    public bool IsWallrunningLeft => Wallrunning.Doing && Wallrunning.StateTicks < 0;
    public bool ISWallrunningRight => Wallrunning.Doing && Wallrunning.StateTicks > 0;

    public float Speed => _velocity.magnitude;
    public float XZSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
    public float YSpeed => _velocity.y;

    private Vector3 _cameraZ; // x = stand, y = crouch, z = impulse

    public void Apply() {
        _cameraZ = new(Controller.CameraParent.localPosition.y, Controller.CameraParent.localPosition.y - 1f, 1);
        GameObject playerObjects = Controller.transform.parent.gameObject;
        Transform fuckOffAndDie = playerObjects?.transform.Find("WallrunObjects");
        fuckOffAndDie?.gameObject.SetActive(false);
        Controller.CameraParent = Controller.CameraAnimation.transform;
        ConfigureEvents();
    }

    public void Revert() {
        GameObject playerObjects = Controller.transform.parent.gameObject;
        Transform eatShit = playerObjects?.transform.Find("WallrunObjects");
        eatShit?.gameObject.SetActive(true);
        Controller = null;
        _mod = null;
    }

    public void Update() {
        if(!_mod.enabled || Controller == null || !Controller.Active || Controller.PlayerManager.PlayerEngaged) return;
        TryReset();
        _wishdir = VectorExtentions.MakeWishdir();

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
        if(!_mod.enabled || Controller == null || !Controller.Active) return;
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
        float distance = Mathf.Abs(RB.position.y - BottomSurface.y);
        if(!hit || Controller.GroundCheck.distance > distance || Vector3.Angle(Vector3.up, Controller.GroundCheck.normal) > _slopeAngle) {
            Grounded.SetDoing(false);
            return;
        }
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

    /// <summary>
    /// While the player is touching the ground, they're able to slide, but not wallrun.
    /// Grounded movement receives much more friction and allows the player to accelerate
    /// faster so that it feels more controllable than moving in the air.
    /// </summary>
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
            return;
        }
        if(Crouching.IsTryingButNotDoing()) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 2.4f * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.IsDoingButNotTrying() && HasHeadroom()) {
            Crouching.SetDoing(false);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
        }
        if(Sliding && Sliding.StateTicks >= 0) {
            EvaluateSliding();
            return;
        } else if(Sliding.StateTicks < 0) Sliding.TickUp();
        if(Crouching.Trying && Speed > 15)
            Sliding.SetTryingAndDoing(true);
        if(Crouching) _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 15, 6);
        else _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 35, 8);
        _velocity = _velocity.ApplyFrictionXZ(14f);
    }

    /// <summary>
    /// All of the implementation for ingesting movement and adjusting velocity
    /// while the character isn't touching the ground. Acceleration and friction
    /// are greatly reduced here so that the player can drift around.
    /// </summary>
    private void MoveInAir() {
        if(EvaluateWallrun()) return;
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
        }
        if(Crouching.IsTryingButNotDoing()) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 2.4f * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.IsDoingButNotTrying() && HasHeadroom()) {
            Crouching.SetDoing(false);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
        }
        _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 20, Crouching ? 4 : 3);
        if(XZSpeed > 30 && _wishdir.magnitude < 0.3)
            _velocity = _velocity.ApplyFrictionXZ(0.05f);
        else _velocity = _velocity.ApplyFrictionXZ(0.5f);
        _velocity = _velocity.ApplyGravity();
    }

    /// <summary>
    /// Significantly reduces friction and acceleration so that the player's
    /// velocity is less controllable. Slopes will accelerate the player as a
    /// natural consequence of experiencing much less friction. Once the player
    /// crosses the minimum speed threshold, sliding is automatically disabled
    /// and the player is allowed to transition smoothly to a crouching state.
    /// </summary>
    private void EvaluateSliding() {
        Sliding.TickUp();
        _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 30, 0.01f);
        if(RB.velocity.y > 0.25f) {
            Sliding.TickUp(15);
            _velocity = _velocity.ApplyFrictionXZ(Sliding.StateTicks * 0.5f);
        } else if(RB.velocity.y < -0.25) {
            Sliding.TickDown(Sliding.StateTicks - 15);
            _velocity = _velocity.ApplyFrictionXZ(0.01f);
        } else _velocity = _velocity.ApplyFrictionXZ(1f);
        if(Sliding.StateTicks < 4 && XZSpeed < 60)
            _velocity += (_velocity.normalized * 8f);
        if(!Crouching.IsExpected() || Speed < 15f) {
            Sliding.SetDoing(false);
            Sliding.Reset();
            Sliding.TickDown(30);
        }
    }

    /// <summary>
    /// Dashes occur by casting the character's bounding box forward to ensure
    /// that the player can't dash through walls. Once a dash is triggered and a 
    /// dash destination is determined, the player is moved along a horizontal path
    /// and their velocity is adjusted to point in the dash direction.
    /// </summary>
    private bool EvaluateDashing() {
        if(Dashing.IsTryingButNotDoing()) {
            if(Grounded.StateTicks > -15 || Controller.CurrentDashCount <= 0) {
                Dashing.SetTryingAndDoing(false);
                Dashing.ResetTicks();
                Dashing.TickDown(15);
                return false;
            }
            DefaultForward();
            bool hit = Physics.BoxCast(CenterMass - _wishdirRotated,
                FloorExtents * 0.7f, _wishdirRotated, out Controller.DashCheck,
                Quaternion.identity, _dashDistance, ~(1 << 8)
            );
            if(hit) {
                if(Controller.DashCheck.distance < Controller.PlayerCollider.radius * 4f) {
                    Dashing.SetTryingAndDoing(false);
                    Dashing.ResetTicks();
                    Dashing.TickDown(15);
                    return false;
                }
                _dashEndpoint = Vector3.Lerp(CenterMass, Controller.DashCheck.point, 0.87f);
            } else _dashEndpoint = RB.position + (_wishdirRotated * _dashDistance);
            if(_wishdir.z >= -0.001) {
                float mag = _velocity.magnitude;
                _velocity = _wishdirRotated * mag;
                if(XZSpeed < 60) _velocity += (_velocity.normalized * 8f);
            } else _velocity = new Vector3(0, 0, 0);
            RefundGroundedState();
            Controller.CurrentDashCount--;
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

    private bool EvaluateWallrun() {
        if(Crouching.IsExpected()) return false;
        Vector3 displacement = new(0, (Controller.PlayerCollider.height / 2.1f) - Controller.PlayerCollider.radius, 0);
        Vector3 xzVelocity = new(_velocity.x, 0, _velocity.z);
        bool movingTowardsSurface = Physics.CapsuleCast(CenterMass + displacement,
            CenterMass - displacement, Controller.PlayerCollider.radius,
            xzVelocity.normalized, out RaycastHit prediction, xzVelocity.magnitude * 1.5f, ~(1 << 8)
        );
        if(!movingTowardsSurface) return false;
        if(prediction.distance > Controller.PlayerCollider.radius * 2.1f) return false;
        Vector3 wallNormal = prediction.normal;
        float angle = Vector3.Angle(Vector3.up, wallNormal);
        if(angle < (90 - _wallAngle) || angle > (90 + _wallAngle)) return false;
        float lookDiff = Vector3.Dot(Controller.transform.rotation * Vector3.forward, wallNormal);
        if(lookDiff < -0.8 || lookDiff > 0.8) return false;
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
    /// Returns true if the character has enough room to stand above their head while crouching.
    /// </summary>
    private bool HasHeadroom() {
        return !Physics.CheckBox(Controller.transform.position + new Vector3(0, Controller.BodyVariables.UncrouchHeight * 0.51f, 0), BodyExtents, Quaternion.identity, ~(1 << 8));
    }

    /// <summary>
    /// Aims the wishdir towards the direction the player is facing 
    /// only if the wishdir is zero. This is useful for movement 
    /// mechanics (mostly dashing) that should default to the
    /// forward direction if the player isn't providing any 
    /// directional input.
    /// </summary>
    private void DefaultForward() {
        if(_wishdir.magnitude > 0.4f) return;
        _wishdir = new(0, 0, 1);
        _wishdirRotated = Controller.transform.rotation * _wishdir;
    }

    /// <summary>
    /// Updates variables to the actual ac_CharacterController
    /// so that other aspects of the game (like viewmodel animations) 
    /// respond properly to locomotion changes.
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

    /// <summary>
    /// Interpolates the head position down/up based on the target % supplied.
    /// 1 -> crouch height, 0 -> stand height. This method is called at frame-time.
    /// </summary>
    private void UpdateHeadZ(float target, float speed) {
        float t = 1f - Mathf.Exp(-speed * Time.deltaTime);
        float currentHeight = Vector3.Dot(Controller.CameraParent.localPosition, Vector3.up);
        _cameraZ.z = Mathf.Approximately(_cameraZ.z, 0) ? 0 : Mathf.Lerp(_cameraZ.z, 0f, t);
        float targetZ = (_cameraZ.x - _cameraZ.y) * (1 - target) + _cameraZ.z;
        float newHeight = Mathf.Lerp(currentHeight, targetZ, t);
        Controller.CameraParent.localPosition = new Vector3(0, newHeight, 0);
    }

    /// <summary>
    /// Reduces the size of the player collider and applies a 
    /// corrective offset so that the collider shrinks relative 
    /// to its bottom surface instead of its center.
    /// </summary>
    private void ResizeCollider(float newHeight) {
        Vector3 center = Controller.PlayerCollider.center;
        float prevHeight = Controller.PlayerCollider.height;
        float delta = newHeight - prevHeight;
        center.y += delta * 0.5f;
        Controller.PlayerCollider.center = center;
        Controller.PlayerCollider.height = newHeight;
    }

    private void ConfigureEvents() {
        Grounded.EntryTrigger += () => {
            if(Grounded.StateTicks > -50) return;
            JumpMovementShake(6.5f);
            Controller.PlayGlobalSoundEffect(2);
            RefundGroundedState(true);
        };
        Jumping.EntryTrigger += () => {
            if(Grounded) {
                JumpMovementShake(7f);
                Controller.PlayGlobalSoundEffect(0);
            } else {
                JumpMovementShake(15f);
                Controller.PlayGlobalSoundEffect(1);
            }
            Controller.CurrentDashCount = Controller.DashCount;
        };
        Sliding.EntryTrigger += () => {
            Controller.PlayGlobalSoundEffect(5);
            Controller.PlayerBody.Recoil(new Vector3(0f, 0.2f, -0.2f), Vector3.zero, 5f, 20f);
        };
        Crouching.EntryTrigger += () => {
            Controller.CameraAnimation.SetTrigger("Crouch");
            Controller.PlayGlobalSoundEffect(3);
            Controller.PlayerBody.Recoil(new Vector3(-0.1f, 0.1f, 0f), Vector3.zero, 3f, 8f);
        };
        Crouching.ExitTrigger += () => {
            Controller.PlayGlobalSoundEffect(3);
            JumpMovementShake(7f);
            Controller.PlayerBody.Recoil(new Vector3(-0.1f, 0f, 0f), Vector3.zero, 3f, 8f);
        };
        Dashing.EntryTrigger += () => {
            Controller.PlayGlobalSoundEffect(Controller.WorldStep ? 9 : 6);
            Controller.Effects.BumpDistortion(-25f, 150f);
            Controller.Health.SetInvulnerability(0.15f);
            Controller.Effects.BumpDash(1f, 25f);
            Controller.MovementShake();
            Controller.PlayerBody.Recoil(new Vector3(_wishdir.x * 0.08f, 0.03f, _wishdir.z * 0.2f), Vector3.zero, 5f, 8f);
        };
        Wallrunning.EntryTrigger += () => { };
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

    private void TryReset() {
        if(Input.GetKey(KeyCode.F2)) {
            _velocity = Vector3.zero;
            RB.velocity = Vector3.zero;
            Controller.transform.position = new Vector3(0, 0, 0);
        }
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
        _mod.Log("FDT: " + Time.fixedDeltaTime);
    }
}
