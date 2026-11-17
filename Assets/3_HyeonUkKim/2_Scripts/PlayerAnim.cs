using System;
using Unity.Netcode;
using UnityEngine;

/// <summary>플레이어 무기 장착 상태.</summary>
public enum EWeaponType
{
    Hide = 0,
    KnifeMode = 1,
    Pistol = 2,
    Ak = 3
}

/// <summary>무기 종류별 장착 프리팹, 그립 오프셋 및 재장전 클립 설정.</summary>
[Serializable]
public struct WeaponSocketConfig
{
    /// <summary>오른손 소켓에 장착할 무기 프리팹.</summary>
    public GameObject weaponPrefab;

    /// <summary>오른손 본 기준 무기 로컬 위치.</summary>
    public Vector3 gripPosition;

    /// <summary>오른손 본 기준 무기 로컬 오일러 회전각 (도 단위).</summary>
    public Vector3 gripRotation;

    /// <summary>오른손 본 기준 무기 로컬 스케일.</summary>
    public Vector3 gripScale;

    /// <summary>왼손 예비 탄창 위치를 샘플링할 손 재장전 애니메이션 클립.</summary>
    public AnimationClip reloadClip;
}

/// <summary>소유자 및 원격 프록시의 몸통/손 애니메이터와 무기 소켓을 동기화한다.</summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(PlayerController))]
public class PlayerAnim : NetworkBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary><c>MoveX</c> 파라미터 보간 감쇠 시간 (초 단위).</summary>
    [Header("애니메이션 보간")]
    [SerializeField] private float moveDampTime = 0.1f;

    /// <summary>손 오브젝트의 <see cref="Animator"/> 컴포넌트.</summary>
    [Header("손 참조 및 무기 소켓")]
    [SerializeField] private Animator handAnimator;

    /// <summary>무기 및 탄창 장착을 관리하는 <see cref="HandScript"/> 컴포넌트.</summary>
    [SerializeField] private HandScript handScript;

    /// <summary><see cref="EWeaponType"/> 순서에 대응하는 무기 소켓 설정 목록.</summary>
    [SerializeField] private WeaponSocketConfig[] weaponSockets;

    /// <summary><see cref="EWeaponType"/> 순서에 대응하는 몸통 애니메이터 컨트롤러 목록.</summary>
    [Header("애니메이터 오버라이드")]
    [SerializeField] private RuntimeAnimatorController[] weaponAnimControllers;

    /// <summary>현재 장착 중인 무기 종류.</summary>
    private readonly NetworkVariable<EWeaponType> _weaponType = new(
        EWeaponType.Hide,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// <summary>몸통 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _animator;

    /// <summary>이동 상태를 제공하는 <see cref="PlayerController"/> 컴포넌트.</summary>
    private PlayerController _playerController;

    /// <summary>프레임 단위로 감쇠 보간된 현재 <c>MoveX</c> 파라미터 값.</summary>
    private float _currentMoveX;

    /// <summary><see cref="Mathf.SmoothDamp(float, float, ref float, float)"/> 내부 변화율 상태값.</summary>
    private float _moveXVelocity;

    /// <summary>무기 교체 애니메이션 진입 직후 <c>isWeaponSwap</c>을 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingWeaponSwapReset;

    /// <summary>재장전 애니메이션 진입 직후 <c>isReload</c>를 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingReloadReset;

    /// <summary>애니메이터 <c>MoveX</c> 파라미터 해시.</summary>
    private static readonly int MoveX = Animator.StringToHash("MoveX");

    /// <summary>애니메이터 <c>isAttack</c> 파라미터 해시.</summary>
    private static readonly int IsAttack = Animator.StringToHash("isAttack");

    /// <summary>애니메이터 <c>isAir</c> 파라미터 해시.</summary>
    private static readonly int IsAir = Animator.StringToHash("isAir");

    /// <summary>애니메이터 <c>WeaponMode</c> 파라미터 해시.</summary>
    private static readonly int WeaponMode = Animator.StringToHash("WeaponMode");

    /// <summary>애니메이터 <c>isJumped</c> 파라미터 해시.</summary>
    private static readonly int IsJumped = Animator.StringToHash("isJumped");

    /// <summary>애니메이터 <c>isWeaponSwap</c> 파라미터 해시.</summary>
    private static readonly int IsWeaponSwap = Animator.StringToHash("isWeaponSwap");

    /// <summary>애니메이터 <c>isReload</c> 파라미터 해시.</summary>
    private static readonly int IsReload = Animator.StringToHash("isReload");

    /// <summary>단검 드로우 애니메이션 상태(<c>Knife_Draw</c>) 해시.</summary>
    private static readonly int KnifeDrawState = Animator.StringToHash("Knife_Draw");

    /// <summary>권총 드로우 애니메이션 상태(<c>M1911_Draw</c>) 해시.</summary>
    private static readonly int M1911DrawState = Animator.StringToHash("M1911_Draw");

    /// <summary>소총 드로우 애니메이션 상태(<c>AK_Draw</c>) 해시.</summary>
    private static readonly int AkDrawState = Animator.StringToHash("AK_Draw");

    /// <summary>권총 재장전 애니메이션 상태(<c>M1911_Reload</c>) 해시.</summary>
    private static readonly int M1911ReloadState = Animator.StringToHash("M1911_Reload");

    /// <summary>소총 재장전 애니메이션 상태(<c>AK_Reload</c>) 해시.</summary>
    private static readonly int AkReloadState = Animator.StringToHash("AK_Reload");

    /// <summary>몸통 기본 이동 블렌드 상태(<c>UnArmedBlend</c>) 해시.</summary>
    private static readonly int UnArmedBlendState = Animator.StringToHash("UnArmedBlend");

    /// <summary>맨손 달리기 애니메이션 상태(<c>Run</c>) 해시.</summary>
    private static readonly int UnarmedRunState = Animator.StringToHash("Run");

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 동기화된 무기 장착 상태.</summary>
    public EWeaponType CurrentWeaponType => _weaponType.Value;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _playerController = GetComponent<PlayerController>();

        if (handScript == null)
        {
            handScript = GetComponentInChildren<HandScript>(true);
        }

        if (handAnimator == null && handScript != null)
        {
            handAnimator = handScript.GetComponent<Animator>();
        }
    }

    private void Update()
    {
        if (!IsSpawned || _animator == null || _playerController == null) return;

        if (_weaponType.Value == EWeaponType.Hide)
        {
            _currentMoveX = _playerController.PlanarSpeedRatio >= 0.1f ? 1f : 0f;
            _moveXVelocity = 0f;
        }
        else
        {
            _currentMoveX = Mathf.SmoothDamp(
                _currentMoveX,
                _playerController.PlanarSpeedRatio,
                ref _moveXVelocity,
                moveDampTime);
        }

        _animator.SetFloat(MoveX, _currentMoveX);

        if (handAnimator == null) return;

        handAnimator.SetFloat(MoveX, _currentMoveX);

        if (!IsOwner)
        {
            handAnimator.SetBool(IsAir, _animator.GetBool(IsAir));
            handAnimator.SetBool(IsJumped, _animator.GetBool(IsJumped));
            handAnimator.SetBool(IsAttack, _animator.GetBool(IsAttack));
            handAnimator.SetInteger(WeaponMode, _animator.GetInteger(WeaponMode));
        }

        int currentHash = handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash;
        int nextHash = handAnimator.IsInTransition(0) ? handAnimator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

        // 맨손 달리기 중 몸통과 손 애니메이션의 재생 위상을 동기화한다.
        if (_weaponType.Value == EWeaponType.Hide
            && currentHash == UnarmedRunState
            && !handAnimator.IsInTransition(0)
            && !_animator.IsInTransition(0))
        {
            AnimatorStateInfo bodyInfo = _animator.GetCurrentAnimatorStateInfo(0);
            if (bodyInfo.shortNameHash == UnArmedBlendState)
            {
                float bodyPhase = Mathf.Repeat(bodyInfo.normalizedTime, 1f);
                float handPhase = Mathf.Repeat(handAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1f);
                float phaseDiff = Mathf.Abs(Mathf.DeltaAngle(bodyPhase * 360f, handPhase * 360f)) / 360f;
                if (phaseDiff > 0.05f)
                {
                    handAnimator.Play(UnarmedRunState, 0, bodyPhase);
                }
            }
        }

        // 드로우 애니메이션 진입이 확인되면 플래그를 내려 반복 전이를 방지한다.
        if (_pendingWeaponSwapReset
            && (currentHash == KnifeDrawState || currentHash == M1911DrawState || currentHash == AkDrawState
                || nextHash == KnifeDrawState || nextHash == M1911DrawState || nextHash == AkDrawState))
        {
            handAnimator.SetBool(IsWeaponSwap, false);
            if (IsOwner)
            {
                _animator.SetBool(IsWeaponSwap, false);
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
                _animator.SetBool(IsReload, false);
            }
            _pendingReloadReset = false;
        }
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnNetworkSpawn()
    {
        _weaponType.OnValueChanged += OnWeaponTypeChanged;
        OnWeaponTypeChanged(EWeaponType.Hide, _weaponType.Value);
    }

    public override void OnNetworkDespawn()
    {
        _weaponType.OnValueChanged -= OnWeaponTypeChanged;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>점프 및 체공 애니메이션 파라미터를 갱신한다.</summary>
    public void SetJumpState(bool isJumping, bool inAir)
    {
        if (!IsOwner || _animator == null) return;

        _animator.SetBool(IsJumped, isJumping);
        _animator.SetBool(IsAir, inAir);

        if (handAnimator != null)
        {
            handAnimator.SetBool(IsJumped, isJumping);
            handAnimator.SetBool(IsAir, inAir);
        }
    }

    /// <summary>공격 애니메이션 파라미터를 갱신한다.</summary>
    public void SetAttackState(bool attacking)
    {
        if (!IsOwner || _animator == null) return;

        bool canAttack = attacking && _weaponType.Value != EWeaponType.Hide;
        _animator.SetBool(IsAttack, canAttack);

        if (handAnimator != null)
        {
            handAnimator.SetBool(IsAttack, canAttack);
        }
    }

    /// <summary>장착 무기 모드를 변경하고 네트워크에 동기화한다.</summary>
    public void SetWeaponMode(EWeaponType weaponType)
    {
        if (!IsOwner) return;

        _weaponType.Value = weaponType;
    }

    /// <summary>총기 장착 상태에서 손 재장전 애니메이션을 요청한다.</summary>
    public void RequestReload()
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

        _animator.SetBool(IsReload, true);
        handAnimator.SetBool(IsReload, true);
        _pendingReloadReset = true;
        TriggerReloadRpc();
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>원격 프록시 클라이언트의 손 재장전 애니메이션을 트리거한다.</summary>
    [Rpc(SendTo.NotOwner)]
    private void TriggerReloadRpc()
    {
        if (handAnimator == null) return;

        handAnimator.SetBool(IsReload, true);
        _pendingReloadReset = true;
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="_weaponType"/> 변경 시 무기 오브젝트와 몸통/손 애니메이터를 갱신한다.</summary>
    private void OnWeaponTypeChanged(EWeaponType previousValue, EWeaponType newValue)
    {
        int index = (int)newValue;

        if (_animator != null)
        {
            bool wasAir = _animator.GetBool(IsAir);
            bool wasJumped = _animator.GetBool(IsJumped);

            if (weaponAnimControllers != null
                && index >= 0
                && index < weaponAnimControllers.Length
                && weaponAnimControllers[index] != null)
            {
                _animator.runtimeAnimatorController = weaponAnimControllers[index];
            }

            _animator.SetFloat(MoveX, _currentMoveX);
            _animator.SetBool(IsAir, wasAir);
            _animator.SetBool(IsJumped, wasJumped);
            _animator.SetInteger(WeaponMode, index);
            _animator.SetBool(IsReload, false);
        }

        if (handScript != null)
        {
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

            if (weaponSockets != null
                && index >= 0
                && index < weaponSockets.Length
                && weaponSockets[index].weaponPrefab != null)
            {
                WeaponSocketConfig socket = weaponSockets[index];
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
            }
        }

        if (handAnimator == null) return;

        handAnimator.SetInteger(WeaponMode, index);
        handAnimator.SetBool(IsReload, false);
        _pendingReloadReset = false;

        if (newValue == EWeaponType.Hide)
        {
            if (IsOwner && _animator != null)
            {
                _animator.SetBool(IsAttack, false);
                _animator.SetBool(IsWeaponSwap, false);
            }
            handAnimator.SetBool(IsAttack, false);
            handAnimator.SetBool(IsWeaponSwap, false);
            _pendingWeaponSwapReset = false;
        }
        else if (previousValue != newValue)
        {
            // 무기 교체 시 손 드로우 애니메이션 상태로 전이하도록 트리거 플래그를 설정한다.
            if (IsOwner && _animator != null)
            {
                _animator.SetBool(IsWeaponSwap, true);
            }
            handAnimator.SetBool(IsWeaponSwap, true);
            _pendingWeaponSwapReset = true;
        }
    }
}
