
using System;
using EZCameraShake;
using UnityEngine;
using static ac_CharacterController;
using static GTTODSauce.impl.TraceHelpers;

namespace GTTODSauce.impl;

public class MovementManager(GTTODSauce mod, ac_CharacterController controller) {

    public ac_CharacterController Controller { get; private set; } = controller;
    private GTTODSauce _mod = mod;

    // replace these with MovementVariables and BodyVariables(?) where possible
    private const float _slopeAngle = 65f;
    private const float _wallAngle = 42f;
    private const float _stepHeight = 0.5f;
    private const float _dashDistance = 15f;
    private const byte _gracePeriod = 15;
    private const byte _wallSearchDistance = 8;
    private const bool _dumpState = false;

    // helpers
    public Rigidbody RB => Controller.PlayerPhysics;
    public CapsuleCollider Collider => Controller.PlayerCollider;
    public Vector3 CenterMass => Collider == null ? Vector3.zero : (Collider.transform.position + Collider.center);
    public Vector3 BottomSurface => CenterMass + new Vector3(0, -Collider.height / 1.9f, 0);
    public Vector3 TopSurface => CenterMass + new Vector3(0, Collider.height / 2f, 0);
    public Vector3 FloorExtents => new(Collider.radius, Collider.radius, Collider.radius);
    public Vector3 BodyExtents => new(Collider.radius, Collider.height / 2f, Collider.radius);
    public int PlayerMask => ~(1 << 8);
    public float FloorCastDistance => Collider.height / 2f;
    public float Speed => _velocity.magnitude;
    public float XZSpeed => new Vector2(_velocity.x, _velocity.z).magnitude;
    public float YSpeed => (RB.position.y - _prevY) / Time.fixedDeltaTime;
    private float _prevY;

    // pseudostatemachine
    public Intention Grounded = new();
    public bool Swimming = false;
    public Intention Jumping = new();
    public Intention Sliding = new();
    public Intention Crouching = new();
    public Intention Dashing = new();
    public Intention Wallrunning = new();

    // state variables
    public readonly WallContainer WallStuff = new();
    public bool HasAirjump { get; private set; } = true;
    private Vector3 _wishdir;
    private Vector3 _wishdirRotated;
    private Vector3 _velocity;
    private Vector3 _dashEndpoint;
    private Vector3 _cameraZ; // x = stand, y = crouch, z = impulse

    // called by the mod object when the code is injected
    public void Apply() {
        Update();
        FixedUpdate();
        _cameraZ = new(Controller.CameraParent.localPosition.y, Controller.CameraParent.localPosition.y - 1f, 1);
        if(Controller.PlayerCollider == null)
            Controller.PlayerCollider = Controller.GetComponent<CapsuleCollider>();
        WallStuff.SetupTransforms(Controller.transform, Controller.CameraParent);

        // this bit is REALLY important for some reason
        Collider.radius = Controller.BodyVariables.ColliderRadius / ((Controller.CharacterGroundState != GroundState.Onwall) ? 1 : 2) * Controller.BodyVariables.SizeModifier;
        ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);

        ConfigureEvents();
        _prevY = RB.position.y;
        _mod.Log("Applied GTTODSauce MovementManager");
    }

    // called by the mod object before its destroyed to clean things up
    public void Revert() {
        WallStuff.TakedownTransforms(Controller.transform, Controller.CameraParent);
        Controller = null;
        _mod.Log("Reverted GTTODSauce MovementManager");
        _mod = null;
    }

    public void RefundDashes() {
        Controller.CurrentDashCount = Controller.DashCount;
    }

    public void RefundAirjump() {
        HasAirjump = true;
    }

    public void RefundSliding() {
        if(Sliding.Ticks > 0) Sliding.ResetTicks();
    }

    public void RefundWallruns(bool forgiving) {
        WallStuff.Reset(forgiving);
    }

    /// <summary>
    /// Applies a sudden velocity change. Callable by other movement objects (like land cannons)
    /// to throw the player in some arbitrary direction
    /// </summary>
    /// <param name="impulse">The actual velocity</param>
    /// <param name="preserve">If false, the velocity of this CC will be replaced</param>
    /// <param name="zeroY">Whether or not to set the Y velocity to zero before applying the impulse</param>
    public void ApplyImpulse(Vector3 impulse, bool preserve = true, bool zeroY = false) {
        if(zeroY && _velocity.y < 0) _velocity = new(_velocity.x, 0, _velocity.z);
        if(preserve) _velocity += impulse;
        else _velocity = impulse;
        RB.velocity = _velocity;
    }

    /// <summary>
    /// Useful to guarantee that the CC isn't grounded so that any impulses you apply
    /// don't get swallowed by ground snapping or dashing
    /// </summary>
    public void EnsureAirtime() {
        Grounded.Reset();
        Grounded.Tick(-_gracePeriod);
        Dashing.Reset();
        Dashing.Tick(-_gracePeriod * 2);
        CancelWallrun();
        Crouching.Reset();
        Jumping.Reset();
    }

    public void Update() {
        if(ShouldPauseUpdates()) return;
        PatchVanillaCC();
        TryReset();
        _wishdir = VectorExtentions.MakeWishdir();
        _wishdirRotated = Controller.transform.rotation * _wishdir;
        if(!Jumping.Trying) Jumping.SetTrying(RisingEdgeJump());
        Crouching.SetTrying(KeyBindingManager.ActionPressed(KeyAction.Crouch));
        Dashing.SetTrying(!Crouching.Expected && KeyBindingManager.ActionPressed(KeyAction.Dash));
        UpdateHeadZ(Crouching.Doing ? 1.6f : 0, Sliding ? 20f : 8f);
        UpdateRotation();
        Controller.ControllerUpdate();
    }

    private void UpdateRotation(float clamp = 89.9f) {

        float deltaX = (Input.GetAxis("Mouse X") + Controller.GetProcessedLookInput("Controller X", ref Controller.horizontalTimer))
            * (GameManager.GM.Settings.SavedSettings.HorizontalSensitivity * Controller.SensitivityModifier);

        float deltaY = (Input.GetAxis("Mouse Y") + Controller.GetProcessedLookInput("Controller Y", ref Controller.verticalTimer))
            * (GameManager.GM.Settings.SavedSettings.VerticalSensitivity * Controller.SensitivityModifier) * ((!Controller.InvertLookAxis) ? 1 : (-1));

        Controller.XCameraRotation = Mathf.Clamp(Controller.XCameraRotation -= deltaY, -clamp, clamp);
        Controller.YCameraRotation += deltaX;

        Vector3 localUp = WallStuff.GetUpBasis(CenterMass, _wallSearchDistance, overrideCondition: Crouching.Doing);
        Vector3 forward = Vector3.ProjectOnPlane(Quaternion.Euler(0, Controller.YCameraRotation, 0) * Vector3.forward, localUp).normalized;
        Quaternion basis = Quaternion.LookRotation(forward, localUp);
        Quaternion targetRot = basis * Quaternion.Euler(Controller.XCameraRotation, 0, Controller.ZCameraRotation);
        Vector3 targetForward = targetRot * Vector3.forward;
        Quaternion yaw = Quaternion.LookRotation(Vector3.ProjectOnPlane(targetForward, localUp).normalized, localUp);

        Controller.transform.rotation = yaw;
        Controller.CameraParent.localRotation = Quaternion.Inverse(yaw) * targetRot;
    }

    public void FixedUpdate() {
        if(ShouldPauseUpdates()) return;
        /// ingesting the velocity from the previous rigidbody update cuz the 
        /// rigidbody is dynamic ("booooo dynamic rigidbody" SHUT UP!!!)
        if(!RB.isKinematic) _velocity = RB.velocity;

        if(Swimming) {
            MoveInWater();
            RB.velocity = _velocity;
            _prevY = RB.position.y;
            Jumping.SetTrying(false);
            Crouching.SetTrying(false);
            return;
        }

        FixupRigidbody();
        InspectWalls();
        InspectGround();

        if(Dashing.Ticks >= 0 && Dashing.Expected) {
            if(EvaluateDash()) return;
        } else if(Dashing.Ticks < 0) Dashing.Tick();

        if(Wallrunning.Ticks >= 0 && Wallrunning.Expected) {
            if(EvaluateWallrun()) return;
        } else if(Wallrunning.Ticks < 0) Wallrunning.Tick();

        if(Grounded) MoveOnGround();
        else MoveInAir();
        if(Jumping.Ticks < 0) Jumping.Tick();

        RB.velocity = _velocity;
        _prevY = RB.position.y;
        DumpState();
        Jumping.SetTrying(false);
        Crouching.SetTrying(false);
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
        if((Wallrunning.Ticks < 0 || !Grounded && YSpeed > 0.1f)) return;
        bool hit = Physics.BoxCast(CenterMass, FloorExtents, Vector3.down, out Controller.GroundCheck, Quaternion.identity, FloorCastDistance, ~(1 << 8));
        float distance = Mathf.Abs(RB.position.y - BottomSurface.y);
        if(!hit || Controller.GroundCheck.distance > distance || Vector3.Angle(Vector3.up, Controller.GroundCheck.normal) > _slopeAngle) {
            Grounded.SetDoing(false);
            return;
        }
        if(!Grounded) {
            RefundDashes();
            RefundAirjump();
            RefundSliding();
            RefundWallruns(true);
        }
        Grounded.SetDoing(true);
        if(Jumping.Expected) return;
        if(Grounded.Ticks > -_gracePeriod * 6 && Grounded.Ticks < 0) return;
        Vector3 diff = Controller.GroundCheck.point - BottomSurface;
        // we project the positional offset along the gravity normal (in this case, just down) to remove any 
        // adverse translation when hitting mesh colliders or slightly sloped surfaces, since the ground
        // contact is not likely to be perfectly aligned directly underneath the player
        Vector3 projected = Vector3.Dot(diff, Vector3.down) * Vector3.down;
        float fraction = Controller.GroundCheck.distance / FloorCastDistance;
        if((fraction > 0f && fraction < 1f)) RB.position += projected * 0.99f;
        RB.velocity = new(RB.velocity.x, 0, RB.velocity.z);
        _velocity.y = 0;
    }

    /// <summary>
    /// Casts the collider to where it will be a few frames later to detect if the player is headed towards
    /// valid wall surfaces.
    /// </summary>
    private void InspectWalls() {
        if(Wallrunning.Doing) return;
        if(Grounded || Jumping.Ticks < -_gracePeriod * 2) {
            CancelWallrun();
            return;
        }

        float intent = (_wishdir.magnitude * 2) + _velocity.XZ().magnitude;
        if(intent < 0.8) {
            CancelWallrun();
            return;
        }

        Quaternion look = Quaternion.Euler(0, Controller.YCameraRotation, 0);
        WallCandidate[] nearby = WallCandidate.Collect(
            CenterMass, Collider.height, Collider.radius,
            look * Vector3.forward,
            Controller.transform.right, _velocity
        );

        WallCandidate wall = WallCandidate.FindBest(nearby);
        if(!wall.IsRelevent(_velocity, _wallSearchDistance)
            || !wall.IsVertical(_wallAngle)
            || !wall.IsInView()
            || !wall.IsOpposingPrevious(WallStuff)) {
            CancelWallrun();
            return;
        }
        WallStuff.Prime(wall, look);
        Wallrunning.SetTrying(true);
    }

    /// <summary>
    /// While the player is touching the ground, they're able to slide, but not wallrun.
    /// Grounded movement receives much more friction and allows the player to accelerate
    /// faster so that it feels more controllable than moving in the air.
    /// </summary>
    private void MoveOnGround() {
        Jumping.SetDoing(false);
        if(Grounded.Ticks < 0)
            Grounded.ResetTicks();
        else Grounded.Tick();
        if(Jumping.TryingButNotDoing && Jumping.Ticks >= 0) {
            _velocity.y = 20; Jumping.SetDoing(true);
            Jumping.Tick((int)(-_gracePeriod * 0.2));
            Jumping.SetTrying(false);
            Grounded.SetTryingAndDoing(false);
            return;
        }
        if(Crouching.TryingButNotDoing && Crouching.Ticks == 0) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 2.4f * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.DoingButNotTrying && HasHeadroom()) {
            Crouching.SetDoing(false);
            Crouching.Tick(-_gracePeriod);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.Ticks < 0) Crouching.Tick();
        if(Sliding && Sliding.Ticks >= 0) {
            EvaluateSlide();
            return;
        } else if(Sliding.Ticks < 0) Sliding.Tick();
        if(Crouching.Expected && Speed > 15)
            Sliding.SetTryingAndDoing(true);
        if(Crouching) _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 15, 6);
        else _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 35, 8);
        _velocity = _velocity.ApplyFrictionXZ(14f);
        WallStuff.AverageNormal = Vector3.zero;
    }

    /// <summary>
    /// All of the implementation for ingesting movement and adjusting velocity
    /// while the character isn't touching the ground. Acceleration and friction
    /// are greatly reduced here so that the player can drift around.
    /// </summary>
    private void MoveInAir() {
        Jumping.SetDoing(false);
        Sliding.SetDoing(false);
        Sliding.Reset();
        if(Grounded.Ticks > 0)
            Grounded.ResetTicks();
        else Grounded.Tick(-1);
        if(Jumping.Trying && HasAirjump && Jumping.Ticks >= 0) {
            if(_velocity.y < 23)
                _velocity.y = 23;
            else _velocity.y += 16;
            Jumping.SetDoing(true);
            Jumping.SetTrying(false);
            Grounded.SetTryingAndDoing(false);
            Jumping.Tick(-_gracePeriod * 3);
            HasAirjump = false;
        }
        if(Crouching.TryingButNotDoing && Crouching.Ticks == 0) {
            Crouching.SetDoing();
            ResizeCollider(Controller.BodyVariables.ColliderHeight / 2.4f * Controller.BodyVariables.SizeModifier);
            Crouching.Tick(-_gracePeriod * 2);
        } else if(Crouching.DoingButNotTrying && HasHeadroom()) {
            Crouching.SetDoing(false);
            ResizeCollider(Controller.BodyVariables.ColliderHeight * Controller.BodyVariables.SizeModifier);
        } else if(Crouching.Ticks < 0) Crouching.Tick();
        _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 20, Crouching ? 4 : 3);
        if(XZSpeed > 30 && _wishdir.magnitude < 0.3)
            _velocity = _velocity.ApplyFrictionXZ(0.05f);
        else _velocity = _velocity.ApplyFrictionXZ(0.5f);
        _velocity = _velocity.ApplyGravity();
    }

    private void MoveInWater() {
        float forwardMagnitude = _wishdir.z;
        Vector3 forward = Controller.CameraParent.forward * forwardMagnitude;
        Vector3 sideways = Controller.transform.rotation * new Vector3(_wishdir.x, 0, _wishdir.z);
        Vector3 combined = new(sideways.x + forward.x, forward.y, sideways.z + forward.z);
        _velocity =
            _velocity.ApplyAcceleration(combined, 40, 4)
            .ApplyFriction(3);
        RefundAirjump();
        RefundDashes();
    }

    /// <summary>
    /// Significantly reduces friction and acceleration so that the player's
    /// velocity is less controllable. Slopes will accelerate the player as a
    /// natural consequence of experiencing much less friction. Once the player
    /// crosses the minimum speed threshold, sliding is automatically disabled
    /// and the player is allowed to transition smoothly to a crouching state.
    /// </summary>
    private void EvaluateSlide() {
        Sliding.Tick();
        _velocity = _velocity.ApplyAcceleration(_wishdirRotated, 30, 0.01f);
        if(YSpeed > 0.5f)
            _velocity = _velocity.ApplyFrictionXZ(Sliding.Ticks * 0.07f);
        else if(YSpeed < -0.5f)
            _velocity = _velocity.ApplyFrictionXZ(0.01f);
        else _velocity = _velocity.ApplyFrictionXZ(0.7f);
        if(Sliding.Ticks < (_gracePeriod / 3f) && XZSpeed < 60)
            _velocity += (_velocity.normalized * 8f);
        if(!Crouching.Expected || Speed < 15f) {
            Sliding.Reset();
            Sliding.Tick(-_gracePeriod * 2);
        }
    }

    /// <summary>
    /// Dashes occur by casting the character's bounding box forward to ensure
    /// that the player can't dash through walls. Once a dash is triggered and a 
    /// dash destination is determined, the player is moved along a horizontal path
    /// and their velocity is adjusted to point in the dash direction.
    /// </summary>
    private bool EvaluateDash() {
        if(Wallrunning) return false;
        // prepare dash target
        if(Dashing.TryingButNotDoing) {
            if(Grounded.Ticks > -_gracePeriod || Controller.CurrentDashCount <= 0) {
                Dashing.SetTryingAndDoing(false);
                Dashing.ResetTicks();
                Dashing.Tick(-_gracePeriod);
                return false;
            }
            if(_wishdir.magnitude < 0.3f) {
                _wishdir = new(0, 0, 1);
                _wishdirRotated = Controller.transform.rotation * _wishdir;
            } else _wishdirRotated = _wishdirRotated.normalized;
            bool hit = Physics.BoxCast(CenterMass - _wishdirRotated,
                FloorExtents * 0.7f, _wishdirRotated, out Controller.DashCheck,
                Quaternion.identity, _dashDistance, PlayerMask
            );
            if(hit) {
                if(Controller.DashCheck.distance < Collider.radius * 4f) {
                    Dashing.SetTryingAndDoing(false);
                    Dashing.ResetTicks();
                    Dashing.Tick(-_gracePeriod);
                    return false;
                }
                _dashEndpoint = Vector3.Lerp(CenterMass, Controller.DashCheck.point, 0.87f);
            } else _dashEndpoint = RB.position + (_wishdirRotated * _dashDistance);
            if(_wishdir.z >= -0.001) {
                float mag = _velocity.magnitude;
                _velocity = _wishdirRotated * mag;
                if(XZSpeed < 60) _velocity += (_velocity.normalized * 8f);
            } else _velocity = new Vector3(0, 0, 0);
            RefundAirjump();
            RefundWallruns(true);
            Controller.CurrentDashCount--;
            Dashing.SetDoing();
        }
        // do dashing
        Controller.PlayerPhysics.isKinematic = true;
        Dashing.Tick();
        RB.position = Vector3.MoveTowards(RB.position, _dashEndpoint, Time.fixedDeltaTime * 250);
        if(!Dashing.Doing) {
            Controller.PlayerPhysics.isKinematic = false;
            return false;
        }
        if(Vector3.Distance(RB.position, _dashEndpoint) <= Collider.radius) {
            Dashing.SetTryingAndDoing(false);
            Dashing.ResetTicks();
            Dashing.Tick(-_gracePeriod);
            Controller.PlayerPhysics.isKinematic = false;
            _velocity.y = 0;
            RB.velocity = _velocity;
        }
        return true;
    }


    /// <summary>
    /// Wallruns are handled similarly to walking on the ground. When the wallrun is executed,
    /// we project the velocity vector along the wall normal and selectively apply friction and
    /// gravity to allow the character to stick to the wall naturally.
    /// </summary>
    /// <returns></returns>
    private bool EvaluateWallrun() {
        bool isOnWall = TraceHelpers.HorizontalFan(CenterMass, -WallStuff.AverageNormal,
            WallStuff.Position, out WallStuff.Position, out WallStuff.AverageNormal,
            distance: Collider.radius * 4f
        );

        if(Grounded || !isOnWall) {
            if(Wallrunning.Ticks > 1) {
                RefundAirjump();
                RefundDashes();
            }
            return CancelWallrun();
        }

        // wallkicks
        if(Crouching.Expected) {
            if(Wallrunning.Ticks < 6) {
                if(Jumping.Trying) {
                    Controller.PlayGlobalSoundEffect(4);
                    Jumping.Tick(-_gracePeriod);
                    _velocity += (WallStuff.AverageNormal * 10) + (Controller.transform.forward * 20);
                    _velocity.y = Mathf.Max(25, _velocity.y);
                    RefundAirjump();
                    RefundDashes();
                    RefundWallruns(true);
                    return CancelWallrun(false);
                }
            } else {
                EnsureAirtime();
                RefundWallruns(false);
                RefundDashes();
                RefundAirjump();
                Wallrunning.Tick(-_gracePeriod);
                return false;
            }
            return false;
        }

        // wall discrimination and first tick
        float offset = Vector3.Distance(CenterMass, WallStuff.Position);
        if(offset > Collider.radius * 3f) {
            RefundAirjump();
            RefundDashes();
            return CancelWallrun();
        }
        if(Wallrunning.TryingButNotDoing) {
            Wallrunning.SetDoing();
            Wallrunning.ResetTicks();
            Grounded.ResetTicks();
            _velocity = Vector3.ProjectOnPlane(_velocity, WallStuff.AverageNormal);
            if(XZSpeed < 60) _velocity += (_velocity.XZ().normalized * 8f);
        }

        Vector3 wishdirWall = Vector3.ProjectOnPlane(_wishdirRotated, WallStuff.AverageNormal);
        if(Wallrunning.Ticks > 2 && WallStuff.Grade < 0.96f) {
            RefundAirjump();
            RefundDashes();
            return CancelWallrun();
        }

        // walljumps
        if(Wallrunning.Ticks > 2 && Jumping.TryingButNotDoing && Jumping.Ticks >= 0) {
            Jumping.SetDoing();
            Jumping.SetTrying(false);
            Jumping.Tick(-_gracePeriod);
            Vector3 kick = (wishdirWall * 2) + (WallStuff.AverageNormal * 10);
            _velocity += kick;
            _velocity.y = 18;
            RefundAirjump();
            RefundDashes(); ;
            return CancelWallrun(false);
        } else if(Jumping.Ticks < 0) Jumping.Tick();

        // walldashes
        if(WallStuff.IsLookingAway(_mod, Controller.transform)
            && Wallrunning.Ticks > 2
            && Dashing.TryingButNotDoing
            && Dashing.Ticks >= 0) {
            Dashing.SetDoing();
            Dashing.SetTrying(false);
            float mag = Mathf.Max(50, _velocity.magnitude);
            Vector3 kick = Controller.CameraParent.transform.forward;
            _velocity.y = 0;
            _velocity = kick * mag;
            EnsureAirtime();
            RefundAirjump();
            RefundDashes();
            RefundWallruns(true);
            Controller.CurrentDashCount--;
            return false;
        }

        // wallruns
        _velocity = _velocity
            .ProjectAndPreserve(WallStuff.AverageNormal)
            .ApplyAcceleration(wishdirWall.XZ(), 86, 2f)
            .ApplyFrictionY(5f)
            .ApplyFrictionXZ(3f);
        if(offset > Collider.radius)
            _velocity -= WallStuff.AverageNormal;

        RB.velocity = _velocity;
        Wallrunning.Tick();
        if(Wallrunning.Ticks % 2 == 0) WallStuff.PreviousNormal = WallStuff.AverageNormal;
        DumpState();
        return true;
    }

    private bool CancelWallrun(bool penalize = true) {
        if(Wallrunning.Doing) {
            Wallrunning.Reset();
            WallStuff.Reset();
            if(penalize) Wallrunning.Tick(-_gracePeriod);
        }
        return false;
    }

    /// <summary>
    /// Returns true if the character has enough room to stand above their head while crouching.
    /// </summary>
    private bool HasHeadroom() {
        return !Physics.CheckBox(Controller.transform.position + new Vector3(0, Controller.BodyVariables.UncrouchHeight * 0.51f, 0), BodyExtents, Quaternion.identity, PlayerMask);
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
            Controller.CharacterGroundState = Sliding ? GroundState.Sliding : GroundState.SteadyGround;
        } else {
            if(ApplySwimming()) return;
            if(Wallrunning) Controller.CharacterGroundState = GroundState.Climbing;
            else Controller.CharacterGroundState = GroundState.InAir;
        }
    }

    private bool ApplySwimming() {
        if(!Swimming) return false;
        Controller.CharacterGroundState = GroundState.Swimming;
        if(_velocity.magnitude < 0.1) {
            Controller.CharacterSwimState = SwimState.TreadingWater;
            return true;
        }
        Controller.CharacterSwimState = ((_wishdir.z > 0f) ? SwimState.SwimmingForward
            : ((_wishdir.z < 0f) ? SwimState.SwimmingBackwards : ((_wishdir.x > 0f) ? SwimState.SwimmingRight
            : ((_wishdir.x < 0f) ? SwimState.SwimmingLeft
            : ((_wishdir.y > 0f) ? SwimState.SwimmingUp
            : ((_wishdir.y < 0f) ? SwimState.SwimmingDown
            : SwimState.TreadingWater))))));
        return true;
    }

    /// <summary>
    /// Messes with the cc's standard rigidbody so that the kinematics can be applied without the game fucking with stuff.
    /// Some of this probably doesn't need to be called each frame but I don't know what objects may attempt to
    /// update these values elsewhere.
    /// </summary>
    private void FixupRigidbody() {
        RB.mass = 0.01f; RB.drag = 0; RB.angularDrag = 0; RB.useGravity = false;
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
        Vector3 center = Collider.center;
        float prevHeight = Collider.height;
        float delta = newHeight - prevHeight;
        center.y += delta * 0.5f;
        Collider.center = center;
        Collider.height = newHeight;
    }

    private void ConfigureEvents() {
        Grounded.EntryTrigger += () => {
            if(Grounded.Ticks > -_gracePeriod * 3) return;
            JumpMovementShake(6.5f);
            Controller.PlayGlobalSoundEffect(2);
        };
        Jumping.EntryTrigger += () => {
            if(Grounded) {
                JumpMovementShake(7f);
                Controller.PlayGlobalSoundEffect(0);
            } else {
                if(Wallrunning.Doing)
                    Controller.PlayGlobalSoundEffect(0);
                else Controller.PlayGlobalSoundEffect(1);
                JumpMovementShake(15f);
            }
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
        Wallrunning.EntryTrigger += () => {
            WallStuff.Sounds?.Play();
            JumpMovementShake(6.5f);
            Controller.PlayerBody.Recoil(new Vector3(0f, 0.15f, -0.1f), Vector3.zero, 8f, 18f);

        };
        Wallrunning.ExitTrigger += () => {
            WallStuff.Sounds?.Stop();
        };
    }

    private void VerySmallMovementShake() {
        CameraShaker.Instance.DefaultPosInfluence = Vector3.zero;
        CameraShaker.Instance.DefaultRotInfluence = new Vector3(-10, 0, 0);
        CameraShaker.Instance.ShakeOnce(6f, 0.5f, 0.1f, 1f);
        CameraShaker.Instance.ResetCamera();
    }

    public void JumpMovementShake(in float mag, in float inTime = 0.2f) {
        CameraShaker.Instance.DefaultPosInfluence = Vector3.zero;
        CameraShaker.Instance.DefaultRotInfluence = new Vector3(-10, 0, 0);
        CameraShaker.Instance.ShakeOnce(mag, 0.7f, inTime, 1f);
        CameraShaker.Instance.ResetCamera();
        Controller.PlayerBody.Recoil(new Vector3(0f, 0.05f, 0f), Vector3.zero, 5f, 8f);
        Controller.PlayerBody.Recoil(Vector3.zero, new Vector3(4, 6, 0), 2f, 6f);
    }

    private bool ShouldPauseUpdates() {
        return !_mod.enabled || Controller == null || !Controller.enabled || !Controller.Active || Controller.PlayerManager.PlayerEngaged;
    }

    private void TryReset() {
        if(Input.GetKey(KeyCode.F2)) {
            _velocity = Vector3.zero;
            RB.velocity = Vector3.zero;
            Controller.transform.position = new Vector3(0, 0, 0);
        }
    }

    private bool RisingEdgeJump() {
        foreach(KeyBindingInput bindInput in KeyBindingManager.BindInputs) {
            if(bindInput.Action != KeyAction.Jump) continue;
            if(Input.GetKeyDown(bindInput.Code)) {
                KeyBindingManager.UsingGamepad = false;
                return true;
            }
        }
        return false;
    }

#pragma warning disable CS0162
    private void DumpState() {
        if(!_dumpState) return;
        _mod.Log("________________________________");
        _mod.Log("Vel: " + _velocity);
        _mod.Log("XZ: " + XZSpeed + " Y: " + (Math.Abs(YSpeed) < 0.01 ? 0 : YSpeed));
        _mod.Log("Wishdir: " + _wishdir + ", " + _wishdirRotated);
        _mod.Log("Grounded: " + Grounded + ", Plane: " + Controller.GroundCheck.normal);
        _mod.Log("Jumping: " + Jumping);
        _mod.Log("HasAirjump: " + HasAirjump);
        _mod.Log("Crouching: " + Crouching);
        _mod.Log("Sliding: " + Sliding);
        _mod.Log("Dashing: " + Dashing + " (" + Controller.CurrentDashCount + "/" + Controller.DashCount + ")");
        _mod.Log("Wallrunning: " + Wallrunning + ", (" + (!Wallrunning.Expected ? "---" : (WallStuff.IsLeft ? "<--" : "-->")) + "), (:: " + WallStuff.AverageNormal + ")");
    }
#pragma warning restore CS0162 
}
