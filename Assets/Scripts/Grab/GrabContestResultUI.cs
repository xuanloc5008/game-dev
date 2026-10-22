using System;
using TMPro;
using UnityEngine;

public class GrabContestResultUI : MonoBehaviour
{
    [SerializeField] private TMP_Text resultText;
    
    private GrabContestManager grabContestManager;

    private void Start()
    {
        resultText.text = "";
        grabContestManager = GrabContestManager.Instance;
        if (grabContestManager == null) return;

        grabContestManager.OnResult += OnResultShow;
    }

    private void OnDestroy()
    {
        if (grabContestManager == null) return;
        grabContestManager.OnResult -= OnResultShow;
    }

    private void OnResultShow(string result)
    {
        resultText.text = result;
    }
}
