using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Transporting;
using UnityEngine;

/// <summary>프롭 동작 상태.</summary>
public enum EPlayerState
{
    Idle,
    Attack,
    Air,
    Die
}

/// <summary>FishNet CSP 기반 <see cref="Rigidbody"/> 이동, 예측/재조정 및 래그돌 대기 상태를 처리하는 프롭 베이스.</summary>
[RequireComponent(typeof(Rigidbody))]
public abstract class PropControllerBase : NetworkBehaviour, IPropEntity
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이 프롭의 이동, 물리 및 애니메이션 클립 설정 데이터.</summary>
    [Header("프롭 데이터")]
    [SerializeField] protected PropDataSO propData;

    /// <summary>발밑 바닥 체크 구체를 캐스팅하는 기준점.</summary>
    [Header("바닥 체크")]
    [SerializeField] private Transform _groundCheck;

    /// <summary>직전 물리 틱의 바닥 접촉 여부.</summary>
    public bool _wasGrounded;

    /// <summary>현재 물리 틱의 바닥 접촉 여부.</summary>
    public bool _isGrounded;

    /// <summary>프롭 물리 <see cref="Rigidbody"/> 컴포넌트.</summary>
    protected Rigidbody _rb;

    /// <summary>래그돌 상태 바닥 체크에 사용하는 프롭 콜라이더.</summary>
    protected Collider _bodyCollider;

    /// <summary>애니메이션 제어 인터페이스 참조.</summary>
    protected IPropAnimController _propAnim;

    /// <summary>현재 프롭 동작 상태.</summary>
    protected EPlayerState _EplayerState;

    /// <summary>현재 루트 <see cref="Rigidbody"/> 회전 제약이 해제된 맨손 쓰러짐 대기 상태 여부.</summary>
    protected bool _isRagdollIdle;

    /// <summary>현재 시뮬레이션된 평면 이동 속력 (초당 월드 단위).</summary>
    private float _currentPlanarSpeed;

    /// <summary><see cref="Mathf.SmoothDampAngle"/> 내부 회전 각속도 상태값.</summary>
    private float _turnSmoothVelocity;

    private const float DefaultMoveSpeed = 5f;
    private const float DefaultRotationSmoothTime = 0.1f;
    private const float DefaultJumpForce = 12f;
    private const float DefaultPushForce = 2.5f;
    private const float DefaultRagdollToppleSpeed = 3.5f;
    private const float DefaultDownwardForce = 0.5f;
    private const float DefaultCheckRadius = 0.2f;
    private const int DefaultGroundLayerMask = 1 << 3;
    private const float GroundedMaxRiseSpeed = 0.1f; // 바닥으로 판정하는 최대 상승 속도 (m/s)

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public EPropType PropType => propData != null ? propData.PropType : EPropType.Banana;

    /// <inheritdoc/>
    public PropDataSO PropData => propData;

    /// <inheritdoc/>
    public float PlanarSpeedRatio
    {
        get
        {
            float maxSpeed = MoveSpeed;
            if (maxSpeed <= 0f) return 0f;
            return Mathf.Clamp01(_currentPlanarSpeed / maxSpeed);
        }
    }

    /// <inheritdoc/>
    public bool IsGrounded => _isGrounded;

    /// <inheritdoc/>
    public bool IsRagdollIdle => _isRagdollIdle;

    /// <summary>현재 프롭의 평면 이동 속력 (초당 월드 단위).</summary>
    protected float MoveSpeed => propData != null ? propData.MoveSpeed : DefaultMoveSpeed;

    /// <summary>현재 프롭의 회전 감쇠 시간 (초 단위).</summary>
    protected float RotationSmoothTime => propData != null ? propData.RotationSmoothTime : DefaultRotationSmoothTime;

    /// <summary>현재 프롭의 점프 수직 임펄스 크기.</summary>
    protected float JumpForce => propData != null ? propData.JumpForce : DefaultJumpForce;

    /// <summary>현재 프롭의 쓰러짐 수평 임펄스 크기.</summary>
    protected float PushForce => propData != null ? propData.PushForce : DefaultPushForce;

    /// <summary>현재 프롭의 쓰러짐 각속도 토크 크기.</summary>
    protected float RagdollToppleSpeed => propData != null ? propData.RagdollToppleSpeed : DefaultRagdollToppleSpeed;

    /// <summary>현재 프롭의 쓰러짐 하향 힘 크기.</summary>
    protected float DownwardForce => propData != null ? propData.DownwardForce : DefaultDownwardForce;

    /// <summary>바닥 체크 구체 반지름 (월드 단위).</summary>
    protected float CheckRadius => propData != null ? propData.GroundCheckRadius : DefaultCheckRadius;

    /// <summary>바닥으로 판정할 물리 레이어 마스크.</summary>
    protected LayerMask GroundLayer => propData != null ? propData.GroundLayer : DefaultGroundLayerMask;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected virtual void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null && propData != null)
        {
            _rb.mass = propData.RigidbodyMass;
            _rb.linearDamping = propData.RigidbodyLinearDamping;
            _rb.angularDamping = propData.RigidbodyAngularDamping;
        }

        _bodyCollider = GetComponentInChildren<Collider>();
        _propAnim = GetComponent<IPropAnimController>();
        _EplayerState = EPlayerState.Idle;

        if (_groundCheck == null)
        {
            _groundCheck = transform.Find("GroundCheck");
        }

        OnPropAwake();
    }

    private void OnDrawGizmosSelected()
    {
        if (_groundCheck == null) return;
        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(_groundCheck.position, CheckRadius);
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
            // base.TimeManager.OnPrePhysicsSimulation += OnPrePhysicsSimulation;
        }
    }

    public override void OnStopNetwork()
    {
        if (base.TimeManager != null)
        {
            base.TimeManager.OnTick -= TimeManager_OnTick;
            base.TimeManager.OnPostTick -= TimeManager_OnPostTick;
            // base.TimeManager.OnPrePhysicsSimulation -= OnPrePhysicsSimulation;
        }
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
            _turnSmoothVelocity);
        PerformReconcile(rd);
    }

    // ***************************************************************
    // Extension Hooks
    // ***************************************************************

    /// <summary>네트워크 틱마다 소유자가 전송할 입력 데이터를 수집한다.</summary>
    protected virtual PlayerInputData CollectTickInput() => default;

    /// <summary>기본 컴포넌트 캐싱 이후 하위 클래스 초기화를 수행한다.</summary>
    protected virtual void OnPropAwake() { }

    /// <summary>공중에서 바닥에 착지한 틱에 호출된다.</summary>
    protected virtual void OnLanded() { }

    /// <summary>점프 임펄스를 적용한 틱에 호출된다.</summary>
    protected virtual void OnJumped() { }

    /// <summary>맨손 정지 쓰러짐 대기 상태 진입 가능 여부를 판정한다.</summary>
    protected virtual bool CanEnterRagdollIdle(in PlayerInputData inputData, Vector3 inputDirection)
    {
        return _propAnim != null
            && _propAnim.CurrentWeaponType == EWeaponType.Hide
            && inputDirection.magnitude < 0.1f
            && (_isGrounded || _isRagdollIdle)
            && !inputData.JumpRequested;
    }

    /// <summary>쓰러짐 대기 상태가 전환될 때 호출된다.</summary>
    protected virtual void OnRagdollStateChanged(bool isRagdoll) { }

    // ***************************************************************
    // Tick & Prediction
    // ***************************************************************

    private void OnPrePhysicsSimulation(float delta)
    {
        PlayerInputData inputData = IsOwner ? CollectTickInput() : default;
        PerformReplicate(inputData);
    }

    /// <summary>네트워크 틱마다 입력을 수집해 예측 시뮬레이션을 실행한다.</summary>
    private void TimeManager_OnTick()
    {
        PlayerInputData inputData = IsOwner ? CollectTickInput() : default;
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
            _isGrounded = Physics.CheckSphere(_groundCheck.position, CheckRadius, GroundLayer);
            if (!_isGrounded && _isRagdollIdle && _bodyCollider != null)
            {
                Bounds bounds = _bodyCollider.bounds;
                Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
                _isGrounded = Physics.CheckSphere(bottomCenter, CheckRadius, GroundLayer);
            }

            // 점프 직후 몇 틱은 체크 구체가 아직 바닥에 닿아 있으므로 상승 중에는 착지로 보지 않음
            _isGrounded &= _rb.linearVelocity.y <= GroundedMaxRiseSpeed;
        }

        if (!_wasGrounded && _isGrounded)
        {
            _EplayerState = EPlayerState.Idle;
            _propAnim?.SetJumpState(false, false);
            OnLanded();
        }

        _wasGrounded = _isGrounded;

        Vector3 inputDirection = new Vector3(rd.MoveInput.x, 0f, rd.MoveInput.y).normalized;
        Vector3 horizontalVelocity = Vector3.zero;
        float currentRotationY = _rb.rotation.eulerAngles.y;
        bool shouldRagdoll = CanEnterRagdollIdle(rd, inputDirection);

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
                Vector3 finalForce = (pushDirection * PushForce) + (Vector3.down * DownwardForce);

                _rb.AddForce(finalForce, ForceMode.Impulse);
                _rb.AddTorque((_rb.rotation * Vector3.right) * RagdollToppleSpeed);
                OnRagdollStateChanged(true);
            }
        }
        else if (_isRagdollIdle)
        {
            _isRagdollIdle = false;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _rb.angularVelocity = Vector3.zero;
            _rb.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            OnRagdollStateChanged(false);
        }

        if (inputDirection.magnitude >= 0.1f)
        {
            float moveSpeed = MoveSpeed;
            float targetAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + rd.CameraYaw;
            currentRotationY = Mathf.SmoothDampAngle(
                currentRotationY,
                targetAngle,
                ref _turnSmoothVelocity,
                RotationSmoothTime,
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
            _rb.AddForce(Vector3.up * JumpForce, ForceMode.Impulse);
            _isGrounded = false;
            _wasGrounded = false;
            _EplayerState = EPlayerState.Air;
            _propAnim?.SetJumpState(true, true);
            OnJumped();
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
        _turnSmoothVelocity = rd.TurnSmoothVelocity;
        _currentPlanarSpeed = rd.PlanarSpeed;
    }
}
