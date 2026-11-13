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

    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int IsAttack = Animator.StringToHash("isAttack");
    private static readonly int IsAir = Animator.StringToHash("isAir");
    private static readonly int WeaponMode = Animator.StringToHash("WeaponMode");
    private static readonly int IsJumped = Animator.StringToHash("isJumped");

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
        if (IsOwner && newValue == EWeaponType.Hide)
        {
            _animator.SetBool(IsAttack, false);
        }
    }
}
