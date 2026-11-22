using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;

public struct ClientTeamId
{
    public TeamId TeamId;
    public int SlotIndex;

    public ClientTeamId(TeamId teamId, int slotIndex)
    {
        TeamId = teamId;
        SlotIndex = slotIndex;
    }
}

public class TeamManager : NetworkBehaviour
{
    private readonly Dictionary<ulong, ClientTeamId> assignments = new();
    public const int MAX_TEAM_CAPACITY = 4;

    public bool TryReserveTeamServer(ulong clientId, out ClientTeamId assignment)
    {
        assignment = default;

        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
            return false;
        
        if (assignments.TryGetValue(clientId, out assignment))
            return true;
        
        int aCount = assignments.Values.Count(entry => entry.TeamId == TeamId.A);
        int bCount = assignments.Values.Count(entry => entry.TeamId == TeamId.B);
        
        if (aCount >= MAX_TEAM_CAPACITY && bCount >= MAX_TEAM_CAPACITY)
            return false;
        
        TeamId selectedTeamId = aCount <= bCount ? TeamId.A : TeamId.B;

        for (int slot = 0; slot < MAX_TEAM_CAPACITY; slot++)
        {
            bool occupied = assignments.Values.Any(entry => entry.TeamId == selectedTeamId
                                                                    && entry.SlotIndex == slot);
            if (occupied)
                continue;

            assignment = new ClientTeamId(selectedTeamId, slot);
            assignments.Add(clientId, assignment);
            
            return true;
        }

        return false;
    }

    public bool TryGetAssignmentServer(ulong clientId, out ClientTeamId assignment)
    {
        assignment = default;
        
        var manager = NetworkManager.Singleton;
        if (manager == null || !manager.IsServer)
            return false;
        
        return assignments.TryGetValue(clientId, out assignment);
    }
}
