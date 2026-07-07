using UnityEngine;

/// <summary>
/// ScriptableObject holding player movement tuning values.
/// </summary>
[CreateAssetMenu(fileName = "PlayerMovementSettings", menuName = "U6-Multiplayer/Player Movement Settings")]
public class PlayerMovementSettings : ScriptableObject
{
    [Header("Speed")]
    public float walkSpeed = 10f;
    public float sprintSpeed = 20f;

    [Header("Jump")]
    public float jumpForce = 15f;
    public float coyoteTime = 0.12f;
    public float jumpBufferTime = 0.15f;

    [Header("Air Movement")]
    public float airSpeedMax = 27f;

    [Header("Gravity")]
    public float gravityScale = 2.99f;
    public float fallGravityMult = 1.25f;

    [Header("Slam")]
    public float slamForce = 28f;

    [Header("Look")]
    public float lookSensitivity = 0.1f;

    [Header("Interact")]
    public float interactRange = 15f;
}
