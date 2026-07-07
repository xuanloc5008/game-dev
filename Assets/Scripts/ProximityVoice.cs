using System;
using Unity.Netcode;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Manages Vivox 3D positional voice chat per player instance.
/// </summary>
public class ProximityVoice : NetworkBehaviour
{
    // Silence beyond this distance (metres). Channel3DProperties requires int.
    [SerializeField] private int _audibleDistance = 32;

    // Full volume within this distance.
    [SerializeField] private int _conversationalDistance = 8;

    // Optional — assign a child SpriteRenderer named "SpeakerIcon" in the prefab.
    [SerializeField] private SpriteRenderer _speakerIcon;

    // Synced to all clients so each can show/hide the icon above remote players.
    public NetworkVariable<bool> IsSpeaking = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    private bool _inChannel;
    private string _channelName;

    public override void OnNetworkSpawn()
    {
        // All clients: find the icon child and subscribe to the speaking variable.
        if (_speakerIcon == null)
        {
            var child = transform.Find("SpeakerIcon");
            if (child != null) _speakerIcon = child.GetComponent<SpriteRenderer>();
        }
        if (_speakerIcon != null) _speakerIcon.enabled = false;

        IsSpeaking.OnValueChanged += OnSpeakingChanged;

        if (!IsOwner) return;

        // Join voice immediately — works in lobby and gameplay alike.
        JoinVoiceChannelAsync();
    }

    public override void OnNetworkDespawn()
    {
        IsSpeaking.OnValueChanged -= OnSpeakingChanged;

        if (!IsOwner) return;

        if (_inChannel)
            LeaveChannelAsync();
    }

    private void OnSpeakingChanged(bool previous, bool current)
    {
        if (_speakerIcon != null) _speakerIcon.enabled = current;
    }

    private async void JoinVoiceChannelAsync()
    {
        var relay = FindFirstObjectByType<RelayManager>();

        // Solo local host has no join code — skip voice.
        if (relay == null || string.IsNullOrEmpty(relay.CurrentJoinCode))
        {
            Debug.Log("[ProximityVoice] No Relay session — voice skipped for solo play.");
            return;
        }

        // Vivox channel names: alphanumeric only. Prefix "s" so the name never starts with a digit.
        _channelName = "s" + relay.CurrentJoinCode.Replace("-", "").ToLowerInvariant();

        try
        {
            await VivoxService.Instance.JoinPositionalChannelAsync(
                _channelName,
                ChatCapability.AudioOnly,
                new Channel3DProperties(
                    _audibleDistance,
                    _conversationalDistance,
                    1f,
                    AudioFadeModel.InverseByDistance));

            _inChannel = true;
            Debug.Log($"[ProximityVoice] Joined channel '{_channelName}'");
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[ProximityVoice] Channel join failed: {e.Message}");
        }
    }

    private async void LeaveChannelAsync()
    {
        _inChannel = false;
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); }
        catch (Exception e) { Debug.LogWarning($"[ProximityVoice] Leave failed: {e.Message}"); }
    }

    private void Update()
    {
        if (!IsOwner || !_inChannel) return;

        VivoxService.Instance.Set3DPosition(
            transform.position,
            transform.position,
            transform.forward,
            transform.up,
            _channelName,
            false);

        // Poll own speech detection and sync to all clients via NetworkVariable.
        PollSpeaking();
    }

    private void PollSpeaking()
    {
        bool speaking = false;

        try
        {
            if (VivoxService.Instance.ActiveChannels.TryGetValue(_channelName, out var participants))
            {
                foreach (var p in participants)
                {
                    if (p.IsSelf) { speaking = p.SpeechDetected; break; }
                }
            }
        }
        catch (Exception) { /* channel not ready yet */ }

        if (IsSpeaking.Value != speaking)
            IsSpeaking.Value = speaking;
    }
}
