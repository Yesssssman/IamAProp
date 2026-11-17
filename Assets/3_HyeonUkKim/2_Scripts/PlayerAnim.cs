using System;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using UnityEngine;

/// <summary>플레이어 무기 장착 상태.</summary>
public enum EWeaponType
{
    Hide = 0,
    KnifeMode = 1,
    Pistol = 2,
    Ak = 3
}

/// <summary>무기 종류별 장착 프리팹, 애니메이터 컨트롤러, 그립 오프셋 및 재장전 클립 설정.</summary>
[Serializable]
public struct WeaponSocketConfig
{
    /// <summary>오른손 소켓에 장착할 무기 프리팹.</summary>
    public GameObject weaponPrefab;

    /// <summary>장착된 무기 오브젝트의 <see cref="Animator"/>에 할당할 컨트롤러.</summary>
    public RuntimeAnimatorController weaponAnimController;

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
    private readonly SyncVar<EWeaponType> _weaponType = new(EWeaponType.Hide);

    /// <summary>이 오브젝트에 마지막으로 적용된 무기 종류 (초기값 -1로 첫 동기화 보장).</summary>
    private EWeaponType _appliedWeaponType = (EWeaponType)(-1);

    /// <summary>몸통 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _animator;

    /// <summary>현재 오른손에 장착된 무기의 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _weaponAnimator;

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

    private const float HandShotFrames = 17f; // 손 발사 클립(Common_M1911_Shot, Common_AK_Shot) 총 프레임 수
    private const float WeaponShotFrames = 9f; // 총기 발사 클립(M1911_Rig|Shot, AK74Rig|Shot) 총 프레임 수
    private const float M1911ReloadHandFrames = 107f; // 권총 손 재장전 클립(Common_M1911_Reload) 총 프레임 수
    private const float M1911ReloadWeaponStartFrame = 78f; // 권총 재장전 중 슬라이드 후퇴 시작 프레임
    private const float M1911DrawHandFrames = 47f; // 권총 손 드로우 클립(Common_M1911_Draw) 총 프레임 수
    private const float M1911DrawWeaponStartFrame = 19f; // 권총 드로우 중 슬라이드 후퇴 시작 프레임
    private const float M1911ReloadWeaponFrames = 9f; // 권총 슬라이드 후퇴 클립(M1911_Rig|Reload) 총 프레임 수
    private const float AkReloadHandFrames = 179f; // 소총 손 재장전 클립(Common_AK_Reload) 총 프레임 수
    private const float AkReloadWeaponStartFrame = 130f; // 소총 재장전 중 노리쇠 후퇴 시작 프레임
    private const float AkDrawHandFrames = 69f; // 소총 손 드로우 클립(Common_AK_Draw) 총 프레임 수
    private const float AkDrawWeaponStartFrame = 15f; // 소총 드로우 중 노리쇠 후퇴 시작 프레임
    private const float AkReloadWeaponFrames = 16f; // 소총 노리쇠 후퇴 클립(AK74Rig|Reload) 총 프레임 수

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

    /// <summary>권총 발사 애니메이션 상태(<c>M1911_Shot</c>) 해시.</summary>
    private static readonly int M1911ShotState = Animator.StringToHash("M1911_Shot");

    /// <summary>소총 발사 애니메이션 상태(<c>AK_Shot</c>) 해시.</summary>
    private static readonly int AkShotState = Animator.StringToHash("AK_Shot");

    /// <summary>권총 재장전 애니메이션 상태(<c>M1911_Reload</c>) 해시.</summary>
    private static readonly int M1911ReloadState = Animator.StringToHash("M1911_Reload");

    /// <summary>소총 재장전 애니메이션 상태(<c>AK_Reload</c>) 해시.</summary>
    private static readonly int AkReloadState = Animator.StringToHash("AK_Reload");

    /// <summary>몸통 기본 이동 블렌드 상태(<c>UnArmedBlend</c>) 해시.</summary>
    private static readonly int UnArmedBlendState = Animator.StringToHash("UnArmedBlend");

    /// <summary>맨손 달리기 애니메이션 상태(<c>Run</c>) 해시.</summary>
    private static readonly int UnarmedRunState = Animator.StringToHash("Run");

    /// <summary>총기 기본 대기 애니메이션 상태(<c>Idle</c>) 해시.</summary>
    private static readonly int WeaponIdleState = Animator.StringToHash("Idle");

    /// <summary>총기 발사 애니메이션 상태(<c>Shot</c>) 해시.</summary>
    private static readonly int WeaponShotState = Animator.StringToHash("Shot");

    /// <summary>총기 재장전 애니메이션 상태(<c>Reload</c>) 해시.</summary>
    private static readonly int WeaponReloadState = Animator.StringToHash("Reload");

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

        // 손 애니메이터의 발사, 재장전 및 드로우 프레임에 맞춰 총기 애니메이터를 동기화한다.
        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
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
            }
            else if (activeHash == M1911ReloadState)
            {
                float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * M1911ReloadHandFrames;
                float weaponFrame = handFrame - M1911ReloadWeaponStartFrame;
                if (weaponFrame >= 0f && weaponFrame < M1911ReloadWeaponFrames)
                {
                    _weaponAnimator.SetBool(IsReload, true);
                    _weaponAnimator.SetBool(IsAttack, false);
                    _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / M1911ReloadWeaponFrames);
                }
                else
                {
                    _weaponAnimator.SetBool(IsReload, false);
                    _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                }
            }
            else if (activeHash == AkReloadState)
            {
                float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * AkReloadHandFrames;
                float weaponFrame = handFrame - AkReloadWeaponStartFrame;
                if (weaponFrame >= 0f && weaponFrame < AkReloadWeaponFrames)
                {
                    _weaponAnimator.SetBool(IsReload, true);
                    _weaponAnimator.SetBool(IsAttack, false);
                    _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / AkReloadWeaponFrames);
                }
                else
                {
                    _weaponAnimator.SetBool(IsReload, false);
                    _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                }
            }
            else if (activeHash == M1911DrawState)
            {
                float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * M1911DrawHandFrames;
                float weaponFrame = handFrame - M1911DrawWeaponStartFrame;
                if (weaponFrame >= 0f && weaponFrame < M1911ReloadWeaponFrames)
                {
                    _weaponAnimator.SetBool(IsReload, true);
                    _weaponAnimator.SetBool(IsAttack, false);
                    _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / M1911ReloadWeaponFrames);
                }
                else
                {
                    _weaponAnimator.SetBool(IsReload, false);
                    _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                }
            }
            else if (activeHash == AkDrawState)
            {
                float handFrame = Mathf.Clamp01(activeHandState.normalizedTime) * AkDrawHandFrames;
                float weaponFrame = handFrame - AkDrawWeaponStartFrame;
                if (weaponFrame >= 0f && weaponFrame < AkReloadWeaponFrames)
                {
                    _weaponAnimator.SetBool(IsReload, true);
                    _weaponAnimator.SetBool(IsAttack, false);
                    _weaponAnimator.Play(WeaponReloadState, 0, weaponFrame / AkReloadWeaponFrames);
                }
                else
                {
                    _weaponAnimator.SetBool(IsReload, false);
                    _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                }
            }
            else
            {
                _weaponAnimator.SetBool(IsAttack, false);
                _weaponAnimator.SetBool(IsReload, false);
                if (_weaponAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash != WeaponIdleState)
                {
                    _weaponAnimator.Play(WeaponIdleState, 0, 0f);
                }
            }
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

        if (_weaponAnimator != null && _weaponAnimator.runtimeAnimatorController != null)
        {
            _weaponAnimator.SetBool(IsAttack, canAttack);
        }
    }

    /// <summary>장착 무기 모드를 변경하고 네트워크에 동기화한다.</summary>
    public void SetWeaponMode(EWeaponType weaponType)
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
