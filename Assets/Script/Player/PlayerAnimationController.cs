using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerAnimationController : MonoBehaviour
{
    [SerializeField] private Animator animator;
    [SerializeField] private List<String> animationParameters = new List<String>();
    
    private Dictionary<string, int> animationParameterHash = new Dictionary<string, int>();
    
    public Dictionary<string, int> AnimationParameterHash => animationParameterHash;

    #region Run animation functions

    public void RunAnimation(int parameterHash, float value)
    {
        animator.SetFloat(parameterHash, value);
    }

    public void RunAnimation(int parameterHash, int value)
    {
        animator.SetInteger(parameterHash, value);
    }

    public void RunAnimation(int parameterHash, bool value)
    {
        animator.SetBool(parameterHash, value);
    }

    public void RunAnimation(int parameterHash)
    {
        animator.SetTrigger(parameterHash);
    }

    #endregion
    
    private void Awake()
    {
        if (animationParameters == null || animationParameters.Count == 0) return;
        foreach (var parameter in animationParameters)
        {
            animationParameterHash[parameter] = Animator.StringToHash(parameter);
        }
    }
}
