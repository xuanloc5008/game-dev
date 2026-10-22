using System;
using UnityEngine;
using UnityEngine.InputSystem;

public class GrabContestController : MonoBehaviour
{
    [Header("Input")]
    [SerializeField] private InputAction _inputAction;

    [Header("Settings")] 
    [SerializeField] private int pointAdd = 1;
    
    private GrabContestManager _grabContestManager;

    private void Start()
    {
        _grabContestManager = GrabContestManager.Instance;
        if (_grabContestManager == null)
        {
            Debug.LogError("GrabContestManager not found");
        }
    }

    private void OnEnable()
    {
        _inputAction.performed += OnPress;
        _inputAction.Enable();
    }

    private void OnDisable()
    {
        _inputAction.performed -= OnPress;
        _inputAction.Disable();
    }

    private void OnPress(InputAction.CallbackContext ctx)
    {
        if (_grabContestManager == null)
        {
            Debug.LogError("GrabContestManager not found");
            return;
        }
        _grabContestManager.PointAdd(pointAdd);
    }
}
