using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>무기별 런타임 탄약/쿨다운 관리와 서버 권위 히트스캔(Raycast) 공격 판정을 처리한다.</summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerAnim))]
public class PlayerWeaponController : NetworkBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>무기 슬롯별 런타임 탄약 및 발사 쿨다운 상태.</summary>
    private struct WeaponRuntimeState
    {
        public int currentAmmo;
        public int reserveAmmo;
        public float nextFireTime;
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float FireRateTolerance = 0.03f; // 네트워크 지터를 고려한 서버 발사 쿨다운 허용 오차 (초)
    private const float MaxOriginOffset = 2.5f; // 서버에서 허용하는 발사 원점 최대 편차 (미터)
    private const float MeleeComboBufferWindow = 0.14f; // 근접 공격 막바지에 선입력을 허용하는 남은 쿨다운 시간 (초)
    private const float MeleeComboGraceWindow = 0.25f; // 근접 쿨다운 종료 후 콤보를 이어갈 수 있는 유예 시간 (초)
    private const int DefaultCombatLayerMask = (1 << 0) | (1 << 3) | (1 << 6) | (1 << 7); // Default, Ground, Player, Hitbox

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>히트스캔 및 근접 판정에 포함할 물리 레이어 마스크.</summary>
    [Header("히트스캔 설정")]
    [SerializeField] private LayerMask hitLayerMask = DefaultCombatLayerMask;

    /// <summary>씬 뷰에 디버그 사격 궤적을 표시할 유지 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float debugRayDuration = 1.5f;

    /// <summary>소유자 측 로컬 예측용 무기 런타임 상태 배열.</summary>
    private WeaponRuntimeState[] _ownerStates;

    /// <summary>서버 권위 검증용 무기 런타임 상태 배열.</summary>
    private WeaponRuntimeState[] _serverStates;

    /// <summary>히트스캔 물리 캐스트 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    /// <summary>조준 좌표와 발사 원점을 제공하는 플레이어 컨트롤러.</summary>
    private PlayerController _playerController;

    /// <summary>장착 무기 상태와 공격/재장전 애니메이션을 제어하는 컴포넌트.</summary>
    private PlayerAnim _playerAnim;

    /// <summary>플레이어 생존 상태를 조회하는 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>현재 공격 입력이 유지되고 있는지 여부 (소유자 전용).</summary>
    private bool _isFireHeld;

    /// <summary>단발 무기 발사를 위한 이번 입력 프레임의 사격 요청 플래그 (소유자 전용).</summary>
    private bool _fireRequested;

    /// <summary>근접 무기 직전 공격 콤보 단계 (0: Swing1, 1: Swing2, 소유자 전용).</summary>
    private byte _meleeComboStep;

    /// <summary>근접 무기 연속 콤보가 유지되는 만료 시각 (<see cref="Time.time"/> 기준, 소유자 전용).</summary>
    private float _meleeComboExpireTime;

    /// <summary>소유자 측 재장전 진행 여부.</summary>
    private bool _isOwnerReloading;

    /// <summary>소유자 측 재장전 완료 예정 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _ownerReloadEndTime;

    /// <summary>소유자 측 재장전 중인 무기 종류.</summary>
    private EWeaponType _ownerReloadWeaponType;

    /// <summary>서버 측 재장전 진행 여부.</summary>
    private bool _isServerReloading;

    /// <summary>서버 측 재장전 완료 예정 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _serverReloadEndTime;

    /// <summary>서버 측 재장전 중인 무기 종류.</summary>
    private EWeaponType _serverReloadWeaponType;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>탄약 수치가 변경될 때 (무기 종류, 현재 탄창, 예비 탄약)을 알리는 이벤트.</summary>
    public event Action<EWeaponType, int, int> OnAmmoChanged;

    /// <summary>서버가 <see cref="IDamageable"/> 적중을 확정했을 때 소유자에게 알리는 이벤트.</summary>
    public event Action OnHitConfirmed;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 장착 중인 무기의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO CurrentWeaponData => _playerAnim != null ? GetWeaponData(_playerAnim.CurrentWeaponType) : null;

    /// <summary>현재 장착 중인 무기의 남은 탄창 수.</summary>
    public int CurrentAmmo
    {
        get
        {
            if (_playerAnim == null || _ownerStates == null) return 0;
            int index = (int)_playerAnim.CurrentWeaponType;
            return index >= 0 && index < _ownerStates.Length ? _ownerStates[index].currentAmmo : 0;
        }
    }

    /// <summary>현재 장착 중인 무기의 남은 예비 탄약 수.</summary>
    public int ReserveAmmo
    {
        get
        {
            if (_playerAnim == null || _ownerStates == null) return 0;
            int index = (int)_playerAnim.CurrentWeaponType;
            return index >= 0 && index < _ownerStates.Length ? _ownerStates[index].reserveAmmo : 0;
        }
    }

    /// <summary>현재 장착 중인 무기의 최대 탄창 용량.</summary>
    public int MaxAmmo => CurrentWeaponData != null ? CurrentWeaponData.MaxAmmo : 0;

    /// <summary>현재 재장전 진행 여부.</summary>
    public bool IsReloading => IsOwner ? _isOwnerReloading : _isServerReloading;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _playerController = GetComponent<PlayerController>();
        _playerAnim = GetComponent<PlayerAnim>();
        _playerHealth = GetComponent<PlayerHealth>();

        int slotCount = Enum.GetValues(typeof(EWeaponType)).Length;
        _ownerStates = new WeaponRuntimeState[slotCount];
        _serverStates = new WeaponRuntimeState[slotCount];
    }

    private void Update()
    {
        if (!IsSpawned || _playerAnim == null) return;

        EWeaponType currentWeapon = _playerAnim.CurrentWeaponType;

        if (IsServerInitialized && _isServerReloading)
        {
            if (currentWeapon != _serverReloadWeaponType)
            {
                _isServerReloading = false;
            }
            else if (Time.time >= _serverReloadEndTime)
            {
                _isServerReloading = false;
                WeaponDataSO reloadData = GetWeaponData(_serverReloadWeaponType);
                int reloadIndex = (int)_serverReloadWeaponType;
                if (reloadData != null && reloadData.UsesAmmo && reloadIndex >= 0 && reloadIndex < _serverStates.Length)
                {
                    int needed = reloadData.MaxAmmo - _serverStates[reloadIndex].currentAmmo;
                    int loaded = Mathf.Min(needed, _serverStates[reloadIndex].reserveAmmo);
                    if (loaded > 0)
                    {
                        _serverStates[reloadIndex].currentAmmo += loaded;
                        _serverStates[reloadIndex].reserveAmmo -= loaded;
                    }

                    Debug.Log($"[PlayerWeaponController] {name}({reloadData.WeaponName}) 재장전 완료: {_serverStates[reloadIndex].currentAmmo}/{_serverStates[reloadIndex].reserveAmmo}");
                    SyncAmmoTargetRpc(Owner, _serverReloadWeaponType, _serverStates[reloadIndex].currentAmmo, _serverStates[reloadIndex].reserveAmmo);
                }
            }
        }

        if (!IsOwner) return;

        if (_isOwnerReloading)
        {
            if (currentWeapon != _ownerReloadWeaponType)
            {
                _isOwnerReloading = false;
            }
            else if (Time.time >= _ownerReloadEndTime)
            {
                _isOwnerReloading = false;
                if (!IsServerInitialized)
                {
                    WeaponDataSO reloadData = GetWeaponData(_ownerReloadWeaponType);
                    int reloadIndex = (int)_ownerReloadWeaponType;
                    if (reloadData != null && reloadData.UsesAmmo && reloadIndex >= 0 && reloadIndex < _ownerStates.Length)
                    {
                        int needed = reloadData.MaxAmmo - _ownerStates[reloadIndex].currentAmmo;
                        int loaded = Mathf.Min(needed, _ownerStates[reloadIndex].reserveAmmo);
                        if (loaded > 0)
                        {
                            _ownerStates[reloadIndex].currentAmmo += loaded;
                            _ownerStates[reloadIndex].reserveAmmo -= loaded;
                            OnAmmoChanged?.Invoke(_ownerReloadWeaponType, _ownerStates[reloadIndex].currentAmmo, _ownerStates[reloadIndex].reserveAmmo);
                        }
                    }
                }
            }
        }

        if (currentWeapon != EWeaponType.KnifeMode)
        {
            _meleeComboStep = 0;
            _meleeComboExpireTime = 0f;
        }

        if (!_isFireHeld && !_fireRequested) return;
        if (_playerHealth != null && _playerHealth.IsDead)
        {
            _fireRequested = false;
            return;
        }
        if (_isOwnerReloading || (IsServerInitialized && _isServerReloading) || _playerAnim.IsSwappingWeapon)
        {
            _fireRequested = false;
            return;
        }

        WeaponDataSO weaponData = GetWeaponData(currentWeapon);
        if (weaponData == null)
        {
            _fireRequested = false;
            return;
        }

        if (!weaponData.IsAutomatic && !_fireRequested) return;

        int slotIndex = (int)currentWeapon;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;

        float remainingCooldown = _ownerStates[slotIndex].nextFireTime - Time.time;
        if (remainingCooldown > 0f)
        {
            // 근접 무기는 직전 스윙 막바지 구간에서의 재입력만 선입력으로 유지한다.
            if (currentWeapon == EWeaponType.KnifeMode && remainingCooldown > MeleeComboBufferWindow)
            {
                _fireRequested = false;
            }
            return;
        }

        _fireRequested = false;

        if (weaponData.UsesAmmo && _ownerStates[slotIndex].currentAmmo <= 0)
        {
            Debug.Log($"[PlayerWeaponController] {weaponData.WeaponName} 탄약이 없습니다. (R키로 재장전)");
            return;
        }

        byte attackVariant = 0;
        if (currentWeapon == EWeaponType.KnifeMode)
        {
            if (Time.time <= _meleeComboExpireTime)
            {
                _meleeComboStep = (byte)((_meleeComboStep + 1) % 2);
            }
            else
            {
                _meleeComboStep = 0;
            }
            attackVariant = _meleeComboStep;
        }

        float nextFireTime = Time.time + weaponData.AttackCooldown;
        _ownerStates[slotIndex].nextFireTime = nextFireTime;
        if (currentWeapon == EWeaponType.KnifeMode)
        {
            _meleeComboExpireTime = nextFireTime + MeleeComboGraceWindow;
        }

        if (weaponData.UsesAmmo)
        {
            _ownerStates[slotIndex].currentAmmo--;
            OnAmmoChanged?.Invoke(currentWeapon, _ownerStates[slotIndex].currentAmmo, _ownerStates[slotIndex].reserveAmmo);
        }

        _playerAnim.TriggerAttack(attackVariant);

        Vector3 origin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.35f;
        Vector3 aimPoint = _playerController != null
            ? _playerController.GetAimPoint()
            : origin + transform.forward * weaponData.Range;
        Vector3 direction = aimPoint - origin;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        RequestFireServerRpc(origin, direction, currentWeapon, attackVariant);
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        InitializeWeaponStates(_serverStates);
    }

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        InitializeWeaponStates(_ownerStates);
    }

    public override void OnStopClient()
    {
        _isFireHeld = false;
        _fireRequested = false;
        _isOwnerReloading = false;
        _meleeComboStep = 0;
        _meleeComboExpireTime = 0f;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>소유자의 공격 입력 상태를 갱신한다.</summary>
    public void SetFireInput(bool isHeld)
    {
        if (!IsOwner) return;

        if (isHeld && !_isFireHeld)
        {
            _fireRequested = true;
        }
        _isFireHeld = isHeld;
    }

    /// <summary>현재 무기가 재장전 가능한 상태면 재장전 시퀀스를 시작한다.</summary>
    public void TryStartReload()
    {
        if (!IsOwner || !IsSpawned || _playerAnim == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_isOwnerReloading || _playerAnim.IsSwappingWeapon) return;

        EWeaponType weaponType = _playerAnim.CurrentWeaponType;
        WeaponDataSO weaponData = GetWeaponData(weaponType);
        if (weaponData == null || !weaponData.UsesAmmo) return;

        int slotIndex = (int)weaponType;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;
        if (_ownerStates[slotIndex].currentAmmo >= weaponData.MaxAmmo) return;
        if (_ownerStates[slotIndex].reserveAmmo <= 0) return;

        _isOwnerReloading = true;
        _ownerReloadWeaponType = weaponType;
        _ownerReloadEndTime = Time.time + weaponData.ReloadTime;
        _fireRequested = false;

        _playerAnim.RequestReload();
        RequestReloadServerRpc(weaponType);
    }

    /// <summary><paramref name="weaponType"/>에 연결된 <see cref="WeaponDataSO"/>를 반환한다.</summary>
    public WeaponDataSO GetWeaponData(EWeaponType weaponType)
    {
        PropDataSO propData = _playerController != null ? _playerController.PropData : null;
        if (propData == null) return null;

        WeaponSocketConfig[] sockets = propData.WeaponSockets;
        int index = (int)weaponType;
        if (sockets == null || index < 0 || index >= sockets.Length) return null;

        return sockets[index].weaponData;
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>서버에서 발사 조건을 검증하고 히트스캔 또는 근접 충돌 판정 및 데미지를 적용한다.</summary>
    [ServerRpc]
    private void RequestFireServerRpc(Vector3 origin, Vector3 direction, EWeaponType weaponType, byte attackVariant, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_playerAnim == null || _playerAnim.CurrentWeaponType != weaponType) return;
        if (_isServerReloading) return;

        WeaponDataSO weaponData = GetWeaponData(weaponType);
        if (weaponData == null) return;

        int slotIndex = (int)weaponType;
        if (slotIndex < 0 || slotIndex >= _serverStates.Length) return;
        if (Time.time < _serverStates[slotIndex].nextFireTime - FireRateTolerance) return;

        if (weaponData.UsesAmmo && _serverStates[slotIndex].currentAmmo <= 0)
        {
            SyncAmmoTargetRpc(Owner, weaponType, _serverStates[slotIndex].currentAmmo, _serverStates[slotIndex].reserveAmmo);
            return;
        }

        if (direction.sqrMagnitude < 0.0001f) return;
        direction.Normalize();

        Vector3 expectedOrigin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.35f;
        if (Vector3.Distance(origin, expectedOrigin) > MaxOriginOffset)
        {
            origin = expectedOrigin;
        }

        _serverStates[slotIndex].nextFireTime = Time.time + weaponData.AttackCooldown;
        if (weaponData.UsesAmmo)
        {
            _serverStates[slotIndex].currentAmmo--;
            SyncAmmoTargetRpc(Owner, weaponType, _serverStates[slotIndex].currentAmmo, _serverStates[slotIndex].reserveAmmo);
        }

        int hitCount = weaponData.MeleeCastRadius > 0f
            ? Physics.SphereCastNonAlloc(
                origin,
                weaponData.MeleeCastRadius,
                direction,
                _hitBuffer,
                weaponData.Range,
                hitLayerMask,
                QueryTriggerInteraction.Ignore)
            : Physics.RaycastNonAlloc(
                origin,
                direction,
                _hitBuffer,
                weaponData.Range,
                hitLayerMask,
                QueryTriggerInteraction.Ignore);

        bool hasValidHit = false;
        float closestDistance = float.MaxValue;
        RaycastHit closestHit = default;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            if (hit.transform == null || hit.transform.IsChildOf(transform)) continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                hasValidHit = true;
            }
        }

        Vector3 hitPoint = origin + direction * weaponData.Range;
        bool hitDamageable = false;

        if (hasValidHit)
        {
            hitPoint = closestHit.point != Vector3.zero
                ? closestHit.point
                : closestHit.collider.ClosestPoint(origin);

            IDamageable damageable = closestHit.collider.GetComponentInParent<IDamageable>();
            if (damageable != null && !damageable.IsDead)
            {
                damageable.TakeDamage(weaponData.Damage, Owner);
                hitDamageable = true;
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 적중 → {closestHit.collider.name} (데미지: {weaponData.Damage:F1}, 거리: {closestHit.distance:F2}m)");
            }
            else
            {
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 충돌 → {closestHit.collider.name} (거리: {closestHit.distance:F2}m)");
            }
        }

        Debug.DrawLine(origin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
        BroadcastFireObserversRpc(origin, hitPoint, hitDamageable, attackVariant);

        if (hitDamageable)
        {
            NotifyHitConfirmedTargetRpc(Owner);
        }
    }

    /// <summary>서버에서 재장전 타이머를 시작한다.</summary>
    [ServerRpc]
    private void RequestReloadServerRpc(EWeaponType weaponType, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_playerAnim == null || _playerAnim.CurrentWeaponType != weaponType) return;
        if (_isServerReloading) return;

        WeaponDataSO weaponData = GetWeaponData(weaponType);
        if (weaponData == null || !weaponData.UsesAmmo) return;

        int slotIndex = (int)weaponType;
        if (slotIndex < 0 || slotIndex >= _serverStates.Length) return;
        if (_serverStates[slotIndex].currentAmmo >= weaponData.MaxAmmo) return;
        if (_serverStates[slotIndex].reserveAmmo <= 0) return;

        _isServerReloading = true;
        _serverReloadWeaponType = weaponType;
        _serverReloadEndTime = Time.time + weaponData.ReloadTime;
    }

    /// <summary>클라이언트에 발사 애니메이션과 디버그 궤적을 전파한다.</summary>
    [ObserversRpc]
    private void BroadcastFireObserversRpc(Vector3 origin, Vector3 hitPoint, bool hitDamageable, byte attackVariant)
    {
        if (!IsOwner)
        {
            _playerAnim?.TriggerAttack(attackVariant);
        }

        if (!IsServerInitialized)
        {
            Debug.DrawLine(origin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
        }
    }

    /// <summary>서버의 권위 탄약 수치를 소유자 클라이언트에 동기화한다.</summary>
    [TargetRpc]
    private void SyncAmmoTargetRpc(NetworkConnection target, EWeaponType weaponType, int currentAmmo, int reserveAmmo)
    {
        int index = (int)weaponType;
        if (_ownerStates == null || index < 0 || index >= _ownerStates.Length) return;

        _ownerStates[index].currentAmmo = currentAmmo;
        _ownerStates[index].reserveAmmo = reserveAmmo;
        OnAmmoChanged?.Invoke(weaponType, currentAmmo, reserveAmmo);
    }

    /// <summary>서버에서 확정된 <see cref="IDamageable"/> 적중 사실을 소유자 클라이언트에 알린다.</summary>
    [TargetRpc]
    private void NotifyHitConfirmedTargetRpc(NetworkConnection target)
    {
        OnHitConfirmed?.Invoke();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>무기 슬롯별 런타임 상태를 각 <see cref="WeaponDataSO"/> 기본값으로 채운다.</summary>
    private void InitializeWeaponStates(WeaponRuntimeState[] states)
    {
        if (states == null) return;

        for (int i = 0; i < states.Length; i++)
        {
            WeaponDataSO data = GetWeaponData((EWeaponType)i);
            states[i] = new WeaponRuntimeState
            {
                currentAmmo = data != null && data.UsesAmmo ? data.MaxAmmo : 0,
                reserveAmmo = data != null && data.UsesAmmo ? data.MaxReserveAmmo : 0,
                nextFireTime = 0f
            };
        }
    }
}
