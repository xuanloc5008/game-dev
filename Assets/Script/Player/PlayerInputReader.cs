using System;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody), typeof(PlayerMovementHandler))]
public class PlayerInputReader : NetworkBehaviour
{
    [SerializeField] private PlayerInput _playerInput;
    [SerializeField] private PlayerMovementHandler _movement;
    // [SerializeField] private PlayerGrabber _grabber;

    #region Event declare
    
    public event Action OnPlayerGrab;

    #endregion
    
    private Camera _viewCamera;
    
    private Vector2 _moveInput;
    private bool _sprintHeld;
    private bool _jumpQueued;
    
    private Vector3 _cameraForward = Vector3.forward;

    private bool CanReadInput => IsSpawned
                                 && IsOwner
                                 && _playerInput.enabled == true
                                 && GameManager.Instance != null
                                 && GameManager.Instance.CurrentState.Value == GameState.Playing
                                 && GrabContestManager.Instance != null 
                                 && !GrabContestManager.Instance.IsPlayerContesting(OwnerClientId)
                                 && StopInput == false;

    private NetworkVariable<bool> stopInput = new(false, NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public bool StopInput => stopInput.Value;
    
    public void SetStopInputServer(bool stopped)
    {
        if (!IsServer || !IsSpawned) return;

        stopInput.Value = stopped;

        if (stopped)
        {
            _movement.ReceiveInput(Vector3.zero, false, false);
        }
    }

    private void Awake()
    {
        if (_playerInput == null) _playerInput = GetComponent<PlayerInput>();
        if (_movement == null) _movement = GetComponent<PlayerMovementHandler>();
        if (IsServer) stopInput.Value = false;
    }

    #region On input call back functions
    
    public void OnMovement(InputAction.CallbackContext ctx)
    {
        if (!CanReadInput) return;
        
        _moveInput = ctx.ReadValue<Vector2>();
    }

    public void OnSprint(InputAction.CallbackContext ctx)
    {
        if (!CanReadInput) return;
        
        _sprintHeld = ctx.ReadValueAsButton();
    }

    public void OnJump(InputAction.CallbackContext ctx)
    {
        if (!CanReadInput || !ctx.performed) return;
        
        _jumpQueued = true;
    }

    public void OnGrab(InputAction.CallbackContext ctx)
    {
        if (!CanReadInput || !ctx.performed) return;

        OnPlayerGrab?.Invoke();
    }
    
    #endregion

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner)
            return;

        if (!CanReadInput)
        {
            ClearLocalInput();
            return;
        }

        if (_viewCamera == null)
        {
            _viewCamera = Camera.main;
        }

        if (_viewCamera == null)
        {
            ClearLocalInput();
            SubmitInputServerRpc(Vector3.zero, false, false);
            return;
        }

        Vector3 worldDirection = CalculateWorldDirection();
        
        SubmitInputServerRpc(worldDirection, _sprintHeld, _jumpQueued);

        _jumpQueued = false;
    }

    [ServerRpc(InvokePermission = RpcInvokePermission.Owner)]
    private void SubmitInputServerRpc(Vector3 worldDirection, bool sprintHeld, bool jumpPressed)
    {
        if (StopInput)
        {
            _movement.ReceiveInput(Vector3.zero, false, false);
            return;
        }
        _movement.ReceiveInput(
            worldDirection,
            sprintHeld,
            jumpPressed
        );
    }
    
    private Vector3 CalculateWorldDirection()
    {
        Vector3 forward = Vector3.ProjectOnPlane(
            _viewCamera.transform.forward,
            Vector3.up
        );

        if (forward.sqrMagnitude > 0.0001f)
            _cameraForward = forward.normalized;

        Vector3 right = Vector3.Cross(
            Vector3.up,
            _cameraForward
        ).normalized;

        Vector3 direction =
            _cameraForward * _moveInput.y +
            right * _moveInput.x;
        
        return Vector3.ClampMagnitude(direction, 1f);
    }

    private void OnDisable()
    {
        ClearLocalInput();
    }

    private void ClearLocalInput()
    {
        _moveInput = Vector2.zero;
        _sprintHeld = false;
        _jumpQueued = false;
    }
}
