using System;
using UnityEngine;
using UnityEngine.UI;

public class StrengthGauge : MonoBehaviour
{
    [SerializeField] private Image ownPower;
    [SerializeField] private Image opponentPower;
    
    private GrabContestManager grabContestManager;
    private void Start()
    {
        grabContestManager = GrabContestManager.Instance;
        if (grabContestManager == null) return;
        grabContestManager.OnPowerChange += OnPowerChanging;
    }

    private void OnDestroy()
    {
        if (grabContestManager == null) return;
        grabContestManager.OnPowerChange -= OnPowerChanging;
    }

    private void OnPowerChanging(int own, int opponent)
    {
        int total = own + opponent;
        ownPower.fillAmount = (float)own / total;
        opponentPower.fillAmount = (float)opponent / total;
    }
}
