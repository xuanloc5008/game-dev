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
/// Implements IVoiceSessionGate so the VoiceRecognition module stays decoupled.
/// </summary>
public class GameManager : NetworkBehaviour, 
{
    public static GameManager Instance { get; private set; }

    // C# event — safe to subscribe before the network starts.
    public static event Action OnGameStarted;

    // IVoiceSessionGate implementation
    public bool IsVoiceSessionActive =>
        CurrentState.Value == GameState.Playing;

    public event Action OnSessionActivated;

    public NetworkVariable<GameState> CurrentState =
        new NetworkVariable<GameState>(
            GameState.Lobby,
            NetworkVariableReadPermission.Everyone,
            NetworkVariableWritePermission.Server);

    private void Awake()
    {
        Instance = this;
    }

    private void Start()
    {
        // "You may not pass in objects that are already persistent"
        // (NGO mark NetworkBehaviour GameObject là persistent trước Awake)
        var vrm = GetComponent<VoiceRoundManager>();
        if (vrm == null)
            vrm = gameObject.AddComponent<VoiceRoundManager>();

        vrm.Initialize(this);
    }

    public override void OnNetworkSpawn()
    {
        CurrentState.OnValueChanged += OnGameStateChanged;

        if (CurrentState.Value == GameState.Playing)
        {
            OnGameStarted?.Invoke();
            OnSessionActivated?.Invoke();
        }
    }

    private void OnGameStateChanged(GameState previous, GameState next)
    {
        if (next != GameState.Playing) return;
        OnGameStarted?.Invoke();
        OnSessionActivated?.Invoke();
    }

    [Rpc(SendTo.Server)]
    public void StartGameRpc()
    {
        if (!IsServer) return;
        CurrentState.Value = GameState.Playing;
    }

    /// <summary>
    /// Host-only: configure the current voice challenge.
    /// VoiceVowel.None = accept any of U / I / A.
    /// </summary>
    [Rpc(SendTo.Server)]
    public void ConfigureVoiceRoundRpc(VoiceVowel expectedVowel, float requiredDuration)
    {
        if (!IsServer) return;
        VoiceRoundManager.Instance?.ConfigureRoundServer(expectedVowel, requiredDuration);
    }

    [Rpc(SendTo.Server)]
    public void ResetVoiceRoundRpc()
    {
        if (!IsServer) return;
        VoiceRoundManager.Instance?.ResetAllServerStates();
    }
}