using System.Diagnostics;
using UnityEngine;

namespace GTTODSauce.impl;

public class TraceHelpers {

    /// <summary>
    /// A container object for managing a heuristic measurement
    /// of how "wall-like" a given RaycastHit is.
    /// </summary>
    public readonly struct WallCandidate {

        public readonly bool Hit;
        public readonly RaycastHit Trace;
        public readonly Vector3 NormXZ;
        private readonly Vector4 _vars;
        public readonly float UpAngle => _vars.x;
        public readonly float LookDiff => _vars.y;
        public readonly float MoveDiff => _vars.z;
        public readonly float Distance => _vars.w;

        public WallCandidate(RaycastHit? trace, in Vector3 pos, in Vector3 wishdir, in Vector3 forwardLook) {
            if(trace == null) {
                Hit = false;
                this.Trace = new();
                return;
            }
            Hit = true;
            this.Trace = trace.Value;
            NormXZ = Trace.normal.XZ();
            _vars = new(
                Vector3.Angle(Vector3.up, Trace.normal),
                Vector3.Dot(forwardLook, NormXZ),
                Vector3.Dot(wishdir, NormXZ),
                Vector3.Distance(pos, trace.Value.point)
            );
        }

        public WallCandidate(in Vector3 pos, in Vector3 disp, in Vector3 norm, in Vector3 forwardLook, in float rad, in int mask = ~(1 << 8)) {
            Hit = Physics.CapsuleCast(pos + disp, pos - disp, rad, norm, out Trace, 20f, mask);
            NormXZ = Trace.normal.XZ();
            _vars = new(
                Vector3.Angle(Trace.normal, Vector3.up),
                Vector3.Dot(forwardLook, NormXZ),
                Vector3.Dot(norm, NormXZ),
                Vector3.Distance(pos, Trace.point)
            );
        }

        public static WallCandidate OfTrajectory(in Vector3 pos, in Vector3 disp, in Vector3 vel, in Vector3 wishdir, in Vector3 forwardLook, in float rad, in int mask = ~(1 << 8)) {
            RaycastHit? hit = TraceHelpers.Trajectory(pos, disp, vel, rad, mask, 20f, Time.fixedDeltaTime * 3);
            if(hit == null) return new(null, pos, wishdir, forwardLook);
            return new(hit.Value, pos, wishdir, forwardLook);
        }

        /// <summary>
        /// Creates 5 WallCandidate objects by casting capsules in various directions. Casts
        /// 1. Along the trajectory of the current velocity, which is useful for predicting walls before they are touched.
        /// 2. Along the current wishdir projected to the left/right, which is useful for capturing subtle strafing movements 
        /// 3. Straight forward, which is useful for detecting walls that the player is looking directly at but may not be moving towards (e.g. if the player is standing still or moving very slowly).
        /// </summary>
        /// <param name="pos">The initial position to cast from</param>
        /// <param name="height">The height of the capsule to cast</param>
        /// <param name="radius">The radius of the capsule</param>
        /// <param name="forwardLook">The direction the capsule is looking</param>
        /// <param name="tangentLook">The tangent of the forwardLook direction (usually just Transform.right)</param>
        /// <param name="velocity">The current velocity of the parent caster</param>
        /// <param name="wishdir">The current attempted moving direction of the parent caster on the XZ plane</param>
        /// <param name="mask">The layermask to use when casting</param>
        /// <returns>A list of WallCandidates in no particular order.</returns>
        public static WallCandidate[] Collect(in Vector3 pos, in float height, in float radius, in Vector3 forwardLook, in Vector3 velocity, in Vector3 wishdir, int mask = ~(1 << 8)) {
            Vector3 displace = new(0, (height / 2) - radius, 0);
            float rad = radius * 0.7f;
            return [
                WallCandidate.OfTrajectory(pos, displace, velocity, forwardLook, wishdir, rad, mask),
                new(pos, displace, Vector3.ProjectOnPlane(wishdir, forwardLook).normalized, forwardLook, rad, mask),
                new(pos, displace, velocity.normalized, forwardLook, rad, mask),
            ];
        }

        /// <summary>
        /// Skims through a list of WallCandidate objects to find
        /// the one that's the most wall-like. Prioritized closer
        /// distances and measures how perpendicular the wall is
        /// to the current moving direction.
        /// </summary>
        /// <param name="candidates"></param>
        /// <returns></returns>
        public static WallCandidate FindBest(in WallCandidate[] candidates) {
            if(candidates.Length <= 0) return new();
            int bestIdx = 0;
            for(int x = 1; x < candidates.Length; x++) {
                WallCandidate current = candidates[x];
                if(!current.Hit) continue;
                WallCandidate best = candidates[bestIdx];
                if(current.Hit && !best.Hit) bestIdx = x;
                if(current.MoveDiff < best.MoveDiff) bestIdx = x;
                if(current.Distance < best.Distance) bestIdx = x;
            }
            return candidates[bestIdx];
        }

        /// <summary>
        /// Produces a formatted string containing all walls in the provided
        /// array. Useful for debug logging.
        /// </summary>
        /// <param name="candidates"></param>
        /// <returns></returns>
        public static string Summarize(in WallCandidate[] candidates, WallCandidate? best = null) {
            string s = "::\n";
            for(int x = 0; x < candidates.Length; x++) {
                WallCandidate c = candidates[x];
                s += $"Wall {x}: {(c.Hit ? c.NormXZ : "n/a")}\n";
            }
            if(best != null) s += $"Best: {(best.Value.Hit ? best.Value.NormXZ : "n/a")}\n";
            return s;
        }

        /// <summary>
        /// Checks to see whether this wall is relevant to the current movement scenario. 
        /// The player must not be moving away the wall, and they must be within a reasonable distance of it. 
        /// </summary>
        /// <param name="wall"></param>
        /// <param name="velocity"></param>
        /// <returns></returns>
        public bool IsRelevent(Vector3 velocity, float distance) {
            return (Hit && Distance < distance && Vector3.Dot(NormXZ, velocity.XZ().normalized) <= 0);
        }

        /// <summary>
        /// Checks this wall to see if the angle is steep enough to be considered a wall. 
        /// </summary>
        /// <param name="wall"></param>
        /// <param name="wallAngle"></param>
        /// <returns></returns>
        public bool IsVertical(float wallAngle = 35) {
            return (UpAngle >= (90 - wallAngle) && UpAngle <= (90 + wallAngle));
        }

        /// <summary>
        /// Checks this wall to see the player is generally looking towards it. This is used to filter out walls
        /// behind the player that are technically valid but would cause the player frustration as they
        /// may unintentionally attach to them.
        /// </summary>
        /// <param name="wall"></param>
        /// <returns></returns>
        public bool IsInView() {
            return (LookDiff > -0.8 && LookDiff < 0.8);
        }

        public bool IsOpposingPrevious(WallContainer wall) {
            if(wall.PreviousWallTouch.magnitude < 0.1) return true;
            float fac = Vector3.Dot(wall.PreviousWallTouch, Trace.normal);
            return fac < 0.8;
        }
    }

    /// <summary>
    /// Used specifically by character controllers for pre-collision predictions.
    /// Advanced movement techniques like wallrunning can use this to slew the camera
    /// before the wall is touched.
    /// </summary>
    /// <param name="pos">The starting position of the object</param>
    /// <param name="displacement">The difference between the two centers of the capsule. This is calculated by (height / 2) - radius</param>
    /// <param name="vel">The initial velocity of the object</param>
    /// <param name="radius">The radius of the capsule</param>
    /// <param name="layerMask">The layermask to use when casting</param>
    /// <param name="traceDistance">The total distance of the trajectory represented as arclength in meters</param>
    /// <param name="step">How far to step along the trajectory when casting. Smaller numbers = higher resolution</param>
    /// <returns>A RaycastHit, or null if nothing was hit.</returns>
    public static RaycastHit? Trajectory(Vector3 pos, in Vector3 displacement, Vector3 vel, in float radius, in int layerMask = ~(1 << 8), in float traceDistance = 20f, in float step = 0.01f) {
        Vector3 dir, delta;
        float progress = 0f, stepDistance;
        bool hit;
        int iter = 0;
        while(progress < traceDistance) {
            if(iter > 255) return null;
            iter++;
            // note that gravity and friction are applied as constants here but that may not be the case in the cc.
            // this is fine for this use case since getting an absolutely perfect velocity prediction isn't super important.
            // I'd do this better by having this step through the actual movement code rather than pretend to do so here.
            vel = vel.ApplyGravity(40, step).ApplyFrictionXZ(0.5f * step);
            delta = vel * step;
            pos += delta;
            stepDistance = delta.magnitude;
            dir = delta.normalized;
            hit = Physics.CapsuleCast(
                pos + displacement, pos - displacement, radius * 0.92f,
                dir, out RaycastHit trace, stepDistance, layerMask
            );
            progress += stepDistance;
            if(!hit) continue;
            return trace;
        }
        return null;
    }

    /// <summary>
    /// Casts a handful of rays in a pie slice shape. This is used to sample a large horizontal swath of a surface
    /// without requiring more advanced methods that would break down when casting towards non-convex mesh colliders.
    /// This method can also smooth out transitions between individual colliders.
    /// </summary>
    /// <param name="pos">The position to cast from</param>
    /// <param name="dir">The normalized direction to cast towards</param>
    /// <param name="avgPos">Output - The average position of all casts that resulted in a hit</param>
    /// <param name="avgNorm">Output - The average surface normal of all casts that resulted in a hit</param>
    /// <param name="resolution">The number of rays to cast</param>
    /// <param name="width">How wide (in degrees) the cast should be.</param>
    /// <param name="distance">The distance of each cast</param>
    /// <param name="layerMask">The layermask to use when casting</param>
    /// <returns>true/false indicating whether or not any collider was hit by any ray during this cast</returns>
    public static bool HorizontalFan(Vector3 pos, Vector3 dir, Vector3 defaultPos, out Vector3 avgPos, out Vector3 avgNorm, int resolution = 10, float width = 45, float distance = 6, int layerMask = ~(1 << 8)) {
        float t, angle;
        Vector3 castNormal;
        avgPos = Vector3.zero;
        avgNorm = Vector3.zero;
        int hits = 0; bool hit;
        for(int x = 0; x < resolution; x++) {
            t = (x / (float)resolution);
            angle = Mathf.Lerp(-width, width, t);
            castNormal = Quaternion.AngleAxis(angle, Vector3.up) * dir;
            hit = Physics.Raycast(pos, castNormal, out RaycastHit trace, distance, layerMask);
            if(!hit) continue;
            avgPos += trace.point;
            avgNorm += trace.normal;
            hits++;
        }
        if(hits <= 0) {
            avgPos = defaultPos;
            avgNorm = -dir;
            return false;
        }
        avgPos /= hits;
        avgNorm = (avgNorm / hits).normalized;
        return true;
    }

}