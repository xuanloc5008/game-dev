using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Assigns a head material colour to each player based on spawn index.
/// </summary>
public class PlayerColour : NetworkBehaviour
{
    [SerializeField] private Renderer _headRenderer;
    [SerializeField] private Material[] _materials = new Material[3]; //0 team A, 1 team B, 2 None
    private PlayerTeam _playerTeam;
    public override void OnNetworkSpawn()
    {
        _playerTeam = GetComponent<PlayerTeam>();
        if (_playerTeam == null)
        {
            Debug.LogError("[PlayerColour] Missing PlayerTeam!");
            return;
        }

        _playerTeam.onTeamChanged += HandleTeamChanged;
        HandleTeamChanged(_playerTeam.currentTeamId);
    }

    public override void OnNetworkDespawn()
    {
        if (_playerTeam != null)
            _playerTeam.onTeamChanged -= HandleTeamChanged;
    }
    
    [ClientRpc]
    public void ApplyTeamColourClientRpc(TeamId teamId)
    {
        Debug.Log($"[PlayerColour] Apply({teamId}) headRenderer={_headRenderer} matCount={_materials?.Length}");
        if (_headRenderer == null)
        {
            Debug.LogError("[PlayerColour] Missing headRenderer!");
            return;
        }
        int index = (int)teamId;
        if (_materials[index] == null)
        {
            Debug.LogError("[PlayerColour] Missing material!");
            return;
        }
        _headRenderer.material = _materials[index];
        Debug.Log($"[PlayerColour] Material set to {_materials[index].name}");
        return;
    }

    private void HandleTeamChanged(TeamId teamId)
    {
        ApplyTeamColourClientRpc(teamId);
    }
}
