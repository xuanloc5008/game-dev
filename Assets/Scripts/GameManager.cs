using System;
using Unity.Netcode;
using UnityEngine;

public enum GameState
{
    Lobby,
    Playing
}

/// <summary>
/// Owns game phase state (Lobby/Playing) for all clients via NetworkVariable.
/// Acts as the Subject (Publisher) in the Observer pattern, broadcasting events
/// to decoupled subsystems (Movement, VoiceRecognition, UI, etc.).
/// </summary>
public class GameManager : NetworkBehaviour
{
    public static GameManager Instance { get; private set; }

    // ── Observer Pattern: Subject Events (Publishers) ─────────────────────────
    public static event Action OnGameStarted;
    public static event Action<VoiceVowel, float> OnVoiceRoundConfigured;
    public static event Action OnVoiceRoundReset;

    public NetworkVariable<GameState> CurrentState =
        new NetworkVariable<GameState>(
            GameState.Lobby,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += OnGameStateChanged;

        if (CurrentState.Value == GameState.Playing)
        {
            OnGameStarted?.Invoke();
        }
    }

    public override void OnNetworkDespawn()
    {
        CurrentState.OnValueChanged -= OnGameStateChanged;
    }

    private void OnGameStateChanged(GameState previous, GameState next)
    {
        if (next != GameState.Playing) return;
        OnGameStarted?.Invoke();
    }

    [Rpc(SendTo.Server)]
    public void StartGameRpc()
    {
        if (!IsServer) return;
        CurrentState.Value = GameState.Playing;
    }

    /// <summary>
    /// Host-only: configure the current voice challenge via Observer event.
    /// VoiceVowel.None = accept any of U / I / A.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void ConfigureVoiceRoundRpc(VoiceVowel expectedVowel, float requiredDuration)
    {
        if (!IsServer) return;
        OnVoiceRoundConfigured?.Invoke(expectedVowel, requiredDuration);
    }

    [Rpc(SendTo.Server)]
    public void ResetVoiceRoundRpc()
    {
        if (!IsServer) return;
        OnVoiceRoundReset?.Invoke();
    }
}