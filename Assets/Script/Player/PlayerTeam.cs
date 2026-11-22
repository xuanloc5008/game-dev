using System;
using Unity.Netcode;
using UnityEngine;

public enum TeamId
{
    A,
    B,
    None,
};
public class PlayerTeam : NetworkBehaviour
{
    private NetworkVariable<TeamId> teamId_ = new NetworkVariable<TeamId>(TeamId.None
    , NetworkVariableReadPermission.Everyone
    , NetworkVariableWritePermission.Server);
    
    public TeamId currentTeamId => teamId_.Value;

    public event Action<TeamId> onTeamChanged;

    public override void OnNetworkSpawn()
    {
        teamId_.OnValueChanged += HandleTeamChange;
    }

    public override void OnNetworkDespawn()
    {
        teamId_.OnValueChanged -= HandleTeamChange;
    }

    public void SetTeamServer(TeamId teamId)
    {
        if (!IsServer || !IsSpawned) 
            return;
        
        teamId_.Value = teamId;
    }

    private void HandleTeamChange(TeamId previousTeamId, TeamId newTeamId)
    {
        onTeamChanged?.Invoke(newTeamId);
    }
}
