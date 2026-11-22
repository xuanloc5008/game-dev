using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Vivox;
using UnityEngine;

/// <summary>
/// Manages Unity Relay connections for hosting and joining multiplayer sessions.
/// </summary>
public class RelayManager : MonoBehaviour
{
    private const int MaxPlayers = 4;

    public bool IsHosting { get; private set; }
    public string CurrentJoinCode { get; private set; }

    public async Task InitializeAsync()
    {
        await UnityServices.InitializeAsync();

#if UNITY_EDITOR
        if (!AuthenticationService.Instance.IsSignedIn)
            AuthenticationService.Instance.SwitchProfile(
                $"Editor_{System.Diagnostics.Process.GetCurrentProcess().Id}");
#endif

        if (!AuthenticationService.Instance.IsSignedIn)
            await AuthenticationService.Instance.SignInAnonymouslyAsync();

        // Guard against re-login on scene reload — Vivox persists as a singleton.
        if (!VivoxService.Instance.IsLoggedIn)
        {
            string displayName = "Player_" + AuthenticationService.Instance.PlayerId[..6];
            await VivoxService.Instance.LoginAsync(new LoginOptions { DisplayName = displayName });
        }
    }

    public async Task<string> StartHostAsync()
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(MaxPlayers - 1);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetHostRelayData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.Key,
            allocation.ConnectionData,
            isSecure: false);

        CurrentJoinCode = joinCode;
        IsHosting = true;
        NetworkManager.Singleton.StartHost();
        return joinCode;
    }

    public async Task JoinAsync(string joinCode)
    {
        CurrentJoinCode = joinCode.Trim();
        JoinAllocation join = await RelayService.Instance.JoinAllocationAsync(CurrentJoinCode);

        var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
        transport.SetClientRelayData(
            join.RelayServer.IpV4,
            (ushort)join.RelayServer.Port,
            join.AllocationIdBytes,
            join.Key,
            join.ConnectionData,
            join.HostConnectionData,
            isSecure: false);

        NetworkManager.Singleton.StartClient();
    }

    public async Task LeaveAsync()
    {
        // Leave any active Vivox channels before shutting down the network session.
        try { await VivoxService.Instance.LeaveAllChannelsAsync(); }
        catch (Exception e) { Debug.LogWarning($"[RelayManager] Vivox leave failed: {e.Message}"); }

        CurrentJoinCode = null;
        NetworkManager.Singleton.Shutdown();
        IsHosting = false;
    }

    public void ResetIsHosting() => IsHosting = false;
}
