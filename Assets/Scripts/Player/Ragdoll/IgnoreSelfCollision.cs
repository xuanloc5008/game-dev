using System;
using System.Collections.Generic;
using UnityEngine;

public class IgnoreSelfCollision : MonoBehaviour
{
    private Collider[] bodyColliders;

    private void Awake()
    {
        bodyColliders = GetComponentsInChildren<Collider>(true);
        for (int i = 0; i < bodyColliders.Length; i++)
        {
            for (int j = i + 1; j < bodyColliders.Length; j++)
            {
                Physics.IgnoreCollision(bodyColliders[i], bodyColliders[j], true);
            }
        }
    }
}
