using Unity.Cinemachine;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerController : NetworkBehaviour
{
    [Header("이동 설정")]
    [SerializeField] private float moveSpeed = 5.0f;
    [SerializeField] private float rotationSmoothTime = 0.1f;

    [Header("참조")]
    [SerializeField] private Transform cameraTransform;

    private PlayerControls inputActions; // 입력 액션

    private Rigidbody _rb;

    private Animator _animator;
    private Vector2 moveInput; // 현재 이동값
    private Vector3 verticalVelocity; 
    private float turnSmoothVelocity;

    private static readonly int MoveX = Animator.StringToHash("MoveX");
    private static readonly int isAttack = Animator.StringToHash("isAttack");

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _animator = GetComponent<Animator>();
    }

    // 네트워크 상에서 오브젝트가 스폰될 때 호출 (Awake/Start 대체)
    public override void OnNetworkSpawn()
    {
        // 플레이어 통제권 여부 검사
        if(IsOwner)
        {
            inputActions = new PlayerControls();
            inputActions.Enable();
            inputActions.Player.Move.performed += OnMovePerformed;
            inputActions.Player.Move.canceled += OnMoveCanceled;

            inputActions.Player.Attack.started += OnAttack;
            inputActions.Player.Attack.canceled += OnAttackEnd;


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

    void OnAttack(InputAction.CallbackContext context)
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

    // 이동
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

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleMoveStopRpc()
    {
        _animator.SetFloat(MoveX, 0f);
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleAttackRpc()
    {
        print("HandleAttackRpc");
        _animator.SetBool(isAttack, true);
    }

    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    void HandleAttackEndRpc()
    {
        print("HandleAttackEndRpc");
        _animator.SetBool(isAttack, false);
    }
}
