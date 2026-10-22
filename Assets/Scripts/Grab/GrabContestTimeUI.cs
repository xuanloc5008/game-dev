using System;
using TMPro;
using Unity.Netcode;
using UnityEngine;

public class GrabContestTimeUI : MonoBehaviour
{
    [SerializeField] private TMP_Text timeText;
    
    private GrabContestManager grabContestManager;
    private int lastDisplayedSecond = -1;

    private void Start()
    {
        grabContestManager = GrabContestManager.Instance;
        if (grabContestManager == null) return;
        
        grabContestManager.OnSessionChange += HandleSessionChange;
    }

    private void OnDestroy()
    {
        if (grabContestManager == null) return;
        
        grabContestManager.OnSessionChange -= HandleSessionChange;
    }

    private void HandleSessionChange(ContestState state)
    {
        lastDisplayedSecond = -1;

        switch (state)
        {
            case ContestState.Contesting:
                RefreshTime();
                break;
            case ContestState.Result:
                timeText.text = "0";
                break;
            case ContestState.End:
                timeText.text = "0";
                break;
        }
    }

    private void Update()
    {
        if (grabContestManager == null || !grabContestManager.IsSpawned || !grabContestManager.HasActiveSession) return;
        
        RefreshTime();
    }

    private void RefreshTime()
    {
        if (grabContestManager == null || !grabContestManager.IsSpawned) return;

        double now = NetworkManager.Singleton.ServerTime.Time;
        
        float remaining = Mathf.Max(
            0f,
            (float)(grabContestManager.CurrentEndTime - now));
        
        int seconds = Mathf.CeilToInt(remaining);
        
        if (seconds == lastDisplayedSecond)
            return;

        lastDisplayedSecond = seconds;
        timeText.text = seconds.ToString();
    }
}
