using Unity.Cinemachine;
using Unity.Netcode;
using Unity.Netcode.Components;
using UnityEngine;
using UnityEngine.InputSystem;

public class RagdollPlayerNetworkBridge : NetworkBehaviour
{
    [Header("Player")]
    [SerializeField] private PlayerInput playerInput;
    [SerializeField] private PlayerMovementHandler movement;
    [SerializeField] private PlayerAnimationController animationController;

    [Header("Owner camera")]
    [SerializeField] private CinemachineCamera ownerCamera;
    [SerializeField] private CinemachineInputAxisController cameraInput;
    [SerializeField] private PlayerCameraController cameraController;

    [Header("Physics bodies: root + ragdoll + both GrabPoints")]
    [SerializeField] private Rigidbody[] physicalBodies;

    private CopyMotion[] _copyMotions;

    private bool _playing;
    private bool _postSpawnReady;

    private void Awake()
    {
        _copyMotions = GetComponentsInChildren<CopyMotion>(true);

        if (cameraController == null)
            cameraController = GetComponent<PlayerCameraController>();

        SetLocalControl(false);
        SetServerSimulation(false);
    }

    public override void OnNetworkSpawn()
    {
        _playing = false;
        _postSpawnReady = false;

        SetLocalControl(false);
        SetServerSimulation(false);
        
        GameManager.OnGameStarted += StartPlaying;
    }

    protected override void OnNetworkPostSpawn()
    {
        _postSpawnReady = true;
        
        if (GameManager.Instance != null &&
            GameManager.Instance.CurrentState.Value == GameState.Playing)
        {
            _playing = true;
        }

        ApplyState();
    }

    private void StartPlaying()
    {
        if (!IsSpawned || _playing)
            return;

        _playing = true;

        if (_postSpawnReady)
            ApplyState();
    }

    private void ApplyState()
    {
        bool localControl = _playing && IsOwner;
        bool serverSimulation = _playing && IsServer;
        
        SetFrozen(!serverSimulation);

        SetServerSimulation(serverSimulation);
        SetLocalControl(localControl);

        if (localControl)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }
    }

    private void SetLocalControl(bool active)
    {
        if (ownerCamera != null)
            ownerCamera.enabled = active;

        if (cameraInput != null)
            cameraInput.enabled = active;

        if (cameraController != null)
            cameraController.enabled = active;

        if (playerInput != null)
            playerInput.enabled = active;
    }

    private void SetServerSimulation(bool active)
    {
        if (movement != null)
            movement.enabled = active;
        
        if (animationController != null)
            animationController.enabled = active;

        foreach (var copyMotion in _copyMotions)
        {
            if (copyMotion != null)
                copyMotion.enabled = active;
        }
    }

    public void SetFrozen(bool frozen)
    {
        if (physicalBodies == null)
            return;

        foreach (var body in physicalBodies)
        {
            if (body == null)
                continue;
            
            if (frozen && !body.isKinematic)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            if (body.TryGetComponent<NetworkRigidbody>(out var networkBody))
            {
                networkBody.SetIsKinematic(frozen);
            }
            else
            {
                body.isKinematic = frozen;
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        GameManager.OnGameStarted -= StartPlaying;

        _playing = false;
        _postSpawnReady = false;

        SetLocalControl(false);
        SetServerSimulation(false);
        SetFrozen(true);

        if (IsOwner)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }
    }
}