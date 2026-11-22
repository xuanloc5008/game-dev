using System;
using UnityEngine;

public class GrabContestUIManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private CanvasGroup grabContestUICanvasGroup;
    [SerializeField] private CanvasGroup timerCanvasGroup;
    [SerializeField] private CanvasGroup strengthGaugeCanvasGroup;
    [SerializeField] private CanvasGroup resultCanvasGroup;
    
    private GrabContestManager grabContestManager;

    private void Awake()
    {
        if (grabContestUICanvasGroup == null)
        {
            grabContestUICanvasGroup = GetComponent<CanvasGroup>();
            grabContestUICanvasGroup.alpha = 0;
            grabContestUICanvasGroup.interactable = false;
            grabContestUICanvasGroup.blocksRaycasts = false;
        }

        if (timerCanvasGroup == null)
        {
            Debug.LogError("GrabContestUIManager is missing timerCanvasGroup");
        }

        if (strengthGaugeCanvasGroup == null)
        {
            Debug.LogError("GrabContestUIManager is missing strengthGaugeCanvasGroup");
        }
    }

    private void Start()
    {
        grabContestManager = GrabContestManager.Instance;
        if (grabContestManager == null)
        {
            Debug.LogError("GrabContestManager is missing grabContestManager");
            return;
        }
        
        grabContestManager.OnSessionChange += HandleSessionChange;
        HandleSessionChange(grabContestManager.CurrentState);
     }

    private void OnDisable()
    {
        grabContestManager = GrabContestManager.Instance;
        if (grabContestManager == null)
        {
            Debug.LogError("GrabContestManager is missing grabContestManager");
            return;
        }
        grabContestManager.OnSessionChange -= HandleSessionChange;
    }

    private void HandleSessionChange(ContestState state)
    {
        if (grabContestUICanvasGroup == null)
        {
            Debug.LogError("GrabContestUIManager is missing grabContestUICanvasGroup");
            return;
        }

        switch (state)
        {
            case ContestState.Contesting:
                //General UI
                grabContestUICanvasGroup.alpha = 1;
                grabContestUICanvasGroup.interactable = true;
                grabContestUICanvasGroup.blocksRaycasts = true;
                
                //Timer UI
                timerCanvasGroup.alpha = 1;
                timerCanvasGroup.interactable = true;
                timerCanvasGroup.blocksRaycasts = true;
                
                //StrengthGauge UI
                strengthGaugeCanvasGroup.alpha = 1;
                strengthGaugeCanvasGroup.interactable = true;
                strengthGaugeCanvasGroup.blocksRaycasts = true;
                
                //Result UI
                resultCanvasGroup.alpha = 0;
                resultCanvasGroup.interactable = false;
                resultCanvasGroup.blocksRaycasts = false;
                break;
            case ContestState.Result:
                //Timer UI
                timerCanvasGroup.alpha = 0;
                timerCanvasGroup.interactable = false;
                timerCanvasGroup.blocksRaycasts = false;
                
                //StrengthGauge UI
                strengthGaugeCanvasGroup.alpha = 0;
                strengthGaugeCanvasGroup.interactable = false;
                strengthGaugeCanvasGroup.blocksRaycasts = false;
                
                //Result UI
                resultCanvasGroup.alpha = 1;
                resultCanvasGroup.interactable = true;
                resultCanvasGroup.blocksRaycasts = true;
                break;
            case ContestState.End:
                //General UI
                grabContestUICanvasGroup.alpha = 0;
                grabContestUICanvasGroup.interactable = false;
                grabContestUICanvasGroup.blocksRaycasts = false;
                break;
            default:
                Debug.LogError("This state is not implemented");
                break;
        }
    }
}
