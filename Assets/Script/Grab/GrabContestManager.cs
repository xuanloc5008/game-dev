using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;

public class GrabContestSession
{
    public int Id;

    public ulong GrabberClientId;
    public ulong TargetClientId;

    public int GrabberScore;
    public int TargetScore;

    public double EndTime;
}

public enum ContestState
{
    Contesting,
    Result,
    End
};

public class GrabContestManager : NetworkBehaviour
{
    public static GrabContestManager Instance {get; private set;}
    [SerializeField] private float contestDuration = 8f;

    private Dictionary<int, GrabContestSession> sessions = new();
    private readonly List<int> expiredSessionIds = new();
    public event Action<int, int> OnPowerChange;
    public event Action<ContestState> OnSessionChange;
    public event Action<string> OnResult;
    public event Action<ulong, ulong, bool> OnContestResolvedServer;
    
    private int currentSessionId;
    public ContestState CurrentState { get; private set; }
        = ContestState.End;

    public bool HasActiveSession =>
        CurrentState == ContestState.Contesting;

    private void SetLocalState(ContestState nextState)
    {
        if (CurrentState == nextState)
            return;

        CurrentState = nextState;
        OnSessionChange?.Invoke(nextState);
    }
    
    public double CurrentEndTime { get; private set; }
    private int nextSessionId;

    private void Awake()
    {
        if (Instance != null)
        {
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void Update()
    {
        if (!IsServer || !IsSpawned) return;

        expiredSessionIds.Clear();
        
        double now = NetworkManager.ServerTime.Time;

        foreach (var entry in sessions)
        {
            if (now >= entry.Value.EndTime)
                expiredSessionIds.Add(entry.Key);
        }

        foreach (int sessionId in expiredSessionIds)
            EndContest(sessionId);
    }

    public bool IsPlayerContesting(ulong clientId)
    {
        if (!IsSpawned) return false;

        if (IsServer)
        {
            double now = NetworkManager.ServerTime.Time;
            
            return sessions.Values.Any(session => now < session.EndTime 
                                                  && (session.GrabberClientId == clientId 
                                                      || session.GrabberClientId == clientId));
        }
        
        return IsClient && clientId == NetworkManager.LocalClientId && HasActiveSession;
    }

    public bool TryCreateSessionServer(ulong grabberId, ulong targetId)
    {
        if (!IsServer || !IsSpawned)
            return false;

        if (grabberId == targetId)
            return false;

        if (!NetworkManager.ConnectedClients.ContainsKey(grabberId) ||
            !NetworkManager.ConnectedClients.ContainsKey(targetId))
            return false;

        bool someoneBusy = sessions.Values.Any(session => session.GrabberClientId == grabberId ||
                                                          session.TargetClientId == grabberId ||
                                                          session.GrabberClientId == targetId ||
                                                          session.TargetClientId == targetId);

        if (someoneBusy) return false;

        var newSession = new GrabContestSession
        {
            Id = ++nextSessionId,
            GrabberClientId = grabberId,
            TargetClientId = targetId,
            GrabberScore = 50,
            TargetScore = 50,
            EndTime = NetworkManager.ServerTime.Time + contestDuration
        };
        
        sessions.Add(newSession.Id, newSession);
        
        var recipients = new ClientRpcParams
        {
            Send = new ClientRpcSendParams
            {
                TargetClientIds = new[]
                {
                    grabberId,
                    targetId
                }
            }
        };
        
        StartContestClientRpc(newSession.Id, newSession.EndTime, recipients);
        
        return true;
    }

    public void PointAdd(int point)
    {
        if (!IsClient || !IsSpawned || !HasActiveSession)
            return;
        
        AddPointServerRpc(point, currentSessionId);
    }

    [ServerRpc(RequireOwnership = false)]
    private void AddPointServerRpc(int point, int sessionId, ServerRpcParams rpcParams = default)
    {
        if (!sessions.TryGetValue(sessionId, out var session))
            return;

        if (NetworkManager.ServerTime.Time >= session.EndTime)
            return;

        ulong senderClientId = rpcParams.Receive.SenderClientId;
        
        if (senderClientId == session.GrabberClientId)
        {
            session.GrabberScore += point;
        }
        else if (senderClientId == session.TargetClientId)
        {
            session.TargetScore += point;
        }
        else
        {
            Debug.LogError($"Can't add point to {session.GrabberClientId}");
            return;
        }

        PointSentClientRpc(session.GrabberClientId
                              , session.GrabberScore
                              , session.TargetScore);
        PointSentClientRpc(session.TargetClientId
                              , session.TargetScore
                              , session.GrabberScore);
    }

    [ClientRpc]
    private void StartContestClientRpc(int sessionId, double endTime, ClientRpcParams rpcParams = default)
    {
        currentSessionId = sessionId;
        CurrentEndTime = endTime;
        
        OnPowerChange?.Invoke(50, 50);
        SetLocalState(ContestState.Contesting);
    }
    
    private void EndContest(int sessionId)
    {
        if (!IsServer || !IsSpawned)
            return;

        if (!sessions.TryGetValue(sessionId, out var session))
            return;
        
        sessions.Remove(sessionId);

        bool grabberWins = session.GrabberScore > session.TargetScore;
        
        OnContestResolvedServer?.Invoke(session.GrabberClientId, session.TargetClientId, grabberWins);
        

        EndContestClientRpc(
            session.GrabberClientId,
            sessionId,
            grabberWins ? "Win" : "Lose");

        EndContestClientRpc(
            session.TargetClientId,
            sessionId,
            grabberWins ? "Lose" : "Win");
    }

    [ClientRpc]
    private void EndContestClientRpc(ulong recipientClientId,int sessionId, string ownResult)
    {
        if (recipientClientId != NetworkManager.LocalClientId) return;
        if (sessionId != currentSessionId)
            return;
        OnResult?.Invoke(ownResult);
        SetLocalState(ContestState.Result);

        StartCoroutine(HideResultAfterDelay(sessionId));
    }

    [ClientRpc]
    private void PointSentClientRpc(ulong recipientClientId, int ownScore, int opponentScore)
    {
        if (recipientClientId != NetworkManager.LocalClientId) return;
        OnPowerChange?.Invoke(ownScore, opponentScore);
    }

    private IEnumerator HideResultAfterDelay(int sessionId)
    {
        yield return new WaitForSecondsRealtime(2f);
        
        if (currentSessionId != sessionId ||
            CurrentState != ContestState.Result)
            yield break;
        
        SetLocalState(ContestState.End);
    }
}
