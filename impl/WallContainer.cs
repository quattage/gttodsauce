using System;
using UnityEngine;

namespace GTTODSauce.impl;

public class WallContainer {
    public AudioSource Sounds { get; set; }
    public Vector3 AverageNormal = Vector3.zero;
    public Vector3 PreviousNormal = Vector3.zero;
    public Vector3 Position { get; set; }
    public float ApproachPercent { get; set; }

    public float Grade => Vector2.Dot(new Vector2(AverageNormal.x, AverageNormal.z), new Vector2(PreviousNormal.x, PreviousNormal.z));

    public void IncrementApproach(float speed) {
        ApproachPercent = Mathf.MoveTowards(ApproachPercent, Math.Sign(speed) == 1 ? 1 : 0, Time.deltaTime * Mathf.Abs(speed));
    }
}