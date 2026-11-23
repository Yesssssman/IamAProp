using System;
using System.Collections.Generic;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>바닥에 떨어진 월드 무기 아이템의 <c>WeaponId</c>, 남은 탄약 및 복합 물리 콜라이더·비주얼을 네트워크 동기화한다.</summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
public class WorldWeaponPickup : NetworkBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float MinColliderAxis = 0.025f; // 얇은 총열/칼날이 바닥을 관통하지 않도록 보장하는 최소 콜라이더 두께 (미터)
    private const float FallbackMinColliderAxis = 0.12f; // 커스텀 콜라이더가 없을 때 자동 생성하는 최소 박스 두께 (미터)
    private const int PickupLayer = 2; // Ignore Raycast (사격 히트스캔 제외, 바닥 충돌 유지)
    private const int DefaultTerrainCollisionMask = (1 << 0) | (1 << 3); // Default, Ground (지형/벽만 물리 충돌 허용)
    private const float SettleSpeedSqrThreshold = 0.0025f; // 정지 상태로 판정할 선속도·각속도 제곱 임계값
    private const float SettleRequiredDuration = 0.25f; // 바닥 안착 후 키네마틱으로 고정하기까지 필요한 정지 유지 시간 (초)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary><c>WeaponId</c>로 무기 모델과 스탯을 조회할 데이터베이스.</summary>
    [Header("무기 데이터베이스")]
    [SerializeField] private WeaponDatabaseSO weaponDatabase;

    /// <summary>씬에 사전 배치된 픽업이 서버 시작 시 초기화할 무기 데이터 (런타임 드롭 시 비워둠).</summary>
    [Header("씬 초기 배치 설정")]
    [SerializeField] private WeaponDataSO initialWeaponData;

    /// <summary>무기 비주얼 모델을 자식으로 부착할 트랜스폼.</summary>
    [Header("비주얼 및 콜라이더 호스트")]
    [SerializeField] private Transform visualRoot;

    /// <summary>무기 형태에 맞춘 복합 <see cref="BoxCollider"/> 자식을 배치할 트랜스폼.</summary>
    [SerializeField] private Transform colliderRoot;

    /// <summary>모든 클라이언트에 동기화되는 무기 고유 식별자.</summary>
    private readonly SyncVar<string> _weaponId = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>모든 클라이언트에 동기화되는 이 무기 인스턴스의 현재 탄창 탄약 수.</summary>
    private readonly SyncVar<int> _currentAmmo = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>모든 클라이언트에 동기화되는 이 무기 인스턴스의 예비 탄약 수.</summary>
    private readonly SyncVar<int> _reserveAmmo = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>현재 활성화된 복합 물리 콜라이더 목록.</summary>
    private readonly List<Collider> _pickupColliders = new(4);

    /// <summary>서버에서 이미 획득 처리되어 중복 획득을 차단하는 플래그.</summary>
    private bool _isConsumed;

    /// <summary>이 인스턴스에 마지막으로 생성된 비주얼의 무기 식별자.</summary>
    private string _appliedWeaponId;

    /// <summary>현재 생성되어 표시 중인 무기 비주얼 오브젝트.</summary>
    private GameObject _visualInstance;

    /// <summary>드롭 물리 시뮬레이션용 <see cref="Rigidbody"/> 컴포넌트.</summary>
    private Rigidbody _rb;

    /// <summary>지형/벽 외의 모든 레이어를 물리 충돌에서 제외하는 마스크.</summary>
    private int _physicsExcludeMask;

    /// <summary>서버에서 바닥 안착 판정을 위해 누적한 정지 유지 시간 (초).</summary>
    private float _settleTimer;

    /// <summary>서버에서 바닥 안착이 완료되어 키네마틱으로 고정되었는지 여부.</summary>
    private bool _isSettled;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>이 월드 아이템이 나타내는 무기 식별자.</summary>
    public string WeaponId => _weaponId.Value;

    /// <summary>이 월드 아이템에 보존된 현재 탄창 탄약 수.</summary>
    public int CurrentAmmo => _currentAmmo.Value;

    /// <summary>이 월드 아이템에 보존된 예비 탄약 수.</summary>
    public int ReserveAmmo => _reserveAmmo.Value;

    /// <summary>서버에서 이미 획득 처리되었는지 여부.</summary>
    public bool IsConsumed => _isConsumed;

    /// <summary>픽업 거리 계산 및 충돌에 사용하는 복합 <see cref="Collider"/> 목록.</summary>
    public IReadOnlyList<Collider> PickupColliders => _pickupColliders;

    /// <summary>이 월드 아이템의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO WeaponData => weaponDatabase != null ? weaponDatabase.GetById(_weaponId.Value) : null;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        gameObject.layer = PickupLayer;

        // 지형/벽(Default, Ground)과만 물리 충돌하도록 나머지 레이어(플레이어/히트박스 등)를 제외한다.
        int terrainMask = LayerMask.GetMask("Default", "Ground");
        if (terrainMask == 0)
        {
            terrainMask = DefaultTerrainCollisionMask;
        }
        _physicsExcludeMask = ~terrainMask;

        if (_rb != null)
        {
            _rb.excludeLayers = _physicsExcludeMask;
            // 순수 클라이언트가 OnStartNetwork 이전에 로컬 물리를 시뮬레이션하지 않도록 기본 키네마틱으로 시작한다.
            _rb.isKinematic = true;
        }

        if (visualRoot == null)
        {
            visualRoot = transform;
        }

        if (colliderRoot == null)
        {
            Transform existing = transform.Find("ColliderRoot");
            if (existing != null)
            {
                colliderRoot = existing;
            }
            else
            {
                GameObject holder = new GameObject("ColliderRoot");
                holder.layer = PickupLayer;
                holder.transform.SetParent(transform, false);
                colliderRoot = holder.transform;
            }
        }
    }

    private void FixedUpdate()
    {
        if (!IsServerInitialized || _isSettled || _rb == null || _rb.isKinematic) return;

        bool isNearlyStopped = _rb.IsSleeping()
            || (_rb.linearVelocity.sqrMagnitude <= SettleSpeedSqrThreshold
                && _rb.angularVelocity.sqrMagnitude <= SettleSpeedSqrThreshold);
        if (isNearlyStopped)
        {
            _settleTimer += Time.fixedDeltaTime;
            if (_settleTimer >= SettleRequiredDuration)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
                _rb.isKinematic = true;
                _isSettled = true;
            }
        }
        else
        {
            _settleTimer = 0f;
        }
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _isSettled = false;
        _settleTimer = 0f;
        if (_rb != null)
        {
            _rb.isKinematic = false;
        }

        if (string.IsNullOrEmpty(_weaponId.Value) && initialWeaponData != null)
        {
            _weaponId.Value = initialWeaponData.WeaponId;
            _currentAmmo.Value = initialWeaponData.UsesAmmo ? initialWeaponData.MaxAmmo : 0;
            _reserveAmmo.Value = initialWeaponData.UsesAmmo ? initialWeaponData.MaxReserveAmmo : 0;
        }
    }

    public override void OnStartNetwork()
    {
        _weaponId.OnChange += HandleWeaponIdChanged;

        if (!IsServerInitialized && _rb != null)
        {
            _rb.isKinematic = true;
        }

        HandleWeaponIdChanged(string.Empty, _weaponId.Value, asServer: IsServerInitialized);
    }

    public override void OnStopNetwork()
    {
        _weaponId.OnChange -= HandleWeaponIdChanged;
        _appliedWeaponId = null;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>서버에서 스폰된 월드 픽업의 무기 식별자, 탄약 상태 및 초기 투척 임펄스/토크를 적용한다.</summary>
    [Server]
    public void Initialize(
        string weaponId,
        int currentAmmo,
        int reserveAmmo,
        Vector3 launchImpulse,
        Vector3 launchTorque)
    {
        _isConsumed = false;
        _isSettled = false;
        _settleTimer = 0f;
        _weaponId.Value = weaponId ?? string.Empty;
        _currentAmmo.Value = Mathf.Max(0, currentAmmo);
        _reserveAmmo.Value = Mathf.Max(0, reserveAmmo);

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(launchImpulse, ForceMode.Impulse);
            // 얇은 총기/단검의 축별 관성 텐서 편차로 회전이 폭주하지 않도록 VelocityChange(rad/s)로 토크를 적용한다.
            _rb.AddTorque(launchTorque, ForceMode.VelocityChange);
        }
    }

    /// <summary>서버에서 중복 획득을 차단하며 이 픽업을 소비하고 보존된 무기 상태를 반환한다.</summary>
    [Server]
    public bool TryConsume(out WeaponSlotState slotState)
    {
        slotState = WeaponSlotState.Empty;
        if (_isConsumed || !IsSpawned || string.IsNullOrEmpty(_weaponId.Value)) return false;

        _isConsumed = true;
        slotState = new WeaponSlotState
        {
            WeaponId = _weaponId.Value,
            CurrentAmmo = _currentAmmo.Value,
            ReserveAmmo = _reserveAmmo.Value
        };

        Despawn(DespawnType.Destroy);
        return true;
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="_weaponId"/> 변경 시 해당 무기의 월드 비주얼과 복합 물리 콜라이더를 갱신한다.</summary>
    private void HandleWeaponIdChanged(string previousValue, string nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        if (string.Equals(_appliedWeaponId, nextValue, StringComparison.Ordinal)) return;

        _appliedWeaponId = nextValue;

        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
            _visualInstance = null;
        }

        _pickupColliders.Clear();
        if (colliderRoot != null)
        {
            for (int i = colliderRoot.childCount - 1; i >= 0; i--)
            {
                Destroy(colliderRoot.GetChild(i).gameObject);
            }
        }

        WeaponDataSO data = weaponDatabase != null ? weaponDatabase.GetById(nextValue) : null;
        GameObject visualPrefab = data != null ? data.WorldVisualPrefab : null;
        if (visualPrefab == null) return;

        Transform host = visualRoot != null ? visualRoot : transform;
        _visualInstance = Instantiate(visualPrefab, host, false);
        _visualInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        _visualInstance.transform.localScale = data.WorldPickupScale;

        foreach (Animator anim in _visualInstance.GetComponentsInChildren<Animator>(true))
        {
            anim.enabled = false;
        }

        foreach (GunScript gun in _visualInstance.GetComponentsInChildren<GunScript>(true))
        {
            gun.enabled = false;
        }

        foreach (DroppedMagazine mag in _visualInstance.GetComponentsInChildren<DroppedMagazine>(true))
        {
            mag.enabled = false;
        }

        foreach (Rigidbody childRb in _visualInstance.GetComponentsInChildren<Rigidbody>(true))
        {
            childRb.isKinematic = true;
            childRb.detectCollisions = false;
        }

        foreach (Collider childCol in _visualInstance.GetComponentsInChildren<Collider>(true))
        {
            childCol.enabled = false;
        }

        foreach (Transform child in _visualInstance.GetComponentsInChildren<Transform>(true))
        {
            child.gameObject.layer = PickupLayer;
        }

        if (colliderRoot == null) return;

        WeaponColliderBox[] customBoxes = data.WorldPickupColliders;
        if (customBoxes != null && customBoxes.Length > 0)
        {
            Vector3 scale = data.WorldPickupScale;
            for (int i = 0; i < customBoxes.Length; i++)
            {
                WeaponColliderBox spec = customBoxes[i];
                GameObject part = new GameObject($"ColliderPart_{i}");
                part.layer = PickupLayer;
                part.transform.SetParent(colliderRoot, false);
                part.transform.SetLocalPositionAndRotation(
                    Vector3.Scale(spec.center, scale),
                    Quaternion.Euler(spec.rotation));

                BoxCollider box = part.AddComponent<BoxCollider>();
                Vector3 scaledSize = Vector3.Scale(spec.size, scale);
                box.size = new Vector3(
                    Mathf.Max(Mathf.Abs(scaledSize.x), MinColliderAxis),
                    Mathf.Max(Mathf.Abs(scaledSize.y), MinColliderAxis),
                    Mathf.Max(Mathf.Abs(scaledSize.z), MinColliderAxis));
                box.excludeLayers = _physicsExcludeMask;
                _pickupColliders.Add(box);
            }
        }
        else
        {
            Renderer[] renderers = _visualInstance.GetComponentsInChildren<Renderer>(true);
            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            bool hasBounds = false;

            for (int i = 0; i < renderers.Length; i++)
            {
                Bounds worldBounds = renderers[i].bounds;
                if (worldBounds.size.sqrMagnitude <= 0.000001f) continue;

                Vector3 c = worldBounds.center;
                Vector3 e = worldBounds.extents;
                for (int x = -1; x <= 1; x += 2)
                {
                    for (int y = -1; y <= 1; y += 2)
                    {
                        for (int z = -1; z <= 1; z += 2)
                        {
                            Vector3 localCorner = colliderRoot.InverseTransformPoint(c + new Vector3(e.x * x, e.y * y, e.z * z));
                            min = Vector3.Min(min, localCorner);
                            max = Vector3.Max(max, localCorner);
                            hasBounds = true;
                        }
                    }
                }
            }

            if (hasBounds)
            {
                GameObject fallbackPart = new GameObject("ColliderPart_Fallback");
                fallbackPart.layer = PickupLayer;
                fallbackPart.transform.SetParent(colliderRoot, false);

                BoxCollider box = fallbackPart.AddComponent<BoxCollider>();
                Vector3 size = max - min;
                box.center = (min + max) * 0.5f;
                box.size = new Vector3(
                    Mathf.Max(size.x, FallbackMinColliderAxis),
                    Mathf.Max(size.y, FallbackMinColliderAxis),
                    Mathf.Max(size.z, FallbackMinColliderAxis));
                box.excludeLayers = _physicsExcludeMask;
                _pickupColliders.Add(box);
            }
        }

        if (_rb != null)
        {
            _rb.ResetCenterOfMass();
            _rb.ResetInertiaTensor();
        }
    }
}
