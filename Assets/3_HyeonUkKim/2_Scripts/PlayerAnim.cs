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

/// <summary>소유자 및 원격 프록시의 플레이어 애니메이터 파라미터와 컨트롤러를 동기화한다.</summary>
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

    /// <summary><see cref="EWeaponType"/> 순서에 대응하는 애니메이터 컨트롤러 목록.</summary>
    [Header("애니메이터 오버라이드")]
    [SerializeField] private RuntimeAnimatorController[] weaponAnimControllers;

    /// <summary>현재 장착 중인 무기 종류.</summary>
    private readonly NetworkVariable<EWeaponType> _weaponType = new(
        EWeaponType.Hide,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// <summary>캐릭터 <see cref="Animator"/> 컴포넌트.</summary>
    private Animator _animator;

    /// <summary>이동 상태를 제공하는 <see cref="PlayerController"/> 컴포넌트.</summary>
    private PlayerController _playerController;

    /// <summary>프레임 단위로 감쇠 보간된 현재 <c>MoveX</c> 파라미터 값.</summary>
    private float _currentMoveX;

    /// <summary><see cref="Mathf.SmoothDamp(float, float, ref float, float)"/> 내부 변화율 상태값.</summary>
    private float _moveXVelocity;

    /// <summary>무기 교체 애니메이션 진입 직후 <c>isWeaponSwap</c>을 초기화하기 위한 대기 플래그.</summary>
    private bool _pendingWeaponSwapReset;

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

    /// <summary>단검 드로우 애니메이션 상태(<c>Knife_Draw</c>) 해시.</summary>
    private static readonly int KnifeDrawState = Animator.StringToHash("Knife_Draw");

    /// <summary>권총 드로우 애니메이션 상태(<c>M1911_Draw</c>) 해시.</summary>
    private static readonly int M1911DrawState = Animator.StringToHash("M1911_Draw");

    /// <summary>소총 드로우 애니메이션 상태(<c>AK_Draw</c>) 해시.</summary>
    private static readonly int AkDrawState = Animator.StringToHash("AK_Draw");

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _animator = GetComponent<Animator>();
        _playerController = GetComponent<PlayerController>();
    }

    private void Update()
    {
        if (!IsSpawned || _animator == null || _playerController == null) return;

        _currentMoveX = Mathf.SmoothDamp(
            _currentMoveX,
            _playerController.PlanarSpeedRatio,
            ref _moveXVelocity,
            moveDampTime);

        _animator.SetFloat(MoveX, _currentMoveX);

        // 드로우 애니메이션 진입이 확인되면 플래그를 내려 반복 전이를 방지한다.
        if (IsOwner && _pendingWeaponSwapReset)
        {
            int currentHash = _animator.GetCurrentAnimatorStateInfo(0).shortNameHash;
            int nextHash = _animator.IsInTransition(0) ? _animator.GetNextAnimatorStateInfo(0).shortNameHash : 0;

            if (currentHash == KnifeDrawState || currentHash == M1911DrawState || currentHash == AkDrawState
                || nextHash == KnifeDrawState || nextHash == M1911DrawState || nextHash == AkDrawState)
            {
                _animator.SetBool(IsWeaponSwap, false);
                _pendingWeaponSwapReset = false;
            }
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
    }

    /// <summary>공격 애니메이션 파라미터를 갱신한다.</summary>
    public void SetAttackState(bool attacking)
    {
        if (!IsOwner || _animator == null) return;

        bool canAttack = attacking && _weaponType.Value != EWeaponType.Hide;
        _animator.SetBool(IsAttack, canAttack);
    }

    /// <summary>장착 무기 모드를 변경하고 네트워크에 동기화한다.</summary>
    public void SetWeaponMode(EWeaponType weaponType)
    {
        if (!IsOwner) return;

        _weaponType.Value = weaponType;
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary><see cref="_weaponType"/> 변경 시 애니메이터 컨트롤러와 파라미터를 갱신한다.</summary>
    private void OnWeaponTypeChanged(EWeaponType previousValue, EWeaponType newValue)
    {
        if (_animator == null) return;

        int index = (int)newValue;
        if (weaponAnimControllers != null
            && index >= 0
            && index < weaponAnimControllers.Length
            && weaponAnimControllers[index] != null)
        {
            _animator.runtimeAnimatorController = weaponAnimControllers[index];
        }

        _animator.SetInteger(WeaponMode, index);
        if (!IsOwner) return;

        if (newValue == EWeaponType.Hide)
        {
            _animator.SetBool(IsAttack, false);
            _animator.SetBool(IsWeaponSwap, false);
            _pendingWeaponSwapReset = false;
        }
        else if (previousValue != newValue)
        {
            // 무기 교체 시 드로우 애니메이션 상태로 전이하도록 트리거 플래그를 설정한다.
            _animator.SetBool(IsWeaponSwap, true);
            _pendingWeaponSwapReset = true;
        }
    }
}
