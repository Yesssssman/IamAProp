using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>소유자 및 원격 프록시의 무기 장착, 재장전, 총기 프레임 동기화를 담당하는 플레이어 애니메이터.</summary>
[RequireComponent(typeof(PlayerController))]
public class PlayerAnim : PropAnimBase
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>손 애니메이션 상태에 대응하는 총기 재장전/드로우 프레임 매핑.</summary>
    private readonly struct WeaponReloadSyncRule
    {
        public readonly int handStateHash;
        public readonly float totalHandFrames;
        public readonly float weaponStartFrame;
        public readonly float weaponTotalFrames;

        public WeaponReloadSyncRule(int handStateHash, float totalHandFrames, float weaponStartFrame, float weaponTotalFrames)
        {
            this.handStateHash = handStateHash;
            this.totalHandFrames = totalHandFrames;
            this.weaponStartFrame = weaponStartFrame;
            this.weaponTotalFrames = weaponTotalFrames;
        }
    }

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>현재 장착 중인 무기 종류.</summary>
    private readonly SyncVar<EWeaponType> _weaponType = new(EWeaponType.Hide);

    /// <summary>이 오브젝트에 마지막으로 적용된 무기 종류 (초기값 -1로 첫 동기화 보장).</summary>
    private EWeaponType _appliedWeaponType = (EWeaponType)(-1);

    /// <summary>현재 오른손에 장착된 무기의 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _weaponAnimator;

    /// <summary>무기 교체 애니메이션 진입 직후 <c>isWeaponSwap</c>을 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingWeaponSwapReset;

    /// <summary>재장전 애니메이션 진입 직후 <c>isReload</c>를 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingReloadReset;

    private const float HandShotFrames = 17f; // 손 발사 클립 총 프레임 수
    private const float WeaponShotFrames = 9f; // 총기 발사 클립 총 프레임 수

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

    /// <summary>손 재장전/드로우 상태별 총기 슬라이드/노리쇠 후퇴 프레임 동기화 테이블.</summary>
    private static readonly WeaponReloadSyncRule[] ReloadSyncRules =
    {
        new(M1911ReloadState, 107f, 78f, 9f),
        new(AkReloadState, 179f, 130f, 16f),
        new(M1911DrawState, 47f, 19f, 9f),
        new(AkDrawState, 69f, 15f, 16f)
    };

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public override EWeaponType CurrentWeaponType => _weaponType.Value;

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

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected override void Update()
    {
        base.Update();

        if (!IsSpawned || bodyAnimator == null || _propEntity == null || handAnimator == null) return;

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
            float handFrame = Mathf.Repeat(activeHandState.normalizedTime, 1f) * HandShotFrames;
            if (handFrame < WeaponShotFrames)
            {
                _weaponAnimator.SetBool(IsAttack, true);
                _weaponAnimator.SetBool(IsReload, false);
                _weaponAnimator.Play(WeaponShotState, 0, handFrame / WeaponShotFrames);
            }
            else
            {
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.Play(WeaponIdleState, 0, 0f);
            }
            return;
        }

        for (int i = 0; i < ReloadSyncRules.Length; i++)
        {
            WeaponReloadSyncRule rule = ReloadSyncRules[i];
            if (activeHash != rule.handStateHash) continue;

            float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * rule.totalHandFrames;
            float weaponFrame = handFrame - rule.weaponStartFrame;
            if (weaponFrame >= 0f && weaponFrame < rule.weaponTotalFrames)
            {
                _weaponAnimator.SetBool(IsReload, true);
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / rule.weaponTotalFrames);
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

    public override void OnStartNetwork()
    {
        _weaponType.OnChange += OnWeaponTypeChanged;
        OnWeaponTypeChanged(EWeaponType.Hide, _weaponType.Value, asServer: true);
    }

    public override void OnStopNetwork()
    {
        _weaponType.OnChange -= OnWeaponTypeChanged;
        _appliedWeaponType = (EWeaponType)(-1);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <inheritdoc/>
    public override void SetAttackState(bool attacking)
    {
        if (!IsOwner || bodyAnimator == null) return;

        bool canAttack = attacking && _weaponType.Value != EWeaponType.Hide;
        bodyAnimator.SetBool(IsAttack, canAttack);

        if (handAnimator != null)
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
        if (handAnimator == null) return;

        EWeaponType currentWeapon = _weaponType.Value;
        int shotState = currentWeapon switch
        {
            EWeaponType.KnifeMode => attackVariant == 1 ? KnifeSwing2State : KnifeSwingState,
            EWeaponType.Pistol => M1911ShotState,
            EWeaponType.Ak => AkShotState,
            _ => 0
        };
        if (shotState == 0) return;

        handAnimator.SetBool(IsAttack, false);
        if (currentWeapon == EWeaponType.KnifeMode)
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
        if (!IsOwner) return;

        if (IsServerInitialized)
        {
            _weaponType.Value = weaponType;
        }
        else
        {
            SetWeaponModeServerRpc(weaponType);
        }
    }

    /// <inheritdoc/>
    public override void RequestReload()
    {
        if (!IsOwner || handAnimator == null) return;
        if (_weaponType.Value != EWeaponType.Pistol && _weaponType.Value != EWeaponType.Ak) return;
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

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>소유자 클라이언트의 무기 모드 변경을 서버에 반영한다.</summary>
    [ServerRpc]
    private void SetWeaponModeServerRpc(EWeaponType weaponType)
    {
        _weaponType.Value = weaponType;
    }

    /// <summary>소유자 클라이언트의 재장전 요청을 서버를 통해 다른 관전자에게 전달한다.</summary>
    [ServerRpc]
    private void RequestReloadServerRpc()
    {
        BroadcastReloadObserversRpc();
    }

    /// <summary>원격 프록시 클라이언트의 손 재장전 애니메이션을 트리거한다.</summary>
    [ObserversRpc(ExcludeOwner = true)]
    private void BroadcastReloadObserversRpc()
    {
        if (handAnimator == null) return;

        handAnimator.SetBool(IsReload, true);
        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsReload, true);
        }
        _pendingReloadReset = true;
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="_weaponType"/> 변경 시 무기 오브젝트와 몸통/손 애니메이터를 갱신한다.</summary>
    private void OnWeaponTypeChanged(EWeaponType previousValue, EWeaponType newValue, bool asServer)
    {
        if (!asServer && IsServerInitialized) return;
        if (_appliedWeaponType == newValue) return;
        _appliedWeaponType = newValue;
        int index = (int)newValue;

        if (bodyAnimator != null)
        {
            bool wasAir = bodyAnimator.GetBool(IsAir);
            bool wasJumped = bodyAnimator.GetBool(IsJumped);

            if (_bodyWeaponControllers != null
                && index >= 0
                && index < _bodyWeaponControllers.Length
                && _bodyWeaponControllers[index] != null)
            {
                bodyAnimator.runtimeAnimatorController = _bodyWeaponControllers[index];
            }

            bodyAnimator.SetFloat(MoveX, _currentMoveX);
            bodyAnimator.SetFloat(MoveZ, _currentMoveZ);
            bodyAnimator.SetBool(IsAir, wasAir);
            bodyAnimator.SetBool(IsJumped, wasJumped);
            bodyAnimator.SetInteger(WeaponMode, index);
            bodyAnimator.SetBool(IsReload, false);
        }

        if (handScript != null)
        {
            if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
            {
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.SetBool(IsReload, false);
            }
            _weaponAnimator = null;

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

            WeaponSocketConfig[] sockets = propData != null ? propData.WeaponSockets : null;
            if (sockets != null
                && index >= 0
                && index < sockets.Length
                && sockets[index].weaponPrefab != null)
            {
                WeaponSocketConfig socket = sockets[index];
                GameObject weaponObject = Instantiate(socket.weaponPrefab);
                Collider weaponCollider = weaponObject.GetComponent<Collider>();
                if (weaponCollider != null)
                {
                    weaponCollider.enabled = false;
                }

                handScript.SetRightHandItem(weaponObject);
                weaponObject.transform.SetLocalPositionAndRotation(
                    socket.gripPosition,
                    Quaternion.Euler(socket.gripRotation));
                weaponObject.transform.localScale = socket.gripScale;

                GunScript gun = weaponObject.GetComponent<GunScript>();
                if (gun != null && socket.reloadClip != null)
                {
                    AnimationEvent equipEvent = Array.Find(
                        socket.reloadClip.events,
                        evt => evt.functionName == nameof(HandScript.EquipMagazineToGun));
                    if (equipEvent != null)
                    {
                        socket.reloadClip.SampleAnimation(handScript.gameObject, equipEvent.time);
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
                    if (socket.weaponAnimController != null
                        && _weaponAnimator.runtimeAnimatorController != socket.weaponAnimController)
                    {
                        _weaponAnimator.runtimeAnimatorController = socket.weaponAnimController;
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

        handAnimator.SetInteger(WeaponMode, index);
        handAnimator.SetBool(IsReload, false);
        _pendingReloadReset = false;

        if (newValue == EWeaponType.Hide)
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
        else if (previousValue != newValue)
        {
            // 무기 교체 시 손 드로우 애니메이션 상태로 전이하도록 트리거 플래그를 설정한다.
            if (IsOwner && bodyAnimator != null)
            {
                bodyAnimator.SetBool(IsWeaponSwap, true);
            }
            handAnimator.SetBool(IsWeaponSwap, true);
            _pendingWeaponSwapReset = true;
        }
    }
}
