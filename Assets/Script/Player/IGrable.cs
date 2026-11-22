using UnityEngine;

public interface IGrable
{
    public bool CanBeGrabbed();
    void OnGrab(ConfigurableJoint grabJoint, ulong grabberId);
    void OnDrop();
}
