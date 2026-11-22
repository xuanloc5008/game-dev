using Unity.Netcode;
using Unity.VisualScripting;
using UnityEngine;
using UnityEngine.Animations.Rigging;

public class PlayerGrabber : NetworkBehaviour
{
    [SerializeField] private float grabRadius = 1f;
    [SerializeField] private Transform handPoint;
    [SerializeField] private LayerMask grabLayer;
    [SerializeField] private PlayerInputReader inputReader;
    [SerializeField] private Transform handIKTarget;
    [SerializeField] private PlayerAnimationController playerAnimationController;
    
    private IGrable _currentlyGrabbedObject;
    private Transform handIKTargetParent;
    private GrabContestManager _grabContestManager;

    private void Awake()
    {
        inputReader.OnPlayerGrab += HandlePlayerPressButtonGrab;
        handIKTargetParent = handIKTarget.parent;
    }

    public override void OnNetworkSpawn()
    {
        if (!IsServer) return;
        
        _grabContestManager = GrabContestManager.Instance;
        if (_grabContestManager == null) return;
        _grabContestManager.OnContestResolvedServer += HandleContestResolved;
    }

    public override void OnNetworkDespawn()
    {
        inputReader.OnPlayerGrab -= HandlePlayerPressButtonGrab;
        if (_grabContestManager == null) return;
        _grabContestManager.OnContestResolvedServer -= HandleContestResolved;
    }

    private void OnDrawGizmos()
    {
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(transform.position, grabRadius);
    }

    private void HandlePlayerPressButtonGrab()
    {
        PlayerGrabServerRpc();
    }

    private void HandleContestResolved(ulong grabberId, ulong targetId, bool grabberWin)
    {
        if (!IsServer) return;

        if (OwnerClientId != grabberId) return;

        var target = _currentlyGrabbedObject as PlayerGrabTarget;
        if (target == null || target.OwnerClientId != targetId)
            return;

        if (grabberWin)
        {
            var targetInputReader =
                target.GetComponent<PlayerInputReader>();

            if (targetInputReader != null)
                targetInputReader.SetStopInputServer(true);
        }
        else
        {
            PlayerDropServerRpc();
        }
    }

    [ServerRpc]
    public void PlayerGrabServerRpc()
    {
        Collider[] collidersArray= Physics.OverlapSphere(transform.position, grabRadius , grabLayer);
        foreach (var collide in collidersArray)
        {
            if (collide.TryGetComponent<IGrable>(out IGrable grabbedObject))
            {
                var targetPlayer = collide.GetComponentInParent<PlayerGrabTarget>();
                if (targetPlayer == null) continue;
                
                var targetInputReader = collide.GetComponentInParent<PlayerInputReader>();
                if (targetInputReader == null) continue;

                var manager = GrabContestManager.Instance;

                if (manager == null ||
                    !manager.TryCreateSessionServer(
                        OwnerClientId,
                        targetPlayer.OwnerClientId))
                {
                    continue;
                }
                if (!grabbedObject.CanBeGrabbed()) continue;
                _currentlyGrabbedObject = grabbedObject;
                ConfigurableJoint grabJoint = handPoint.AddComponent<ConfigurableJoint>();
                grabbedObject.OnGrab(grabJoint, NetworkObjectId);
                handIKTarget.position = collide.transform.position;
                handIKTarget.SetParent(collide.transform);
                playerAnimationController.RunAnimation(playerAnimationController.AnimationParameterHash["IsGrabbing"], true);
            }
        }
    }

    [ServerRpc]
    private void PlayerDropServerRpc()
    {
        _currentlyGrabbedObject.OnDrop();
        _currentlyGrabbedObject = null;
        handIKTarget.SetParent(handIKTargetParent);
        playerAnimationController.RunAnimation(playerAnimationController.AnimationParameterHash["IsGrabbing"], false);
    }
    
}
