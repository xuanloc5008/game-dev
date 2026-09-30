using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// ─────────────────────────────────────────────────────────────────────────────
// Types  (enums + struct dùng chung trong toàn module)
// ─────────────────────────────────────────────────────────────────────────────

public enum VoiceTrackState : byte
{
    Idle,
    Detecting,
    Sustaining,
    Completed,
    Interrupted,
    Invalid
}

public enum VoiceEndReason : byte
{
    None,
    Silence,
    VowelChanged,
    TooShort,
    LowConfidence,
    MicrophoneStopped,
    HeartbeatTimeout
}

public readonly struct VoicePlayerSnapshot
{
    public readonly ulong           ClientId;
    public readonly uint            Sequence;
    public readonly VoiceTrackState State;
    public readonly VoiceVowel      Vowel;
    public readonly float           ContinuousDuration;
    public readonly VoiceEndReason  EndReason;
    public readonly bool            IsActive;

    public VoicePlayerSnapshot(
        ulong clientId, uint sequence,
        VoiceTrackState state, VoiceVowel vowel,
        float continuousDuration, VoiceEndReason endReason, bool isActive)
    {
        ClientId           = clientId;
        Sequence           = sequence;
        State              = state;
        Vowel              = vowel;
        ContinuousDuration = continuousDuration;
        EndReason          = endReason;
        IsActive           = isActive;
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// VoiceRoundManager  (server-authoritative state machine)
// ─────────────────────────────────────────────────────────────────────────────

/// <summary>
/// Server-authoritative tracker for simultaneous sustained voice input.
/// Speech recognition runs client-side; this component validates timing,
/// ownership, vowel continuity and heartbeat continuity on the host.
///
/// Decoupled from GameManager via IVoiceSessionGate dependency injection.
/// Call Initialize(gate) right after AddComponent.
/// </summary>
public sealed class VoiceRoundManager : MonoBehaviour
{
    // ── Inner state ───────────────────────────────────────────────────────────
    private sealed class ServerVoiceState
    {
        public SherpaVoiceController Controller;
        public uint            Sequence;
        public VoiceTrackState State;
        public VoiceVowel      Vowel;
        public double          StartedAt;
        public double          LastHeartbeatAt;
        public float           ContinuousDuration;
        public VoiceEndReason  EndReason;
        public bool            IsActive;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    public static VoiceRoundManager Instance { get; private set; }
    public static event Action<VoicePlayerSnapshot> OnPlayerVoiceStateChanged;

    [Header("Server Voice Rules")]
    [SerializeField, Min(0.1f)] private float     _requiredContinuousDuration = 0.65f;
    [SerializeField, Min(0.1f)] private float     _heartbeatTimeout           = 0.45f;
    [SerializeField]            private VoiceVowel _expectedVowel              = VoiceVowel.None;

    public float      RequiredContinuousDuration => _requiredContinuousDuration;
    public VoiceVowel ExpectedVowel              => _expectedVowel;

    // ── Private state ─────────────────────────────────────────────────────────
    private readonly Dictionary<ulong, ServerVoiceState> _states = new();
    private IVoiceSessionGate _gate;

    private bool   IsServerRunning => NetworkManager.Singleton != null && NetworkManager.Singleton.IsServer;
    private double ServerNow       => NetworkManager.Singleton != null
                                       ? NetworkManager.Singleton.ServerTime.Time
                                       : Time.unscaledTimeAsDouble;

    // ── Dependency injection ──────────────────────────────────────────────────
    /// <summary>Inject session gate. Gọi ngay sau AddComponent.</summary>
    public void Initialize(IVoiceSessionGate gate) => _gate = gate;

    // ── Unity lifecycle ───────────────────────────────────────────────────────
    private void Awake()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
    }

    private void Start()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
        if (Instance == this) Instance = null;
    }

    private void Update()
    {
        if (!IsServerRunning || _states.Count == 0) return;

        double now = ServerNow;
        foreach (var pair in _states)
        {
            var state = pair.Value;
            if (!state.IsActive) continue;
            if (now - state.LastHeartbeatAt > _heartbeatTimeout)
                FinishAsInterrupted(pair.Key, state, VoiceEndReason.HeartbeatTimeout, now);
        }
    }

    // ── Public round control ──────────────────────────────────────────────────

    /// <summary>Set round rules. VoiceVowel.None = accept any U/I/A.</summary>
    public void ConfigureRoundServer(VoiceVowel expectedVowel, float requiredDuration)
    {
        if (!IsServerRunning) return;
        _expectedVowel              = expectedVowel;
        _requiredContinuousDuration = Mathf.Max(0.1f, requiredDuration);
        ResetAllServerStates();
    }

    public void ResetAllServerStates()
    {
        if (!IsServerRunning) return;
        foreach (var pair in _states)
        {
            var state = pair.Value;
            state.State              = VoiceTrackState.Idle;
            state.Vowel              = VoiceVowel.None;
            state.ContinuousDuration = 0f;
            state.EndReason          = VoiceEndReason.None;
            state.IsActive           = false;
            Publish(pair.Key, state);
        }
    }

    public bool TryGetSnapshot(ulong clientId, out VoicePlayerSnapshot snapshot)
    {
        if (_states.TryGetValue(clientId, out var state))
        {
            snapshot = CreateSnapshot(clientId, state);
            return true;
        }
        snapshot = default;
        return false;
    }

    // ── Internal RPC handlers (called by SherpaVoiceController) ──────────────

    internal void BeginVoice(
        SherpaVoiceController controller, ulong senderClientId,
        uint sequence, VoiceVowel initialVowel)
    {
        if (!CanAccept(controller, senderClientId)) return;

        double now = ServerNow;
        var state = new ServerVoiceState
        {
            Controller      = controller,
            Sequence        = sequence,
            State           = VoiceTrackState.Detecting,
            Vowel           = VoiceVowel.None,
            StartedAt       = now,
            LastHeartbeatAt = now,
            ContinuousDuration = 0f,
            EndReason       = VoiceEndReason.None,
            IsActive        = true
        };
        _states[senderClientId] = state;
        ApplyVowel(senderClientId, state, initialVowel);
        Publish(senderClientId, state);
    }

    internal void Heartbeat(
        SherpaVoiceController controller, ulong senderClientId,
        uint sequence, VoiceVowel vowel)
    {
        if (!CanAccept(controller, senderClientId) ||
            !_states.TryGetValue(senderClientId, out var state) ||
            state.Sequence   != sequence ||
            state.Controller != controller ||
            !state.IsActive  ||
            state.State == VoiceTrackState.Interrupted ||
            state.State == VoiceTrackState.Invalid) return;

        double now = ServerNow;
        state.LastHeartbeatAt    = now;
        state.ContinuousDuration = (float)Math.Max(0d, now - state.StartedAt);

        if (!ApplyVowel(senderClientId, state, vowel)) return;

        state.State = state.Vowel != VoiceVowel.None &&
                      state.ContinuousDuration >= _requiredContinuousDuration
            ? VoiceTrackState.Completed
            : state.Vowel == VoiceVowel.None
                ? VoiceTrackState.Detecting
                : VoiceTrackState.Sustaining;

        Publish(senderClientId, state);
    }

    internal void EndVoice(
        SherpaVoiceController controller, ulong senderClientId,
        uint sequence, VoiceVowel vowel, VoiceEndReason reason)
    {
        if (!CanAccept(controller, senderClientId) ||
            !_states.TryGetValue(senderClientId, out var state) ||
            state.Sequence != sequence || state.Controller != controller) return;

        double now = ServerNow;
        state.LastHeartbeatAt    = now;
        state.ContinuousDuration = (float)Math.Max(0d, now - state.StartedAt);
        state.IsActive           = false;

        if (state.State == VoiceTrackState.Interrupted &&
            state.EndReason == VoiceEndReason.HeartbeatTimeout)
        { Publish(senderClientId, state); return; }

        bool canValidate = state.State != VoiceTrackState.Completed &&
                           state.State != VoiceTrackState.Interrupted &&
                           state.State != VoiceTrackState.Invalid;
        if (canValidate && !ApplyVowel(senderClientId, state, vowel)) return;

        if (state.State != VoiceTrackState.Completed)
        {
            bool validVowel = state.Vowel != VoiceVowel.None &&
                              (_expectedVowel == VoiceVowel.None || state.Vowel == _expectedVowel);
            if (validVowel && state.ContinuousDuration >= _requiredContinuousDuration)
            { state.State = VoiceTrackState.Completed; state.EndReason = VoiceEndReason.None; }
            else
            { FinishAsInterrupted(senderClientId, state, validVowel ? reason : VoiceEndReason.LowConfidence, now); return; }
        }
        Publish(senderClientId, state);
    }

    internal void ConfirmRecognition(
        SherpaVoiceController controller, ulong senderClientId,
        uint sequence, VoiceVowel vowel)
    {
        if (!CanAccept(controller, senderClientId) || vowel == VoiceVowel.None ||
            !_states.TryGetValue(senderClientId, out var state) ||
            state.Sequence != sequence || state.Controller != controller ||
            state.State == VoiceTrackState.Invalid) return;

        if ((_expectedVowel != VoiceVowel.None && vowel != _expectedVowel) ||
            (state.Vowel    != VoiceVowel.None && state.Vowel != vowel))
        {
            state.State     = VoiceTrackState.Invalid;
            state.EndReason = VoiceEndReason.VowelChanged;
            state.IsActive  = false;
            Publish(senderClientId, state);
            return;
        }

        state.Vowel = vowel;
        if (state.ContinuousDuration >= _requiredContinuousDuration &&
            state.EndReason          != VoiceEndReason.HeartbeatTimeout)
        { state.State = VoiceTrackState.Completed; state.EndReason = VoiceEndReason.None; }

        Publish(senderClientId, state);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private bool ApplyVowel(ulong clientId, ServerVoiceState state, VoiceVowel vowel)
    {
        if (vowel == VoiceVowel.None) return true;

        if (_expectedVowel != VoiceVowel.None && vowel != _expectedVowel)
        { Invalidate(clientId, state, VoiceEndReason.VowelChanged); return false; }

        if (state.Vowel != VoiceVowel.None && state.Vowel != vowel)
        { Invalidate(clientId, state, VoiceEndReason.VowelChanged); return false; }

        state.Vowel = vowel;
        return true;
    }

    private void Invalidate(ulong clientId, ServerVoiceState state, VoiceEndReason reason)
    {
        state.State     = VoiceTrackState.Invalid;
        state.EndReason = reason;
        state.IsActive  = false;
        Publish(clientId, state);
    }

    private void FinishAsInterrupted(ulong clientId, ServerVoiceState state,
                                      VoiceEndReason reason, double now)
    {
        state.ContinuousDuration = (float)Math.Max(0d, now - state.StartedAt);
        state.State              = VoiceTrackState.Interrupted;
        state.EndReason          = reason;
        state.IsActive           = false;
        Publish(clientId, state);
    }

    private void Publish(ulong clientId, ServerVoiceState state)
    {
        state.Controller?.ApplyServerTracking(
            state.State, state.Vowel,
            state.ContinuousDuration, state.EndReason, state.IsActive);

        OnPlayerVoiceStateChanged?.Invoke(CreateSnapshot(clientId, state));
    }

    private static VoicePlayerSnapshot CreateSnapshot(ulong clientId, ServerVoiceState state) =>
        new VoicePlayerSnapshot(clientId, state.Sequence, state.State, state.Vowel,
                                state.ContinuousDuration, state.EndReason, state.IsActive);

    private bool CanAccept(SherpaVoiceController controller, ulong senderClientId) =>
        IsServerRunning &&
        controller != null &&
        controller.IsSpawned &&
        controller.OwnerClientId == senderClientId &&
        (_gate == null || _gate.IsVoiceSessionActive);

    private void OnClientDisconnected(ulong clientId)
    {
        if (IsServerRunning) _states.Remove(clientId);
    }
}
