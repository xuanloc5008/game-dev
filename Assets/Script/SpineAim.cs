using UnityEngine;

/// <summary>
/// Slides eye meshes vertically to reflect the player's look direction.
/// </summary>
public class SpineAim : MonoBehaviour
{
    [SerializeField] private Transform _cameraPoint;
    [SerializeField] private Transform _eyes;
    [SerializeField] private float _eyeBaseY = 0.3f;
    [SerializeField] private float _travelRange = 0.35f;

    // Must match the clamp in PlayerMovement (80°).
    [SerializeField] private float _maxPitch = 80f;

    private void LateUpdate()
    {
        if (_cameraPoint == null || _eyes == null) return;

        float pitch = _cameraPoint.localEulerAngles.x;
        if (pitch > 180f) pitch -= 360f; // convert 0–360 → signed –180..180

        // Map pitch to a Y offset — looking up raises the eyes, looking down lowers them.
        float yOffset = (pitch / _maxPitch) * _travelRange;

        Vector3 pos = _eyes.localPosition;
        pos.y = _eyeBaseY - yOffset;
        _eyes.localPosition = pos;
    }
}
