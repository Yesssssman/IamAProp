using Unity.Cinemachine;
using Unity.Netcode;
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

/// <summary>소유자 권한 기반 이동, 클라이언트 예측/서버 보정 및 입력을 처리한다.</summary>
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

    /// <summary>서버 보정을 수행할 위치 오차 임계값 (월드 단위).</summary>
    [Header("예측 및 보정 설정")]
    [SerializeField] private float reconciliationThreshold = 0.5f;

    /// <summary>원격 프록시의 복제 속도 보간 계수.</summary>
    [SerializeField] private float proxyVelocitySmoothSpeed = 15f;

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

    /// <summary>소유자가 기록하고 원격 프록시가 읽는 월드 이동 속도 벡터 (초당 월드 단위).</summary>
    private readonly NetworkVariable<Vector3> _networkVelocity = new(
        Vector3.zero,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Owner);

    /// <summary>클라이언트 예측 히스토리를 저장할 링 버퍼 크기.</summary>
    private const int PredictionBufferSize = 1024;

    /// <summary>틱별 입력 스냅샷 링 버퍼.</summary>
    private readonly PlayerInputPayload[] _inputBuffer = new PlayerInputPayload[PredictionBufferSize];

    /// <summary>틱별 예측 결과 상태 링 버퍼.</summary>
    private readonly PlayerStatePayload[] _stateBuffer = new PlayerStatePayload[PredictionBufferSize];

    /// <summary>서버 벽 관통 검사에 재사용할 레이캐스트 결과 버퍼.</summary>
    private readonly RaycastHit[] _wallHits = new RaycastHit[4];

    /// <summary>서버에서 마지막으로 검증 완료한 플레이어 상태 스냅샷.</summary>
    private PlayerStatePayload _lastServerValidatedState;

    /// <summary>소유자가 마지막으로 서버 검증을 요청한 네트워크 틱 번호.</summary>
    private int _lastProcessedTick = -1;

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

    /// <summary>다음 물리 틱에 소비할 점프 입력 플래그.</summary>
    private bool _jumpRequested;

    /// <summary><see cref="Mathf.SmoothDampAngle"/> 내부 회전 각속도 상태값.</summary>
    private float turnSmoothVelocity;

    /// <summary>원격 프록시에서 프레임 단위로 보간된 이동 속도 벡터.</summary>
    private Vector3 _smoothedProxyVelocity;

    /// <summary>소유자의 현재 평면 이동 속력 (초당 월드 단위).</summary>
    private float _ownerPlanarSpeed;

    /// <summary>현재 플레이어 동작 상태.</summary>
    private EPlayerState _EplayerState;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary><see cref="moveSpeed"/> 대비 정규화된 현재 평면 이동 속력 비율 (0~1).</summary>
    public float PlanarSpeedRatio
    {
        get
        {
            if (moveSpeed <= 0f) return 0f;

            float planarSpeed = IsOwner
                ? _ownerPlanarSpeed
                : new Vector2(_smoothedProxyVelocity.x, _smoothedProxyVelocity.z).magnitude;

            return Mathf.Clamp01(planarSpeed / moveSpeed);
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

    private void Start()
    {
        if (!IsOwner) return;

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void Update()
    {
        if (!IsSpawned || IsOwner) return;

        _smoothedProxyVelocity = Vector3.Lerp(
            _smoothedProxyVelocity,
            _networkVelocity.Value,
            Time.deltaTime * proxyVelocitySmoothSpeed);
    }

    private void FixedUpdate()
    {
        if (!IsSpawned || !IsOwner) return;

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        if (_groundCheck != null)
        {
            _isGrounded = Physics.CheckSphere(_groundCheck.position, checkRadius, _groundLayer);
        }

        if (!_wasGrounded && _isGrounded)
        {
            _EplayerState = EPlayerState.Idle;
            _playerAnim.SetJumpState(false, false);
        }

        _wasGrounded = _isGrounded;

        float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        Vector3 horizontalVelocity = Vector3.zero;
        float currentRotationY = _rb.rotation.eulerAngles.y;

        if (inputDirection.magnitude >= 0.1f)
        {
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cameraYaw;
            currentRotationY = Mathf.SmoothDampAngle(
                currentRotationY,
                targetAngle,
                ref turnSmoothVelocity,
                rotationSmoothTime);

            _rb.MoveRotation(Quaternion.Euler(0f, currentRotationY, 0f));

            Vector3 moveDirection = (Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward).normalized;
            Vector3 targetPosition = _rb.position + moveDirection * (moveSpeed * Time.fixedDeltaTime);
            _rb.MovePosition(targetPosition);

            horizontalVelocity = moveDirection * moveSpeed;
        }

        bool jumpedThisTick = false;
        if (_jumpRequested)
        {
            _jumpRequested = false;
            if (_isGrounded)
            {
                _rb.AddForce(Vector3.up * _JumpForce, ForceMode.Impulse);
                _isGrounded = false;
                _wasGrounded = false;
                _EplayerState = EPlayerState.Air;
                _playerAnim.SetJumpState(true, true);
                jumpedThisTick = true;
            }
        }

        _ownerPlanarSpeed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
        Vector3 simulatedVelocity = new Vector3(horizontalVelocity.x, _rb.linearVelocity.y, horizontalVelocity.z);

        if ((_networkVelocity.Value - simulatedVelocity).sqrMagnitude > 0.001f)
        {
            _networkVelocity.Value = simulatedVelocity;
        }

        int currentTick = NetworkManager.LocalTime.Tick;
        int bufferIndex = currentTick % PredictionBufferSize;

        PlayerInputPayload inputPayload = new PlayerInputPayload
        {
            Tick = currentTick,
            MoveInput = moveInput,
            CameraYaw = cameraYaw,
            JumpRequested = jumpedThisTick
        };

        PlayerStatePayload statePayload = new PlayerStatePayload
        {
            Tick = currentTick,
            Position = _rb.position,
            RotationY = currentRotationY,
            Velocity = simulatedVelocity,
            IsGrounded = _isGrounded
        };

        _inputBuffer[bufferIndex] = inputPayload;
        _stateBuffer[bufferIndex] = statePayload;

        if (!IsServer && currentTick != _lastProcessedTick)
        {
            _lastProcessedTick = currentTick;
            ValidateMovementServerRpc(inputPayload, statePayload);
        }
        else
        {
            _lastProcessedTick = currentTick;
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

    public override void OnNetworkSpawn()
    {
        _lastServerValidatedState = new PlayerStatePayload
        {
            Tick = -1,
            Position = transform.position,
            RotationY = transform.eulerAngles.y,
            Velocity = Vector3.zero,
            IsGrounded = true
        };
        _lastProcessedTick = -1;

        if (!IsOwner) return;

        _inputActions = new PlayerControls();
        _inputActions.Enable();
        _inputActions.Player.Move.performed += OnMovePerformed;
        _inputActions.Player.Move.canceled += OnMoveCanceled;
        _inputActions.Player.Attack.started += OnAttackStarted;
        _inputActions.Player.Attack.canceled += OnAttackEnd;
        _inputActions.Player.Jump.started += OnJump;
        _inputActions.Player.ChgWeapon.started += ChangeWeaponMode;

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

    public override void OnNetworkDespawn()
    {
        if (!IsOwner || _inputActions == null) return;

        _inputActions.Player.Move.performed -= OnMovePerformed;
        _inputActions.Player.Move.canceled -= OnMoveCanceled;
        _inputActions.Player.Attack.started -= OnAttackStarted;
        _inputActions.Player.Attack.canceled -= OnAttackEnd;
        _inputActions.Player.Jump.started -= OnJump;
        _inputActions.Player.ChgWeapon.started -= ChangeWeaponMode;
        _inputActions.Player.Disable();
        _inputActions.Dispose();
        _inputActions = null;
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

        int weaponIndex = Mathf.RoundToInt(context.ReadValue<float>());
        _playerAnim.SetWeaponMode((EWeaponType)weaponIndex);
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>소유자 클라이언트가 예측한 이동 상태를 서버에서 검증한다.</summary>
    [Rpc(SendTo.Server, Delivery = RpcDelivery.Unreliable)]
    private void ValidateMovementServerRpc(PlayerInputPayload inputPayload, PlayerStatePayload clientState)
    {
        if (_lastServerValidatedState.Tick < 0)
        {
            _lastServerValidatedState = clientState;
            return;
        }

        int tickDelta = clientState.Tick - _lastServerValidatedState.Tick;
        if (tickDelta <= 0) return;

        Vector3 validatedPosition = clientState.Position;
        bool needsReconcile = false;

        float elapsedTime = tickDelta * NetworkManager.ServerTime.FixedDeltaTime;
        Vector2 prevXZ = new Vector2(_lastServerValidatedState.Position.x, _lastServerValidatedState.Position.z);
        Vector2 currXZ = new Vector2(validatedPosition.x, validatedPosition.z);
        float planarDistance = Vector2.Distance(prevXZ, currXZ);
        float maxAllowedDistance = (moveSpeed * elapsedTime) + reconciliationThreshold;

        if (planarDistance > maxAllowedDistance && planarDistance > Mathf.Epsilon)
        {
            Vector2 clampedXZ = Vector2.MoveTowards(prevXZ, currXZ, moveSpeed * elapsedTime);
            validatedPosition = new Vector3(clampedXZ.x, validatedPosition.y, clampedXZ.y);
            needsReconcile = true;
        }

        Vector3 castOffset = _capsuleCollider != null
            ? Vector3.up * _capsuleCollider.center.y
            : Vector3.up * 0.18f;
        Vector3 castStart = _lastServerValidatedState.Position + castOffset;
        Vector3 castEnd = validatedPosition + castOffset;
        Vector3 castDelta = castEnd - castStart;
        float castDistance = castDelta.magnitude;

        if (castDistance > Mathf.Epsilon)
        {
            Vector3 castDir = castDelta / castDistance;
            int hitCount = Physics.RaycastNonAlloc(
                castStart,
                castDir,
                _wallHits,
                castDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);

            float closestDistance = castDistance;
            bool hitWall = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _wallHits[i];
                if (hit.collider == null || hit.collider == _capsuleCollider || hit.transform.IsChildOf(transform))
                {
                    continue;
                }

                if (hit.distance < closestDistance)
                {
                    closestDistance = hit.distance;
                    hitWall = true;
                }
            }

            if (hitWall)
            {
                float skinRadius = _capsuleCollider != null ? _capsuleCollider.radius : 0.05f;
                float safeDistance = Mathf.Max(0f, closestDistance - skinRadius);
                validatedPosition = _lastServerValidatedState.Position + castDir * safeDistance;
                needsReconcile = true;
            }
        }

        if (needsReconcile)
        {
            PlayerStatePayload correctedState = clientState;
            correctedState.Position = validatedPosition;
            _lastServerValidatedState = correctedState;

            ReconcileMovementOwnerRpc(correctedState);
            return;
        }

        _lastServerValidatedState = clientState;
    }

    /// <summary>서버 보정 상태를 소유자에게 적용하고 이후 틱의 입력을 재시뮬레이션한다.</summary>
    [Rpc(SendTo.Owner, Delivery = RpcDelivery.Reliable)]
    private void ReconcileMovementOwnerRpc(PlayerStatePayload authoritativeState)
    {
        int bufferIndex = authoritativeState.Tick % PredictionBufferSize;
        PlayerStatePayload predictedState = _stateBuffer[bufferIndex];

        if (predictedState.Tick == authoritativeState.Tick
            && Vector3.Distance(predictedState.Position, authoritativeState.Position) <= reconciliationThreshold)
        {
            return;
        }

        Vector3 reconciledPosition = authoritativeState.Position;
        float reconciledRotationY = authoritativeState.RotationY;
        _stateBuffer[bufferIndex] = authoritativeState;

        float dt = NetworkManager.LocalTime.FixedDeltaTime;
        for (int tick = authoritativeState.Tick + 1; tick <= _lastProcessedTick; tick++)
        {
            int idx = tick % PredictionBufferSize;
            PlayerInputPayload input = _inputBuffer[idx];
            if (input.Tick != tick) continue;

            Vector3 dir = new Vector3(input.MoveInput.x, 0f, input.MoveInput.y).normalized;
            Vector3 horizVel = Vector3.zero;
            if (dir.magnitude >= 0.1f)
            {
                float targetAngle = Mathf.Atan2(dir.x, dir.z) * Mathf.Rad2Deg + input.CameraYaw;
                reconciledRotationY = targetAngle;
                Vector3 moveDir = (Quaternion.Euler(0f, targetAngle, 0f) * Vector3.forward).normalized;
                reconciledPosition += moveDir * (moveSpeed * dt);
                horizVel = moveDir * moveSpeed;
            }

            PlayerStatePayload replayedState = _stateBuffer[idx];
            replayedState.Tick = tick;
            replayedState.Position = reconciledPosition;
            replayedState.RotationY = reconciledRotationY;
            replayedState.Velocity = new Vector3(horizVel.x, replayedState.Velocity.y, horizVel.z);
            _stateBuffer[idx] = replayedState;
        }

        _rb.position = reconciledPosition;
        _rb.rotation = Quaternion.Euler(0f, reconciledRotationY, 0f);
    }
}
