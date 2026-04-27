using System;
using UnityEngine;
using static GTTODSauce.impl.TraceHelpers;

namespace GTTODSauce.impl;

public class WallContainer {

    private const bool _makeParentTransform = false;
    public AudioSource Sounds { get; private set; }

    public Vector3 Position = Vector3.zero;
    private Vector3 _smoothNormal = Vector3.zero;
    private Vector3 _upBasis;

    public Vector3 AverageNormal = Vector3.zero;
    public Vector3 PreviousNormal = Vector3.zero;
    public Vector3 SmoothedNormal {
        get {
            _smoothNormal = Vector3.Lerp(_smoothNormal, AverageNormal, Time.fixedDeltaTime * 10).normalized;
            return _smoothNormal;
        }
    }
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
        _smoothNormal = AverageNormal;
        if(PreviousNormal.magnitude < 0.1f)
            PreviousNormal = AverageNormal;
        Position = candidiate.Trace.point;
        float tanDiff = Vector3.Dot(AverageNormal, look * Vector3.right);
        IsLeft = tanDiff > 0;
    }


    public void Reset() {
        _smoothNormal = Vector3.zero;
        AverageNormal = Vector3.zero;
        Position = Vector3.zero;
    }

    public Vector3 GetUpBasis(float strength = 0.15f, float speed = 2f) {
        Vector3 current = Vector3.Slerp(Vector3.up, _smoothNormal, strength);
        _upBasis = Vector3.MoveTowards(_upBasis, current, Time.deltaTime * speed);
        return _upBasis;
    }
}