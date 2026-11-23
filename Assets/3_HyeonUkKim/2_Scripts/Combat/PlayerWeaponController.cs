using System;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>무기 인스턴스별 탄약/쿨다운 관리와 서버 권위 히트스캔(Raycast) 공격 판정을 처리한다.</summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerAnim))]
[RequireComponent(typeof(PlayerWeaponInventory))]
public class PlayerWeaponController : NetworkBehaviour
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>소유자 측 로컬 예측용 슬롯별 탄약 및 발사 쿨다운 상태.</summary>
    private struct OwnerSlotRuntimeState
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

    /// <summary>총기 사격 및 재장전 사운드를 3D 공간에서 재생할 <see cref="AudioSource"/> 컴포넌트.</summary>
    [Header("사운드 설정")]
    [SerializeField] private AudioSource weaponAudioSource;

    /// <summary>소유자 측 로컬 예측용 슬롯별 탄약 및 쿨다운 배열.</summary>
    private OwnerSlotRuntimeState[] _ownerStates;

    /// <summary>서버 측 슬롯별 다음 발사 허용 시각 배열 (<see cref="Time.time"/> 기준).</summary>
    private float[] _serverNextFireTimes;

    /// <summary>히트스캔 물리 캐스트 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    /// <summary>조준 좌표와 발사 원점을 제공하는 플레이어 컨트롤러.</summary>
    private PlayerController _playerController;

    /// <summary>공격/재장전 애니메이션을 제어하는 컴포넌트.</summary>
    private PlayerAnim _playerAnim;

    /// <summary>4슬롯 보유 무기와 탄약 상태를 관리하는 인벤토리 컴포넌트.</summary>
    private PlayerWeaponInventory _weaponInventory;

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

    /// <summary>소유자 측 재장전 중인 무기 슬롯.</summary>
    private EWeaponSlotType _ownerReloadSlot;

    /// <summary>소유자 측 재장전 중인 무기 식별자.</summary>
    private string _ownerReloadWeaponId;

    /// <summary>서버 측 재장전 진행 여부.</summary>
    private bool _isServerReloading;

    /// <summary>서버 측 재장전 완료 예정 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _serverReloadEndTime;

    /// <summary>서버 측 재장전 중인 무기 슬롯.</summary>
    private EWeaponSlotType _serverReloadSlot;

    /// <summary>서버 측 재장전 중인 무기 식별자.</summary>
    private string _serverReloadWeaponId;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>탄약 수치가 변경될 때 (무기 슬롯 모드, 현재 탄창, 예비 탄약)을 알리는 이벤트.</summary>
    public event Action<EWeaponType, int, int> OnAmmoChanged;

    /// <summary>서버가 <see cref="IDamageable"/> 적중을 확정했을 때 소유자에게 알리는 이벤트.</summary>
    public event Action OnHitConfirmed;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 장착 중인 무기의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO CurrentWeaponData => _weaponInventory != null ? _weaponInventory.EquippedWeaponData : null;

    /// <summary>현재 장착 중인 무기의 남은 탄창 수.</summary>
    public int CurrentAmmo
    {
        get
        {
            if (_weaponInventory == null || _ownerStates == null) return 0;
            int index = (int)_weaponInventory.ActiveSlot;
            return index >= 0 && index < _ownerStates.Length ? _ownerStates[index].currentAmmo : 0;
        }
    }

    /// <summary>현재 장착 중인 무기의 남은 예비 탄약 수.</summary>
    public int ReserveAmmo
    {
        get
        {
            if (_weaponInventory == null || _ownerStates == null) return 0;
            int index = (int)_weaponInventory.ActiveSlot;
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
        _weaponInventory = GetComponent<PlayerWeaponInventory>();
        _playerHealth = GetComponent<PlayerHealth>();
        if (weaponAudioSource == null)
        {
            weaponAudioSource = GetComponent<AudioSource>();
        }

        _ownerStates = new OwnerSlotRuntimeState[PlayerWeaponInventory.SlotCount];
        _serverNextFireTimes = new float[PlayerWeaponInventory.SlotCount];
    }

    private void Update()
    {
        if (!IsSpawned || _playerAnim == null || _weaponInventory == null) return;

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            CancelCombatActions();
            return;
        }

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        string activeWeaponId = _weaponInventory.EquippedWeaponId;

        if (IsServerInitialized && _isServerReloading)
        {
            if (activeSlot != _serverReloadSlot
                || !string.Equals(activeWeaponId, _serverReloadWeaponId, StringComparison.Ordinal))
            {
                _isServerReloading = false;
            }
            else if (Time.time >= _serverReloadEndTime)
            {
                _isServerReloading = false;
                WeaponDataSO reloadData = _weaponInventory.GetWeaponDataById(_serverReloadWeaponId);
                if (reloadData != null
                    && reloadData.UsesAmmo
                    && _weaponInventory.TryApplyReload(_serverReloadSlot, _serverReloadWeaponId, reloadData.MaxAmmo, out WeaponSlotState updated))
                {
                    Debug.Log($"[PlayerWeaponController] {name}({reloadData.WeaponName}) 재장전 완료: {updated.CurrentAmmo}/{updated.ReserveAmmo}");
                    SyncAmmoTargetRpc(Owner, _serverReloadSlot, updated.CurrentAmmo, updated.ReserveAmmo);
                }
            }
        }

        if (!IsOwner) return;

        if (_isOwnerReloading)
        {
            if (activeSlot != _ownerReloadSlot
                || !string.Equals(activeWeaponId, _ownerReloadWeaponId, StringComparison.Ordinal))
            {
                _isOwnerReloading = false;
            }
            else if (Time.time >= _ownerReloadEndTime)
            {
                _isOwnerReloading = false;
                if (!IsServerInitialized)
                {
                    WeaponDataSO reloadData = _weaponInventory.GetWeaponDataById(_ownerReloadWeaponId);
                    int reloadIndex = (int)_ownerReloadSlot;
                    if (reloadData != null && reloadData.UsesAmmo && reloadIndex >= 0 && reloadIndex < _ownerStates.Length)
                    {
                        int needed = reloadData.MaxAmmo - _ownerStates[reloadIndex].currentAmmo;
                        int loaded = Mathf.Min(needed, _ownerStates[reloadIndex].reserveAmmo);
                        if (loaded > 0)
                        {
                            _ownerStates[reloadIndex].currentAmmo += loaded;
                            _ownerStates[reloadIndex].reserveAmmo -= loaded;
                            OnAmmoChanged?.Invoke((EWeaponType)_ownerReloadSlot, _ownerStates[reloadIndex].currentAmmo, _ownerStates[reloadIndex].reserveAmmo);
                        }
                    }
                }
            }
        }

        if (activeSlot != EWeaponSlotType.Melee)
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

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null)
        {
            _fireRequested = false;
            return;
        }

        if (!weaponData.IsAutomatic && !_fireRequested) return;

        int slotIndex = (int)activeSlot;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;

        float remainingCooldown = _ownerStates[slotIndex].nextFireTime - Time.time;
        if (remainingCooldown > 0f)
        {
            // 근접 무기는 직전 스윙 막바지 구간에서의 재입력만 선입력으로 유지한다.
            if (activeSlot == EWeaponSlotType.Melee && remainingCooldown > MeleeComboBufferWindow)
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
        if (activeSlot == EWeaponSlotType.Melee)
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
        if (activeSlot == EWeaponSlotType.Melee)
        {
            _meleeComboExpireTime = nextFireTime + MeleeComboGraceWindow;
        }

        if (weaponData.UsesAmmo)
        {
            _ownerStates[slotIndex].currentAmmo--;
            OnAmmoChanged?.Invoke((EWeaponType)activeSlot, _ownerStates[slotIndex].currentAmmo, _ownerStates[slotIndex].reserveAmmo);
        }

        _playerAnim.TriggerAttack(attackVariant);
        PlayShotSound(activeWeaponId);

        Vector3 origin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.35f;
        Vector3 aimPoint = _playerController != null
            ? _playerController.GetAimPoint()
            : origin + transform.forward * weaponData.Range;
        Vector3 direction = aimPoint - origin;
        direction = direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;

        if (PredictionManager == null || !PredictionManager.IsReconciling)
        {
            _playerController?.ApplyWeaponCameraFeedback(weaponData);
        }

        RequestFireServerRpc(origin, direction, activeSlot, activeWeaponId, attackVariant);
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartNetwork()
    {
        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged += HandleEquippedWeaponChanged;
            _weaponInventory.OnSlotUpdated += HandleSlotUpdated;
        }
    }

    public override void OnStopNetwork()
    {
        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged -= HandleEquippedWeaponChanged;
            _weaponInventory.OnSlotUpdated -= HandleSlotUpdated;
        }

        _isServerReloading = false;
    }

    public override void OnStartClient()
    {
        if (!IsOwner || _weaponInventory == null || _ownerStates == null) return;

        for (int i = 0; i < _ownerStates.Length; i++)
        {
            WeaponSlotState state = _weaponInventory.GetSlotState((EWeaponSlotType)i);
            _ownerStates[i].currentAmmo = state.CurrentAmmo;
            _ownerStates[i].reserveAmmo = state.ReserveAmmo;
            _ownerStates[i].nextFireTime = 0f;
        }

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        int activeIndex = (int)activeSlot;
        if (activeIndex >= 0 && activeIndex < _ownerStates.Length)
        {
            OnAmmoChanged?.Invoke((EWeaponType)activeSlot, _ownerStates[activeIndex].currentAmmo, _ownerStates[activeIndex].reserveAmmo);
        }
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

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            _isFireHeld = false;
            _fireRequested = false;
            return;
        }

        if (isHeld && !_isFireHeld)
        {
            _fireRequested = true;
        }
        _isFireHeld = isHeld;
    }

    /// <summary>진행 중인 사격 입력, 재장전 타이머 및 근접 콤보 상태를 즉시 초기화한다.</summary>
    public void CancelCombatActions()
    {
        _isFireHeld = false;
        _fireRequested = false;
        _isOwnerReloading = false;
        _isServerReloading = false;
        _meleeComboStep = 0;
        _meleeComboExpireTime = 0f;
    }

    /// <summary>현재 무기가 재장전 가능한 상태면 재장전 시퀀스를 시작한다.</summary>
    public void TryStartReload()
    {
        if (!IsOwner || !IsSpawned || _playerAnim == null || _weaponInventory == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_isOwnerReloading || _playerAnim.IsSwappingWeapon) return;

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        string activeWeaponId = _weaponInventory.EquippedWeaponId;
        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo) return;

        int slotIndex = (int)activeSlot;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;
        if (_ownerStates[slotIndex].currentAmmo >= weaponData.MaxAmmo) return;
        if (_ownerStates[slotIndex].reserveAmmo <= 0) return;

        _isOwnerReloading = true;
        _ownerReloadSlot = activeSlot;
        _ownerReloadWeaponId = activeWeaponId;
        _ownerReloadEndTime = Time.time + weaponData.ReloadTime;
        _fireRequested = false;

        _playerAnim.RequestReload();
        RequestReloadServerRpc(activeSlot, activeWeaponId);
    }

    /// <summary><paramref name="weaponType"/> 슬롯에 보유 중인 <see cref="WeaponDataSO"/>를 반환한다.</summary>
    public WeaponDataSO GetWeaponData(EWeaponType weaponType)
    {
        return _weaponInventory != null ? _weaponInventory.GetWeaponDataInSlot((EWeaponSlotType)weaponType) : null;
    }

    /// <summary>지정한 <paramref name="weaponId"/>의 사격 사운드를 1회 재생한다.</summary>
    public void PlayShotSound(string weaponId)
    {
        if (!IsSpawned || weaponAudioSource == null || _weaponInventory == null) return;
        if (PredictionManager != null && PredictionManager.IsReconciling) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        WeaponDataSO weaponData = _weaponInventory.GetWeaponDataById(weaponId);
        if (weaponData == null || weaponData.ShotClip == null) return;

        weaponAudioSource.PlayOneShot(weaponData.ShotClip, weaponData.ShotVolume);
    }

    /// <summary>지정한 <paramref name="weaponType"/> 슬롯 무기의 사격 사운드를 1회 재생한다.</summary>
    public void PlayShotSound(EWeaponType weaponType)
    {
        WeaponDataSO weaponData = GetWeaponData(weaponType);
        if (weaponData != null)
        {
            PlayShotSound(weaponData.WeaponId);
        }
    }

    /// <summary>현재 장착 무기의 재장전 단계(<paramref name="step"/>: 1~3) 사운드를 재생한다.</summary>
    public void PlayReloadSound(int step)
    {
        if (!IsSpawned || weaponAudioSource == null || _playerAnim == null || _weaponInventory == null) return;
        if (PredictionManager != null && PredictionManager.IsReconciling) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_playerController != null && _playerController.IsRagdollIdle) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo) return;
        if (_playerAnim.IsSwappingWeapon || !_playerAnim.IsReloadAnimationActive) return;

        AudioClip reloadClip = weaponData.GetReloadClip(step);
        if (reloadClip == null) return;

        weaponAudioSource.PlayOneShot(reloadClip, weaponData.ReloadVolume);
    }

    /// <summary>현재 장착 무기의 드로우 노리쇠/슬라이드 사운드(재장전 3단계 클립)를 재생한다.</summary>
    public void PlayDrawSound()
    {
        if (!IsSpawned || weaponAudioSource == null || _playerAnim == null || _weaponInventory == null) return;
        if (PredictionManager != null && PredictionManager.IsReconciling) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_playerController != null && _playerController.IsRagdollIdle) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo) return;
        if (!_playerAnim.IsDrawAnimationActive) return;

        AudioClip drawClip = weaponData.GetReloadClip(3);
        if (drawClip == null) return;

        weaponAudioSource.PlayOneShot(drawClip, weaponData.ReloadVolume);
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>서버에서 발사 조건을 검증하고 히트스캔 또는 근접 충돌 판정 및 데미지를 적용한다.</summary>
    [ServerRpc]
    private void RequestFireServerRpc(
        Vector3 origin,
        Vector3 direction,
        EWeaponSlotType slot,
        string weaponId,
        byte attackVariant,
        Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_weaponInventory == null || _weaponInventory.ActiveSlot != slot) return;
        if (!string.Equals(_weaponInventory.EquippedWeaponId, weaponId, StringComparison.Ordinal)) return;
        if (_isServerReloading) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null) return;

        int slotIndex = (int)slot;
        if (slotIndex < 0 || slotIndex >= _serverNextFireTimes.Length) return;
        if (Time.time < _serverNextFireTimes[slotIndex] - FireRateTolerance) return;

        if (weaponData.UsesAmmo)
        {
            if (!_weaponInventory.TryConsumeAmmo(slot, weaponId, out WeaponSlotState updatedAmmo))
            {
                SyncAmmoTargetRpc(Owner, slot, updatedAmmo.CurrentAmmo, updatedAmmo.ReserveAmmo);
                return;
            }

            SyncAmmoTargetRpc(Owner, slot, updatedAmmo.CurrentAmmo, updatedAmmo.ReserveAmmo);
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

        _serverNextFireTimes[slotIndex] = Time.time + weaponData.AttackCooldown;

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
                DamageHitInfo hitInfo = new DamageHitInfo
                {
                    HitDirection = direction,
                    HitPoint = hitPoint,
                    HasHitPoint = true,
                    IsMelee = weaponData.Category == EWeaponCategory.Melee,
                    AttackerObject = NetworkObject
                };
                damageable.TakeDamage(weaponData.Damage, Owner, in hitInfo);
                hitDamageable = true;
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 적중 → {closestHit.collider.name} (데미지: {weaponData.Damage:F1}, 거리: {closestHit.distance:F2}m)");
            }
            else
            {
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 충돌 → {closestHit.collider.name} (거리: {closestHit.distance:F2}m)");
            }
        }

        Debug.DrawLine(origin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
        BroadcastFireObserversRpc(origin, hitPoint, hitDamageable, weaponId, attackVariant);

        if (hitDamageable)
        {
            NotifyHitConfirmedTargetRpc(Owner);
        }
    }

    /// <summary>서버에서 재장전 타이머를 시작한다.</summary>
    [ServerRpc]
    private void RequestReloadServerRpc(EWeaponSlotType slot, string weaponId, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_weaponInventory == null || _weaponInventory.ActiveSlot != slot) return;
        if (!string.Equals(_weaponInventory.EquippedWeaponId, weaponId, StringComparison.Ordinal)) return;
        if (_isServerReloading) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo) return;

        WeaponSlotState slotState = _weaponInventory.GetSlotState(slot);
        if (slotState.IsEmpty || slotState.CurrentAmmo >= weaponData.MaxAmmo || slotState.ReserveAmmo <= 0) return;

        _isServerReloading = true;
        _serverReloadSlot = slot;
        _serverReloadWeaponId = weaponId;
        _serverReloadEndTime = Time.time + weaponData.ReloadTime;
    }

    /// <summary>클라이언트에 발사 애니메이션, 사격 사운드와 디버그 궤적을 전파한다.</summary>
    [ObserversRpc]
    private void BroadcastFireObserversRpc(Vector3 origin, Vector3 hitPoint, bool hitDamageable, string weaponId, byte attackVariant)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;

        if (!IsOwner)
        {
            _playerAnim?.TriggerAttack(attackVariant);
            PlayShotSound(weaponId);
        }

        if (!IsServerInitialized)
        {
            Debug.DrawLine(origin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
        }
    }

    /// <summary>서버의 권위 탄약 수치를 소유자 클라이언트에 동기화한다.</summary>
    [TargetRpc]
    private void SyncAmmoTargetRpc(NetworkConnection target, EWeaponSlotType slot, int currentAmmo, int reserveAmmo)
    {
        int index = (int)slot;
        if (_ownerStates == null || index < 0 || index >= _ownerStates.Length) return;

        _ownerStates[index].currentAmmo = currentAmmo;
        _ownerStates[index].reserveAmmo = reserveAmmo;
        OnAmmoChanged?.Invoke((EWeaponType)slot, currentAmmo, reserveAmmo);
    }

    /// <summary>서버에서 확정된 <see cref="IDamageable"/> 적중 사실을 소유자 클라이언트에 알린다.</summary>
    [TargetRpc]
    private void NotifyHitConfirmedTargetRpc(NetworkConnection target)
    {
        OnHitConfirmed?.Invoke();
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>장착 무기 변경 시 진행 중인 재장전과 공격 상태를 정리하고 탄약을 반영한다.</summary>
    private void HandleEquippedWeaponChanged(EquippedWeaponState state)
    {
        _isOwnerReloading = false;
        _isServerReloading = false;
        _fireRequested = false;

        if (state.Slot != EWeaponSlotType.Melee)
        {
            _meleeComboStep = 0;
            _meleeComboExpireTime = 0f;
        }

        if (!IsOwner || _weaponInventory == null || _ownerStates == null) return;

        int slotIndex = (int)state.Slot;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;

        WeaponSlotState slotState = _weaponInventory.GetSlotState(state.Slot);
        _ownerStates[slotIndex].currentAmmo = slotState.CurrentAmmo;
        _ownerStates[slotIndex].reserveAmmo = slotState.ReserveAmmo;
        _ownerStates[slotIndex].nextFireTime = 0f;
        OnAmmoChanged?.Invoke((EWeaponType)state.Slot, slotState.CurrentAmmo, slotState.ReserveAmmo);
    }

    /// <summary>인벤토리 슬롯 상태가 동기화될 때 소유자 로컬 예측 탄약을 서버 권위 값에 맞춘다.</summary>
    private void HandleSlotUpdated(EWeaponSlotType slot, WeaponSlotState state)
    {
        if (!IsOwner || _ownerStates == null) return;

        int index = (int)slot;
        if (index < 0 || index >= _ownerStates.Length) return;

        _ownerStates[index].currentAmmo = state.CurrentAmmo;
        _ownerStates[index].reserveAmmo = state.ReserveAmmo;
        OnAmmoChanged?.Invoke((EWeaponType)slot, state.CurrentAmmo, state.ReserveAmmo);
    }
}
