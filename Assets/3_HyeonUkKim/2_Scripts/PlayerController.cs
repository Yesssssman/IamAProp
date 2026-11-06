using System;
using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

enum EWeaponType {Hide =0, KnifeMode = 1, Pistol = 2, Ak = 3 }

public enum EPlayerState { Idle, Attack, Air, Die }

public class PlayerController : NetworkBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float rotationSmoothTime = 0.1f;

    [SerializeField] private float _JumpForce = 5f;


    [Header("참조")]
    [SerializeField] private Transform cameraTransform;

    [Header("바닥 체크")]
    [SerializeField] private Transform groundCheck; // 발밑에 빈 GameObject 배치 후 연결
    [SerializeField] private float checkRadius = 0.2f;
    [SerializeField] private LayerMask groundLayer;  // 바닥 오브젝트들의 Layer

    [Header("애니메이터 오버라이드")]
    [SerializeField] private RuntimeAnimatorController[] weaponAnimControllers;

    public bool _wasGrounded; // 이전 프레임의 땅 접촉여부
    public bool _isGrounded; // 현재 프레임의 땅 접촉여부

    private PlayerControls inputActions; // 입력 액션

    private Rigidbody _rb;

    private Animator _animator;
    private Vector2 moveInput; // 현재 이동값
    private Vector3 verticalVelocity; 
    private float turnSmoothVelocity;

    private EWeaponType _EweaponType;

    private EPlayerState _EplayerState;

    /*
     Animator 변수
     */
    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int isAttack = Animator.StringToHash("isAttack");
    private static readonly int isAir = Animator.StringToHash("isAir");

    private static readonly int _weaponMode = Animator.StringToHash("WeaponMode");

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();
        _EweaponType = EWeaponType.Hide;
        _EplayerState = EPlayerState.Idle;

    }

    // 네트워크 상에서 오브젝트가 스폰될 때 호출 (Awake/Start 대체)
    public override void OnNetworkSpawn()
    {
        // 플레이어 통제권 여부 검사
        if(IsOwner)
        {
            // 키 이벤트 등록
            inputActions = new PlayerControls();
            inputActions.Enable();
            inputActions.Player.Move.performed += OnMovePerformed;
            inputActions.Player.Move.canceled += OnMoveCanceled;

            inputActions.Player.Attack.started += OnAttackStarted;
            inputActions.Player.Attack.canceled += OnAttackEnd;

            inputActions.Player.Jump.started += OnJump;

            inputActions.Player.ChgWeapon.started += ChangeWeaponMode;

            CinemachineCamera cam = FindAnyObjectByType<CinemachineCamera>();
            if (cam != null)
            {
                Transform target = cameraTransform != null ? cameraTransform : transform;
                cam.Follow = transform; // 혹은 가슴/머리 위치의 빈 오브젝트
                cam.LookAt = transform;

                // TPS 시점 강제 정렬 (선택 사항: 스폰 직후 카메라가 캐릭터 뒤로 오도록 초기화)
                var orbitalFollow = cam.GetComponent<CinemachineOrbitalFollow>();
                if (orbitalFollow != null)
                {
                    // 캐릭터가 바라보는 방향(캐릭터 뒤)으로 카메라 축을 정렬
                    orbitalFollow.HorizontalAxis.Value = transform.eulerAngles.y;
                }

            }

            if (Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }
    }

    // 오브젝트가 파괴되거나 디스폰될 때 구독 해제
    public override void OnNetworkDespawn()
    {
        if (IsOwner && inputActions != null)
        {
            inputActions.Player.Disable();
        }
    }

    private void Start()
    {
        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    void Update()
    {
        
    }

    private void FixedUpdate()
    {
        // 내 캐릭터가 아니면 early Exit;
        if (!IsOwner) return;

        // 바닥 체크
        _isGrounded = Physics.CheckSphere(groundCheck.position, checkRadius, groundLayer);
        // 착지 순간 판정 (이전엔 공중이었는데 지금은 땅일때)
        if(!_wasGrounded && _isGrounded )
        {
            print("땅 착지!");
            OnJumpEnd(); // 땅에 닿을 떄 구독된 이벤트 함수들 호출
        }

        _wasGrounded = _isGrounded;

        HandleMovement();
    }

    // 이동값 입력시작
    void OnMovePerformed(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    // 이동값 입력종료
    void OnMoveCanceled(InputAction.CallbackContext context)
    {
        moveInput = Vector2.zero;
    }

    void OnJump(InputAction.CallbackContext context)
    {
        // 내 캐릭터가 아니면 early Exit;
        if (!IsOwner) return;
        // 공중에서 점프 불가능하게 처리
        if (!_isGrounded) return;

        // 서버에 점프 RPC 요청
        HandleJumpRpc();
        print("OnJump");
    }

    void OnJumpEnd()
    {
        // 여전히 공중이라면 return
        if (!_isGrounded) return;
        // 서버에 점프END RPC 요청
        HandleJumpEndRpc();
        print("OnJumpEnd");
    }

    void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        HandleAttackRpc();
        print("OnAttack");
    }

    void OnAttackEnd(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;
        HandleAttackEndRpc();
        print("OnAttackEnd");
    }

    private void HandleMovement()
    {
        Vector3 direction = new Vector3(moveInput.x, 0f, moveInput.y).normalized;

        // 이동값이 0.1f 이상일떄만 RPC 호출
        if (direction.magnitude >= 0.1f)
        {
            // 클라이언트 측에서 목표 각도만 계산
            float targetAngle = Mathf.Atan2(direction.x, direction.z) * Mathf.Rad2Deg + cameraTransform.eulerAngles.y;

            // 서버로 계산된 타겟 각도를 보냄
            HandleMovementRpc(targetAngle);
        }
        else
        {
            HandleMoveStopRpc();
        }
    }

    private void ChangeWeaponMode(InputAction.CallbackContext context)
    {
        float weaponType = context.ReadValue<float>();
        _EweaponType = (EWeaponType)(int)weaponType;

        ChangeWeaponModeRpc();
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void ChangeWeaponModeRpc()
    {
        if (weaponAnimControllers[(int)_EweaponType] != null)
        {
            _animator.runtimeAnimatorController = weaponAnimControllers[(int)_EweaponType];
        }
    }

    // 에디터 씬 뷰에서 체크 반경 시각화
    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, checkRadius);
    }

    /*
    RPC 함수 목록
    RPC는 무조건 서버에서 실행되어지는 함수    
    */

    // 이동 RPC 함수
    [Rpc(SendTo.Server,Delivery = RpcDelivery.Unreliable)]
    void HandleMovementRpc(float targetAngle)
    {
        // 서버의 현재 Rigidbody 각도를 기준으로 부드러운 회전
        float smoothAngle = Mathf.SmoothDampAngle(_rb.rotation.eulerAngles.y, targetAngle, ref turnSmoothVelocity, rotationSmoothTime);
        _rb.MoveRotation(Quaternion.Euler(0f, smoothAngle, 0f));

        // 서버에서 물리적 이동 처리
        Vector3 moveDirection = Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward;
        Vector3 targetPosition = _rb.position + moveDirection.normalized * (moveSpeed * Time.fixedDeltaTime);
        _rb.MovePosition(targetPosition);

        _animator.SetFloat(MoveX, 1f);
    }

    // 점프 RPC 함수
    [Rpc(SendTo.Server,Delivery =RpcDelivery.Unreliable)]
    void HandleJumpRpc()
    {
        _rb.AddForce(Vector3.up * _JumpForce, ForceMode.Impulse);
        _animator.SetBool(isAir, true);
    }
    // 점프 END RPC 함수
    [Rpc(SendTo.Server,Delivery =RpcDelivery.Unreliable)]
    void HandleJumpEndRpc()
    {
        _animator.SetBool(isAir, false);
    }

    // 이동 정지 RPC 함수
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleMoveStopRpc()
    {
        _animator.SetFloat(MoveX, 0f);
    }

    // 공격 RPC 함수
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleAttackRpc()
    {
        print("HandleAttackRpc");
        _animator.SetBool(isAttack, true);
    }

    // 공격 END RPC 함수
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleAttackEndRpc()
    {
        print("HandleAttackEndRpc");
        _animator.SetBool(isAttack, false);
    }
}
