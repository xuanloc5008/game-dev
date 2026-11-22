using Unity.Netcode;
using UnityEngine;

/// <summary>
/// Networked item that can be picked up, held, and dropped by players.
/// </summary>
public class PickupItem : NetworkBehaviour
{
    [SerializeField]
    private float _holdDistance = 3f;

    // Must clear the holder's world-scale capsule radius (~1.6 m at scale 3.19).
    [SerializeField]
    private float _minHoldDistance = 1.8f;

    // Drop the item automatically if it is jammed at minimum distance for this long.
    [SerializeField]
    private float _stuckDropTime = 0.5f;

    public NetworkVariable<ulong> HeldBy = new NetworkVariable<ulong>(
        ulong.MaxValue,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server);

    private Rigidbody _rb;
    private Collider _col;
    private Quaternion _heldRotation = Quaternion.identity;
    private Transform _holderCamPoint;

    // Root of the holding player — used to exclude the holder from surface raycasts.
    private GameObject _holderRoot;

    private float _currentDist;
    private float _distVelocity;
    private float _stuckTimer;

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _col = GetComponent<Collider>();
        _currentDist = _holdDistance;
    }

    public override void OnNetworkSpawn()
    {
        HeldBy.OnValueChanged += OnHeldByChanged;
        OnHeldByChanged(ulong.MaxValue, HeldBy.Value);
    }

    public override void OnNetworkDespawn()
    {
        HeldBy.OnValueChanged -= OnHeldByChanged;
    }

    private void OnHeldByChanged(ulong previous, ulong current)
    {
        bool held = current != ulong.MaxValue;

        _col.enabled = !held;

        if (held)
        {
            // Clear velocities before making kinematic — Unity 6 blocks writes on kinematic bodies.
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
        }

        _rb.isKinematic = held;
        _rb.interpolation = held ? RigidbodyInterpolation.None
                                 : RigidbodyInterpolation.Interpolate;
    }

    private void LateUpdate()
    {
        if (!IsServer || HeldBy.Value == ulong.MaxValue) return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(HeldBy.Value, out var client)
            || client.PlayerObject == null)
        {
            HeldBy.Value = ulong.MaxValue;
            return;
        }

        var camPoint = _holderCamPoint != null ? _holderCamPoint : client.PlayerObject.transform;
        var origin = camPoint.position;
        var forward = camPoint.forward;

        // RaycastAll so we can skip hits on the holder's own body before checking geometry.
        float rawTarget = _holdDistance;
        var hits = Physics.RaycastAll(origin, forward, _holdDistance);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var h in hits)
        {
            if (_holderRoot != null && h.collider.transform.IsChildOf(_holderRoot.transform))
                continue;

            rawTarget = Mathf.Max(h.distance - 0.15f, _minHoldDistance);
            break;
        }

        // Auto-drop when jammed at minimum distance — item is stuck against geometry.
        if (rawTarget <= _minHoldDistance + 0.01f)
        {
            _stuckTimer += Time.deltaTime;
            if (_stuckTimer >= _stuckDropTime)
            {
                _holderCamPoint = null;
                _holderRoot = null;
                _heldRotation = Quaternion.identity;
                _stuckTimer = 0f;
                HeldBy.Value = ulong.MaxValue;
                return;
            }
        }
        else
        {
            _stuckTimer = 0f;
        }

        // Shrink quickly when approaching a surface, expand slowly when clearing one.
        float smoothTime = rawTarget < _currentDist ? 0.04f : 0.3f;
        _currentDist = Mathf.SmoothDamp(_currentDist, rawTarget, ref _distVelocity, smoothTime);

        transform.position = origin + forward * _currentDist;
        transform.rotation = client.PlayerObject.transform.rotation * _heldRotation;
    }

    [Rpc(SendTo.Server)]
    public void SetHeldRotationRpc(Quaternion rotation) => _heldRotation = rotation;

    [Rpc(SendTo.Server)]
    public void PickUpRpc(ulong clientId)
    {
        if (HeldBy.Value != ulong.MaxValue) return;

        if (!NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client)
            || client.PlayerObject == null) return;

        _holderCamPoint = client.PlayerObject.transform.Find("CameraPoint");
        _holderRoot = client.PlayerObject.gameObject;
        _currentDist = _holdDistance;
        _distVelocity = 0f;
        _stuckTimer = 0f;
        HeldBy.Value = clientId;
    }

    [Rpc(SendTo.Server)]
    public void DropRpc()
    {
        if (HeldBy.Value == ulong.MaxValue) return;

        Vector3 throwDir = _holderCamPoint != null ? _holderCamPoint.forward : Vector3.forward;

        _holderCamPoint = null;
        _holderRoot = null;
        _heldRotation = Quaternion.identity;
        _stuckTimer = 0f;
        HeldBy.Value = ulong.MaxValue;
        _rb.AddForce(throwDir * 4f, ForceMode.Impulse);
    }
}
