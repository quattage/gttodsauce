using UnityEngine;

namespace gttoduf.impl;

public class TraceHelpers {

    public readonly struct WallCandidate {

        public readonly bool Hit;
        public readonly RaycastHit Trace;
        public readonly Vector3 NormXZ;
        private readonly Vector4 _vars; // up, lookDiff, moveDiff, distance, 
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
            NormXZ = trace.Value.normal.XZ();
            _vars = new(
                Vector3.Angle(NormXZ, Vector3.up),
                Vector3.Dot(forwardLook, NormXZ),
                Vector3.Dot(wishdir, NormXZ),
                Vector3.Distance(pos, trace.Value.point)
            );
        }

        public WallCandidate(in Vector3 pos, in Vector3 disp, in Vector3 norm, in Vector3 forwardLook, in float rad, in int mask = ~(1 << 8)) {
            Hit = Physics.CapsuleCast(pos + disp, pos - disp, rad, norm, out Trace, 10f, mask);
            NormXZ = Trace.normal.XZ();
            _vars = new(
                Vector3.Angle(NormXZ, Vector3.up),
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

        public static WallCandidate[] Collect(in Vector3 pos, in float height, in float radius, in Vector3 forwardLook, in Vector3 tangentLook, in Vector3 velocity, in Vector3 wishdir, int mask = ~(1 << 8)) {
            Vector3 displace = new(0, (height / 2) - radius, 0);
            float rad = radius * 0.7f;
            return [
                WallCandidate.OfTrajectory(pos, displace, velocity, forwardLook, wishdir, rad, mask),
                new(pos, displace, Vector3.ProjectOnPlane(wishdir, forwardLook).normalized, forwardLook, rad, mask),
                new(pos, displace, velocity.normalized, forwardLook, rad, mask),
                new(pos, displace, tangentLook, forwardLook, rad, mask),
                new(pos, displace, -tangentLook, forwardLook, rad, mask),
            ];
        }

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
    }

    public static RaycastHit? Trajectory(Vector3 pos, in Vector3 displacement, Vector3 vel, in float radius, in int layerMask = ~(1 << 8), in float traceDistance = 20f, in float step = 0.01f) {
        Vector3 dir, delta;
        float progress = 0f, stepDistance;
        while(progress < traceDistance) {
            vel = vel.ApplyGravity(40, step).ApplyFrictionXZ(0.5f * step);
            delta = vel * step;
            pos += delta;
            stepDistance = delta.magnitude;
            dir = delta.normalized;
            bool hit = Physics.CapsuleCast(
                pos + displacement, pos - displacement, radius * 0.92f,
                dir, out RaycastHit trace, stepDistance, layerMask
            );
            progress += stepDistance;
            if(!hit) continue;
            return trace;
        }
        return null;
    }

    public static bool HorizontalFan(Vector3 pos, Vector3 dir, out Vector3 avgPos, out Vector3 avgNorm, int rays = 10, float distance = 6, int layerMask = ~(1 << 8)) {
        const float reach = 45;
        float t, angle;
        Vector3 castNormal;
        avgPos = Vector3.zero;
        avgNorm = Vector3.zero;
        int hits = 0;
        for(int x = 0; x < rays; x++) {
            t = (x / (float)rays);
            angle = Mathf.Lerp(-reach, reach, t);
            castNormal = Quaternion.AngleAxis(angle, Vector3.up) * dir;
            bool hit = Physics.Raycast(pos, castNormal, out RaycastHit trace, distance, layerMask);
            if(!hit) continue;
            avgPos += trace.point;
            avgNorm += trace.normal;
            hits++;
        }
        if(hits <= 0) {
            avgPos = pos;
            avgNorm = dir;
            return false;
        }
        avgPos /= hits;
        avgNorm /= hits;
        return true;
    }

}