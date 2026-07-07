using System;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// Handles lobby UI element visibility, input events, and display state.
/// </summary>
public class LobbyUI : MonoBehaviour
{
    public event Action HostClicked;
    public event Action JoinClicked;
    public event Action StartClicked;
    public event Action LeaveClicked;
    public event Action QuitClicked;

    public string JoinCode { get; private set; } = "";

    private Button _hostBtn, _joinBtn, _startBtn, _leaveBtn, _quitBtn, _copyBtn;
    private TextField _joinCodeField;
    private Label _statusLabel;

    private string _currentJoinCode;

    private static readonly Color32 Green = new Color32(117, 255, 81, 255);
    private static readonly Color32 Red = new Color32(220, 80, 80, 255);
    private static readonly Color32 DarkText = new Color32(8, 8, 14, 255);
    private static readonly Color32 LightText = new Color32(240, 240, 248, 255);

    private void Start()
    {
        var root = GetComponent<UIDocument>().rootVisualElement;

        _hostBtn = root.Q<Button>("host-btn");
        _joinBtn = root.Q<Button>("join-btn");
        _startBtn = root.Q<Button>("start-btn");
        _leaveBtn = root.Q<Button>("leave-btn");
        _quitBtn = root.Q<Button>("quit-btn");
        _copyBtn = root.Q<Button>("copy-btn");
        _joinCodeField = root.Q<TextField>("join-code-field");
        _statusLabel = root.Q<Label>("status-label");

        if (_hostBtn != null) _hostBtn.clicked += () => HostClicked?.Invoke();
        if (_joinBtn != null) _joinBtn.clicked += () => JoinClicked?.Invoke();
        if (_startBtn != null) _startBtn.clicked += () => StartClicked?.Invoke();
        if (_leaveBtn != null) _leaveBtn.clicked += () => LeaveClicked?.Invoke();
        if (_quitBtn != null) _quitBtn.clicked += () => QuitClicked?.Invoke();
        if (_copyBtn != null) _copyBtn.clicked += () => GUIUtility.systemCopyBuffer = _currentJoinCode;

        if (_joinCodeField != null)
            _joinCodeField.RegisterValueChangedCallback(e => JoinCode = e.newValue);

        SetIdle();
    }

    public void SetIdle()
    {
        Show(_hostBtn); Show(_joinBtn); Show(_joinCodeField); Show(_quitBtn); Show(_startBtn);
        Hide(_leaveBtn); Hide(_copyBtn);
        if (_hostBtn != null) { _hostBtn.style.backgroundColor = new StyleColor(Red); _hostBtn.style.color = new StyleColor(LightText); _hostBtn.text = "Host"; }
        if (_statusLabel != null) _statusLabel.text = "Not hosting";
    }

    public void SetHosting(string joinCode)
    {
        _currentJoinCode = joinCode;
        Show(_hostBtn); Show(_startBtn); Show(_quitBtn); Show(_copyBtn);
        Hide(_joinBtn); Hide(_joinCodeField); Hide(_leaveBtn);
        if (_hostBtn != null) { _hostBtn.style.backgroundColor = new StyleColor(Green); _hostBtn.style.color = new StyleColor(DarkText); _hostBtn.text = "Leave"; }
        if (_statusLabel != null) _statusLabel.text = $"Join code: {joinCode}";
    }

    public void SetClient()
    {
        Show(_leaveBtn); Show(_quitBtn);
        Hide(_hostBtn); Hide(_joinBtn); Hide(_joinCodeField); Hide(_startBtn); Hide(_copyBtn);
        if (_statusLabel != null) _statusLabel.text = "Connected to host";
    }

    private void Show(VisualElement el) { if (el != null) el.style.display = DisplayStyle.Flex; }
    private void Hide(VisualElement el) { if (el != null) el.style.display = DisplayStyle.None; }
}
