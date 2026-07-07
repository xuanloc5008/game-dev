using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

/// <summary>
/// Handles ESC pause menu with voice player list, mute toggles, and leave option.
/// </summary>
public class PauseUI : MonoBehaviour
{
    private VisualElement _pausePanel;
    private ScrollView _playerList;
    private Button _leaveBtn;

    private bool _isPlaying;
    private bool _isPaused;

    private void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _pausePanel = root.Q<VisualElement>("pause-panel");
        _playerList = root.Q<ScrollView>("player-list");
        _leaveBtn = root.Q<Button>("pause-leave-btn");

        if (_leaveBtn != null) _leaveBtn.clicked += OnLeaveClicked;

        GameManager.OnGameStarted += OnGameStarted;
    }

    private void OnDestroy()
    {
        GameManager.OnGameStarted -= OnGameStarted;
        if (VivoxService.Instance != null)
        {
            VivoxService.Instance.ParticipantAddedToChannel -= OnParticipantChanged;
            VivoxService.Instance.ParticipantRemovedFromChannel -= OnParticipantChanged;
        }
    }

    private void OnGameStarted()
    {
        _isPlaying = true;

        // Reactively update the player list whenever someone joins or leaves voice.
        VivoxService.Instance.ParticipantAddedToChannel += OnParticipantChanged;
        VivoxService.Instance.ParticipantRemovedFromChannel += OnParticipantChanged;
    }

    private void OnParticipantChanged(VivoxParticipant _) => RefreshPlayerList();

    private void Update()
    {
        if (!_isPlaying) return;
        if (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame)
            TogglePause();
    }

    private void TogglePause()
    {
        if (_pausePanel == null) return;

        _isPaused = !_isPaused;

        if (_isPaused)
        {
            RefreshPlayerList();
            _pausePanel.style.display = DisplayStyle.Flex;
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;
        }
        else
        {
            _pausePanel.style.display = DisplayStyle.None;
            UnityEngine.Cursor.lockState = CursorLockMode.Locked;
            UnityEngine.Cursor.visible = false;
        }
    }

    private void RefreshPlayerList()
    {
        if (_playerList == null) return;
        _playerList.Clear();

        bool anyAdded = false;

        try
        {
            foreach (var kvp in VivoxService.Instance.ActiveChannels)
            {
                foreach (var participant in kvp.Value)
                {
                    if (participant.IsSelf) continue;

                    var row = new VisualElement();
                    row.style.flexDirection = FlexDirection.Row;
                    row.style.alignItems = Align.Center;
                    row.style.marginBottom = 10;

                    var label = new Label(participant.DisplayName);
                    label.style.color = new StyleColor(Color.white);
                    label.style.fontSize = 18;
                    label.style.flexGrow = 1;

                    var p = participant;
                    var muteBtn = new Button();
                    muteBtn.text = p.IsMuted ? "Unmute" : "Mute";
                    muteBtn.style.width = 80;
                    muteBtn.style.height = 32;
                    muteBtn.style.fontSize = 15;
                    muteBtn.clicked += () =>
                    {
                        if (p.IsMuted) p.UnmutePlayerLocally();
                        else p.MutePlayerLocally();
                        muteBtn.text = p.IsMuted ? "Unmute" : "Mute";
                    };

                    row.Add(label);
                    row.Add(muteBtn);
                    _playerList.Add(row);
                    anyAdded = true;
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[PauseUI] Could not list voice participants: {e.Message}");
        }

        if (!anyAdded)
        {
            var msg = new Label("No other players in voice");
            msg.style.color = new StyleColor(Color.white);
            msg.style.fontSize = 22;
            msg.style.marginBottom = 8;
            _playerList.Add(msg);
        }
    }

    private async void OnLeaveClicked()
    {
        // Unlock cursor before anything async — feels more responsive.
        UnityEngine.Cursor.lockState = CursorLockMode.None;
        UnityEngine.Cursor.visible = true;

        // Cleanly leave Vivox channels and shut down NGO.
        var relay = FindFirstObjectByType<RelayManager>();
        if (relay != null)
            await relay.LeaveAsync();

        // Wait for NGO shutdown to complete before reloading.
        var nm = NetworkManager.Singleton;
        float t = 0f;
        while (nm != null && (nm.ShutdownInProgress || nm.IsListening) && t < 5f)
        {
            await Task.Yield();
            t += Time.deltaTime;
        }

        // Full scene reload — resets all state cleanly without manual teardown.
        SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex);
    }
}
