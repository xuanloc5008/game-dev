using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

[Serializable]
public struct SpawnPoint
{
    public TeamId TeamId;
    public int SlotIdx;
    public Transform SpawnTransform;
}

/// <summary>
/// Approves connections and spawns players at designated spawn points.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerSpawnManager : NetworkBehaviour
{
    [SerializeField] private SpawnPoint[] _spawnPoints;
    [SerializeField] private TeamManager _teamManager;
    public const int MAX_PLAYER = 8;

    private NetworkList<ulong> _playerList;
    public event Action OnPlayerListChanged;
    public int PlayerCount => _playerList.Count;
    public ulong GetPlayerId(int index) => _playerList[index];
    private NetworkManager _networkManager;
    private void Awake()
    {
        _playerList = new NetworkList<ulong>();
    }
    private void Start()
    {
        _networkManager = NetworkManager.Singleton;
        if (_networkManager == null) return;
        _networkManager.NetworkConfig.ConnectionApproval = true;
        _networkManager.ConnectionApprovalCallback += OnApprove;
        _networkManager.OnClientConnectedCallback += OnClientConnected;
        _networkManager.OnClientDisconnectCallback += OnClientDisconnected;
        _networkManager.OnServerStopped += OnServerStopped;
    }

    private void OnDestroy()
    {
        if (_networkManager == null) return;
        _networkManager.ConnectionApprovalCallback -= OnApprove;
        _networkManager.OnClientConnectedCallback -= OnClientConnected;
        _networkManager.OnClientDisconnectCallback -= OnClientDisconnected;
        _networkManager.OnServerStopped -= OnServerStopped;
        base.OnDestroy();
    }

    public override void OnNetworkSpawn()
    {
        _playerList.OnListChanged += HandListChange;
        if (IsServer)
        {
            foreach (ulong playerId in _networkManager.ConnectedClientsIds)
                OnClientConnected(playerId);
        }
        OnPlayerListChanged?.Invoke();
    }

    public override void OnNetworkDespawn()
    {
        _playerList.OnListChanged -= HandListChange;
        OnPlayerListChanged?.Invoke();
    }

    private void HandListChange(NetworkListEvent<ulong> changeEvent)
    {
        OnPlayerListChanged?.Invoke();
    }

    // Approve up to 4 players; tell NGO we handle spawning ourselves.
    private void OnApprove(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        res.CreatePlayerObject = false;
        var gameManager = GameManager.Instance;
        res.Approved = gameManager != null &&
                       gameManager.CurrentState.Value == GameState.Lobby &&
                       _networkManager.ConnectedClientsIds.Count < MAX_PLAYER;
        if (!res.Approved)
            res.Reason = "Lobby is full or playing";
    }

    // Runs on server only — instantiate the player at the next available spawn point.
    private void OnClientConnected(ulong clientId)
    {
        if (!IsServer) return;

        if (!_playerList.Contains(clientId))
            _playerList.Add(clientId);
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (!IsServer) return;
        if (!_playerList.Contains(clientId))
            _playerList.Remove(clientId);
    }

    // Clear the list when the session ends so the next host starts fresh.
    private void OnServerStopped(bool _) => _playerList.Clear();

    [ServerRpc]
    public void TrySpawnPlayersServerRpc()
    {
        List<ulong> assignedPlayersList = new List<ulong>();

        for (int i = 0; i < _playerList.Count; i++)
        {
            int randomIndex;
            do
            {
                randomIndex = UnityEngine.Random.Range(0, _playerList.Count);
            }while (assignedPlayersList.Contains(_playerList[randomIndex]));

            if (!_teamManager.TryReserveTeamServer(_playerList[randomIndex], out var assignment))
                return;
            assignedPlayersList.Add(_playerList[randomIndex]);
            
            var spawnPoint = _spawnPoints.FirstOrDefault(s => s.TeamId == assignment.TeamId &&
                                                                         s.SlotIdx == assignment.SlotIndex);
            var prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
            if (prefab == null)
            {
                Debug.LogError("Player has no prefab");
                return;
            }
            
            var player = Instantiate(prefab, 
                                                spawnPoint.SpawnTransform.position, 
                                                spawnPoint.SpawnTransform.rotation);
            if (!player.TryGetComponent<NetworkObject>(out var playerNetwork))
            {
                Debug.LogError("[SpawnManager] Player prefab has no NetworkObject!");
                return;
            }
            
            playerNetwork.SpawnAsPlayerObject(_playerList[randomIndex], destroyWithScene: true);
            if (!player.TryGetComponent<PlayerColour>(out var playerColour))
            {
                Debug.LogError("[SpawnManager] Player prefab has no PlayerColour!");
                return;
            }

            playerColour.ApplyTeamColourClientRpc(assignment.TeamId);
        }
    }
}
