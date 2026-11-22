using System;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerCameraController : MonoBehaviour
{
    [Header("Third Person Camera")]
    [SerializeField] private CinemachineCamera thirdPersonCamera;
    [SerializeField] private CinemachineOrbitalFollow thirdPersonOrbitalFollow;
    [SerializeField] private CinemachineInputAxisController thirdPersonInputAxisController;
    [Header("Settings")]
    [SerializeField] private float mouseSensitivity = 2f;
    [SerializeField] private float scrollSpeed = 2f;
    [SerializeField] private float zoomLerpSpeed = 10f;
    [SerializeField] private float minDist = 3f;
    [SerializeField] private float maxDist = 15f;
    
    private Vector2 _scrollDelta;
    private float _currentZoom;
    private float _targetZoom;

    public Camera CurrentCamera { get; private set; }

    private void Start()
    {
        if (thirdPersonOrbitalFollow == null) return;
        _targetZoom = _currentZoom = thirdPersonOrbitalFollow.Radius;
        
        if (!thirdPersonCamera) return;
        
        CurrentCamera = Camera.main;
        SetCameraAxisGain(mouseSensitivity);
    }
    
    

    #region On event callback functions

    public void OnScroll(InputAction.CallbackContext ctx)
    {
        _scrollDelta = ctx.ReadValue<Vector2>();
    }

    #endregion

    #region Handler functions

    public void HandleZoom()
    {
        if (_scrollDelta.y == 0 || thirdPersonOrbitalFollow == null || CurrentCamera != thirdPersonCamera) return;
        _targetZoom = Mathf.Clamp(thirdPersonOrbitalFollow.Radius - _scrollDelta.y * scrollSpeed, minDist, maxDist);
        _scrollDelta = Vector2.zero;
        
        _currentZoom = Mathf.Lerp(_currentZoom, _targetZoom, Time.fixedDeltaTime * zoomLerpSpeed);
        thirdPersonOrbitalFollow.Radius =  _currentZoom;
    }

    #endregion

    private void SetCameraAxisGain(float value)
    {
        foreach (var axisController in thirdPersonInputAxisController.Controllers)
        {
            axisController.Input.Gain = value;
        }
    }

    private void Update()
    {
        HandleZoom();
    }
}
