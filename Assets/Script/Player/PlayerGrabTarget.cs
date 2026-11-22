using System;
using Unity.Netcode;
using UnityEngine;

public class PlayerGrabTarget : NetworkBehaviour, IGrable
{
    [SerializeField] private Rigidbody targetBody;
    public bool IsGrabbed { get; private set; } = false;
    private ConfigurableJoint currentGrabJoint;
    
    public bool CanBeGrabbed()
    {
        return !IsGrabbed;
    }

    public void OnGrab(ConfigurableJoint grabJoint, ulong grabberId)
    {
        if (!IsServer || NetworkObjectId == grabberId) return;
        IsGrabbed = true;

        currentGrabJoint = grabJoint;
        grabJoint.connectedBody = targetBody;
        grabJoint.enableCollision = false;

        grabJoint.xMotion = ConfigurableJointMotion.Locked;
        grabJoint.yMotion = ConfigurableJointMotion.Locked;
        grabJoint.zMotion = ConfigurableJointMotion.Locked;

        grabJoint.angularXLimitSpring = new SoftJointLimitSpring
        {
            spring = 1000f,
            damper = 100f
        };
        grabJoint.angularYZLimitSpring = new SoftJointLimitSpring
        {
            spring = 1000f,
            damper = 100f
        };
    }

    public void OnDrop()
    {
        IsGrabbed = false;
        if (currentGrabJoint == null) return;
        Destroy(currentGrabJoint);
    }
}
