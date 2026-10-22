using System;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

public class PlayerMovementHandler : NetworkBehaviour
{
    [Header("Movement stats")]
    [SerializeField] private float walkForce = 100f;
    [SerializeField] private float sprintForce = 170f;
    [SerializeField] private float maxWalkSpeed = 3f;
    [SerializeField] private float maxSprintSpeed = 6f;
    [SerializeField] private float rotateSpeed = 180f;
    [SerializeField] private float jumpForce = 15f;

    [Header("Physics body")]
    [SerializeField] private Rigidbody rigidBody;

    [Header("Ground check")]
    [SerializeField] private Transform groundCheck;
    [SerializeField] private float groundCheckRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;

    [Header("Input")]
    [SerializeField] private float inputTimeout = 0.5f;
    
    [Header("Animation")]
    [SerializeField] private PlayerAnimationController playerAnimationController;

    private Vector3 _moveDirection;
    private bool _jumpQueued;
    private float _lastInputTime;

    // Tránh nhận là đang đứng đất ngay sau khi vừa bật nhảy.
    private float _ignoreGroundUntil;

    public bool IsGrounded { get; private set; }
    public bool IsJumping { get; private set; }
    public bool IsSprinting { get; private set; }

    public bool IsMoving =>
        _moveDirection.sqrMagnitude > 0.001f;

    private bool IsPlaying =>
        GameManager.Instance != null &&
        GameManager.Instance.CurrentState.Value == GameState.Playing;

    public void ReceiveInput(Vector3 worldDirection, bool sprintHeld, bool jumpPressed)
    {
        if (!IsSpawned || !IsServer || !isActiveAndEnabled || !IsPlaying)
            return;

        worldDirection.y = 0f;
        float magnitudeSquared = worldDirection.sqrMagnitude;
        if (float.IsNaN(magnitudeSquared) || float.IsInfinity(magnitudeSquared)) return;

        _moveDirection = Vector3.ClampMagnitude(worldDirection, 1f);
        IsSprinting = sprintHeld;
        
        _jumpQueued |= jumpPressed;
        
        _lastInputTime = Time.unscaledTime;
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsServer)
            return;

        if (!IsPlaying ||
            rigidBody == null ||
            rigidBody.isKinematic ||
            groundCheck == null)
        {
            ClearInput();
            IsGrounded = false;
            IsJumping = false;
            return;
        }
        
        if (Time.unscaledTime - _lastInputTime > inputTimeout) ClearInput();
        
        CheckGround();
        HandleMovement();
        HandleRotation();
        HandleJump();

        if (playerAnimationController == null) return;
        float targetSpeed = IsSprinting ? 1f : IsMoving ? 0.5f : 0f;
        playerAnimationController.RunAnimation(playerAnimationController.AnimationParameterHash["Speed"], targetSpeed);
    }
    
    private void CheckGround()
    {
        IsGrounded =
            Time.time >= _ignoreGroundUntil &&
            Physics.CheckSphere(
                groundCheck.position,
                groundCheckRadius,
                groundLayer,
                QueryTriggerInteraction.Ignore
            );

        if (IsGrounded)
            IsJumping = false;
    }
    
    #region Handle functions

    private void HandleMovement()
    {
        if (!IsMoving)
            return;
        
        float maxSpeed = IsSprinting
            ? maxSprintSpeed
            : maxWalkSpeed;

        float moveForce = IsSprinting
            ? sprintForce
            : walkForce;

        Vector3 planarVelocity = Vector3.ProjectOnPlane(
            rigidBody.linearVelocity,
            Vector3.up
        );

        float speedTowardsMovement = Vector3.Dot(
            planarVelocity,
            _moveDirection.normalized
        );

        if (speedTowardsMovement >= maxSpeed)
            return;

        rigidBody.AddForce(
            _moveDirection * moveForce,
            ForceMode.Force
        );
    }

    private void HandleRotation()
    {
        if (!IsMoving)
            return;

        Quaternion desiredRotation = Quaternion.LookRotation(
            _moveDirection.normalized,
            Vector3.up
        );

        Quaternion nextRotation = Quaternion.RotateTowards(
            rigidBody.rotation,
            desiredRotation,
            rotateSpeed * Time.fixedDeltaTime
        );

        rigidBody.MoveRotation(nextRotation);
    }

    private void HandleJump()
    {
        bool requestedJump = _jumpQueued;
        _jumpQueued = false;
        
        if (!requestedJump || !IsGrounded)
            return;

        rigidBody.AddForce(
            Vector3.up * jumpForce,
            ForceMode.Impulse
        );

        IsGrounded = false;
        IsJumping = true;
        _ignoreGroundUntil = Time.time + 0.15f;
    }

    #endregion
    

    private void OnDisable()
    {
        ClearInput();
    }

    private void ClearInput()
    {
        _moveDirection = Vector3.zero;
        IsSprinting = false;
        _jumpQueued = false;
    }
}
