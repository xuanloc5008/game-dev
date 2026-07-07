using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Bridges PlayerMovement with NGO ownership and game state events.
/// </summary>
public class PlayerNetworkBridge : NetworkBehaviour
{
    private PlayerMovement _movement;

    private void Awake() => _movement = GetComponent<PlayerMovement>();

    /// <summary>
    /// Handling logic for not allowing the player to roam in the menu state.
    /// </summary>
    public override void OnNetworkSpawn()
    {
        if (!IsOwner)
        {
            // Remote players must be on Default layer so other FPS cameras can see them.
            int defaultLayer = LayerMask.NameToLayer("Default");
            foreach (var t in GetComponentsInChildren<Transform>(true))
                t.gameObject.layer = defaultLayer;

            var pi = GetComponent<PlayerInput>();
            if (pi != null) pi.enabled = false;

            _movement.enabled = false;
            return;
        }

        GameManager.OnGameStarted += _movement.StartPlaying;

        // Already playing (e.g. reconnect or late spawn).
        if (GameManager.Instance != null &&
            GameManager.Instance.CurrentState.Value == GameState.Playing)
            _movement.StartPlaying();
    }

    public override void OnNetworkDespawn()
    {
        if (!IsOwner) return;
        GameManager.OnGameStarted -= _movement.StartPlaying;
    }
}
