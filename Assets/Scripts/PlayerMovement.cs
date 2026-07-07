using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering.Universal;

/// <summary>
/// Handles FPS movement, jumping, slamming, and camera look.
/// </summary>
public class PlayerMovement : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform _cameraPoint;
    [SerializeField] private Camera _playerCamera;
    [SerializeField] private CinemachineCamera _playerVCam;
    [SerializeField] private PlayerMovementSettings _settings;

    private CharacterController _cc;
    private PlayerInteraction _interaction;
    private PlayerInput _playerInput;
    private InputActionAsset _ownActions;

    private InputAction _moveAction;
    private InputAction _lookAction;
    private InputAction _jumpAction;
    private InputAction _sprintAction;
    private InputAction _slamAction;

    private Vector3 _horizontalVelocity;
    private float _verticalVelocity;
    private float _cameraPitch;
    private bool _isPlaying;
    private float _coyoteTimer;
    private float _jumpBufferTimer;
    private bool _slamming;

    public Ray InteractRay { get; private set; }
    public float InteractRange => _settings != null ? _settings.interactRange : 5f;

    private void Awake()
    {
        _cc = GetComponent<CharacterController>();
        _interaction = GetComponent<PlayerInteraction>();
        _playerInput = GetComponent<PlayerInput>();
    }

    public void StartPlaying()
    {
        EnableActions();

        _slamAction = new InputAction("Slam", InputActionType.Button);
        _slamAction.AddBinding("<Keyboard>/leftCtrl");
        _slamAction.Enable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
        StartCoroutine(BlendToPlayerCamera());
    }

    private void EnableActions()
    {
        if (_playerInput == null) return;

        // PlayerInput's user/device pairing breaks on NGO-spawned objects.
        // Clone the InputActionAsset so this player owns its own action instances,
        // then enable the map directly — bypasses PlayerInput but keeps the asset bindings.
        var sourceAsset = _playerInput.actions;
        _playerInput.enabled = false;

        _ownActions = Instantiate(sourceAsset);
        var playerMap = _ownActions.FindActionMap("Player");
        playerMap?.Enable();

        _moveAction = _ownActions.FindAction("Player/Move");
        _lookAction = _ownActions.FindAction("Player/Look");
        _jumpAction = _ownActions.FindAction("Player/Jump");
        _sprintAction = _ownActions.FindAction("Player/Sprint");
    }

    private void OnDestroy()
    {
        _slamAction?.Disable();
        _slamAction?.Dispose();
        if (_ownActions != null)
        {
            _ownActions.Disable();
            Destroy(_ownActions);
        }
    }

    private IEnumerator BlendToPlayerCamera()
    {
        var mainCam = Camera.main;
        var brain = mainCam != null ? mainCam.GetComponent<CinemachineBrain>() : null;

        if (_playerVCam != null)
            _playerVCam.enabled = true;

        yield return null;

        if (brain != null)
        {
            float elapsed = 0f;
            while (brain.IsBlending && elapsed < 5f)
            {
                elapsed += Time.deltaTime;
                yield return null;
            }
        }

        if (brain != null) brain.enabled = false;
        if (mainCam != null) mainCam.enabled = false;

        if (mainCam != null && mainCam.TryGetComponent<AudioListener>(out var mainListener))
            mainListener.enabled = false;

        if (_playerCamera != null)
        {
            int fpvLayer = LayerMask.NameToLayer("FPV Cull");
            if (fpvLayer != -1)
                _playerCamera.cullingMask &= ~(1 << fpvLayer);

            _playerCamera.nearClipPlane = 0.1f;

            if (_playerCamera.TryGetComponent<UniversalAdditionalCameraData>(out var urpData))
                urpData.renderPostProcessing = true;

            _playerCamera.enabled = true;

            if (_playerCamera.TryGetComponent<AudioListener>(out var listener))
                listener.enabled = true;
        }

        _isPlaying = true;
    }

    private void Update()
    {
        if (!_isPlaying) return;

        HandleLook();
        InteractRay = new Ray(_cameraPoint.position, _cameraPoint.forward);
        HandleMovement();
    }

    private void HandleLook()
    {
        if (_lookAction == null || _settings == null) return;
        if (_interaction != null && _interaction.IsRotatingItem) return;

        var delta = _lookAction.ReadValue<Vector2>() * _settings.lookSensitivity;

        transform.Rotate(0f, delta.x, 0f);
        _cameraPitch = Mathf.Clamp(_cameraPitch - delta.y, -80f, 80f);
        _cameraPoint.localEulerAngles = new Vector3(_cameraPitch, 0f, 0f);
    }

    private void HandleMovement()
    {
        if (_moveAction == null || _settings == null) return;

        bool grounded = _cc.isGrounded;
        float speed = (_sprintAction != null && _sprintAction.IsPressed()) ? _settings.sprintSpeed : _settings.walkSpeed;
        Vector3 inputDir = ReadMoveInput();

        UpdateJumpTimers(grounded);

        bool canJump = _coyoteTimer > 0f;
        bool wantsJump = _jumpBufferTimer > 0f;

        HandleJump(canJump, wantsJump);
        HandleSlam(grounded);
        HandleHorizontalMovement(grounded, wantsJump, inputDir, speed);
        HandleGravity(grounded);

        _cc.Move((_horizontalVelocity + Vector3.up * _verticalVelocity) * Time.deltaTime);
    }

    private Vector3 ReadMoveInput()
    {
        var v = _moveAction.ReadValue<Vector2>();
        return (transform.right * v.x + transform.forward * v.y).normalized;
    }

    private void UpdateJumpTimers(bool grounded)
    {
        _coyoteTimer = grounded ? _settings.coyoteTime : _coyoteTimer - Time.deltaTime;

        if (_jumpAction != null && _jumpAction.WasPressedThisFrame())
            _jumpBufferTimer = _settings.jumpBufferTime;
        else
            _jumpBufferTimer -= Time.deltaTime;
    }

    private void HandleJump(bool canJump, bool wantsJump)
    {
        if (!canJump || !wantsJump) return;

        _verticalVelocity = _settings.jumpForce;
        _coyoteTimer = 0f;
        _jumpBufferTimer = 0f;
        _slamming = false;
    }

    private void HandleSlam(bool grounded)
    {
        if (grounded) { _slamming = false; return; }

        if (!_slamming && _slamAction.WasPressedThisFrame())
        {
            _verticalVelocity = -_settings.slamForce;
            _slamming = true;
        }
    }

    private void HandleHorizontalMovement(bool grounded, bool wantsJump, Vector3 inputDir, float speed)
    {
        if (grounded)
        {
            if (!wantsJump)
                _horizontalVelocity = Vector3.Lerp(_horizontalVelocity, inputDir * speed, Time.deltaTime * 14f);
        }
        else
        {
            if (inputDir != Vector3.zero)
                _horizontalVelocity += inputDir * (speed * 3.6f * Time.deltaTime);

            _horizontalVelocity = Vector3.ClampMagnitude(_horizontalVelocity, _settings.airSpeedMax);
        }
    }

    private void HandleGravity(bool grounded)
    {
        if (grounded && _verticalVelocity < 0f) { _verticalVelocity = -1f; return; }
        if (grounded) return;

        float mult = _slamming
            ? _settings.gravityScale
            : _settings.gravityScale * (_verticalVelocity < 0f ? _settings.fallGravityMult : 1f);

        _verticalVelocity += Physics.gravity.y * mult * Time.deltaTime;
    }
}
