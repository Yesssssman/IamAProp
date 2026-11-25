using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;

/// <summary>무기 인스턴스별 탄약/쿨다운 관리와 서버 권위 히트스캔(SphereCast) 공격 판정을 처리한다.</summary>
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

    /// <summary>씬 뷰 기즈모로 표시할 단일 사격 구체 캐스트 궤적 기록.</summary>
    private struct ShotGizmoTrace
    {
        public Vector3 origin;
        public Vector3 endPoint;
        public float radius;
        public bool hitDamageable;
        public float expireTime;
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float FireRateTolerance = 0.03f; // 네트워크 지터를 고려한 서버 발사 쿨다운 허용 오차 (초)
    private const float MaxOriginOffset = 2.5f; // 서버에서 허용하는 발사 원점 최대 편차 (미터)
    private const float MeleeComboBufferWindow = 0.14f; // 근접 공격 막바지에 선입력을 허용하는 남은 쿨다운 시간 (초)
    private const float MeleeComboGraceWindow = 0.25f; // 근접 쿨다운 종료 후 콤보를 이어갈 수 있는 유예 시간 (초)
    private const int DefaultCombatLayerMask = (1 << 0) | (1 << 3) | (1 << 6) | (1 << 7); // Default, Ground, Player, Hitbox
    private const int MaxShotGizmoTraces = 48; // 동시에 표시할 최근 사격 궤적 기즈모 최대 개수 (12발 산탄 4회분)
    private const float ShellInsertTimeTolerance = 0.08f; // 1발 장전 서버 간격 검증 허용 오차 (초)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>히트스캔 및 근접 판정에 포함할 물리 레이어 마스크.</summary>
    [Header("히트스캔 설정")]
    [SerializeField] private LayerMask hitLayerMask = DefaultCombatLayerMask;

    /// <summary>총기 히트스캔 구체 캐스트 기본 반지름 (미터).</summary>
    [Min(0.01f)]
    [SerializeField] private float shotSphereRadius = 0.05f;

    /// <summary>씬 뷰에 디버그 사격 궤적을 표시할 유지 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float debugRayDuration = 1.5f;

    /// <summary>씬 뷰에 구체 캐스트 사격 궤적 기즈모를 표시할지 여부.</summary>
    [SerializeField] private bool drawShotGizmos = true;

    /// <summary>총기 사격 및 재장전 사운드를 3D 공간에서 재생할 <see cref="AudioSource"/> 컴포넌트.</summary>
    [Header("사운드 설정")]
    [SerializeField] private AudioSource weaponAudioSource;

    /// <summary>소유자 측 로컬 예측용 슬롯별 탄약 및 쿨다운 배열.</summary>
    private OwnerSlotRuntimeState[] _ownerStates;

    /// <summary>서버 측 슬롯별 다음 발사 허용 시각 배열 (<see cref="Time.time"/> 기준).</summary>
    private float[] _serverNextFireTimes;

    /// <summary>히트스캔 물리 캐스트 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[32];

    /// <summary>총구 초근접 겹침 판정 결과를 저장하는 재사용 버퍼.</summary>
    private readonly Collider[] _overlapBuffer = new Collider[16];

    /// <summary>씬 뷰 기즈모 렌더링용 최근 사격 궤적 링 버퍼.</summary>
    private readonly ShotGizmoTrace[] _shotGizmoTraces = new ShotGizmoTrace[MaxShotGizmoTraces];

    /// <summary>다음 사격 궤적 기즈모를 기록할 링 버퍼 인덱스.</summary>
    private int _shotGizmoWriteIndex;

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

    /// <summary>소유자 측 1발씩 반복 장전(샷건) 진행 여부.</summary>
    private bool _isOwnerShellReloading;

    /// <summary>샷건 장전 중 사격 입력으로 루프 종료 후 발사가 예약되었는지 여부.</summary>
    private bool _isPendingShellReloadFire;

    /// <summary>서버 측 재장전 진행 여부.</summary>
    private bool _isServerReloading;

    /// <summary>서버 측 재장전 완료 예정 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _serverReloadEndTime;

    /// <summary>서버 측 재장전 중인 무기 슬롯.</summary>
    private EWeaponSlotType _serverReloadSlot;

    /// <summary>서버 측 재장전 중인 무기 식별자.</summary>
    private string _serverReloadWeaponId;

    /// <summary>서버 측 1발씩 반복 장전(샷건) 진행 여부.</summary>
    private bool _isServerShellReloading;

    /// <summary>서버 측 다음 1발 장전 허용 최소 시각 (<see cref="Time.time"/> 기준).</summary>
    private float _serverNextShellInsertTime;

    /// <summary>서버 권위 고정 탄착군 프리셋 순환 인덱스.</summary>
    private int _serverSpreadPresetIndex;

    /// <summary>다중 펠릿 사격 시 대상별 적중 합산용 재사용 버퍼.</summary>
    private readonly List<IDamageable> _pelletHitTargets = new(8);

    /// <summary>다중 펠릿 사격 시 대상별 적중 펠릿 수 버퍼.</summary>
    private readonly List<int> _pelletHitCounts = new(8);

    /// <summary>다중 펠릿 사격 시 대상별 대표 적중 좌표 버퍼.</summary>
    private readonly List<Vector3> _pelletHitPoints = new(8);

    /// <summary>다중 펠릿 사격 시 대상별 대표 적중 방향 버퍼.</summary>
    private readonly List<Vector3> _pelletHitDirections = new(8);

    /// <summary>다중 펠릿 사격 시 대상별 대표 적중 콜라이더 이름 버퍼.</summary>
    private readonly List<string> _pelletHitNames = new(8);

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
                _isServerShellReloading = false;
            }
            else if (!_isServerShellReloading && Time.time >= _serverReloadEndTime)
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
                _isOwnerShellReloading = false;
                _isPendingShellReloadFire = false;
            }
            else if (!_isOwnerShellReloading && Time.time >= _ownerReloadEndTime)
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

        // 샷건 1발 장전 중 사격 입력이 들어오면 루프 종료(Has Exit Time) 후 예약 발사한다.
        if (_isOwnerShellReloading)
        {
            int reloadSlotIdx = (int)activeSlot;
            bool hasShellLoaded = reloadSlotIdx >= 0 && reloadSlotIdx < _ownerStates.Length && _ownerStates[reloadSlotIdx].currentAmmo > 0;

            if ((_isFireHeld || _fireRequested) && !_isPendingShellReloadFire)
            {
                _isPendingShellReloadFire = true;
                _fireRequested = false;
                if (hasShellLoaded)
                {
                    _playerAnim.EndShellReload(interruptForFire: true);
                    RequestCancelShellReloadServerRpc(activeSlot, activeWeaponId);
                }
            }

            if (_isPendingShellReloadFire && hasShellLoaded && _playerAnim.CanFireAfterShellReloadCancel)
            {
                _isOwnerReloading = false;
                _isOwnerShellReloading = false;
                _isPendingShellReloadFire = false;
                _fireRequested = true;
            }
            else
            {
                return;
            }
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

        bool isMelee = weaponData.Category == EWeaponCategory.Melee;
        Vector3 pivotOrigin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.35f;
        Vector3 clientOrigin = !isMelee && _playerAnim.CurrentMuzzlePoint != null
            ? _playerAnim.CurrentMuzzlePoint.position
            : pivotOrigin;
        Vector3 aimPoint = _playerController != null
            ? _playerController.GetAimPoint()
            : clientOrigin + transform.forward * weaponData.Range;

        if (PredictionManager == null || !PredictionManager.IsReconciling)
        {
            _playerController?.ApplyWeaponCameraFeedback(weaponData);
        }

        bool isAds = _playerController != null && _playerController.IsAdsActive;
        RequestFireServerRpc(clientOrigin, aimPoint, activeSlot, activeWeaponId, attackVariant, isAds);
    }

    private void OnDrawGizmos()
    {
        if (!drawShotGizmos || debugRayDuration <= 0f) return;

        float now = Time.time;
        for (int i = 0; i < _shotGizmoTraces.Length; i++)
        {
            ShotGizmoTrace trace = _shotGizmoTraces[i];
            if (trace.expireTime <= now) continue;

            Color mainColor = trace.hitDamageable ? Color.red : Color.yellow;
            Gizmos.color = mainColor;
            Gizmos.DrawLine(trace.origin, trace.endPoint);

            if (trace.radius > 0f)
            {
                Gizmos.color = Color.cyan;
                Gizmos.DrawWireSphere(trace.origin, trace.radius);

                Gizmos.color = mainColor;
                Gizmos.DrawWireSphere(trace.endPoint, trace.radius);

                Vector3 delta = trace.endPoint - trace.origin;
                if (delta.sqrMagnitude > 0.0001f)
                {
                    Vector3 dir = delta.normalized;
                    Vector3 refUp = Mathf.Abs(Vector3.Dot(dir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
                    Vector3 right = Vector3.Cross(dir, refUp).normalized * trace.radius;
                    Vector3 up = Vector3.Cross(right, dir).normalized * trace.radius;

                    Gizmos.color = new Color(mainColor.r, mainColor.g, mainColor.b, 0.55f);
                    Gizmos.DrawLine(trace.origin + right, trace.endPoint + right);
                    Gizmos.DrawLine(trace.origin - right, trace.endPoint - right);
                    Gizmos.DrawLine(trace.origin + up, trace.endPoint + up);
                    Gizmos.DrawLine(trace.origin - up, trace.endPoint - up);
                }
            }

            Gizmos.color = mainColor;
            float markerRadius = trace.radius > 0f ? Mathf.Min(trace.radius * 0.35f, 0.03f) : 0.02f;
            Gizmos.DrawSphere(trace.endPoint, markerRadius);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!drawShotGizmos || _playerAnim == null || _playerAnim.CurrentMuzzlePoint == null) return;

        WeaponDataSO weaponData = CurrentWeaponData;
        if (weaponData == null || weaponData.Category == EWeaponCategory.Melee) return;

        float previewRadius = Mathf.Max(
            0.01f,
            weaponData.ShotSphereRadiusOverride > 0f ? weaponData.ShotSphereRadiusOverride : shotSphereRadius);
        Transform muzzle = _playerAnim.CurrentMuzzlePoint;

        Gizmos.color = Color.green;
        Gizmos.DrawWireSphere(muzzle.position, previewRadius);
        Gizmos.DrawRay(muzzle.position, muzzle.forward * 0.5f);
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
        _isServerShellReloading = false;
        Array.Clear(_shotGizmoTraces, 0, _shotGizmoTraces.Length);
        _shotGizmoWriteIndex = 0;
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
        _isOwnerShellReloading = false;
        _isPendingShellReloadFire = false;
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
        _isOwnerShellReloading = false;
        _isPendingShellReloadFire = false;
        _isServerReloading = false;
        _isServerShellReloading = false;
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
        _isOwnerShellReloading = weaponData.IsShellReload;
        _isPendingShellReloadFire = false;
        _ownerReloadSlot = activeSlot;
        _ownerReloadWeaponId = activeWeaponId;
        _ownerReloadEndTime = Time.time + weaponData.ReloadTime;
        _fireRequested = false;

        _playerAnim.RequestReload();
        RequestReloadServerRpc(activeSlot, activeWeaponId);
    }

    /// <summary>샷건 <c>Reload_Loop</c> 애니메이션 이벤트에서 호출되어 1발 장전과 루프 종료 여부를 처리한다.</summary>
    public void HandleReloadShellInserted()
    {
        if (!IsSpawned || _playerAnim == null || _weaponInventory == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo || !weaponData.IsShellReload) return;

        _playerAnim.HideLeftHandItem();
        PlayReloadSound(2);

        if (!IsOwner || !_isOwnerShellReloading) return;

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        string activeWeaponId = _weaponInventory.EquippedWeaponId;
        if (activeSlot != _ownerReloadSlot
            || !string.Equals(activeWeaponId, _ownerReloadWeaponId, StringComparison.Ordinal))
        {
            return;
        }

        int slotIndex = (int)activeSlot;
        if (slotIndex < 0 || slotIndex >= _ownerStates.Length) return;
        if (_ownerStates[slotIndex].currentAmmo >= weaponData.MaxAmmo || _ownerStates[slotIndex].reserveAmmo <= 0)
        {
            _isOwnerReloading = false;
            _isOwnerShellReloading = false;
            _playerAnim.EndShellReload(interruptForFire: _isPendingShellReloadFire);
            return;
        }

        int projectedAmmo = _ownerStates[slotIndex].currentAmmo + 1;
        int projectedReserve = _ownerStates[slotIndex].reserveAmmo - 1;
        if (!IsServerInitialized)
        {
            _ownerStates[slotIndex].currentAmmo = projectedAmmo;
            _ownerStates[slotIndex].reserveAmmo = projectedReserve;
            OnAmmoChanged?.Invoke((EWeaponType)activeSlot, projectedAmmo, projectedReserve);
        }

        RequestInsertShellServerRpc(activeSlot, activeWeaponId);

        if (_isPendingShellReloadFire)
        {
            _playerAnim.EndShellReload(interruptForFire: true);
            RequestCancelShellReloadServerRpc(activeSlot, activeWeaponId);
        }
        else if (projectedAmmo >= weaponData.MaxAmmo || projectedReserve <= 0)
        {
            _isOwnerReloading = false;
            _isOwnerShellReloading = false;
            _playerAnim.EndShellReload(interruptForFire: false);
        }
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

    /// <summary>현재 장착 무기의 재장전 단계(<paramref name="step"/>: 1부터 시작) 사운드를 재생한다.</summary>
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

    /// <summary>현재 장착 무기의 드로우 노리쇠/슬라이드 사운드(재장전 마지막 단계 클립)를 재생한다.</summary>
    public void PlayDrawSound()
    {
        if (!IsSpawned || weaponAudioSource == null || _playerAnim == null || _weaponInventory == null) return;
        if (PredictionManager != null && PredictionManager.IsReconciling) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_playerController != null && _playerController.IsRagdollIdle) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo) return;
        if (!_playerAnim.IsDrawAnimationActive) return;

        AudioClip drawClip = weaponData.DrawSoundClip;
        if (drawClip == null) return;

        weaponAudioSource.PlayOneShot(drawClip, weaponData.ReloadVolume);
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>서버에서 발사 조건과 총구 원점을 검증하고 히트스캔 충돌 판정 및 데미지를 적용한다.</summary>
    [ServerRpc]
    private void RequestFireServerRpc(
        Vector3 clientOrigin,
        Vector3 aimPoint,
        EWeaponSlotType slot,
        string weaponId,
        byte attackVariant,
        bool isAds,
        Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_weaponInventory == null || _weaponInventory.ActiveSlot != slot) return;
        if (!string.Equals(_weaponInventory.EquippedWeaponId, weaponId, StringComparison.Ordinal)) return;
        if (_isServerReloading) return;
        if (!IsFiniteVector(clientOrigin) || !IsFiniteVector(aimPoint)) return;

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

        _serverNextFireTimes[slotIndex] = Time.time + weaponData.AttackCooldown;

        bool isMelee = weaponData.Category == EWeaponCategory.Melee;
        Vector3 serverPivotOrigin = _playerController != null
            ? _playerController.GetFireOrigin()
            : transform.position + Vector3.up * 0.35f;
        Vector3 serverMuzzleOrigin = !isMelee && _playerAnim != null && _playerAnim.CurrentMuzzlePoint != null
            ? _playerAnim.CurrentMuzzlePoint.position
            : serverPivotOrigin;

        // 클라이언트 총구 좌표가 서버 총구 기준 허용 반경 내이고 사이에 장애물이 없을 때만 채택한다.
        Vector3 fireOrigin = serverMuzzleOrigin;
        Vector3 originDelta = clientOrigin - serverMuzzleOrigin;
        float originDeltaDist = originDelta.magnitude;
        if (originDeltaDist <= MaxOriginOffset)
        {
            bool blockedBetweenOrigins = false;
            if (originDeltaDist > 0.001f)
            {
                int verifyHits = Physics.RaycastNonAlloc(
                    serverMuzzleOrigin,
                    originDelta / originDeltaDist,
                    _hitBuffer,
                    originDeltaDist,
                    hitLayerMask,
                    QueryTriggerInteraction.Ignore);
                blockedBetweenOrigins = TryFindClosestNonSelfHit(verifyHits, out _);
            }

            if (!blockedBetweenOrigins)
            {
                fireOrigin = clientOrigin;
            }
        }

        Vector3 toAimFromMuzzle = aimPoint - fireOrigin;
        Vector3 toAimFromPivot = aimPoint - serverPivotOrigin;
        Vector3 fallbackForward = toAimFromPivot.sqrMagnitude > 0.0001f
            ? toAimFromPivot.normalized
            : transform.forward;
        Vector3 direction = toAimFromMuzzle.sqrMagnitude > 0.0001f && Vector3.Dot(toAimFromMuzzle, fallbackForward) > 0f
            ? toAimFromMuzzle.normalized
            : fallbackForward;

        float castRadius = isMelee
            ? weaponData.MeleeCastRadius
            : Mathf.Max(0.01f, weaponData.ShotSphereRadiusOverride > 0f ? weaponData.ShotSphereRadiusOverride : shotSphereRadius);

        bool hasValidHit = false;
        Collider hitCollider = null;
        Vector3 hitPoint = fireOrigin + direction * weaponData.Range;
        float hitDistance = 0f;
        Vector3 traceOrigin = fireOrigin;

        // 1단계: 총기 사격 시 몸체 피벗~총구 구간의 벽 관통 차단 및 총구 뒤 초근접 적 검출
        if (!isMelee)
        {
            Vector3 toMuzzle = fireOrigin - serverPivotOrigin;
            float toMuzzleDist = toMuzzle.magnitude;
            if (toMuzzleDist > 0.001f)
            {
                int barrelHits = Physics.RaycastNonAlloc(
                    serverPivotOrigin,
                    toMuzzle / toMuzzleDist,
                    _hitBuffer,
                    toMuzzleDist,
                    hitLayerMask,
                    QueryTriggerInteraction.Ignore);

                if (TryFindClosestNonSelfHit(barrelHits, out RaycastHit barrelHit))
                {
                    hasValidHit = true;
                    hitCollider = barrelHit.collider;
                    hitPoint = barrelHit.point != Vector3.zero
                        ? barrelHit.point
                        : GetSafeClosestPoint(barrelHit.collider, serverPivotOrigin);
                    hitDistance = barrelHit.distance;
                    traceOrigin = serverPivotOrigin;
                }
            }
        }

        // 1-B단계: 다중 펠릿 무기(샷건)는 5개 고정 탄착군 프리셋을 순환하며 12발 Raycast를 일괄 판정한다.
        if (!isMelee && weaponData.IsMultiPellet)
        {
            int pelletCount = weaponData.PelletsPerShot;
            int presetCount = weaponData.SpreadPresets != null ? weaponData.SpreadPresets.Length : 0;
            byte presetIndex = 0;
            WeaponSpreadPreset preset = default;
            if (presetCount > 0)
            {
                presetIndex = (byte)(_serverSpreadPresetIndex % presetCount);
                _serverSpreadPresetIndex = (_serverSpreadPresetIndex + 1) % presetCount;
                preset = weaponData.GetSpreadPreset(presetIndex);
            }

            bool anyHitDamageable = false;
            if (hasValidHit && hitCollider != null)
            {
                IDamageable barrelDamageable = hitCollider.GetComponentInParent<IDamageable>();
                if (barrelDamageable != null && !barrelDamageable.IsDead)
                {
                    float totalDamage = weaponData.Damage * pelletCount;
                    DamageHitInfo hitInfo = new DamageHitInfo
                    {
                        HitDirection = direction,
                        HitPoint = hitPoint,
                        HasHitPoint = true,
                        IsMelee = false,
                        AttackerObject = NetworkObject
                    };
                    barrelDamageable.TakeDamage(totalDamage, Owner, in hitInfo);
                    anyHitDamageable = true;
                    Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 초근접 적중 → {hitCollider.name} ({pelletCount}펠릿, 데미지: {totalDamage:F1})");
                }
                RecordShotGizmo(traceOrigin, hitPoint, 0f, anyHitDamageable);
                Debug.DrawLine(traceOrigin, hitPoint, anyHitDamageable ? Color.red : Color.yellow, debugRayDuration);
            }
            else
            {
                float spreadMultiplier = isAds ? weaponData.AdsSpreadMultiplier : 1f;
                Vector3 refUp = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 basisRight = Vector3.Cross(refUp, direction).normalized;
                Vector3 basisUp = Vector3.Cross(direction, basisRight).normalized;

                _pelletHitTargets.Clear();
                _pelletHitCounts.Clear();
                _pelletHitPoints.Clear();
                _pelletHitDirections.Clear();
                _pelletHitNames.Clear();

                for (int p = 0; p < pelletCount; p++)
                {
                    Vector2 angleDeg = preset.GetPelletAngle(p) * spreadMultiplier;
                    Vector3 pelletDir = ComputePelletDirection(direction, basisRight, basisUp, angleDeg);
                    Vector3 pelletEnd = fireOrigin + pelletDir * weaponData.Range;
                    bool pelletHitTarget = false;

                    int hitCount = Physics.RaycastNonAlloc(
                        fireOrigin,
                        pelletDir,
                        _hitBuffer,
                        weaponData.Range,
                        hitLayerMask,
                        QueryTriggerInteraction.Ignore);

                    if (TryFindClosestNonSelfHit(hitCount, out RaycastHit pelletHit))
                    {
                        pelletEnd = pelletHit.point != Vector3.zero
                            ? pelletHit.point
                            : GetSafeClosestPoint(pelletHit.collider, fireOrigin);
                        IDamageable damageable = pelletHit.collider.GetComponentInParent<IDamageable>();
                        if (damageable != null && !damageable.IsDead)
                        {
                            pelletHitTarget = true;
                            int existingIdx = _pelletHitTargets.IndexOf(damageable);
                            if (existingIdx >= 0)
                            {
                                _pelletHitCounts[existingIdx]++;
                            }
                            else
                            {
                                _pelletHitTargets.Add(damageable);
                                _pelletHitCounts.Add(1);
                                _pelletHitPoints.Add(pelletEnd);
                                _pelletHitDirections.Add(pelletDir);
                                _pelletHitNames.Add(pelletHit.collider.name);
                            }
                        }
                    }

                    RecordShotGizmo(fireOrigin, pelletEnd, 0f, pelletHitTarget);
                    Debug.DrawLine(fireOrigin, pelletEnd, pelletHitTarget ? Color.red : Color.yellow, debugRayDuration);
                }

                for (int i = 0; i < _pelletHitTargets.Count; i++)
                {
                    int pelletsHit = _pelletHitCounts[i];
                    float totalDamage = weaponData.Damage * pelletsHit;
                    DamageHitInfo hitInfo = new DamageHitInfo
                    {
                        HitDirection = _pelletHitDirections[i],
                        HitPoint = _pelletHitPoints[i],
                        HasHitPoint = true,
                        IsMelee = false,
                        AttackerObject = NetworkObject
                    };
                    _pelletHitTargets[i].TakeDamage(totalDamage, Owner, in hitInfo);
                    anyHitDamageable = true;
                    Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 프리셋#{presetIndex} 적중 → {_pelletHitNames[i]} ({pelletsHit}/{pelletCount}펠릿, 데미지: {totalDamage:F1})");
                }
            }

            BroadcastFireObserversRpc(traceOrigin, fireOrigin + direction * weaponData.Range, 0f, anyHitDamageable, weaponId, attackVariant, presetIndex, isAds);
            if (anyHitDamageable)
            {
                NotifyHitConfirmedTargetRpc(Owner);
            }
            return;
        }

        // 2단계: 시작 구체에 이미 겹쳐 있는 초근접 적 검출 (Physics.SphereCast의 초기 겹침 무시 보완)
        if (!hasValidHit && castRadius > 0f)
        {
            int overlapCount = Physics.OverlapSphereNonAlloc(
                fireOrigin,
                castRadius,
                _overlapBuffer,
                hitLayerMask,
                QueryTriggerInteraction.Ignore);

            float closestOverlapDistSqr = float.MaxValue;
            for (int i = 0; i < overlapCount; i++)
            {
                Collider col = _overlapBuffer[i];
                if (col == null || col.transform.IsChildOf(transform)) continue;

                IDamageable candidate = col.GetComponentInParent<IDamageable>();
                if (candidate == null || candidate.IsDead) continue;

                Vector3 closestPt = GetSafeClosestPoint(col, fireOrigin);
                Vector3 toCandidate = closestPt - fireOrigin;
                float distSqr = toCandidate.sqrMagnitude;
                if (distSqr >= closestOverlapDistSqr) continue;

                if (distSqr > 0.0001f)
                {
                    float dist = Mathf.Sqrt(distSqr);
                    int losHits = Physics.RaycastNonAlloc(
                        fireOrigin,
                        toCandidate / dist,
                        _hitBuffer,
                        dist,
                        hitLayerMask,
                        QueryTriggerInteraction.Ignore);

                    if (TryFindClosestNonSelfHit(losHits, out RaycastHit losHit)
                        && losHit.collider.GetComponentInParent<IDamageable>() != candidate)
                    {
                        continue;
                    }
                }

                closestOverlapDistSqr = distSqr;
                hitCollider = col;
                hitPoint = closestPt;
                hitDistance = Mathf.Sqrt(distSqr);
                hasValidHit = true;
            }
        }

        // 3단계: 총구에서 크로스헤어 조준점 방향으로 구체 캐스트 실행
        if (!hasValidHit)
        {
            int hitCount = castRadius > 0f
                ? Physics.SphereCastNonAlloc(
                    fireOrigin,
                    castRadius,
                    direction,
                    _hitBuffer,
                    weaponData.Range,
                    hitLayerMask,
                    QueryTriggerInteraction.Ignore)
                : Physics.RaycastNonAlloc(
                    fireOrigin,
                    direction,
                    _hitBuffer,
                    weaponData.Range,
                    hitLayerMask,
                    QueryTriggerInteraction.Ignore);

            if (TryFindClosestNonSelfHit(hitCount, out RaycastHit closestHit))
            {
                hasValidHit = true;
                hitCollider = closestHit.collider;
                hitPoint = closestHit.point != Vector3.zero
                    ? closestHit.point
                    : GetSafeClosestPoint(closestHit.collider, fireOrigin);
                hitDistance = closestHit.distance;
            }
        }

        bool hitDamageable = false;
        if (hasValidHit && hitCollider != null)
        {
            IDamageable damageable = hitCollider.GetComponentInParent<IDamageable>();
            if (damageable != null && !damageable.IsDead)
            {
                DamageHitInfo hitInfo = new DamageHitInfo
                {
                    HitDirection = direction,
                    HitPoint = hitPoint,
                    HasHitPoint = true,
                    IsMelee = isMelee,
                    AttackerObject = NetworkObject
                };
                damageable.TakeDamage(weaponData.Damage, Owner, in hitInfo);
                hitDamageable = true;
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 적중 → {hitCollider.name} (데미지: {weaponData.Damage:F1}, 거리: {hitDistance:F2}m)");
            }
            else
            {
                Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 충돌 → {hitCollider.name} (거리: {hitDistance:F2}m)");
            }
        }

        RecordShotGizmo(traceOrigin, hitPoint, castRadius, hitDamageable);
        Debug.DrawLine(traceOrigin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
        BroadcastFireObserversRpc(traceOrigin, hitPoint, castRadius, hitDamageable, weaponId, attackVariant, 0, isAds);

        if (hitDamageable)
        {
            NotifyHitConfirmedTargetRpc(Owner);
        }
    }

    /// <summary>서버에서 재장전 타이머 또는 샷건 1발 장전 상태를 시작한다.</summary>
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
        _isServerShellReloading = weaponData.IsShellReload;
        _serverReloadSlot = slot;
        _serverReloadWeaponId = weaponId;
        _serverReloadEndTime = Time.time + weaponData.ReloadTime;
        _serverNextShellInsertTime = Time.time + Mathf.Max(0f, weaponData.ShellReloadStartDuration - ShellInsertTimeTolerance);
    }

    /// <summary>서버에서 샷건 <c>Reload_Loop</c> 1회당 탄약 1발을 검증 후 장전한다.</summary>
    [ServerRpc]
    private void RequestInsertShellServerRpc(EWeaponSlotType slot, string weaponId, Channel channel = Channel.Reliable)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (!_isServerReloading || !_isServerShellReloading) return;
        if (_weaponInventory == null || _weaponInventory.ActiveSlot != slot) return;
        if (slot != _serverReloadSlot || !string.Equals(weaponId, _serverReloadWeaponId, StringComparison.Ordinal)) return;
        if (!string.Equals(_weaponInventory.EquippedWeaponId, weaponId, StringComparison.Ordinal)) return;
        if (Time.time < _serverNextShellInsertTime) return;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null || !weaponData.UsesAmmo || !weaponData.IsShellReload) return;

        if (_weaponInventory.TryInsertSingleShell(slot, weaponId, weaponData.MaxAmmo, out WeaponSlotState updated))
        {
            _serverNextShellInsertTime = Time.time + Mathf.Max(0.1f, weaponData.ShellInsertInterval - ShellInsertTimeTolerance);
            Debug.Log($"[PlayerWeaponController] {name}({weaponData.WeaponName}) 1발 장전: {updated.CurrentAmmo}/{updated.ReserveAmmo}");
            SyncAmmoTargetRpc(Owner, slot, updated.CurrentAmmo, updated.ReserveAmmo);

            if (updated.CurrentAmmo >= weaponData.MaxAmmo || updated.ReserveAmmo <= 0)
            {
                _isServerReloading = false;
                _isServerShellReloading = false;
            }
        }
        else
        {
            _isServerReloading = false;
            _isServerShellReloading = false;
        }
    }

    /// <summary>서버에서 샷건 1발 장전 중 사격 인터럽트 요청 시 재장전 잠금을 해제한다.</summary>
    [ServerRpc]
    private void RequestCancelShellReloadServerRpc(EWeaponSlotType slot, string weaponId, Channel channel = Channel.Reliable)
    {
        if (!_isServerReloading || !_isServerShellReloading) return;
        if (slot != _serverReloadSlot || !string.Equals(weaponId, _serverReloadWeaponId, StringComparison.Ordinal)) return;

        _isServerReloading = false;
        _isServerShellReloading = false;
    }

    /// <summary>클라이언트에 발사 애니메이션, 사격 사운드와 디버그 궤적을 전파한다.</summary>
    [ObserversRpc]
    private void BroadcastFireObserversRpc(
        Vector3 origin,
        Vector3 hitPoint,
        float castRadius,
        bool hitDamageable,
        string weaponId,
        byte attackVariant,
        byte spreadPresetIndex,
        bool isAds)
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;

        if (!IsOwner)
        {
            _playerAnim?.TriggerAttack(attackVariant);
            PlayShotSound(weaponId);
        }

        if (!IsServerInitialized)
        {
            WeaponDataSO weaponData = _weaponInventory != null ? _weaponInventory.GetWeaponDataById(weaponId) : null;
            if (weaponData != null && weaponData.IsMultiPellet)
            {
                Vector3 delta = hitPoint - origin;
                Vector3 baseDir = delta.sqrMagnitude > 0.0001f ? delta.normalized : transform.forward;
                float range = weaponData.Range;
                float spreadMultiplier = isAds ? weaponData.AdsSpreadMultiplier : 1f;
                WeaponSpreadPreset preset = weaponData.GetSpreadPreset(spreadPresetIndex);
                Vector3 refUp = Mathf.Abs(Vector3.Dot(baseDir, Vector3.up)) < 0.99f ? Vector3.up : Vector3.right;
                Vector3 basisRight = Vector3.Cross(refUp, baseDir).normalized;
                Vector3 basisUp = Vector3.Cross(baseDir, basisRight).normalized;

                for (int p = 0; p < weaponData.PelletsPerShot; p++)
                {
                    Vector3 pelletDir = ComputePelletDirection(baseDir, basisRight, basisUp, preset.GetPelletAngle(p) * spreadMultiplier);
                    Vector3 end = origin + pelletDir * range;
                    RecordShotGizmo(origin, end, 0f, hitDamageable);
                    Debug.DrawLine(origin, end, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
                }
            }
            else
            {
                RecordShotGizmo(origin, hitPoint, castRadius, hitDamageable);
                Debug.DrawLine(origin, hitPoint, hitDamageable ? Color.red : Color.yellow, debugRayDuration);
            }
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
        _isOwnerShellReloading = false;
        _isPendingShellReloadFire = false;
        _isServerReloading = false;
        _isServerShellReloading = false;
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

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary><see cref="_hitBuffer"/>에서 발사자 본인을 제외한 가장 가까운 충돌 결과를 찾는다.</summary>
    private bool TryFindClosestNonSelfHit(int hitCount, out RaycastHit closestHit)
    {
        bool found = false;
        float closestDistance = float.MaxValue;
        closestHit = default;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _hitBuffer[i];
            if (hit.collider == null || hit.transform == null || hit.transform.IsChildOf(transform)) continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestHit = hit;
                found = true;
            }
        }

        return found;
    }

    /// <summary>비볼록 <see cref="MeshCollider"/>에서도 예외 없이 <paramref name="point"/>에 가장 가까운 표면 좌표를 반환한다.</summary>
    private static Vector3 GetSafeClosestPoint(Collider collider, Vector3 point)
    {
        if (collider is MeshCollider meshCollider && !meshCollider.convex)
        {
            return collider.ClosestPointOnBounds(point);
        }

        return collider.ClosestPoint(point);
    }

    /// <summary>기준 방향 <paramref name="baseDirection"/>에 (Yaw, Pitch) 각도 오프셋을 적용한 펠릿 방향을 반환한다.</summary>
    private static Vector3 ComputePelletDirection(Vector3 baseDirection, Vector3 basisRight, Vector3 basisUp, Vector2 angleDeg)
    {
        Quaternion spreadRot = Quaternion.AngleAxis(angleDeg.x, basisUp) * Quaternion.AngleAxis(-angleDeg.y, basisRight);
        return (spreadRot * baseDirection).normalized;
    }

    /// <summary><paramref name="v"/>의 모든 성분이 유한한 실수인지 검사한다.</summary>
    private static bool IsFiniteVector(Vector3 v)
    {
        return float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }

    /// <summary>최근 사격 구체 캐스트 궤적을 씬 뷰 기즈모 링 버퍼에 기록한다.</summary>
    private void RecordShotGizmo(Vector3 origin, Vector3 endPoint, float radius, bool hitDamageable)
    {
        if (!drawShotGizmos || debugRayDuration <= 0f) return;

        _shotGizmoTraces[_shotGizmoWriteIndex] = new ShotGizmoTrace
        {
            origin = origin,
            endPoint = endPoint,
            radius = radius,
            hitDamageable = hitDamageable,
            expireTime = Time.time + debugRayDuration
        };
        _shotGizmoWriteIndex = (_shotGizmoWriteIndex + 1) % _shotGizmoTraces.Length;
    }
}
