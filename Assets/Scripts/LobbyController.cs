using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Coordinates lobby flow between UI, Relay, and NetworkManager.
/// </summary>
public class LobbyController : MonoBehaviour
{
    private LobbyUI _ui;
    private RelayManager _relay;

    private async void Start()
    {
        _ui = FindFirstObjectByType<LobbyUI>();
        _relay = FindFirstObjectByType<RelayManager>();

        // Wire UI and NGO events immediately — Quit works from frame 1.
        _ui.HostClicked += OnHost;
        _ui.JoinClicked += OnJoin;
        _ui.LeaveClicked += OnLeave;
        _ui.StartClicked += OnStart;
        _ui.QuitClicked += () => Application.Quit();

        GameManager.OnGameStarted += OnGameStarted;
        NetworkManager.Singleton.OnTransportFailure += OnTransportFailure;

        // Start local host first — P1 spawns instantly without waiting for Unity Services.
        StartLocalHost();

        // Init Unity Services (auth + Vivox) in the background.
        await _relay.InitializeAsync();
    }

    private void OnDestroy()
    {
        GameManager.OnGameStarted -= OnGameStarted;
        if (NetworkManager.Singleton != null)
            NetworkManager.Singleton.OnTransportFailure -= OnTransportFailure;
    }

    private async void OnHost()
    {
        if (_relay.IsHosting)
        {
            // Already on Relay — leave it and drop back to local solo host.
            await _relay.LeaveAsync();
            await WaitForShutdown();
            StartLocalHost();
            _ui.SetIdle();
        }
        else
        {
            // Leave local host, create a Relay session for others to join.
            NetworkManager.Singleton.Shutdown();
            await WaitForShutdown();
            try
            {
                string joinCode = await _relay.StartHostAsync();
                _ui.SetHosting(joinCode);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Host failed: {e.Message}");
                StartLocalHost();
                _ui.SetIdle();
            }
        }
    }

    private async void OnJoin()
    {
        // Leave local host, then join someone else's Relay session.
        NetworkManager.Singleton.Shutdown();
        await WaitForShutdown();
        try
        {
            await _relay.JoinAsync(_ui.JoinCode);
            _ui.SetClient();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Join failed: {e.Message}");
            StartLocalHost();
            _ui.SetIdle();
        }
    }

    private async void OnLeave()
    {
        // Leave Relay client session, return to solo local host.
        await _relay.LeaveAsync();
        await WaitForShutdown();
        StartLocalHost();
        _ui.SetIdle();
    }

    // If the relay transport dies (e.g. network drop), recover gracefully.
    private async void OnTransportFailure()
    {
        Debug.LogWarning("[LobbyController] Transport failure — returning to local host.");

        // Clean up voice before the network session fully collapses.
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); }
        catch (System.Exception e) { Debug.LogWarning($"[LobbyController] Vivox leave failed: {e.Message}"); }

        _relay.ResetIsHosting();
        await WaitForShutdown();
        StartLocalHost();
        _ui.SetIdle();
    }

    // Yield until NGO has fully completed its deferred shutdown.
    private static async Task WaitForShutdown()
    {
        var nm = NetworkManager.Singleton;
        float timer = 0f;
        while ((nm.ShutdownInProgress || nm.IsListening) && timer < 5f)
        {
            await Task.Yield();
            timer += Time.deltaTime;
        }
    }

    private void OnStart()
    {
        // NetworkManager is always running (local or Relay) — just start the game.
        GameManager.Instance.StartGameRpc();
    }

    private void OnGameStarted()
    {
        _ui.gameObject.SetActive(false);
    }

    // Use port 0 for local-only sessions so the OS picks a free port.
    private void StartLocalHost()
    {
        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        if (transport != null)
            transport.SetConnectionData("127.0.0.1", 0);
        NetworkManager.Singleton.StartHost();
    }
}
