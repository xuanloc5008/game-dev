using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Approves connections and spawns players at designated spawn points.
/// </summary>
[DefaultExecutionOrder(-10)]
public class PlayerSpawnManager : MonoBehaviour
{
    [SerializeField] private Transform[] _spawnPoints;

    private int _nextSpawnIndex;

    private void Start()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.NetworkConfig.ConnectionApproval = true;
        NetworkManager.Singleton.ConnectionApprovalCallback += OnApprove;
        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnServerStopped += OnServerStopped;
    }

    private void OnDestroy()
    {
        if (NetworkManager.Singleton == null) return;
        NetworkManager.Singleton.ConnectionApprovalCallback -= OnApprove;
        NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
        NetworkManager.Singleton.OnServerStopped -= OnServerStopped;
    }

    // Approve up to 4 players; tell NGO we handle spawning ourselves.
    private void OnApprove(NetworkManager.ConnectionApprovalRequest req, NetworkManager.ConnectionApprovalResponse res)
    {
        res.Approved = _nextSpawnIndex < _spawnPoints.Length;
        res.CreatePlayerObject = false;
    }

    // Runs on server only — instantiate the player at the next available spawn point.
    private void OnClientConnected(ulong clientId)
    {
        if (!NetworkManager.Singleton.IsServer) return;
        if (_nextSpawnIndex >= _spawnPoints.Length) return;

        int spawnIndex = _nextSpawnIndex++;
        Transform spawn = _spawnPoints[spawnIndex];
        var prefab = NetworkManager.Singleton.NetworkConfig.PlayerPrefab;
        if (prefab == null) { Debug.LogError("[SpawnManager] PlayerPrefab is null on NetworkConfig!"); return; }

        var go = Instantiate(prefab, spawn.position, spawn.rotation);
        var no = go.GetComponent<NetworkObject>();
        if (no == null) { Debug.LogError("[SpawnManager] Player prefab has no NetworkObject!"); return; }

        no.SpawnAsPlayerObject(clientId, destroyWithScene: true);

        var colour = go.GetComponent<PlayerColour>();
        if (colour != null) colour.PlayerIndex.Value = spawnIndex;
    }

    // Reset the index when the session ends so the next host starts fresh.
    private void OnServerStopped(bool _) => _nextSpawnIndex = 0;
}
