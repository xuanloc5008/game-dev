using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Handles player input for picking up, dropping, and rotating held items.
/// </summary>
public class PlayerInteraction : NetworkBehaviour
{
    [SerializeField]
    private float _rotateSensitivity = 0.15f;

    private PlayerMovement _movement;
    private PickupItem _heldItem;
    private Quaternion _heldRotation = Quaternion.identity;

    // Per-player input actions — MPPM-safe.
    private InputAction _interactAction;
    private InputAction _rotateAction;

    public bool IsRotatingItem { get; private set; }

    private void Awake()
    {
        _movement = GetComponent<PlayerMovement>();

        // Right-click item rotation is not in the shared asset — bind inline.
        _rotateAction = new InputAction("RotateItem", InputActionType.Button);
        _rotateAction.AddBinding("<Mouse>/rightButton");
    }

    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            enabled = false;
            return;
        }

        var playerInput = GetComponent<PlayerInput>();
        _interactAction = playerInput.actions["Interact"];
        _rotateAction.Enable();
    }

    public override void OnNetworkDespawn()
    {
        if (IsOwner)
        {
            _rotateAction?.Disable();
        }
        _rotateAction?.Dispose();
    }

    private void Update()
    {
        HandlePickup();
        HandleItemRotation();
    }

    private void HandlePickup()
    {
        if (_interactAction == null) return;
        if (!_interactAction.WasPressedThisFrame()) return;

        if (_heldItem != null)
            DropItem();
        else
            TryPickUp();
    }

    private void TryPickUp()
    {
        if (!Physics.Raycast(_movement.InteractRay, out var hit, _movement.InteractRange)) return;

        var item = hit.collider.GetComponent<PickupItem>();
        if (item == null) return;

        item.PickUpRpc(OwnerClientId);
        _heldItem = item;
        _heldRotation = Quaternion.identity;
    }

    private void DropItem()
    {
        _heldItem.DropRpc();
        _heldItem = null;
        _heldRotation = Quaternion.identity;
    }

    private void HandleItemRotation()
    {
        IsRotatingItem = _heldItem != null && _rotateAction != null && _rotateAction.IsPressed();

        if (!IsRotatingItem) return;

        // TODO: wire Look delta through if needed.
        var mouseAction = GetComponent<PlayerInput>()?.actions["Look"];
        if (mouseAction == null) return;

        var delta = mouseAction.ReadValue<Vector2>() * _rotateSensitivity;

        _heldRotation = Quaternion.AngleAxis(delta.x, Vector3.up)
                      * Quaternion.AngleAxis(-delta.y, Vector3.right)
                      * _heldRotation;

        _heldItem.SetHeldRotationRpc(_heldRotation);
    }
}
