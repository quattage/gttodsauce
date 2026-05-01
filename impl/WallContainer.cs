using System;
using System.Runtime.InteropServices;
using UnityEngine;
using static GTTODSauce.impl.TraceHelpers;

namespace GTTODSauce.impl;

public class WallContainer {

    private const bool _makeParentTransform = false;
    public AudioSource Sounds { get; private set; }

    public Vector3 Position = Vector3.zero;
    private Vector3 _upBasis;

    public Vector3 AverageNormal = Vector3.zero;
    public Vector3 PreviousNormal = Vector3.zero;
    public float AttachPercent = 1;

    public Vector3 PreviousWallTouch = Vector3.zero;

    private bool _left;

    public bool IsLeft {
        get => _left;
        set => _left = value;
    }

    public bool IsRight {
        get => !_left;
        set => _left = !value;
    }

    public float Grade => Vector3.Dot(AverageNormal, PreviousNormal);

    public void SetupTransforms(Transform baseTransform, Transform cameraParent) {
        GameObject playerObjects = baseTransform.parent.gameObject;
        ac_WallController wallruns = playerObjects?.transform.GetComponentInChildren<ac_WallController>(true);
        ac_WallDetection why = playerObjects?.transform.GetComponentInChildren<ac_WallDetection>(true);
        if(wallruns != null) {
            wallruns.enabled = false;
            why?.enabled = false;
            Sounds = wallruns.gameObject.GetComponent<AudioSource>();
        }
    }

    public void TakedownTransforms(Transform baseTransform, Transform cameraParent) {
        GameObject playerObjects = baseTransform.parent.gameObject;
        ac_WallController wallruns = playerObjects?.transform.GetComponentInChildren<ac_WallController>(true);
        ac_WallDetection why = playerObjects?.transform.GetComponentInChildren<ac_WallDetection>(true);
        wallruns?.enabled = true;
        why?.enabled = true;
    }

    public void Prime(WallCandidate candidiate, Quaternion look) {
        AverageNormal = candidiate.NormXZ;
        PreviousWallTouch = AverageNormal;
        if(PreviousNormal.magnitude < 0.1f)
            PreviousNormal = AverageNormal;
        Position = candidiate.Trace.point;
        float tanDiff = Vector3.Dot(AverageNormal, look * Vector3.right);
        IsLeft = tanDiff > 0;
    }

    public bool IsLookingAway(GTTODSauce sauce, Transform cameraParent) {
        float lookDiff = Vector3.Dot(cameraParent.transform.forward, AverageNormal);
        return lookDiff > 0.2f;
    }

    public void Reset(bool resetTouch = false) {
        if(resetTouch) PreviousWallTouch = Vector3.zero;
        AverageNormal = Vector3.zero;
        AttachPercent = 1;
    }

    private float GetEasedAttach() {
        return AttachPercent <= 0 ? 0 : AttachPercent >= 1 ? 1 : 1 - Mathf.Pow(2, 10 * AttachPercent - 10);
    }

    public Vector3 GetUpBasis(Vector3 pos, float contactDistance, float strength = 0.15f, bool overrideCondition = false) {
        if(AverageNormal.magnitude < 0.1f || overrideCondition) {
            _upBasis = Vector3.MoveTowards(_upBasis, Vector3.up, Time.deltaTime);
            return _upBasis;
        }
        Vector3 wallDifference = pos - Position;
        float diffLength = wallDifference.magnitude;
        float offsetPercent = 1 - (diffLength / (contactDistance));

        Vector3 current = Vector3.Slerp(Vector3.up, AverageNormal, strength * offsetPercent * GetEasedAttach());
        Vector3 newUpBasis = Vector3.MoveTowards(_upBasis, current, Time.deltaTime * 0.8f);
        _upBasis = Vector3.MoveTowards(_upBasis, newUpBasis, Time.deltaTime * 0.8f);
        return _upBasis;
    }
}