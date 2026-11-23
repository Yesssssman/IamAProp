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

    /// <summary>총기 피격 사망 시 피격 방향으로 가하는 넉백 임펄스 크기.</summary>
    [Header("사망 래그돌 설정")]
    [Min(0f)]
    [SerializeField] private float gunDeathForce = 20f;

    /// <summary>근접 무기 피격 사망 시 타격 방향으로 가하는 넉백 임펄스 크기.</summary>
    [Min(0f)]
    [SerializeField] private float meleeDeathForce = 15f;

    /// <summary>사망 시 위쪽으로 띄우는 상방 임펄스 크기.</summary>
    [Min(0f)]
    [SerializeField] private float deathUpwardForce = 6f;

    /// <summary>피격 지점이 없을 때 시체가 굴러가도록 가하는 회전 토크 크기.</summary>
    [Min(0f)]
    [SerializeField] private float deathTorque = 6f;

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

    /// <summary>체력 및 사망 상태를 제공하는 컴포넌트.</summary>
    protected PlayerHealth _playerHealth;

    /// <summary>현재 프롭 동작 상태.</summary>
    protected EPlayerState _EplayerState;

    /// <summary>현재 루트 <see cref="Rigidbody"/> 회전 제약이 해제된 맨손 쓰러짐 대기 상태 여부.</summary>
    protected bool _isRagdollIdle;

    /// <summary>현재 사망 래그돌 상태가 활성화되었는지 여부.</summary>
    protected bool _isDead;

    /// <summary>원격 클라이언트에 동기화되는 목표 상하 조준 각도 (도 단위).</summary>
    private readonly SyncVar<float> _syncedAimPitch = new();

    /// <summary>이번 사망에서 피격 넉백 임펄스를 이미 적용했는지 여부.</summary>
    private bool _hasAppliedDeathImpulse;

    /// <summary>이번 사망 이후 서버의 사망 Reconcile 상태를 최소 1회 수신했는지 여부.</summary>
    private bool _hasReceivedDeadReconcile;

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

    /// <summary>직전 시뮬레이션 틱 시작 시점의 <see cref="Rigidbody"/> 월드 위치.</summary>
    private Vector3 _lastTickPosition;

    private const float DefaultMoveSpeed = 5f;
    private const float DefaultRotationSmoothTime = 0.1f;
    private const float DefaultJumpForce = 12f;
    private const float DefaultDirectionalJumpForce = 6f;
    private const float DefaultDirectionalJumpThresholdSqr = 0.0001f;
    private const float DefaultPushForce = 2.5f;
    private const float DefaultStationaryPushMultiplier = 0.5f;
    private const float DefaultMovingPushMultiplier = 1f;
    private const float DefaultRagdollToppleSpeed = 3.5f;
    private const float DefaultDownwardForce = 0.5f;
    private const float DefaultCheckRadius = 0.2f;
    private const int DefaultGroundLayerMask = 1 << 3;
    private const float GroundedMaxRiseSpeed = 0.1f; // 바닥으로 판정하는 최대 상승 속도 (m/s)
    private const float RagdollMomentumThresholdSqr = 0.01f; // 쓰러짐 시 이동 관성으로 인정할 최소 수평 속력 제곱
    private const float MinMomentumPushScale = 0.5f; // 관성 쓰러짐 시 최소 힘 배율
    private const float MaxMomentumPushScale = 1.5f; // 관성 쓰러짐 시 최대 힘 배율

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

    /// <inheritdoc/>
    public bool IsDead => _isDead || (_playerHealth != null && _playerHealth.IsDead);

    /// <summary>상하 조준 최소 피치 각도 (도 단위).</summary>
    protected float MinAimPitch => minAimPitch;

    /// <summary>상하 조준 최대 피치 각도 (도 단위).</summary>
    protected float MaxAimPitch => maxAimPitch;

    /// <summary>현재 프롭의 회전 감쇠 시간 (초 단위).</summary>
    protected float RotationSmoothTime => propData != null ? propData.RotationSmoothTime : DefaultRotationSmoothTime;

    /// <summary>현재 프롭의 점프 수직 임펄스 크기.</summary>
    protected float JumpForce => propData != null ? propData.JumpForce : DefaultJumpForce;

    /// <summary>이동 중 점프 시 이동 방향으로 가하는 수평 도약 임펄스 크기.</summary>
    protected float DirectionalJumpForce => propData != null ? propData.DirectionalJumpForce : DefaultDirectionalJumpForce;

    /// <summary>이동 중 점프로 판정할 틱 간 수평 변위 제곱 임계값 (제곱 미터).</summary>
    protected float DirectionalJumpThresholdSqr => propData != null ? propData.DirectionalJumpThresholdSqr : DefaultDirectionalJumpThresholdSqr;

    /// <summary>현재 프롭의 쓰러짐 수평 임펄스 크기.</summary>
    protected float PushForce => propData != null ? propData.PushForce : DefaultPushForce;

    /// <summary>정지 상태 쓰러짐 시 <see cref="PushForce"/>에 곱하는 배율.</summary>
    protected float StationaryPushMultiplier => propData != null ? propData.StationaryPushMultiplier : DefaultStationaryPushMultiplier;

    /// <summary>이동 중 쓰러짐 시 <see cref="PushForce"/>에 곱하는 배율.</summary>
    protected float MovingPushMultiplier => propData != null ? propData.MovingPushMultiplier : DefaultMovingPushMultiplier;

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
        if (_rb != null)
        {
            if (propData != null)
            {
                _rb.mass = propData.RigidbodyMass;
                _rb.linearDamping = propData.RigidbodyLinearDamping;
                _rb.angularDamping = propData.RigidbodyAngularDamping;
            }
            _lastTickPosition = _rb.position;
        }

        _bodyCollider = GetComponentInChildren<Collider>();
        _propAnim = GetComponent<IPropAnimController>();
        _playerHealth = GetComponent<PlayerHealth>();
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

        if (IsDead)
        {
            _currentAimPitch = 0f;
            if (aimPitchTarget != null)
            {
                aimPitchTarget.localRotation = _aimPitchInitialLocalRotation;
            }
            return;
        }

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
        if (_rb != null)
        {
            _lastTickPosition = _rb.position;
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnDeathEffectsTriggered += HandleDeathEffectsTriggered;
            _playerHealth.OnDeadStateChanged += HandleDeadStateChanged;
            if (_playerHealth.IsDead)
            {
                EnterDeathRagdoll(default, applyImpulse: false);
            }
        }

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
        _isDead = false;
        _hasAppliedDeathImpulse = false;
        _hasReceivedDeadReconcile = false;

        if (_playerHealth != null)
        {
            _playerHealth.OnDeathEffectsTriggered -= HandleDeathEffectsTriggered;
            _playerHealth.OnDeadStateChanged -= HandleDeadStateChanged;
        }

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
            IsDead,
            _isFiring,
            _turnSmoothVelocity,
            _lastTickPosition);
        PerformReconcile(rd);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>사망 즉시 HideMode 래그돌로 전환하고 필요 시 피격 방향 넉백 임펄스를 적용한다.</summary>
    public void EnterDeathRagdoll(in DamageHitInfo hitInfo, bool applyImpulse)
    {
        bool wasDead = _isDead;
        _isDead = true;
        _isRagdollIdle = true;
        _EplayerState = EPlayerState.Die;
        _isFiring = false;
        _planarVelocity = Vector3.zero;
        _currentPlanarSpeed = 0f;
        _turnSmoothVelocity = 0f;
        _currentAimPitch = 0f;
        _lastSentAimPitch = 0f;

        if (aimPitchTarget != null)
        {
            aimPitchTarget.localRotation = _aimPitchInitialLocalRotation;
        }

        _propAnim?.EnterDeathHidePose();

        if (_rb != null)
        {
            _rb.useGravity = true;
            _rb.constraints = RigidbodyConstraints.None;
        }

        if (!wasDead)
        {
            OnRagdollStateChanged(true);
            OnDeathEntered();
        }

        if (!applyImpulse || _hasAppliedDeathImpulse || _hasReceivedDeadReconcile || _rb == null || _rb.isKinematic)
        {
            return;
        }

        _hasAppliedDeathImpulse = true;

        Vector3 hitDir = hitInfo.HitDirection.sqrMagnitude > 0.0001f
            ? hitInfo.HitDirection.normalized
            : -transform.forward;
        // 큰 프롭이 작은 프롭을 내려다보며 공격할 때 하향 성분이 상방 힘을 상쇄해 바닥에 박히지 않도록 보정한다.
        if (hitDir.y < 0f)
        {
            Vector3 horizontalHitDir = new Vector3(hitDir.x, 0f, hitDir.z);
            if (horizontalHitDir.sqrMagnitude > 0.0001f)
            {
                hitDir = horizontalHitDir.normalized;
            }
        }

        float knockbackForce = hitInfo.IsMelee ? meleeDeathForce : gunDeathForce;
        Vector3 impulse = hitDir * knockbackForce + Vector3.up * deathUpwardForce;

        if (hitInfo.HasHitPoint)
        {
            _rb.AddForceAtPosition(impulse, hitInfo.HitPoint, ForceMode.Impulse);
        }
        else
        {
            _rb.AddForce(impulse, ForceMode.Impulse);
            if (deathTorque > 0f)
            {
                Vector3 horizontalDir = new Vector3(hitDir.x, 0f, hitDir.z);
                Vector3 torqueAxis = horizontalDir.sqrMagnitude > 0.0001f
                    ? Vector3.Cross(Vector3.up, horizontalDir.normalized)
                    : transform.right;
                _rb.AddTorque(torqueAxis * deathTorque, ForceMode.Impulse);
            }
        }
    }

    /// <summary>리스폰 또는 라운드 초기화 시 사망 래그돌과 물리 제약을 정상 상태로 원복한다.</summary>
    public void ResetDeathState()
    {
        _isDead = false;
        _isRagdollIdle = false;
        _hasAppliedDeathImpulse = false;
        _hasReceivedDeadReconcile = false;
        _EplayerState = EPlayerState.Idle;
        _isFiring = false;
        _planarVelocity = Vector3.zero;
        _currentPlanarSpeed = 0f;
        _turnSmoothVelocity = 0f;
        _currentAimPitch = 0f;
        _lastSentAimPitch = 0f;

        if (_rb != null)
        {
            float currentYaw = _rb.rotation.eulerAngles.y;
            _rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            if (!_rb.isKinematic)
            {
                _rb.linearVelocity = Vector3.zero;
                _rb.angularVelocity = Vector3.zero;
            }
            Quaternion uprightRotation = Quaternion.Euler(0f, currentYaw, 0f);
            transform.rotation = uprightRotation;
            _rb.rotation = uprightRotation;
            _lastTickPosition = _rb.position;
        }

        _propAnim?.ExitDeathHidePose();
        OnRagdollStateChanged(false);
        OnDeathReset();
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

    /// <summary>사망 상태에 최초 진입할 때 호출된다.</summary>
    protected virtual void OnDeathEntered() { }

    /// <summary>사망 상태가 해제되어 원복될 때 호출된다.</summary>
    protected virtual void OnDeathReset() { }

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

        Vector3 currentTickPosition = _rb.position;

        if (IsDead)
        {
            _isDead = true;
            _isRagdollIdle = true;
            _EplayerState = EPlayerState.Die;
            _isFiring = false;
            _planarVelocity = Vector3.zero;
            _currentPlanarSpeed = 0f;
            _turnSmoothVelocity = 0f;
            if (_rb.constraints != RigidbodyConstraints.None)
            {
                _rb.constraints = RigidbodyConstraints.None;
            }
            _lastTickPosition = currentTickPosition;
            return;
        }

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

                Vector3 horizontalMomentum = new Vector3(_planarVelocity.x, 0f, _planarVelocity.z);
                if (horizontalMomentum.sqrMagnitude < RagdollMomentumThresholdSqr)
                {
                    horizontalMomentum = new Vector3(_rb.linearVelocity.x, 0f, _rb.linearVelocity.z);
                }

                Vector3 pushDirection;
                float pushMagnitude;

                if (horizontalMomentum.sqrMagnitude >= RagdollMomentumThresholdSqr)
                {
                    float momentumSpeed = horizontalMomentum.magnitude;
                    pushDirection = horizontalMomentum / momentumSpeed;
                    float speedScale = MoveSpeed > 0f
                        ? Mathf.Clamp(momentumSpeed / MoveSpeed, MinMomentumPushScale, MaxMomentumPushScale)
                        : 1f;
                    pushMagnitude = PushForce * MovingPushMultiplier * speedScale;
                }
                else
                {
                    pushDirection = Quaternion.Euler(0f, currentRotationY, 0f) * Vector3.forward;
                    pushMagnitude = PushForce * StationaryPushMultiplier;
                }

                Vector3 finalForce = (pushDirection * pushMagnitude) + (Vector3.down * DownwardForce);
                Vector3 torqueAxis = Vector3.Cross(Vector3.up, pushDirection);

                if (!_rb.isKinematic)
                {
                    _rb.AddForce(finalForce, ForceMode.Impulse);
                    _rb.AddTorque(torqueAxis * RagdollToppleSpeed);
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
            Vector3 jumpImpulse = Vector3.up * JumpForce;
            Vector3 tickDisplacement = currentTickPosition - _lastTickPosition;
            tickDisplacement.y = 0f;
            if (tickDisplacement.sqrMagnitude > DirectionalJumpThresholdSqr && DirectionalJumpForce > 0f)
            {
                jumpImpulse += tickDisplacement.normalized * DirectionalJumpForce;
            }

            _rb.AddForce(jumpImpulse, ForceMode.Impulse);
            _isGrounded = false;
            _wasGrounded = false;
            _EplayerState = EPlayerState.Air;
            _propAnim?.SetJumpState(true, true);
            OnJumped();
        }

        _planarVelocity = horizontalVelocity;
        _currentPlanarSpeed = new Vector2(horizontalVelocity.x, horizontalVelocity.z).magnitude;
        _lastTickPosition = currentTickPosition;
    }

    /// <summary>서버에서 전송된 권위 상태 <paramref name="rd"/>로 <see cref="Rigidbody"/>와 이동 상태를 강제 덮어쓴다.</summary>
    [Reconcile]
    private void PerformReconcile(PlayerReconcileData rd, Channel channel = Channel.Unreliable)
    {
        if (_rb == null) return;

        // 사망 직후 지연 도착한 사망 이전 틱의 Reconcile은 무시하여 자세 롤백과 떨림을 방지한다.
        if (IsDead && !rd.IsDead) return;

        _isDead = rd.IsDead;
        if (_isDead)
        {
            _hasReceivedDeadReconcile = true;
            _EplayerState = EPlayerState.Die;
        }

        _isRagdollIdle = rd.IsRagdollIdle || _isDead;
        _rb.constraints = (_isDead || _isRagdollIdle)
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
        _isFiring = !_isDead && rd.IsFiring;
        _turnSmoothVelocity = rd.TurnSmoothVelocity;
        _planarVelocity = _isDead ? Vector3.zero : rd.PlanarVelocity;
        _currentPlanarSpeed = _isDead ? 0f : rd.PlanarSpeed;
        _lastTickPosition = rd.LastTickPosition;
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>소유자 클라이언트의 목표 상하 조준 각도를 서버 <see cref="_syncedAimPitch"/>에 반영한다.</summary>
    [ServerRpc]
    private void SetAimPitchServerRpc(float pitch, Channel channel = Channel.Unreliable)
    {
        if (IsDead) return;
        _syncedAimPitch.Value = Mathf.Clamp(pitch, minAimPitch, maxAimPitch);
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>사망 연출 이벤트 수신 시 HideMode 래그돌과 피격 방향 임펄스를 적용한다.</summary>
    private void HandleDeathEffectsTriggered(DamageHitInfo hitInfo)
    {
        EnterDeathRagdoll(in hitInfo, applyImpulse: true);
    }

    /// <summary><see cref="PlayerHealth"/>의 사망 상태 동기화 변경에 따라 래그돌 진입 또는 원복을 처리한다.</summary>
    private void HandleDeadStateChanged(bool isDead)
    {
        if (isDead)
        {
            EnterDeathRagdoll(default, applyImpulse: false);
        }
        else
        {
            ResetDeathState();
        }
    }
}
