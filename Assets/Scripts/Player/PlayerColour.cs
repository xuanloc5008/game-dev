using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Assigns a head material colour to each player based on spawn index.
/// </summary>
public class PlayerColour : NetworkBehaviour
{
    [SerializeField] private Renderer _headRenderer;
    [SerializeField] private Material[] _materials; // index 0=Blue 1=Pink 2=Green 3=Yellow

    public NetworkVariable<int> PlayerIndex = new NetworkVariable<int>(
        -1,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    public override void OnNetworkSpawn()
    {
        PlayerIndex.OnValueChanged += OnIndexChanged;

        // Value may already be set if we're a late-joining observer.
        if (PlayerIndex.Value >= 0)
            Apply(PlayerIndex.Value);
    }

    public override void OnNetworkDespawn()
    {
        PlayerIndex.OnValueChanged -= OnIndexChanged;
    }

    private void OnIndexChanged(int _, int current)
    {
        if (current >= 0) Apply(current);
    }

    private void Apply(int index)
    {
        Debug.Log($"[PlayerColour] Apply({index}) headRenderer={_headRenderer} matCount={_materials?.Length}");
        if (_headRenderer == null || index < 0 || index >= _materials.Length) return;
        if (_materials[index] == null) return;
        _headRenderer.material = _materials[index];
        Debug.Log($"[PlayerColour] Material set to {_materials[index].name}");
    }
}
