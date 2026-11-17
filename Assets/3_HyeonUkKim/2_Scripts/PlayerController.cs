using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>플레이어 동작 상태.</summary>
public enum EPlayerState
{
    Idle,
    Attack,
    Air,
    Die
}

/// <summary>FishNet CSP 기반 <see cref="Rigidbody"/> 이동, 예측/재조정 및 입력을 처리한다.</summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PlayerAnim))]
public class PlayerController : NetworkBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>평면 이동 속력 (초당 월드 단위).</summary>
    [Header("이동 설정")]
    [SerializeField] private float moveSpeed = 5.0f;

    /// <summary>이동 방향 회전 감쇠 시간 (초 단위).</summary>
    [SerializeField] private float rotationSmoothTime = 0.1f;

    /// <summary>점프 시 가하는 수직 임펄스 크기.</summary>
    [SerializeField] private float _JumpForce = 5f;

    [Header("Ragdoll 설정")]
    [Tooltip("수평으로 밀어내는 힘")]
    [SerializeField] private float _pushForce = 2.5f;

    /// <summary>맨손 대기 상태 진입 시 쓰러짐을 유도하는 초기 각속도 (초당 라디안).</summary>
    [SerializeField] private float ragdollToppleSpeed = 3.5f;

    [Tooltip("넘어질 때 바닥으로 살짝 누르는 하향 힘")]
    [SerializeField] private float _downwardForce = 0.5f;

    /// <summary>이동 기준 방향을 계산할 카메라 트랜스폼.</summary>
    [Header("참조")]
    [SerializeField] private Transform cameraTransform;

    /// <summary>발밑 바닥 체크 구체를 캐스팅하는 기준점.</summary>
    [Header("바닥 체크")]
    [SerializeField] private Transform _groundCheck;

    /// <summary>바닥 체크 구체의 반지름 (월드 단위).</summary>
    [SerializeField] private float checkRadius = 0.2f;

    /// <summary>바닥으로 판정할 물리 레이어 마스크.</summary>
    [SerializeField] private LayerMask _groundLayer;

    /// <summary>직전 물리 틱의 바닥 접촉 여부.</summary>
    public bool _wasGrounded;

    /// <summary>현재 물리 틱의 바닥 접촉 여부.</summary>
    public bool _isGrounded;

    /// <summary>로컬 플레이어 입력 액션 에셋 인스턴스.</summary>
    private PlayerControls _inputActions;

    /// <summary>캐릭터 물리 <see cref="Rigidbody"/> 컴포넌트.</summary>
    private Rigidbody _rb;

    /// <summary>캐릭터 충돌체 <see cref="CapsuleCollider"/> 컴포넌트.</summary>
    private CapsuleCollider _capsuleCollider;

    /// <summary>애니메이션 동기화를 담당하는 <see cref="PlayerAnim"/> 컴포넌트.</summary>
    private PlayerAnim _playerAnim;

    /// <summary>현재 프레임의 2D 이동 입력값.</summary>
    private Vector2 moveInput;

    /// <summary>다음 네트워크 틱에 소비할 점프 입력 플래그.</summary>
    private bool _jumpRequested;

    /// <summary><see cref="Mathf.SmoothDampAngle"/> 내부 회전 각속도 상태값.</summary>
    private float turnSmoothVelocity;

    /// <summary>현재 시뮬레이션된 평면 이동 속력 (초당 월드 단위).</summary>
    private float _currentPlanarSpeed;

    /// <summary>현재 플레이어 동작 상태.</summary>
    private EPlayerState _EplayerState;

    /// <summary>현재 루트 <see cref="Rigidbody"/> 회전 제약이 해제된 맨손 쓰러짐 대기 상태 여부.</summary>
    private bool _isRagdollIdle;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary><see cref="moveSpeed"/> 대비 정규화된 현재 평면 이동 속력 비율 (0~1).</summary>
    public float PlanarSpeedRatio
    {
        get
        {
            if (moveSpeed <= 0f) return 0f;
            return Mathf.Clamp01(_currentPlanarSpeed / moveSpeed);
        }
    }

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _capsuleCollider = GetComponentInChildren<CapsuleCollider>();
        _playerAnim = GetComponent<PlayerAnim>();
        _EplayerState = EPlayerState.Idle;

        if (_groundCheck == null)
        {
            _groundCheck = transform.Find("GroundCheck");
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (_groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_groundCheck.position, checkRadius);
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartNetwork()
    {
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick += TimeManager_OnTick;
            base.TimeManager.OnPostTick += TimeManager_OnPostTick;
        }
    }

    public override void OnStopNetwork()
    {
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick -= TimeManager_OnTick;
            base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
        }
    }

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;

        _inputActions = new PlayerControls();
        _inputActions.Enable();
        _inputActions.Player.Move.performed += OnMovePerformed;
        _inputActions.Player.Move.canceled += OnMoveCanceled;
        _inputActions.Player.Attack.started += OnAttackStarted;
        _inputActions.Player.Attack.canceled += OnAttackEnd;
        _inputActions.Player.Jump.started += OnJump;
        _inputActions.Player.ChgWeapon.started += ChangeWeaponMode;
        _inputActions.Player.Reload.started += OnReloadStarted;

        CinemachineCamera cam = FindAnyObjectByType<CinemachineCamera>();
        if (cam != null)
        {
            cam.Follow = transform;
            cam.LookAt = transform;

            CinemachineOrbitalFollow orbitalFollow = cam.GetComponent<CinemachineOrbitalFollow>();
            if (orbitalFollow != null)
            {
                orbitalFollow.HorizontalAxis.Value = transform.eulerAngles.y;
            }
        }

        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }
    }

    public override void OnStopClient()
    {
        moveInput = Vector2.zero;
        _jumpRequested = false;

        if (_inputActions == null) return;

        _inputActions.Player.Move.performed -= OnMovePerformed;
        _inputActions.Player.Move.canceled -= OnMoveCanceled;
        _inputActions.Player.Attack.started -= OnAttackStarted;
        _inputActions.Player.Attack.canceled -= OnAttackEnd;
        _inputActions.Player.Jump.started -= OnJump;
        _inputActions.Player.ChgWeapon.started -= ChangeWeaponMode;
        _inputActions.Player.Reload.started -= OnReloadStarted;
        _inputActions.Player.Disable();
        _inputActions.Dispose();
        _inputActions = null;
    }

    public override void CreateReconcile()
    {
        if (_rb == null) return;

        PlayerReconcileData rd = new PlayerReconcileData(
            _rb.position,
            _rb.rotation,
            _rb.linearVelocity,
            _rb.angularVelocity,
            _currentPlanarSpeed,
            _isGrounded,
            _wasGrounded,
            _isRagdollIdle,
            turnSmoothVelocity);
        PerformReconcile(rd);
    }

    // ***************************************************************
    // Tick & Prediction
    // ***************************************************************

    /// <summary>네트워크 틱마다 입력을 수집해 예측 시뮬레이션을 실행한다.</summary>
    private void TimeManager_OnTick()
    {
        PlayerInputData inputData = default;
        if (IsOwner)
        {
            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }

            float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
            inputData = new PlayerInputData(moveInput, cameraYaw, _jumpRequested);
            _jumpRequested = false;
        }

        PerformReplicate(inputData);
    }

    /// <summary>물리 시뮬레이션이 완료된 틱 후반에 서버/로컬 상태 스냅샷을 기록한다.</summary>
    private void TimeManager_OnPostTick()
    {
        CreateReconcile();
    }

    /// <summary>틱 입력과 <see cref="NetworkBehaviour.TimeManager"/>의 <c>TickDelta</c>로 <see cref="Rigidbody"/> 이동 및 점프를 시뮬레이션한다.</summary>
    [Replicate]
    private void PerformReplicate(PlayerInputData rd, ReplicateState state = ReplicateState.Invalid, Channel channel = Channel.Unreliable)
    {
        if (_rb == null) return;

        float delta = (float)base.TimeManager.TickDelta;

        if (_groundCheck != null)
        {
            _isGrounded = Physics.CheckSphere(_groundCheck.position, checkRadius, _groundLayer);
            if (!_isGrounded && _isRagdollIdle && _capsuleCollider != null)
            {
                Vector3 capsuleCenterWorld = _capsuleCollider.transform.TransformPoint(_capsuleCollider.center);
                _isGrounded = Physics.CheckSphere(capsuleCenterWorld, checkRadius, _groundLayer);
            }
        }

        if (!_wasGrounded && _isGrounded)
        {
            _EplayerState = EPlayerState.Idle;
            if (_playerAnim != null)
            {
                _playerAnim.SetJumpState(false, false);
            }
        }

        _wasGrounded = _isGrounded;

        Vector3 inputDirection = new Vector3(rd.MoveInput.x, 0f, rd.MoveInput.y).normalized;
        Vector3 horizontalVelocity = Vector3.zero;
        float currentRotationY = _rb.rotation.eulerAngles.y;
        bool shouldRagdoll = _playerAnim != null
            && _playerAnim.CurrentWeaponType == EWeaponType.Hide
            && inputDirection.magnitude < 0.1f
            && _isGrounded
            && !rd.JumpRequested;

        if (shouldRagdoll)
        {
            if (!_isRagdollIdle)
            {
                _isRagdollIdle = true;
                _rb.constraints = RigidbodyConstraints.None;

                // 클라이언트와 서버가 동일한 방향으로 쓰러지도록 틱 기반 결정적 각도를 사용한다.
                uint tick = rd.GetTick();
                float angleRad = ((tick * 1103515245u + 12345u) & 0xFFFFu) * (Mathf.PI * 2f / 65536f);
                Vector3 pushDirection = new Vector3(Mathf.Cos(angleRad), 0f, Mathf.Sin(angleRad));
                Vector3 finalForce = (pushDirection * _pushForce) + (Vector3.down * _downwardForce);

                _rb.AddForce(finalForce, ForceMode.Impulse);
                _rb.AddTorque((_rb.rotation * Vector3.right) * ragdollToppleSpeed);
            }
        }
        else if (_isRagdollIdle)
        {
            _isRagdollIdle = false;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.angularVelocity = Vector3.zero;
            _rb.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
        }

        if (inputDirection.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + rd.CameraYaw;
            currentRotationY = Mathf.SmoothDampAngle(
                currentRotationY,
                targetAngle,
                ref turnSmoothVelocity,
                rotationSmoothTime,
                Mathf.Infinity,
                delta);

            _rb.MoveRotation(Quaternion.Euler(0f, currentRotationY, 0f));

            Vector3 moveDirection = (Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward).normalized;
            Vector3 targetPosition = _rb.position + moveDirection * (moveSpeed * delta);
            _rb.MovePosition(targetPosition);

            horizontalVelocity = moveDirection * moveSpeed;
        }

        if (rd.JumpRequested && _isGrounded)
        {
            _rb.AddForce(Vector3.up * _JumpForce, ForceMode.Impulse);
            _isGrounded = false;
            _wasGrounded = false;
            _EplayerState = EPlayerState.Air;
            if (_playerAnim != null)
            {
                _playerAnim.SetJumpState(true, true);
            }
        }

        _currentPlanarSpeed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
    }

    /// <summary>서버에서 전송된 권위 상태 <paramref name="rd"/>로 <see cref="Rigidbody"/>와 이동 상태를 강제 덮어쓴다.</summary>
    [Reconcile]
    private void PerformReconcile(PlayerReconcileData rd, Channel channel = Channel.Unreliable)
    {
        if (_rb == null) return;

        _isRagdollIdle = rd.IsRagdollIdle;
        _rb.constraints = _isRagdollIdle
            ? RigidbodyConstraints.None
            : RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;

        transform.SetPositionAndRotation(rd.Position, rd.Rotation);
        _rb.position = rd.Position;
        _rb.rotation = rd.Rotation;
        if (!_rb.isKinematic)
        {
            _rb.linearVelocity = rd.Velocity;
            _rb.angularVelocity = rd.AngularVelocity;
        }

        _isGrounded = rd.IsGrounded;
        _wasGrounded = rd.WasGrounded;
        turnSmoothVelocity = rd.TurnSmoothVelocity;
        _currentPlanarSpeed = rd.PlanarSpeed;
    }

    // ***************************************************************
    // Input Event Handlers
    // ***************************************************************

    /// <summary>이동 입력 벡터를 갱신한다.</summary>
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    /// <summary>이동 입력이 해제되면 입력 벡터를 초기화한다.</summary>
    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        moveInput = Vector2.zero;
    }

    /// <summary>바닥에 닿아 있을 때 점프 요청 플래그를 설정한다.</summary>
    private void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner || !_isGrounded) return;

        _jumpRequested = true;
    }

    /// <summary>공격 입력 시작 시 상태를 갱신한다.</summary>
    private void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _EplayerState = EPlayerState.Attack;
        _playerAnim.SetAttackState(true);
    }

    /// <summary>공격 입력 종료 시 상태를 복원한다.</summary>
    private void OnAttackEnd(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        _playerAnim.SetAttackState(false);
    }

    /// <summary>무기 전환 입력에 따라 무기 모드를 변경한다.</summary>
    private void ChangeWeaponMode(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        int weaponIndex = Mathf.Clamp(Mathf.RoundToInt(context.ReadValue<float>()) - 1, 0, 3);
        _playerAnim.SetWeaponMode((EWeaponType)weaponIndex);
    }

    /// <summary>재장전 입력 시 손 재장전 시퀀스를 요청한다.</summary>
    private void OnReloadStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _playerAnim.RequestReload();
    }
}
