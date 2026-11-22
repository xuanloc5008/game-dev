using System;
using UnityEngine;

public class CopyMotion : MonoBehaviour
{
    Rigidbody _rigidbody;
    private ConfigurableJoint _configurableJoint;
    
    [SerializeField] Rigidbody animatedRigidbody;
    [SerializeField] bool syncAnimation;
    
    Quaternion _startLocalRotation;
    void Awake()
    {
        _rigidbody = GetComponent<Rigidbody>();
        _configurableJoint = GetComponent<ConfigurableJoint>();
        
        _startLocalRotation = transform.localRotation;
    }

    public void UpdateJointFromAnimation()
    {
        if (!syncAnimation)
            return;
        
        ConfigurableJointExtensions.SetTargetRotationLocal(_configurableJoint, animatedRigidbody.transform.localRotation, _startLocalRotation);
    }

    public void FixedUpdate()
    {
        UpdateJointFromAnimation();
    }
}
