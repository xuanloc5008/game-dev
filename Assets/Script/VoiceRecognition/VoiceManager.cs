using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Quản lý toàn bộ phiên thi đấu ngân giọng (Voice Session) và luật chơi, đặt tại Server.
/// - Nhận tín hiệu từ GameManager (OnGameStarted) để ra lệnh cho SherpaVoiceController bật Mic.
/// - Nhận báo cáo vi phạm từ các Client khi đứt hơi/đổi âm để xử thua (HandlePlayerDefeat).
/// </summary>
public class VoiceManager : NetworkBehaviour
{
    public static VoiceManager Instance { get; private set; }

    // Observer Event cho UI hoặc các hệ thống khác muốn theo dõi sự kiện thua cuộc
    public static event Action<ulong, VoiceVowel, float, string> OnPlayerDefeated;

    private void Awake()
    {
        Instance = this;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer)
        {
            // Observer: Lắng nghe tín hiệu bắt đầu trận đấu từ GameManager
            GameManager.OnGameStarted += HandleGameStarted;

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback += HandleClientConnected;
            }

            // Nếu Server vào phòng khi trận đấu đã đang diễn ra
            if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameState.Playing)
            {
                HandleGameStarted();
            }
        }
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer)
        {
            GameManager.OnGameStarted -= HandleGameStarted;

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }
    }

    private void OnDestroy()
    {
        if (IsServer)
        {
            GameManager.OnGameStarted -= HandleGameStarted;

            if (NetworkManager != null)
            {
                NetworkManager.OnClientConnectedCallback -= HandleClientConnected;
            }
        }
    }

    // ── 1. Nhận tín hiệu từ GameManager -> Gọi thẳng xuống SherpaVoiceController ──
    private void HandleGameStarted()
    {
        if (!IsServer) return;

        Debug.Log("<color=yellow>[VoiceManager - Server] Nhận tín hiệu OnGameStarted từ GameManager. Ra lệnh cho các người chơi bắt đầu phiên ngân giọng...</color>");

        // Duyệt qua tất cả người chơi và gọi thẳng xuống SherpaVoiceController
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null && client.PlayerObject.TryGetComponent<SherpaVoiceController>(out var voiceController))
            {
                voiceController.StartListeningRpc();
            }
        }
    }

    private void HandleClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        // Nếu trận đấu đang diễn ra mà có người chơi mới spawn, bật Mic cho họ
        if (GameManager.Instance != null && GameManager.Instance.CurrentState.Value == GameState.Playing)
        {
            if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client) &&
                client.PlayerObject != null &&
                client.PlayerObject.TryGetComponent<SherpaVoiceController>(out var voiceController))
            {
                voiceController.StartListeningRpc();
            }
        }
    }

    // ── 2. Nhận báo cáo vi phạm từ Client khi có người đứt hơi / đổi âm ────────
    [Rpc(SendTo.Server)]
    public void ReportDefeatServerRpc(ulong clientId, VoiceVowel vowel, float duration, string reason)
    {
        if (!IsServer) return;
        HandlePlayerDefeat(clientId, vowel, duration, reason);
    }

    // ── 3. Xử lý thua cuộc trên Server và thông báo toàn phòng ─────────────────
    public void HandlePlayerDefeat(ulong clientId, VoiceVowel vowel, float duration, string reason)
    {
        // In trực tiếp ra Terminal trên Server
        Debug.LogError($"<color=red><b>★★★ [KẾT QUẢ - SERVER] Người chơi {clientId} bị thua! ★★★</b></color> (Lý do: {reason} sau {duration:F2}s ngân âm '{vowel}')");

        // Broadcast thông báo thua tới tất cả người chơi (Clients)
        NotifyPlayerDefeatedRpc(clientId, vowel, duration, reason);
    }

    [Rpc(SendTo.Everyone)]
    private void NotifyPlayerDefeatedRpc(ulong clientId, VoiceVowel vowel, float duration, string reason)
    {
        // In ra Terminal trên máy của các Client
        if (!IsServer)
        {
            Debug.LogError($"<color=red><b>★★★ [KẾT QUẢ] Người chơi {clientId} bị thua! ★★★</b></color> (Lý do: {reason} sau {duration:F2}s ngân âm '{vowel}')");
        }

        // Bắn sự kiện Observer cho các module khác (UI, VFX...)
        OnPlayerDefeated?.Invoke(clientId, vowel, duration, reason);
    }
}
