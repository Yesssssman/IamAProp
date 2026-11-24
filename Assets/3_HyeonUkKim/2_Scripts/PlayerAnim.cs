using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;

/// <summary>소유자 및 원격 프록시의 무기 장착, 재장전, 총기 프레임 동기화를 담당하는 플레이어 애니메이터.</summary>
[RequireComponent(typeof(PlayerController))]
[RequireComponent(typeof(PlayerWeaponInventory))]
public class PlayerAnim : PropAnimBase
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float DefaultHandShotFrames = 17f; // 손 발사 클립 기본 총 프레임 수
    private const float DefaultWeaponShotFrames = 9f; // 총기 발사 클립 기본 총 프레임 수

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>무기 보유 현황과 현재 장착 상태를 제공하는 인벤토리 컴포넌트.</summary>
    private PlayerWeaponInventory _weaponInventory;

    /// <summary>서버 피격 이벤트와 사망 상태를 조회하는 체력 컴포넌트.</summary>
    private PlayerHealth _playerHealth;

    /// <summary>이 오브젝트에 마지막으로 적용된 장착 무기 스냅샷.</summary>
    private EquippedWeaponState _appliedEquippedState;

    /// <summary>최초 1회 장착 무기 동기화가 적용되었는지 여부.</summary>
    private bool _hasAppliedEquippedState;

    /// <summary>현재 적용된 무기의 <see cref="WeaponDataSO"/> 에셋.</summary>
    private WeaponDataSO _appliedWeaponData;

    /// <summary>현재 오른손에 장착된 무기의 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _weaponAnimator;

    /// <summary>현재 오른손에 장착된 무기 인스턴스.</summary>
    private GameObject _currentWeaponObject;

    /// <summary>현재 장착 무기 프리팹의 1인칭 정조준 기준 앵커 트랜스폼.</summary>
    private Transform _currentAdsCameraAnchor;

    /// <summary>현재 장착 무기 프리팹의 총구 기준 트랜스폼.</summary>
    private Transform _currentMuzzlePoint;

    /// <summary>무기 교체 애니메이션 진입 직후 <c>isWeaponSwap</c>을 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingWeaponSwapReset;

    /// <summary>재장전 애니메이션 진입 직후 <c>isReload</c>를 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingReloadReset;

    /// <summary>사망 HideMode 포즈와 애니메이터 비활성화가 적용된 상태인지 여부.</summary>
    private bool _isDeadVisualApplied;

    /// <summary>손 오버라이드 컨트롤러 갱신에 재사용하는 클립 매핑 버퍼.</summary>
    private readonly List<KeyValuePair<AnimationClip, AnimationClip>> _handOverrideBuffer = new(16);

    /// <summary>단검 드로우 애니메이션 상태(<c>Knife_Draw</c>) 해시.</summary>
    private static readonly int KnifeDrawState = Animator.StringToHash("Knife_Draw");

    /// <summary>권총 드로우 애니메이션 상태(<c>M1911_Draw</c>) 해시.</summary>
    private static readonly int M1911DrawState = Animator.StringToHash("M1911_Draw");

    /// <summary>소총 드로우 애니메이션 상태(<c>AK_Draw</c>) 해시.</summary>
    private static readonly int AkDrawState = Animator.StringToHash("AK_Draw");

    /// <summary>단검 1타 공격 애니메이션 상태(<c>Knife_Swing</c>) 해시.</summary>
    private static readonly int KnifeSwingState = Animator.StringToHash("Knife_Swing");

    /// <summary>단검 2타 공격 애니메이션 상태(<c>Knife_Swing2</c>) 해시.</summary>
    private static readonly int KnifeSwing2State = Animator.StringToHash("Knife_Swing2");

    /// <summary>권총 발사 애니메이션 상태(<c>M1911_Shot</c>) 해시.</summary>
    private static readonly int M1911ShotState = Animator.StringToHash("M1911_Shot");

    /// <summary>소총 발사 애니메이션 상태(<c>AK_Shot</c>) 해시.</summary>
    private static readonly int AkShotState = Animator.StringToHash("AK_Shot");

    /// <summary>권총 재장전 애니메이션 상태(<c>M1911_Reload</c>) 해시.</summary>
    private static readonly int M1911ReloadState = Animator.StringToHash("M1911_Reload");

    /// <summary>소총 재장전 애니메이션 상태(<c>AK_Reload</c>) 해시.</summary>
    private static readonly int AkReloadState = Animator.StringToHash("AK_Reload");

    /// <summary>총기 기본 대기 애니메이션 상태(<c>Idle</c>) 해시.</summary>
    private static readonly int WeaponIdleState = Animator.StringToHash("Idle");

    /// <summary>총기 발사 애니메이션 상태(<c>Shot</c>) 해시.</summary>
    private static readonly int WeaponShotState = Animator.StringToHash("Shot");

    /// <summary>총기 재장전 애니메이션 상태(<c>Reload</c>) 해시.</summary>
    private static readonly int WeaponReloadState = Animator.StringToHash("Reload");

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public override EWeaponType CurrentWeaponType =>
        _weaponInventory != null ? (EWeaponType)_weaponInventory.ActiveSlot : EWeaponType.Hide;

    /// <inheritdoc/>
    public override bool IsSwappingWeapon
    {
        get
        {
            if (_pendingWeaponSwapReset) return true;
            if (handAnimator == null) return false;

            int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
            return currentHash == KnifeDrawState || currentHash == M1911DrawState || currentHash == AkDrawState
                || nextHash == KnifeDrawState || nextHash == M1911DrawState || nextHash == AkDrawState;
        }
    }

    /// <summary>손 애니메이터가 현재 장착 무기의 재장전 상태를 재생 중이거나 진입 전이 중인지 여부.</summary>
    public bool IsReloadAnimationActive
    {
        get
        {
            if (handAnimator == null || _weaponInventory == null || _appliedWeaponData == null || !_appliedWeaponData.UsesAmmo)
            {
                return false;
            }

            int expectedHash = _weaponInventory.ActiveSlot switch
            {
                EWeaponSlotType.Secondary => M1911ReloadState,
                EWeaponSlotType.Primary => AkReloadState,
                _ => 0
            };
            if (expectedHash == 0) return false;

            if (handAnimator.IsInTransition(0))
            {
                return handAnimator.GetNextAnimatorStateInfo(0).shortNameHash == expectedHash;
            }

            return handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == expectedHash;
        }
    }

    /// <summary>손 애니메이터가 현재 장착 무기의 드로우 상태를 재생 중인지 여부.</summary>
    public bool IsDrawAnimationActive
    {
        get
        {
            if (handAnimator == null || _weaponInventory == null || _appliedWeaponData == null || !_appliedWeaponData.UsesAmmo)
            {
                return false;
            }

            int expectedHash = _weaponInventory.ActiveSlot switch
            {
                EWeaponSlotType.Secondary => M1911DrawState,
                EWeaponSlotType.Primary => AkDrawState,
                _ => 0
            };
            if (expectedHash == 0) return false;

            return !handAnimator.IsInTransition(0)
                && handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == expectedHash;
        }
    }

    /// <summary>현재 장착된 무기 인스턴스 오브젝트.</summary>
    public GameObject CurrentWeaponObject => _currentWeaponObject;

    /// <summary>현재 장착된 무기의 1인칭 정조준 기준 앵커(<c>AdsCameraAnchor</c>) 트랜스폼.</summary>
    public Transform CurrentAdsCameraAnchor => _currentAdsCameraAnchor;

    /// <summary>현재 장착된 무기의 총구(<c>MuzzlePoint</c>) 트랜스폼.</summary>
    public Transform CurrentMuzzlePoint => _currentMuzzlePoint;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected override void Awake()
    {
        base.Awake();
        _weaponInventory = GetComponent<PlayerWeaponInventory>();
        _playerHealth = GetComponent<PlayerHealth>();
    }

    protected override void Update()
    {
        base.Update();

        if (!IsSpawned || bodyAnimator == null || _propEntity == null || handAnimator == null) return;
        if (_isDeadVisualApplied || _propEntity.IsDead || !bodyAnimator.enabled || !handAnimator.enabled) return;

        int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

        // 드로우 애니메이션 진입이 확인되면 플래그를 내려 반복 전이를 방지한다.
        if (_pendingWeaponSwapReset
            && (currentHash == KnifeDrawState || currentHash == M1911DrawState || currentHash == AkDrawState
                || nextHash == KnifeDrawState || nextHash == M1911DrawState || nextHash == AkDrawState))
        {
            handAnimator.SetBool(IsWeaponSwap, false);
            if (IsOwner)
            {
                bodyAnimator.SetBool(IsWeaponSwap, false);
            }
            _pendingWeaponSwapReset = false;
        }

        // 재장전 애니메이션 진입이 확인되면 플래그를 내려 반복 전이를 방지한다.
        if (_pendingReloadReset
            && (currentHash == M1911ReloadState || currentHash == AkReloadState
                || nextHash == M1911ReloadState || nextHash == AkReloadState))
        {
            handAnimator.SetBool(IsReload, false);
            if (IsOwner)
            {
                bodyAnimator.SetBool(IsReload, false);
            }
            _pendingReloadReset = false;
        }

        // 손 애니메이터의 발사, 재장전 및 드로우 프레임에 맞춰 총기 애니메이터를 동기화한다.
        if (_weaponAnimator == null || _weaponAnimator.runtimeAnimatorController == null) return;

        bool useNextState = nextHash == M1911ShotState
            || nextHash == AkShotState
            || nextHash == M1911ReloadState
            || nextHash == AkReloadState
            || nextHash == M1911DrawState
            || nextHash == AkDrawState;
        AnimatorStateInfo activeHandState = useNextState
            ? handAnimator.GetNextAnimatorStateInfo(0)
            : handAnimator.GetCurrentAnimatorStateInfo(0);
        int activeHash = activeHandState.shortNameHash;

        if (activeHash == M1911ShotState || activeHash == AkShotState)
        {
            bool isExitingShot = !useNextState && handAnimator.IsInTransition(0);
            float normalizedTime = isExitingShot ? 1f : Mathf.Clamp01(activeHandState.normalizedTime);
            float handShotFrames = _appliedWeaponData != null && _appliedWeaponData.HandShotFrames > 0f
                ? _appliedWeaponData.HandShotFrames
                : DefaultHandShotFrames;
            float weaponShotFrames = _appliedWeaponData != null && _appliedWeaponData.WeaponShotFrames > 0f
                ? _appliedWeaponData.WeaponShotFrames
                : DefaultWeaponShotFrames;
            float handFrame = normalizedTime * handShotFrames;
            if (handFrame < weaponShotFrames)
            {
                _weaponAnimator.SetBool(IsAttack, true);
                _weaponAnimator.SetBool(IsReload, false);
                _weaponAnimator.Play(WeaponShotState, 0, handFrame / weaponShotFrames);
            }
            else
            {
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }
            return;
        }

        if (_appliedWeaponData != null
            && (activeHash == M1911ReloadState || activeHash == AkReloadState)
            && _appliedWeaponData.HandReloadTotalFrames > 0f
            && _appliedWeaponData.WeaponReloadTotalFrames > 0f)
        {
            float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * _appliedWeaponData.HandReloadTotalFrames;
            float weaponFrame = handFrame - _appliedWeaponData.WeaponReloadStartFrame;
            if (weaponFrame >= 0f && weaponFrame < _appliedWeaponData.WeaponReloadTotalFrames)
            {
                _weaponAnimator.SetBool(IsReload, true);
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / _appliedWeaponData.WeaponReloadTotalFrames);
            }
            else
            {
                _weaponAnimator.SetBool(IsReload, false);
                _weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }
            return;
        }

        if (_appliedWeaponData != null
            && (activeHash == M1911DrawState || activeHash == AkDrawState)
            && _appliedWeaponData.HandDrawTotalFrames > 0f
            && _appliedWeaponData.WeaponDrawTotalFrames > 0f)
        {
            float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * _appliedWeaponData.HandDrawTotalFrames;
            float weaponFrame = handFrame - _appliedWeaponData.WeaponDrawStartFrame;
            if (weaponFrame >= 0f && weaponFrame < _appliedWeaponData.WeaponDrawTotalFrames)
            {
                _weaponAnimator.SetBool(IsReload, true);
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / _appliedWeaponData.WeaponDrawTotalFrames);
            }
            else
            {
                _weaponAnimator.SetBool(IsReload, false);
                _weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }
            return;
        }

        _weaponAnimator.SetBool(IsAttack, false);
        _weaponAnimator.SetBool(IsReload, false);
        if (_weaponAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash != WeaponIdleState)
        {
            _weaponAnimator.Play(WeaponIdleState, 0, 0f);
        }
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnDamaged += HandleServerDamaged;
        }
    }

    public override void OnStopServer()
    {
        if (_playerHealth != null)
        {
            _playerHealth.OnDamaged -= HandleServerDamaged;
        }
    }

    public override void OnStartNetwork()
    {
        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged += HandleEquippedWeaponChanged;
            HandleEquippedWeaponChanged(_weaponInventory.EquippedState);
        }

        if (_playerHealth != null && _playerHealth.IsDead)
        {
            EnterDeathHidePose();
        }
    }

    public override void OnStopNetwork()
    {
        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged -= HandleEquippedWeaponChanged;
        }

        _hasAppliedEquippedState = false;
        _appliedEquippedState = default;
        _appliedWeaponData = null;
        _isDeadVisualApplied = false;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <inheritdoc/>
    public override void SetAttackState(bool attacking)
    {
        if (!IsOwner || bodyAnimator == null || !bodyAnimator.enabled) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        bool canAttack = attacking && CurrentWeaponType != EWeaponType.Hide;
        bodyAnimator.SetBool(IsAttack, canAttack);

        if (handAnimator != null && handAnimator.enabled)
        {
            handAnimator.SetBool(IsAttack, canAttack);
        }

        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsAttack, canAttack);
        }
    }

    /// <inheritdoc/>
    public override void TriggerAttack(int attackVariant = 0)
    {
        if (handAnimator == null || !handAnimator.enabled || _weaponInventory == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        int shotState = activeSlot switch
        {
            EWeaponSlotType.Melee => attackVariant == 1 ? KnifeSwing2State : KnifeSwingState,
            EWeaponSlotType.Secondary => M1911ShotState,
            EWeaponSlotType.Primary => AkShotState,
            _ => 0
        };
        if (shotState == 0) return;

        handAnimator.SetBool(IsAttack, false);
        if (activeSlot == EWeaponSlotType.Melee)
        {
            handAnimator.CrossFadeInFixedTime(shotState, 0.05f, 0, 0f);
        }
        else
        {
            handAnimator.Play(shotState, 0, 0f);
        }

        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsAttack, true);
            _weaponAnimator.SetBool(IsReload, false);
            _weaponAnimator.Play(WeaponShotState, 0, 0f);
        }
    }

    /// <inheritdoc/>
    public override void SetWeaponMode(EWeaponType weaponType)
    {
        if (!IsOwner || _weaponInventory == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;

        _weaponInventory.TrySelectSlot((EWeaponSlotType)weaponType);
    }

    /// <inheritdoc/>
    public override void RequestReload()
    {
        if (!IsOwner || handAnimator == null || !handAnimator.enabled || _weaponInventory == null) return;
        if (_playerHealth != null && _playerHealth.IsDead) return;
        if (_appliedWeaponData == null || !_appliedWeaponData.UsesAmmo) return;

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        if (activeSlot != EWeaponSlotType.Secondary && activeSlot != EWeaponSlotType.Primary) return;
        if (_pendingReloadReset || _pendingWeaponSwapReset) return;

        int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;
        if (currentHash == M1911ReloadState || currentHash == AkReloadState
            || currentHash == M1911DrawState || currentHash == AkDrawState
            || nextHash == M1911ReloadState || nextHash == AkReloadState
            || nextHash == M1911DrawState || nextHash == AkDrawState)
        {
            return;
        }

        bodyAnimator.SetBool(IsReload, true);
        handAnimator.SetBool(IsReload, true);
        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsReload, true);
        }
        _pendingReloadReset = true;

        if (IsServerInitialized)
        {
            BroadcastReloadObserversRpc();
        }
        else
        {
            RequestReloadServerRpc();
        }
    }

    /// <inheritdoc/>
    public override void EnterDeathHidePose()
    {
        if (_isDeadVisualApplied) return;

        _isDeadVisualApplied = true;
        _pendingWeaponSwapReset = false;
        _pendingReloadReset = false;
        _currentMoveX = 0f;
        _currentMoveZ = 0f;
        _currentHandMoveX = 0f;

        ClearEquippedWeaponObjects();

        // 다리와 손이 완전히 숨겨진 HideMode 포즈를 1회 평가한 뒤 Animator를 꺼 물리가 우선되도록 고정한다.
        if (bodyAnimator != null)
        {
            bodyAnimator.enabled = true;
            if (_bodyWeaponControllers != null && _bodyWeaponControllers.Length > 0 && _bodyWeaponControllers[0] != null)
            {
                bodyAnimator.runtimeAnimatorController = _bodyWeaponControllers[0];
            }

            bodyAnimator.SetFloat(MoveX, 0f);
            bodyAnimator.SetFloat(MoveZ, 0f);
            bodyAnimator.SetInteger(WeaponMode, (int)EWeaponType.Hide);
            bodyAnimator.SetBool(IsAir, false);
            bodyAnimator.SetBool(IsJumped, false);
            bodyAnimator.SetBool(IsAttack, false);
            bodyAnimator.SetBool(IsReload, false);
            bodyAnimator.SetBool(IsWeaponSwap, false);
            bodyAnimator.ResetTrigger(Hit);
            bodyAnimator.Play(UnArmedBlendState, 0, 0f);
            if (bodyAnimator.layerCount > 1)
            {
                bodyAnimator.Play(HitEmptyState, 1, 0f);
            }
            bodyAnimator.Update(0f);
            bodyAnimator.enabled = false;
        }

        if (handAnimator != null)
        {
            handAnimator.enabled = true;
            handAnimator.SetFloat(MoveX, 0f);
            handAnimator.SetInteger(WeaponMode, (int)EWeaponType.Hide);
            handAnimator.SetBool(IsAir, false);
            handAnimator.SetBool(IsJumped, false);
            handAnimator.SetBool(IsAttack, false);
            handAnimator.SetBool(IsReload, false);
            handAnimator.SetBool(IsWeaponSwap, false);
            handAnimator.Play(HandIdleState, 0, 0f);
            handAnimator.Update(0f);
            handAnimator.enabled = false;
        }
    }

    /// <inheritdoc/>
    public override void ExitDeathHidePose()
    {
        if (!_isDeadVisualApplied) return;

        _isDeadVisualApplied = false;

        if (bodyAnimator != null)
        {
            bodyAnimator.enabled = true;
        }

        if (handAnimator != null)
        {
            handAnimator.enabled = true;
        }

        if (_weaponInventory != null)
        {
            _hasAppliedEquippedState = false;
            HandleEquippedWeaponChanged(_weaponInventory.EquippedState);
        }
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>소유자 클라이언트의 재장전 요청을 서버를 통해 다른 관전자에게 전달한다.</summary>
    [ServerRpc]
    private void RequestReloadServerRpc()
    {
        if (_playerHealth != null && _playerHealth.IsDead) return;
        BroadcastReloadObserversRpc();
    }

    /// <summary>원격 프록시 클라이언트의 손 재장전 애니메이션을 트리거한다.</summary>
    [ObserversRpc(ExcludeOwner = true)]
    private void BroadcastReloadObserversRpc()
    {
        if (_isDeadVisualApplied || (_playerHealth != null && _playerHealth.IsDead)) return;
        if (handAnimator == null || !handAnimator.enabled) return;

        handAnimator.SetBool(IsReload, true);
        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsReload, true);
        }
        _pendingReloadReset = true;
    }

    /// <summary>서버가 결정한 <paramref name="hitIndex"/>로 모든 클라이언트의 피격 애니메이션을 재생한다.</summary>
    [ObserversRpc]
    private void PlayHitObserversRpc(int hitIndex)
    {
        if (_isDeadVisualApplied || (_playerHealth != null && _playerHealth.IsDead)) return;
        TriggerHit(hitIndex);
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>서버 피격 이벤트 수신 시 숨김/사망 상태를 제외하고 모든 클라이언트에 랜덤 피격 모션을 전파한다.</summary>
    private void HandleServerDamaged(float damage, NetworkConnection attacker)
    {
        bool isHidden = _propEntity != null && _propEntity.IsRagdollIdle;
        bool isDead = _playerHealth != null && _playerHealth.IsDead;
        if (isHidden || isDead) return;

        int hitIndex = UnityEngine.Random.Range(0, HitClipCount);
        PlayHitObserversRpc(hitIndex);
    }

    /// <summary>장착 무기 변경 시 무기 오브젝트, 손 오버라이드 클립 및 몸통/손 애니메이터를 갱신한다.</summary>
    private void HandleEquippedWeaponChanged(EquippedWeaponState newState)
    {
        if (_hasAppliedEquippedState && _appliedEquippedState == newState) return;

        bool hadPrevious = _hasAppliedEquippedState;
        EWeaponSlotType previousSlot = _appliedEquippedState.Slot;
        _hasAppliedEquippedState = true;
        _appliedEquippedState = newState;

        int slotIndex = (int)newState.Slot;
        WeaponDataSO weaponData = newState.Slot != EWeaponSlotType.Unarmed && _weaponInventory != null
            ? _weaponInventory.GetWeaponDataById(newState.WeaponId)
            : null;
        _appliedWeaponData = weaponData;

        if (_isDeadVisualApplied || (_playerHealth != null && _playerHealth.IsDead))
        {
            ClearEquippedWeaponObjects();
            return;
        }

        bool wasAir = bodyAnimator != null && bodyAnimator.GetBool(IsAir);
        bool wasJumped = bodyAnimator != null && bodyAnimator.GetBool(IsJumped);

        if (bodyAnimator != null)
        {
            int bodyControllerIndex = slotIndex;
            if (newState.Slot != EWeaponSlotType.Unarmed && weaponData != null)
            {
                bodyControllerIndex = weaponData.UsesAimLocomotion
                    ? (int)EWeaponSlotType.Primary
                    : (int)EWeaponSlotType.Melee;
            }

            if (_bodyWeaponControllers != null
                && bodyControllerIndex >= 0
                && bodyControllerIndex < _bodyWeaponControllers.Length
                && _bodyWeaponControllers[bodyControllerIndex] != null)
            {
                bodyAnimator.runtimeAnimatorController = _bodyWeaponControllers[bodyControllerIndex];
            }

            bodyAnimator.SetFloat(MoveX, _currentMoveX);
            bodyAnimator.SetFloat(MoveZ, _currentMoveZ);
            bodyAnimator.SetBool(IsAir, wasAir);
            bodyAnimator.SetBool(IsJumped, wasJumped);
            bodyAnimator.SetInteger(WeaponMode, slotIndex);
            bodyAnimator.SetBool(IsReload, false);
        }

        // 같은 슬롯의 다른 무기(예: AK ↔ M4)를 장착할 때 손 오버라이드 클립을 동적으로 교체한다.
        if (handAnimator != null && _handOverrideController != null && weaponData != null && propData != null)
        {
            WeaponSlotBaseHandClips[] slotBases = propData.SlotBaseHandClips;
            if (slotBases != null && slotIndex >= 0 && slotIndex < slotBases.Length)
            {
                WeaponSlotBaseHandClips baseClips = slotBases[slotIndex];
                _handOverrideBuffer.Clear();
                _handOverrideController.GetOverrides(_handOverrideBuffer);

                bool modified = false;
                for (int i = 0; i < _handOverrideBuffer.Count; i++)
                {
                    AnimationClip baseClip = _handOverrideBuffer[i].Key;
                    AnimationClip targetClip = null;

                    if (baseClip == baseClips.drawClip && weaponData.HandDrawClip != null)
                    {
                        targetClip = weaponData.HandDrawClip;
                    }
                    else if (baseClip == baseClips.idleClip && weaponData.HandIdleClip != null)
                    {
                        targetClip = weaponData.HandIdleClip;
                    }
                    else if (baseClip == baseClips.attackClip && weaponData.HandAttackClip != null)
                    {
                        targetClip = weaponData.HandAttackClip;
                    }
                    else if (baseClip == baseClips.secondaryAttackClip && weaponData.HandSecondaryAttackClip != null)
                    {
                        targetClip = weaponData.HandSecondaryAttackClip;
                    }
                    else if (baseClip == baseClips.reloadClip && weaponData.HandReloadClip != null)
                    {
                        targetClip = weaponData.HandReloadClip;
                    }

                    if (targetClip != null && _handOverrideBuffer[i].Value != targetClip)
                    {
                        _handOverrideBuffer[i] = new KeyValuePair<AnimationClip, AnimationClip>(baseClip, targetClip);
                        modified = true;
                    }
                }

                if (modified)
                {
                    _handOverrideController.ApplyOverrides(_handOverrideBuffer);
                }
            }
        }

        if (handScript != null)
        {
            ClearEquippedWeaponObjects();

            if (weaponData != null && weaponData.EquippedPrefab != null)
            {
                GameObject weaponObject = Instantiate(weaponData.EquippedPrefab);
                Collider weaponCollider = weaponObject.GetComponent<Collider>();
                if (weaponCollider != null)
                {
                    weaponCollider.enabled = false;
                }

                handScript.SetRightHandItem(weaponObject);
                weaponObject.transform.SetLocalPositionAndRotation(
                    weaponData.GripPosition,
                    Quaternion.Euler(weaponData.GripRotation));
                weaponObject.transform.localScale = weaponData.GripScale;

                _currentWeaponObject = weaponObject;
                _currentAdsCameraAnchor = weaponObject.transform.Find("AdsCameraAnchor");
                _currentMuzzlePoint = weaponObject.transform.Find("MuzzlePoint");

                GunScript gun = weaponObject.GetComponent<GunScript>();
                if (gun != null && weaponData.HandReloadClip != null)
                {
                    AnimationEvent equipEvent = Array.Find(
                        weaponData.HandReloadClip.events,
                        evt => evt.functionName == nameof(HandScript.EquipMagazineToGun));
                    if (equipEvent != null)
                    {
                        weaponData.HandReloadClip.SampleAnimation(handScript.gameObject, equipEvent.time);
                    }

                    GameObject magazine = Instantiate(gun.GetMagazine(), handScript.GetLeftHand(), true);
                    magazine.SetActive(false);
                    handScript.SetLeftHandItem(magazine);

                    if (handAnimator != null)
                    {
                        handAnimator.Update(0f);
                    }
                }

                _weaponAnimator = weaponObject.GetComponent<Animator>();
                if (_weaponAnimator != null)
                {
                    if (weaponData.WeaponAnimController != null
                        && _weaponAnimator.runtimeAnimatorController != weaponData.WeaponAnimController)
                    {
                        _weaponAnimator.runtimeAnimatorController = weaponData.WeaponAnimController;
                    }

                    if (_weaponAnimator.runtimeAnimatorController != null)
                    {
                        _weaponAnimator.Rebind();
                        _weaponAnimator.SetBool(IsAttack, false);
                        _weaponAnimator.SetBool(IsReload, false);
                        _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                        _weaponAnimator.Update(0f);
                    }
                }
            }
        }

        if (handAnimator == null) return;

        handAnimator.SetFloat(MoveX, _currentHandMoveX);
        handAnimator.SetBool(IsAir, wasAir);
        handAnimator.SetBool(IsJumped, wasJumped);
        handAnimator.SetInteger(WeaponMode, slotIndex);
        handAnimator.SetBool(IsReload, false);
        _pendingReloadReset = false;

        if (newState.Slot == EWeaponSlotType.Unarmed)
        {
            if (IsOwner && bodyAnimator != null)
            {
                bodyAnimator.SetBool(IsAttack, false);
                bodyAnimator.SetBool(IsWeaponSwap, false);
            }
            handAnimator.SetBool(IsAttack, false);
            handAnimator.SetBool(IsWeaponSwap, false);
            _pendingWeaponSwapReset = false;
        }
        else
        {
            // 무기 교체 및 줍기 직후 손 드로우 애니메이션 상태로 전이하도록 트리거 플래그를 설정한다.
            if (IsOwner && bodyAnimator != null)
            {
                bodyAnimator.SetBool(IsWeaponSwap, true);
            }
            handAnimator.SetBool(IsWeaponSwap, true);
            _pendingWeaponSwapReset = true;

            if (hadPrevious && previousSlot == newState.Slot)
            {
                int drawStateHash = newState.Slot switch
                {
                    EWeaponSlotType.Melee => KnifeDrawState,
                    EWeaponSlotType.Secondary => M1911DrawState,
                    EWeaponSlotType.Primary => AkDrawState,
                    _ => 0
                };
                if (drawStateHash != 0)
                {
                    handAnimator.Play(drawStateHash, 0, 0f);
                }
            }
        }
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>양손에 장착된 무기 및 탄창 오브젝트와 무기 애니메이터 참조를 정리한다.</summary>
    private void ClearEquippedWeaponObjects()
    {
        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsAttack, false);
            _weaponAnimator.SetBool(IsReload, false);
        }
        _weaponAnimator = null;
        _currentWeaponObject = null;
        _currentAdsCameraAnchor = null;
        _currentMuzzlePoint = null;

        if (handScript == null) return;

        GameObject prevLeft = handScript.GetLeftHandItem();
        if (prevLeft != null)
        {
            prevLeft.SetActive(false);
            Destroy(prevLeft);
        }

        GameObject prevRight = handScript.GetRightHandItem();
        if (prevRight != null)
        {
            prevRight.SetActive(false);
            Destroy(prevRight);
        }
    }
}
