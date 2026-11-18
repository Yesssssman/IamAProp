using FishNet.Component.Animating;
using FishNet.Object;
using UnityEngine;

/// <summary><see cref="PropDataSO"/> 기반 몸통/비무장 손 애니메이션 오버라이드와 기본 이동 위상을 제어하는 베이스.</summary>
public abstract class PropAnimBase : NetworkBehaviour, IPropAnimController
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이 프롭의 애니메이션 클립 및 무기 소켓 설정 데이터.</summary>
    [Header("프롭 데이터")]
    [SerializeField] protected PropDataSO propData;

    /// <summary><c>MoveX</c> 파라미터 보간 감쇠 시간 (초 단위).</summary>
    [Header("애니메이션 보간")]
    [SerializeField] private float moveDampTime = 0.1f;

    /// <summary>몸통 <see cref="Animator"/> 컴포넌트.</summary>
    [Header("애니메이터 및 손 참조")]
    [SerializeField] protected Animator bodyAnimator;

    /// <summary>손 오브젝트의 <see cref="Animator"/> 컴포넌트.</summary>
    [SerializeField] protected Animator handAnimator;

    /// <summary>무기 및 탄창 장착을 관리하는 <see cref="HandScript"/> 컴포넌트.</summary>
    [SerializeField] protected HandScript handScript;

    /// <summary>이동 속력 및 상태를 제공하는 <see cref="IPropEntity"/> 컴포넌트.</summary>
    protected IPropEntity _propEntity;

    /// <summary><see cref="propData"/>로 생성된 무기 모드별 몸통 컨트롤러 배열.</summary>
    protected RuntimeAnimatorController[] _bodyWeaponControllers;

    /// <summary><see cref="propData"/>로 생성된 런타임 손 오버라이드 컨트롤러.</summary>
    protected RuntimeAnimatorController _handOverrideController;

    /// <summary>프레임 단위로 감쇠 보간된 현재 <c>MoveX</c> 파라미터 값.</summary>
    protected float _currentMoveX;

    /// <summary><see cref="Mathf.SmoothDamp(float, float, ref float, float)"/> 내부 변화율 상태값.</summary>
    private float _moveXVelocity;

    /// <summary>애니메이터 <c>MoveX</c> 파라미터 해시.</summary>
    protected static readonly int MoveX = Animator.StringToHash("MoveX");

    /// <summary>애니메이터 <c>isAttack</c> 파라미터 해시.</summary>
    protected static readonly int IsAttack = Animator.StringToHash("isAttack");

    /// <summary>애니메이터 <c>isAir</c> 파라미터 해시.</summary>
    protected static readonly int IsAir = Animator.StringToHash("isAir");

    /// <summary>애니메이터 <c>WeaponMode</c> 파라미터 해시.</summary>
    protected static readonly int WeaponMode = Animator.StringToHash("WeaponMode");

    /// <summary>애니메이터 <c>isJumped</c> 파라미터 해시.</summary>
    protected static readonly int IsJumped = Animator.StringToHash("isJumped");

    /// <summary>애니메이터 <c>isWeaponSwap</c> 파라미터 해시.</summary>
    protected static readonly int IsWeaponSwap = Animator.StringToHash("isWeaponSwap");

    /// <summary>애니메이터 <c>isReload</c> 파라미터 해시.</summary>
    protected static readonly int IsReload = Animator.StringToHash("isReload");

    /// <summary>몸통 기본 이동 블렌드 상태(<c>UnArmedBlend</c>) 해시.</summary>
    private static readonly int UnArmedBlendState = Animator.StringToHash("UnArmedBlend");

    /// <summary>맨손 달리기 애니메이션 상태(<c>Run</c>) 해시.</summary>
    private static readonly int UnarmedRunState = Animator.StringToHash("Run");

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public virtual EWeaponType CurrentWeaponType => EWeaponType.Hide;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected virtual void Awake()
    {
        _propEntity = GetComponent<IPropEntity>();

        if (handScript == null)
        {
            handScript = GetComponentInChildren<HandScript>(true);
        }

        if (handAnimator == null && handScript != null)
        {
            handAnimator = handScript.GetComponent<Animator>();
        }

        if (bodyAnimator == null)
        {
            bodyAnimator = GetComponent<Animator>();
            if (bodyAnimator == null)
            {
                PlayerScript playerScript = GetComponentInChildren<PlayerScript>(true);
                if (playerScript != null)
                {
                    bodyAnimator = playerScript.GetComponent<Animator>();
                }
            }
        }

        if (propData != null)
        {
            _bodyWeaponControllers = propData.CreateBodyControllers();
            _handOverrideController = propData.CreateHandController();

            if (bodyAnimator != null && _bodyWeaponControllers.Length > 0 && _bodyWeaponControllers[0] != null)
            {
                bodyAnimator.runtimeAnimatorController = _bodyWeaponControllers[0];
            }

            if (handAnimator != null && _handOverrideController != null)
            {
                handAnimator.runtimeAnimatorController = _handOverrideController;
            }
        }

        NetworkAnimator networkAnimator = GetComponent<NetworkAnimator>();
        if (networkAnimator != null && networkAnimator.Animator == null && bodyAnimator != null)
        {
            networkAnimator.SetAnimator(bodyAnimator);
        }
    }

    protected virtual void Update()
    {
        if (!IsSpawned || bodyAnimator == null || _propEntity == null) return;

        if (CurrentWeaponType == EWeaponType.Hide)
        {
            _currentMoveX = _propEntity.PlanarSpeedRatio >= 0.1f ? 1f : 0f;
            _moveXVelocity = 0f;
        }
        else
        {
            _currentMoveX = Mathf.SmoothDamp(
                _currentMoveX,
                _propEntity.PlanarSpeedRatio,
                ref _moveXVelocity,
                moveDampTime);
        }

        bodyAnimator.SetFloat(MoveX, _currentMoveX);

        if (handAnimator == null) return;

        handAnimator.SetFloat(MoveX, _currentMoveX);

        if (!IsOwner)
        {
            handAnimator.SetBool(IsAir, bodyAnimator.GetBool(IsAir));
            handAnimator.SetBool(IsJumped, bodyAnimator.GetBool(IsJumped));
            handAnimator.SetBool(IsAttack, bodyAnimator.GetBool(IsAttack));
            handAnimator.SetInteger(WeaponMode, bodyAnimator.GetInteger(WeaponMode));
        }

        // 맨손 달리기 중 몸통과 손 애니메이션의 재생 위상을 동기화한다.
        if (CurrentWeaponType == EWeaponType.Hide
            && handAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash == UnarmedRunState
            && !handAnimator.IsInTransition(0)
            && !bodyAnimator.IsInTransition(0))
        {
            AnimatorStateInfo bodyInfo = bodyAnimator.GetCurrentAnimatorStateInfo(0);
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
    }

    protected virtual void OnDestroy()
    {
        if (_bodyWeaponControllers != null)
        {
            for (int i = 0; i < _bodyWeaponControllers.Length; i++)
            {
                if (_bodyWeaponControllers[i] != null)
                {
                    Destroy(_bodyWeaponControllers[i]);
                    _bodyWeaponControllers[i] = null;
                }
            }
        }

        if (_handOverrideController != null)
        {
            Destroy(_handOverrideController);
            _handOverrideController = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <inheritdoc/>
    public virtual void SetJumpState(bool isJumping, bool inAir)
    {
        if (!IsOwner || bodyAnimator == null) return;

        bodyAnimator.SetBool(IsJumped, isJumping);
        bodyAnimator.SetBool(IsAir, inAir);

        if (handAnimator != null)
        {
            handAnimator.SetBool(IsJumped, isJumping);
            handAnimator.SetBool(IsAir, inAir);
        }
    }

    /// <inheritdoc/>
    public virtual void SetAttackState(bool attacking) { }

    /// <inheritdoc/>
    public virtual void SetWeaponMode(EWeaponType weaponType) { }

    /// <inheritdoc/>
    public virtual void RequestReload() { }
}
