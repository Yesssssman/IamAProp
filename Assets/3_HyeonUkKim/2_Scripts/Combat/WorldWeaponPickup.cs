using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>바닥에 떨어진 월드 무기 아이템의 <c>WeaponId</c>, 남은 탄약 및 비주얼 모델을 네트워크 동기화한다.</summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(BoxCollider))]
public class WorldWeaponPickup : NetworkBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float MinColliderAxis = 0.16f; // 바닥 관통과 픽업 판정 누락을 막기 위한 최소 콜라이더 두께 (미터)
    private const int PickupLayer = 2; // Ignore Raycast (사격 히트스캔 제외, 바닥 충돌 유지)
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
    [Header("비주얼 호스트")]
    [SerializeField] private Transform visualRoot;

    /// <summary>모든 클라이언트에 동기화되는 무기 고유 식별자.</summary>
    private readonly SyncVar<string> _weaponId = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>모든 클라이언트에 동기화되는 이 무기 인스턴스의 현재 탄창 탄약 수.</summary>
    private readonly SyncVar<int> _currentAmmo = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>모든 클라이언트에 동기화되는 이 무기 인스턴스의 예비 탄약 수.</summary>
    private readonly SyncVar<int> _reserveAmmo = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>서버에서 이미 획득 처리되어 중복 획득을 차단하는 플래그.</summary>
    private bool _isConsumed;

    /// <summary>이 인스턴스에 마지막으로 생성된 비주얼의 무기 식별자.</summary>
    private string _appliedWeaponId;

    /// <summary>현재 생성되어 표시 중인 무기 비주얼 오브젝트.</summary>
    private GameObject _visualInstance;

    /// <summary>드롭 물리 시뮬레이션용 <see cref="Rigidbody"/> 컴포넌트.</summary>
    private Rigidbody _rb;

    /// <summary>바닥 충돌 및 픽업 탐지용 <see cref="BoxCollider"/> 컴포넌트.</summary>
    private BoxCollider _boxCollider;

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

    /// <summary>픽업 거리 계산 및 충돌에 사용하는 루트 <see cref="Collider"/>.</summary>
    public Collider PickupCollider => _boxCollider;

    /// <summary>이 월드 아이템의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO WeaponData => weaponDatabase != null ? weaponDatabase.GetById(_weaponId.Value) : null;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _boxCollider = GetComponent<BoxCollider>();
        gameObject.layer = PickupLayer;

        int playerExcludeMask = LayerMask.GetMask("Player", "Hitbox");
        if (playerExcludeMask != 0)
        {
            if (_boxCollider != null)
            {
                _boxCollider.excludeLayers |= playerExcludeMask;
            }
            if (_rb != null)
            {
                _rb.excludeLayers |= playerExcludeMask;
            }
        }

        if (visualRoot == null)
        {
            visualRoot = transform;
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

    /// <summary>서버에서 스폰된 월드 픽업의 무기 식별자, 탄약 상태 및 초기 투척 속도를 설정한다.</summary>
    [Server]
    public void Initialize(
        string weaponId,
        int currentAmmo,
        int reserveAmmo,
        Vector3 initialVelocity,
        Vector3 initialAngularVelocity)
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
            _rb.linearVelocity = initialVelocity;
            _rb.angularVelocity = initialAngularVelocity;
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

    /// <summary><see cref="_weaponId"/> 변경 시 해당 무기의 월드 비주얼과 콜라이더 크기를 갱신한다.</summary>
    private void HandleWeaponIdChanged(string previousValue, string nextValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        if (string.Equals(_appliedWeaponId, nextValue, System.StringComparison.Ordinal)) return;

        _appliedWeaponId = nextValue;

        if (_visualInstance != null)
        {
            Destroy(_visualInstance);
            _visualInstance = null;
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

        if (_boxCollider == null) return;

        Renderer[] renderers = _visualInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0) return;

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
                        Vector3 localCorner = transform.InverseTransformPoint(c + new Vector3(e.x * x, e.y * y, e.z * z));
                        min = Vector3.Min(min, localCorner);
                        max = Vector3.Max(max, localCorner);
                        hasBounds = true;
                    }
                }
            }
        }

        if (!hasBounds) return;

        Vector3 size = max - min;
        _boxCollider.center = (min + max) * 0.5f;
        _boxCollider.size = new Vector3(
            Mathf.Max(size.x, MinColliderAxis),
            Mathf.Max(size.y, MinColliderAxis),
            Mathf.Max(size.z, MinColliderAxis));
    }
}
