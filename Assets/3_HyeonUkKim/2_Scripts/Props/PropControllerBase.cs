using FishNet.Object;
using FishNet.Object.Prediction;
using FishNet.Object.Synchronizing;
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

    /// <summary>1D 상하 조준(Pitch) X축 회전을 적용할 피벗 트랜스폼.</summary>
    [Header("상하 조준 (Pitch)")]
    [SerializeField] protected Transform aimPitchTarget;

    /// <summary>상하 조준 시 허용하는 최소(위쪽) 피치 각도 (도 단위).</summary>
    [SerializeField] private float minAimPitch = -60f;

    /// <summary>상하 조준 시 허용하는 최대(아래쪽) 피치 각도 (도 단위).</summary>
    [SerializeField] private float maxAimPitch = 60f;

    /// <summary>상하 조준 피치 각도 보간 속력.</summary>
    [SerializeField] private float aimPitchLerpSpeed = 20f;

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

    /// <summary>원격 클라이언트에 동기화되는 목표 상하 조준 각도 (도 단위).</summary>
    private readonly SyncVar<float> _syncedAimPitch = new();

    /// <summary>현재 시뮬레이션된 월드 공간 평면 이동 속도 벡터 (초당 월드 단위).</summary>
    private Vector3 _planarVelocity;

    /// <summary>현재 시뮬레이션된 평면 이동 속력 (초당 월드 단위).</summary>
    private float _currentPlanarSpeed;

    /// <summary>현재 물리 틱의 사격(조준) 상태 활성화 여부.</summary>
    private bool _isFiring;

    /// <summary><see cref="Mathf.SmoothDampAngle"/> 내부 회전 각속도 상태값.</summary>
    private float _turnSmoothVelocity;

    /// <summary>현재 프레임에 보간 적용된 상하 조준 각도 (도 단위).</summary>
    private float _currentAimPitch;

    /// <summary>서버에 마지막으로 전송한 상하 조준 각도 (도 단위).</summary>
    private float _lastSentAimPitch;

    /// <summary><see cref="aimPitchTarget"/>의 초기 로컬 회전값.</summary>
    private Quaternion _aimPitchInitialLocalRotation = Quaternion.identity;

    /// <summary>원격 클라이언트가 미래 틱 예측에 재사용할 직전 수신 입력 데이터.</summary>
    private PlayerInputData _lastCreatedInput;

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
    public Vector3 PlanarVelocity => _planarVelocity;

    /// <inheritdoc/>
    public float MoveSpeed => propData != null ? propData.MoveSpeed : DefaultMoveSpeed;

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

    /// <inheritdoc/>
    public bool IsFiring => _isFiring;

    /// <summary>상하 조준 최소 피치 각도 (도 단위).</summary>
    protected float MinAimPitch => minAimPitch;

    /// <summary>상하 조준 최대 피치 각도 (도 단위).</summary>
    protected float MaxAimPitch => maxAimPitch;

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

        if (aimPitchTarget == null)
        {
            HandScript hand = GetComponentInChildren<HandScript>(true);
            if (hand != null)
            {
                aimPitchTarget = hand.transform.parent != null && hand.transform.parent.name == "AimPivot"
                    ? hand.transform.parent
                    : hand.transform;
            }
        }

        if (aimPitchTarget != null)
        {
            _aimPitchInitialLocalRotation = aimPitchTarget.localRotation;
        }

        OnPropAwake();
    }

    protected virtual void LateUpdate()
    {
        if (!IsSpawned) return;

        float targetPitch;
        if (IsOwner)
        {
            targetPitch = Mathf.Clamp(ComputeOwnerTargetAimPitch(), minAimPitch, maxAimPitch);
            if (Mathf.Abs(targetPitch - _lastSentAimPitch) >= 0.1f
                || (Mathf.Approximately(targetPitch, 0f) && !Mathf.Approximately(_lastSentAimPitch, 0f)))
            {
                _lastSentAimPitch = targetPitch;
                if (IsServerInitialized)
                {
                    _syncedAimPitch.Value = targetPitch;
                }
                else
                {
                    SetAimPitchServerRpc(targetPitch);
                }
            }
        }
        else
        {
            targetPitch = _syncedAimPitch.Value;
        }

        _currentAimPitch = Mathf.Lerp(_currentAimPitch, targetPitch, Time.deltaTime * aimPitchLerpSpeed);
        if (aimPitchTarget != null)
        {
            aimPitchTarget.localRotation = _aimPitchInitialLocalRotation * Quaternion.Euler(_currentAimPitch, 0f, 0f);
        }
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
        _lastCreatedInput = default;
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
            _planarVelocity,
            _currentPlanarSpeed,
            _isGrounded,
            _wasGrounded,
            _isRagdollIdle,
            _isFiring,
            _turnSmoothVelocity);
        PerformReconcile(rd);
    }

    // ***************************************************************
    // Extension Hooks
    // ***************************************************************

    /// <summary>네트워크 틱마다 소유자가 전송할 입력 데이터를 수집한다.</summary>
    protected virtual PlayerInputData CollectTickInput() => default;

    /// <summary>소유자 클라이언트의 현재 프레임 목표 상하 조준 각도(도 단위)를 계산한다.</summary>
    protected virtual float ComputeOwnerTargetAimPitch() => 0f;

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

        if (!IsServerStarted && !IsOwner)
        {
            if (state.ContainsCreated())
            {
                _lastCreatedInput = rd;
            }
            else if (state.IsFuture()
                && _lastCreatedInput.GetTick() != 0
                && rd.GetTick() > _lastCreatedInput.GetTick()
                && rd.GetTick() - _lastCreatedInput.GetTick() <= 1)
            {
                uint currentTick = rd.GetTick();
                rd = _lastCreatedInput;
                rd.JumpRequested = false;
                rd.SetTick(currentTick);
            }
        }

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
        bool isMoving = inputDirection.magnitude >= 0.1f;
        bool isFiring = rd.IsFiring && (_propAnim == null || _propAnim.CurrentWeaponType != EWeaponType.Hide);
        _isFiring = isFiring;
        Vector3 horizontalVelocity = Vector3.zero;
        float currentRotationY = _rb.rotation.eulerAngles.y;
        bool shouldRagdoll = state.ContainsCreated()
            ? CanEnterRagdollIdle(rd, inputDirection)
            : _isRagdollIdle;

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

                if (!_rb.isKinematic)
                {
                    _rb.AddForce(finalForce, ForceMode.Impulse);
                    _rb.AddTorque((_rb.rotation * Vector3.right) * RagdollToppleSpeed);
                }
                OnRagdollStateChanged(true);
            }
        }
        else if (_isRagdollIdle)
        {
            _isRagdollIdle = false;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (!_rb.isKinematic)
            {
                _rb.angularVelocity = Vector3.zero;
            }
            _rb.rotation = Quaternion.Euler(0f, currentRotationY, 0f);
            OnRagdollStateChanged(false);
        }

        float moveAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + rd.CameraYaw;

        if (isFiring || isMoving)
        {
            float targetAngle = isFiring ? rd.AimYaw : moveAngle;
            currentRotationY = Mathf.SmoothDampAngle(
                currentRotationY,
                targetAngle,
                ref _turnSmoothVelocity,
                RotationSmoothTime,
                Mathf.Infinity,
                delta);

            _rb.MoveRotation(Quaternion.Euler(0f, currentRotationY, 0f));
        }

        if (isMoving)
        {
            float moveSpeed = MoveSpeed;
            Vector3 moveDirection = (Quaternion.Euler(0f, moveAngle, 0f) * Vector3.forward).normalized;
            Vector3 targetPosition = _rb.position + moveDirection * (moveSpeed * delta);
            _rb.MovePosition(targetPosition);

            horizontalVelocity = moveDirection * moveSpeed;
        }

        if (rd.JumpRequested && _isGrounded && !_rb.isKinematic)
        {
            _rb.AddForce(Vector3.up * JumpForce, ForceMode.Impulse);
            _isGrounded = false;
            _wasGrounded = false;
            _EplayerState = EPlayerState.Air;
            _propAnim?.SetJumpState(true, true);
            OnJumped();
        }

        _planarVelocity = horizontalVelocity;
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
        _isFiring = rd.IsFiring;
        _turnSmoothVelocity = rd.TurnSmoothVelocity;
        _planarVelocity = rd.PlanarVelocity;
        _currentPlanarSpeed = rd.PlanarSpeed;
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>소유자 클라이언트의 목표 상하 조준 각도를 서버 <see cref="_syncedAimPitch"/>에 반영한다.</summary>
    [ServerRpc]
    private void SetAimPitchServerRpc(float pitch, Channel channel = Channel.Unreliable)
    {
        _syncedAimPitch.Value = Mathf.Clamp(pitch, minAimPitch, maxAimPitch);
    }
}
