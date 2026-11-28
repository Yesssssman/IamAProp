using System;
using FishNet.Component.Transforming.Beta;
using FishNet.Object;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>로컬 플레이어 입력과 카메라 추적 기준을 <see cref="PropControllerBase"/>에 연결한다.</summary>
[DefaultExecutionOrder(-100)]
[RequireComponent(typeof(PlayerAnim))]
public class PlayerController : PropControllerBase
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float DefaultTpsViewOffsetY = 0.45f; // TpsViewPos가 없을 때 사용할 기본 추적 높이
    private const int DefaultWallRetractLayerMask = (1 << 0) | (1 << 3) | (1 << 8); // Default, Ground, Wall
    private const float InteractRayRadius = 0.25f; // 문/창문 크로스헤어 탐색 구체 반지름 (미터)
    private const float InteractCameraRangeBonus = 4.5f; // 3인칭 카메라와 캐릭터 사이 거리 보정 (미터)
    private const float InteractFallbackRadius = 3.2f; // 크로스헤어 미적중 시 주변 근접 탐색 반경 (미터)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이동 기준 방향을 계산할 카메라 트랜스폼.</summary>
    [Header("참조")]
    [SerializeField] private Transform cameraTransform;

    /// <summary>카메라 추적 기준 높이를 결정하는 트랜스폼.</summary>
    [SerializeField] private Transform tpsViewPos;

    /// <summary><see cref="NetworkTickSmoother"/>로 보간되는 시각 루트 트랜스폼.</summary>
    [SerializeField] private Transform visualRoot;

    /// <summary>크로스헤어 조준 레이캐스트에 포함할 물리 레이어 마스크.</summary>
    [Header("크로스헤어 조준")]
    [SerializeField] private LayerMask aimLayerMask = ~0;

    /// <summary>크로스헤어 레이캐스트 미충돌 시 사용할 카메라 전방 기본 조준 거리 (미터).</summary>
    [SerializeField] private float defaultAimDistance = 100f;

    /// <summary>벽 근접 시 총기 후퇴(Wall-Retract) 기능 활성화 여부.</summary>
    [Header("벽 근접 총기 후퇴 (Wall-Retract)")]
    [SerializeField] private bool enableWallRetract = true;

    /// <summary>총기 후퇴 감지에 포함할 벽/지형 레이어 마스크.</summary>
    [SerializeField] private LayerMask wallRetractLayerMask = DefaultWallRetractLayerMask;

    /// <summary>벽 근접 감지 구체 캐스트 반지름 (미터).</summary>
    [Min(0.01f)]
    [SerializeField] private float wallRetractCheckRadius = 0.04f;

    /// <summary>총구와 벽 사이에 유지할 최소 여유 간격 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float wallRetractClearance = 0.08f;

    /// <summary>벽 근접 시 총기를 뒤로 당길 수 있는 최대 거리 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float wallRetractMaxDistance = 0.65f;

    /// <summary>총기 후퇴 및 원복 보간 속도.</summary>
    [Min(0.1f)]
    [SerializeField] private float wallRetractSpeed = 18f;

    /// <summary>1인칭 정조준(ADS) 진입을 제한하는 총기 후퇴 거리 임계값 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float adsEnterWallRetractThreshold = 0.05f;

    /// <summary>1인칭 정조준(ADS) 유지 중 자동 해제되는 총기 후퇴 거리 임계값 (미터).</summary>
    [Min(0f)]
    [SerializeField] private float adsExitWallRetractThreshold = 0.12f;

    /// <summary>크로스헤어 레이캐스트 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _aimHits = new RaycastHit[16];

    /// <summary>주변 상호작용 대상 탐색 결과를 저장하는 재사용 버퍼.</summary>
    private readonly Collider[] _interactOverlaps = new Collider[24];

    /// <summary>로컬 플레이어 입력 액션 에셋 인스턴스.</summary>
    private PlayerControls _inputActions;

    /// <summary>발사, 탄약 및 재장전을 처리하는 무기 전투 컴포넌트.</summary>
    private PlayerWeaponController _weaponController;

    /// <summary>4슬롯 무기 보유 및 줍기/버리기를 관리하는 인벤토리 컴포넌트.</summary>
    private PlayerWeaponInventory _weaponInventory;

    /// <summary>로컬 소유자 시네머신 카메라의 무기 반동 및 미세 펄스 흔들림 확장 컴포넌트.</summary>
    private CinemachineWeaponFeedback _cameraFeedback;

    /// <summary>사망 후 시체 포커스 및 킬러 관전을 제어하는 씬 데스캠 컨트롤러.</summary>
    private DeathCamController _deathCamController;

    /// <summary>3인칭 ↔ 1인칭 정조준 시점 전환과 로컬 가시성을 제어하는 씬 ADS 컨트롤러.</summary>
    private FpsAdsController _fpsAdsController;

    /// <summary>장착 무기 상태와 재장전 애니메이션 상태를 조회할 플레이어 애니메이터.</summary>
    private PlayerAnim _playerAnimComponent;

    /// <summary>래그돌 회전과 무관하게 수직 자세를 유지하는 카메라 추적 앵커.</summary>
    private Transform _cameraAnchor;

    /// <summary>플레이어 기준 위치에서 앵커까지의 월드 수직 오프셋 (미터).</summary>
    private float _tpsViewOffsetY;

    /// <summary>현재 프레임의 2D 이동 입력값.</summary>
    private Vector2 _moveInput;

    /// <summary>다음 네트워크 틱에 소비할 점프 입력 플래그.</summary>
    private bool _jumpRequested;

    /// <summary>현재 마우스 우클릭(정조준) 입력이 유지되고 있는지 여부.</summary>
    private bool _isAimHeld;

    /// <summary>벽 근접 감지로 현재 적용 중인 총기 후퇴 거리 (미터).</summary>
    private float _currentWallRetract;

    /// <summary>카운트다운·종료 등 매치 상태에 의해 이동·전투 입력이 잠겨 있는지 여부.</summary>
    private bool _isInputLocked;

    /// <summary>로컬 소유자에 바인딩된 기본 3인칭 시네머신 카메라 참조.</summary>
    private CinemachineCamera _boundTpsCamera;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>로컬 소유자(<see cref="NetworkBehaviour.IsOwner"/>) 플레이어가 클라이언트에서 시작될 때 발생하는 이벤트.</summary>
    public static event Action<PlayerController> OnLocalPlayerStarted;

    /// <summary>로컬 소유자(<see cref="NetworkBehaviour.IsOwner"/>) 플레이어가 클라이언트에서 정지될 때 발생하는 이벤트.</summary>
    public static event Action<PlayerController> OnLocalPlayerStopped;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 클라이언트에 활성화된 로컬 소유자 <see cref="PlayerController"/> 인스턴스.</summary>
    public static PlayerController LocalPlayer { get; private set; }

    /// <summary>로컬 소유자 시네머신 카메라가 추적하는 수직 기준 앵커 트랜스폼.</summary>
    public Transform CameraAnchor => _cameraAnchor;

    /// <summary><see cref="NetworkTickSmoother"/>로 보간되는 시각 루트 트랜스폼.</summary>
    public Transform VisualRoot => visualRoot;

    /// <summary>현재 장착 중인 무기의 <see cref="WeaponDataSO"/> 에셋.</summary>
    public WeaponDataSO CurrentEquippedWeaponData => _weaponInventory != null ? _weaponInventory.EquippedWeaponData : null;

    /// <summary>현재 1인칭 정조준(ADS) 모드가 활성화되어 있는지 여부.</summary>
    public bool IsAdsActive => _fpsAdsController != null && _fpsAdsController.IsAdsActive;

    /// <summary>벽 근접 감지로 현재 적용 중인 총기 후퇴 거리 (미터).</summary>
    public float CurrentWallRetractDistance => _currentWallRetract;

    /// <summary>매치 상태에 의해 로컬 입력이 잠겨 있는지 여부.</summary>
    public bool IsInputLocked => _isInputLocked;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Update()
    {
        if (!IsOwner || _fpsAdsController == null) return;

        if (_fpsAdsController.IsAdsActive)
        {
            if (_isInputLocked || !CanUseAds())
            {
                _fpsAdsController.ExitAds();
            }
        }
        else if (!_isInputLocked && _fpsAdsController.InputMode == EAdsInputMode.Hold && _isAimHeld && CanUseAds())
        {
            _fpsAdsController.EnterAds(CurrentEquippedWeaponData);
        }
    }

    protected override void LateUpdate()
    {
        if (IsOwner && _cameraAnchor != null)
        {
            UpdateCameraAnchorPosition();
        }

        base.LateUpdate();

        if (!IsSpawned || IsDead || IsRagdollIdle || aimPitchTarget == null)
        {
            _currentWallRetract = 0f;
            return;
        }

        WeaponDataSO equippedWeapon = CurrentEquippedWeaponData;
        Transform muzzlePoint = _playerAnimComponent != null ? _playerAnimComponent.CurrentMuzzlePoint : null;
        float targetRetract = 0f;
        Vector3 retractAxis = aimPitchTarget.forward;

        if (enableWallRetract
            && equippedWeapon != null
            && equippedWeapon.Category != EWeaponCategory.Melee
            && equippedWeapon.Category != EWeaponCategory.Throwable
            && muzzlePoint != null)
        {
            Vector3 restPivotPos = aimPitchTarget.position;
            Vector3 restMuzzlePos = muzzlePoint.position;
            float radius = Mathf.Max(0.01f, wallRetractCheckRadius);
            float clearance = Mathf.Max(0f, wallRetractClearance);
            float maxRetract = Mathf.Max(0f, wallRetractMaxDistance);

            Vector3 castOrigin;
            Vector3 castDir;
            float barrelLength;

            Transform adsAnchor = _playerAnimComponent.CurrentAdsCameraAnchor;
            if (IsOwner
                && IsAdsActive
                && _fpsAdsController != null
                && _fpsAdsController.FpsAdsCamera != null
                && adsAnchor != null)
            {
                castOrigin = GetCameraTargetPosition();
                castDir = _fpsAdsController.FpsAdsCamera.State.GetFinalOrientation() * Vector3.forward;
                barrelLength = Vector3.Distance(adsAnchor.position, restMuzzlePos);
                retractAxis = castDir;
            }
            else
            {
                Vector3 localMuzzle = aimPitchTarget.InverseTransformPoint(restMuzzlePos);
                float forwardLength = Mathf.Max(0f, localMuzzle.z);
                float safeLocalY = Mathf.Max(0f, localMuzzle.y);
                Vector3 alignedStart = aimPitchTarget.TransformPoint(new Vector3(localMuzzle.x, safeLocalY, 0f));
                Vector3 lateralDelta = alignedStart - restPivotPos;
                float lateralDist = lateralDelta.magnitude;

                bool lateralBlocked = false;
                if (lateralDist > 0.001f)
                {
                    int lateralHits = Physics.RaycastNonAlloc(
                        restPivotPos,
                        lateralDelta / lateralDist,
                        _aimHits,
                        lateralDist,
                        wallRetractLayerMask,
                        QueryTriggerInteraction.Ignore);
                    lateralBlocked = TryFindClosestWallDistance(lateralHits, out _);
                }

                if (!lateralBlocked && forwardLength > 0.001f)
                {
                    castOrigin = alignedStart;
                    castDir = aimPitchTarget.forward;
                    barrelLength = forwardLength;
                    retractAxis = castDir;
                }
                else
                {
                    Vector3 safeMuzzlePos = aimPitchTarget.TransformPoint(new Vector3(localMuzzle.x, safeLocalY, forwardLength));
                    Vector3 pivotToMuzzle = safeMuzzlePos - restPivotPos;
                    barrelLength = pivotToMuzzle.magnitude;
                    castOrigin = restPivotPos;
                    castDir = barrelLength > 0.001f ? pivotToMuzzle / barrelLength : aimPitchTarget.forward;
                    retractAxis = castDir;
                }
            }

            if (barrelLength > 0.001f)
            {
                float checkDistance = barrelLength + clearance;
                int sphereHits = Physics.SphereCastNonAlloc(
                    castOrigin,
                    radius,
                    castDir,
                    _aimHits,
                    checkDistance,
                    wallRetractLayerMask,
                    QueryTriggerInteraction.Ignore);

                bool hitWall = TryFindClosestWallDistance(sphereHits, out float wallDistance);
                if (!hitWall)
                {
                    int rayHits = Physics.RaycastNonAlloc(
                        castOrigin,
                        castDir,
                        _aimHits,
                        checkDistance,
                        wallRetractLayerMask,
                        QueryTriggerInteraction.Ignore);
                    hitWall = TryFindClosestWallDistance(rayHits, out wallDistance);
                }

                if (hitWall)
                {
                    float clampedMax = Mathf.Min(barrelLength, maxRetract);
                    targetRetract = Mathf.Clamp(checkDistance - wallDistance, 0f, clampedMax);
                }
            }
        }

        float dt = Time.deltaTime;
        if (dt > 0f)
        {
            float lerpT = 1f - Mathf.Exp(-wallRetractSpeed * dt);
            _currentWallRetract = Mathf.Lerp(_currentWallRetract, targetRetract, lerpT);
            if (targetRetract <= 0f && _currentWallRetract < 0.0005f)
            {
                _currentWallRetract = 0f;
            }
        }

        if (_currentWallRetract > 0f)
        {
            aimPitchTarget.position -= retractAxis * _currentWallRetract;
        }
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartClient()
    {
        if (!IsOwner) return;

        bool allowGameplayInput = MatchManager.Instance == null || MatchManager.Instance.IsGameplayInputAllowed;
        _isInputLocked = !allowGameplayInput;
        Cursor.lockState = allowGameplayInput ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !allowGameplayInput;

        _inputActions = new PlayerControls();
        _inputActions.Enable();
        _inputActions.Player.Move.performed += OnMovePerformed;
        _inputActions.Player.Move.canceled += OnMoveCanceled;
        _inputActions.Player.Attack.started += OnAttackStarted;
        _inputActions.Player.Attack.canceled += OnAttackEnd;
        _inputActions.Player.Jump.started += OnJump;
        _inputActions.Player.ChgWeapon.started += ChangeWeaponMode;
        _inputActions.Player.Reload.started += OnReloadStarted;
        _inputActions.Player.Pickup.started += OnPickupStarted;
        _inputActions.Player.DropWeapon.started += OnDropWeaponStarted;
        _inputActions.Player.Aim.started += OnAimStarted;
        _inputActions.Player.Aim.canceled += OnAimCanceled;
        _inputActions.Player.Interact.started += OnInteractStarted;

        _cameraAnchor = new GameObject("CameraAnchor").transform;
        _cameraAnchor.SetParent(transform, false);
        UpdateCameraAnchorPosition();

        _deathCamController = FindAnyObjectByType<DeathCamController>(FindObjectsInactive.Include);
        _fpsAdsController = FindAnyObjectByType<FpsAdsController>(FindObjectsInactive.Include);

        if (_deathCamController != null && MatchManager.Instance != null)
        {
            _deathCamController.SetAllowSpectateButton(MatchManager.Instance.AllowSpectateButton);
        }

        CinemachineCamera cam = _deathCamController != null ? _deathCamController.PlayerCamera : null;
        if (cam == null && _fpsAdsController != null)
        {
            cam = _fpsAdsController.TpsCamera;
        }
        if (cam == null)
        {
            CinemachineCamera[] cameras = FindObjectsByType<CinemachineCamera>();
            for (int i = 0; i < cameras.Length; i++)
            {
                if (cameras[i] != null
                    && cameras[i].GetComponent<DeathCamController>() == null
                    && cameras[i].GetComponent<FpsAdsController>() == null)
                {
                    cam = cameras[i];
                    break;
                }
            }
        }

        _boundTpsCamera = cam;

        if (cam != null)
        {
            _deathCamController?.BindPlayerCamera(cam);
            _deathCamController?.ResetDeathCam();

            cam.Follow = _cameraAnchor;
            cam.LookAt = _cameraAnchor;

            CinemachineOrbitalFollow orbitalFollow = cam.GetComponent<CinemachineOrbitalFollow>();
            if (orbitalFollow != null)
            {
                orbitalFollow.HorizontalAxis.Value = transform.eulerAngles.y;
            }
            cam.PreviousStateIsValid = false;

            CinemachineInputAxisController axisController = cam.GetComponent<CinemachineInputAxisController>();
            if (axisController != null)
            {
                axisController.enabled = allowGameplayInput;
            }

            _cameraFeedback = cam.GetComponent<CinemachineWeaponFeedback>();
            if (_cameraFeedback == null)
            {
                _cameraFeedback = cam.gameObject.AddComponent<CinemachineWeaponFeedback>();
            }
            _cameraFeedback.ResetFeedback();
        }

        _fpsAdsController?.BindLocalPlayer(this, cam);

        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged += HandleOwnerEquippedWeaponChanged;
        }

        if (_playerHealth != null)
        {
            _playerHealth.OnOwnerDiedWithKiller += HandleOwnerDiedWithKiller;
        }

        if (Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        LocalPlayer = this;
        OnLocalPlayerStarted?.Invoke(this);
    }

    public override void OnStopClient()
    {
        if (LocalPlayer == this)
        {
            LocalPlayer = null;
            OnLocalPlayerStopped?.Invoke(this);
        }

        _moveInput = Vector2.zero;
        _jumpRequested = false;
        _isAimHeld = false;
        _isInputLocked = false;
        _boundTpsCamera = null;

        if (_playerHealth != null)
        {
            _playerHealth.OnOwnerDiedWithKiller -= HandleOwnerDiedWithKiller;
        }

        if (_weaponInventory != null)
        {
            _weaponInventory.OnEquippedWeaponChanged -= HandleOwnerEquippedWeaponChanged;
        }

        if (IsOwner && _fpsAdsController != null)
        {
            _fpsAdsController.UnbindLocalPlayer(this);
        }
        _fpsAdsController = null;

        if (IsOwner && _deathCamController != null)
        {
            _deathCamController.ResetDeathCam();
        }
        _deathCamController = null;

        if (_cameraAnchor != null)
        {
            Destroy(_cameraAnchor.gameObject);
            _cameraAnchor = null;
        }

        if (_cameraFeedback != null)
        {
            _cameraFeedback.ResetFeedback();
            _cameraFeedback = null;
        }

        if (_inputActions == null) return;

        _inputActions.Player.Move.performed -= OnMovePerformed;
        _inputActions.Player.Move.canceled -= OnMoveCanceled;
        _inputActions.Player.Attack.started -= OnAttackStarted;
        _inputActions.Player.Attack.canceled -= OnAttackEnd;
        _inputActions.Player.Jump.started -= OnJump;
        _inputActions.Player.ChgWeapon.started -= ChangeWeaponMode;
        _inputActions.Player.Reload.started -= OnReloadStarted;
        _inputActions.Player.Pickup.started -= OnPickupStarted;
        _inputActions.Player.DropWeapon.started -= OnDropWeaponStarted;
        _inputActions.Player.Aim.started -= OnAimStarted;
        _inputActions.Player.Aim.canceled -= OnAimCanceled;
        _inputActions.Player.Interact.started -= OnInteractStarted;
        _inputActions.Player.Disable();
        _inputActions.Dispose();
        _inputActions = null;
    }

    // ***************************************************************
    // PropControllerBase
    // ***************************************************************

    /// <inheritdoc/>
    protected override void OnPropAwake()
    {
        _weaponController = GetComponent<PlayerWeaponController>();
        _weaponInventory = GetComponent<PlayerWeaponInventory>();
        _playerAnimComponent = GetComponent<PlayerAnim>();

        if (tpsViewPos == null)
        {
            tpsViewPos = transform.Find("TpsViewPos");
        }

        if (visualRoot == null)
        {
            NetworkTickSmoother smoother = GetComponentInChildren<NetworkTickSmoother>(true);
            if (smoother != null)
            {
                visualRoot = smoother.transform;
            }
        }

        _tpsViewOffsetY = tpsViewPos != null
            ? tpsViewPos.localPosition.y * transform.localScale.y
            : DefaultTpsViewOffsetY;
    }

    /// <inheritdoc/>
    protected override EWeaponSlotType GetCurrentWeaponSlot()
    {
        return _weaponInventory != null ? _weaponInventory.ActiveSlot : base.GetCurrentWeaponSlot();
    }

    /// <inheritdoc/>
    protected override PlayerInputData CollectTickInput()
    {
        if (IsDead || _isInputLocked)
        {
            _moveInput = Vector2.zero;
            _jumpRequested = false;
            return default;
        }

        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        EWeaponSlotType weaponSlot = GetCurrentWeaponSlot();
        bool isAiming = _propAnim != null && _propAnim.CurrentWeaponType != EWeaponType.Hide;
        float aimYaw = cameraYaw;
        if (isAiming && !IsAdsActive)
        {
            Vector3 aimPoint = GetAimPoint();
            Vector3 toAim = aimPoint - transform.position;
            toAim.y = 0f;
            if (toAim.sqrMagnitude > 0.0001f)
            {
                aimYaw = Mathf.Atan2(toAim.x, toAim.z) * Mathf.Rad2Deg;
            }
        }

        bool jumpRequested = _jumpRequested;
        _jumpRequested = false;
        return new PlayerInputData(_moveInput, cameraYaw, jumpRequested, isAiming, aimYaw, weaponSlot);
    }

    /// <inheritdoc/>
    protected override float ComputeOwnerTargetAimPitch()
    {
        if (IsDead) return 0f;

        bool isAiming = _propAnim != null && _propAnim.CurrentWeaponType != EWeaponType.Hide;
        if (!isAiming) return 0f;

        if (IsAdsActive && _fpsAdsController != null)
        {
            return Mathf.Clamp(_fpsAdsController.CurrentAimPitch, MinAimPitch, MaxAimPitch);
        }

        Vector3 aimPoint = GetAimPoint();
        Vector3 pivotPos = GetFireOrigin();
        Vector3 aimDir = aimPoint - pivotPos;
        float horizontalDist = new Vector2(aimDir.x, aimDir.z).magnitude;
        if (horizontalDist <= 0.0001f && Mathf.Abs(aimDir.y) <= 0.0001f) return 0f;

        float pitch = -Mathf.Atan2(aimDir.y, horizontalDist) * Mathf.Rad2Deg;
        return Mathf.Clamp(pitch, MinAimPitch, MaxAimPitch);
    }

    /// <inheritdoc/>
    protected override void OnDeathEntered()
    {
        _moveInput = Vector2.zero;
        _jumpRequested = false;
        _isAimHeld = false;
        _currentWallRetract = 0f;
        _weaponController?.CancelCombatActions();
        _cameraFeedback?.ResetFeedback();
        if (IsOwner)
        {
            _fpsAdsController?.ForceExitAds();
        }
    }

    /// <inheritdoc/>
    protected override void OnDeathReset()
    {
        _moveInput = Vector2.zero;
        _jumpRequested = false;
        _isAimHeld = false;
        _currentWallRetract = 0f;
        _weaponController?.CancelCombatActions();
        _cameraFeedback?.ResetFeedback();

        if (IsOwner)
        {
            _fpsAdsController?.ForceExitAds();
            _deathCamController?.ResetDeathCam();
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>카운트다운·종료 등 매치 상태에 따라 이동·공격·상호작용 입력을 잠그거나 해제한다.</summary>
    public void SetInputLocked(bool locked)
    {
        _isInputLocked = locked;
        if (locked)
        {
            _moveInput = Vector2.zero;
            _jumpRequested = false;
            _isAimHeld = false;
            _weaponController?.CancelCombatActions();
            if (IsOwner)
            {
                _fpsAdsController?.ForceExitAds();
            }
            return;
        }

        // 카운트다운 종료 시점에 이미 WASD를 누르고 있던 입력을 즉시 반영한다.
        if (IsOwner && _inputActions != null)
        {
            _moveInput = _inputActions.Player.Move.ReadValue<Vector2>();
        }
    }

    /// <summary>로컬 소유자의 3인칭 시점 마우스 회전 입력을 켜거나 끈다.</summary>
    public void SetCameraInputEnabled(bool enabled)
    {
        if (!IsOwner || _boundTpsCamera == null) return;

        CinemachineInputAxisController axisController = _boundTpsCamera.GetComponent<CinemachineInputAxisController>();
        if (axisController != null)
        {
            axisController.enabled = enabled;
        }
    }

    /// <summary>보간된 시각 루트 기준 카메라 추적 월드 좌표를 반환한다.</summary>
    public Vector3 GetCameraTargetPosition()
    {
        Vector3 basePosition = visualRoot != null ? visualRoot.position : transform.position;
        return basePosition + Vector3.up * _tpsViewOffsetY;
    }

    /// <summary>로컬 소유자 카메라에 <paramref name="weaponData"/> 기반 발사 반동과 미세 펄스 흔들림을 적용한다.</summary>
    public void ApplyWeaponCameraFeedback(WeaponDataSO weaponData)
    {
        if (!IsOwner || IsDead || weaponData == null) return;

        if (IsAdsActive && _fpsAdsController != null)
        {
            _fpsAdsController.ApplyWeaponFeedback(weaponData);
            return;
        }

        _cameraFeedback?.ApplyWeaponFeedback(weaponData);
    }

    /// <summary>조준 및 히트스캔 발사의 기준이 되는 월드 원점을 반환한다.</summary>
    public Vector3 GetFireOrigin()
    {
        if (IsOwner && IsAdsActive && Camera.main != null)
        {
            return Camera.main.transform.position;
        }

        if (aimPitchTarget != null) return aimPitchTarget.position;

        float offsetY = _tpsViewOffsetY > 0f ? _tpsViewOffsetY : DefaultTpsViewOffsetY;
        return transform.position + Vector3.up * offsetY;
    }

    /// <summary>메인 카메라 정중앙(Viewport 0.5, 0.5) 레이캐스트로 조준 월드 좌표를 계산한다.</summary>
    public Vector3 GetAimPoint()
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
        {
            Vector3 fallbackForward = Quaternion.Euler(0f, transform.eulerAngles.y, 0f) * Vector3.forward;
            return transform.position + fallbackForward * defaultAimDistance;
        }

        Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
        float minDistance = IsAdsActive ? 0f : Vector3.Distance(ray.origin, GetFireOrigin());

        int hitCount = Physics.RaycastNonAlloc(
            ray,
            _aimHits,
            defaultAimDistance,
            aimLayerMask,
            QueryTriggerInteraction.Ignore);

        float closestDistance = float.MaxValue;
        Vector3 closestPoint = ray.GetPoint(defaultAimDistance);

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _aimHits[i];
            if (hit.transform == null || hit.transform.IsChildOf(transform)) continue;
            if (hit.distance <= minDistance) continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                closestPoint = hit.point;
            }
        }

        return closestPoint;
    }

    // ***************************************************************
    // Input Event Handlers
    // ***************************************************************

    /// <summary>이동 입력 벡터를 갱신한다.</summary>
    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        if (IsDead || _isInputLocked)
        {
            _moveInput = Vector2.zero;
            return;
        }

        _moveInput = context.ReadValue<Vector2>();
    }

    /// <summary>이동 입력이 해제되면 입력 벡터를 초기화한다.</summary>
    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        _moveInput = Vector2.zero;
    }

    /// <summary>바닥에 닿아 있을 때 점프 요청 플래그를 설정한다.</summary>
    private void OnJump(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked || !_isGrounded) return;

        _jumpRequested = true;
    }

    /// <summary>공격 입력 시작 시 무기 공격 또는 상태를 갱신한다.</summary>
    private void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked) return;

        _EplayerState = EPlayerState.Attack;
        if (_weaponController != null)
        {
            _weaponController.SetFireInput(true);
        }
        else
        {
            _propAnim?.SetAttackState(true);
        }
    }

    /// <summary>공격 입력 종료 시 상태를 복원한다.</summary>
    private void OnAttackEnd(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        if (IsDead || _isInputLocked)
        {
            _weaponController?.SetFireInput(false);
            return;
        }

        _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        if (_weaponController != null)
        {
            _weaponController.SetFireInput(false);
        }
        else
        {
            _propAnim?.SetAttackState(false);
        }
    }

    /// <summary>무기 전환 입력에 따라 보유 중인 슬롯으로 무기 모드를 변경한다.</summary>
    private void ChangeWeaponMode(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked) return;

        int slotIndex = Mathf.Clamp(Mathf.RoundToInt(context.ReadValue<float>()) - 1, 0, PlayerWeaponInventory.SlotCount - 1);
        EWeaponSlotType targetSlot = (EWeaponSlotType)slotIndex;

        if (_weaponInventory != null)
        {
            if (targetSlot != EWeaponSlotType.Unarmed && !_weaponInventory.HasWeaponInSlot(targetSlot))
            {
                return;
            }
            if (_weaponInventory.ActiveSlot == targetSlot)
            {
                if (targetSlot == EWeaponSlotType.Throwable && _weaponInventory.CanCycleThrowable)
                {
                    _fpsAdsController?.ExitAds();
                    _weaponController?.CancelGrenadeAim();
                    _weaponController?.SetFireInput(false);
                    _weaponInventory.TrySelectSlot(targetSlot);
                }
                return;
            }

            if (targetSlot == EWeaponSlotType.Unarmed)
            {
                _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
            }

            _fpsAdsController?.ExitAds();
            _weaponController?.CancelGrenadeAim();
            _weaponController?.SetFireInput(false);
            _weaponInventory.TrySelectSlot(targetSlot);
            return;
        }

        EWeaponType weaponType = (EWeaponType)slotIndex;
        if (weaponType == EWeaponType.Hide)
        {
            _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        }

        _fpsAdsController?.ExitAds();
        _weaponController?.SetFireInput(false);
        _propAnim?.SetWeaponMode(weaponType);
    }

    /// <summary>재장전 입력 시 무기 재장전 시퀀스를 요청한다.</summary>
    private void OnReloadStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked) return;

        if (_weaponController != null)
        {
            _weaponController.TryStartReload();
            if (_weaponController.IsReloading)
            {
                _fpsAdsController?.ExitAds();
            }
        }
        else
        {
            _fpsAdsController?.ExitAds();
            _propAnim?.RequestReload();
        }
    }

    /// <summary><c>G</c> 입력 시 <c>Alt</c> 조합이 아닐 때만 주변 무기 획득을 요청한다.</summary>
    private void OnPickupStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked || _weaponInventory == null) return;
        if (Keyboard.current != null && Keyboard.current.altKey.isPressed) return;

        _fpsAdsController?.ExitAds();
        _weaponController?.CancelGrenadeAim();
        _weaponController?.SetFireInput(false);
        _weaponInventory.TryPickupNearbyWeapon();
    }

    /// <summary><c>E</c> 입력 시 조준 방향 또는 근접 범위의 문·창문 개폐를 요청한다.</summary>
    private void OnInteractStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked) return;

        Vector3 playerPos = transform.position;
        NetworkHingedPortal bestPortal = null;

        Camera mainCamera = Camera.main;
        if (mainCamera != null)
        {
            Ray ray = mainCamera.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0f));
            int hitCount = Physics.SphereCastNonAlloc(
                ray,
                InteractRayRadius,
                _aimHits,
                InteractFallbackRadius + InteractCameraRangeBonus,
                ~0,
                QueryTriggerInteraction.Ignore);

            float closestRayDist = float.MaxValue;
            for (int i = 0; i < hitCount; i++)
            {
                Collider col = _aimHits[i].collider;
                if (col == null) continue;

                NetworkHingedPortal candidate = col.GetComponentInParent<NetworkHingedPortal>();
                if (candidate == null && col.transform.parent != null)
                {
                    candidate = col.transform.parent.GetComponentInChildren<NetworkHingedPortal>();
                }
                if (candidate == null || !candidate.IsSpawned) continue;

                float playerDist = Vector3.Distance(playerPos, candidate.transform.position);
                if (playerDist > candidate.InteractRange) continue;

                if (_aimHits[i].distance < closestRayDist)
                {
                    closestRayDist = _aimHits[i].distance;
                    bestPortal = candidate;
                }
            }
        }

        if (bestPortal == null)
        {
            int count = Physics.OverlapSphereNonAlloc(
                playerPos + Vector3.up * 0.9f,
                InteractFallbackRadius,
                _interactOverlaps,
                ~0,
                QueryTriggerInteraction.Ignore);

            float closestDist = float.MaxValue;
            for (int i = 0; i < count; i++)
            {
                Collider col = _interactOverlaps[i];
                if (col == null) continue;

                NetworkHingedPortal candidate = col.GetComponentInParent<NetworkHingedPortal>();
                if (candidate == null && col.transform.parent != null)
                {
                    candidate = col.transform.parent.GetComponentInChildren<NetworkHingedPortal>();
                }
                if (candidate == null || !candidate.IsSpawned) continue;

                float dist = Vector3.Distance(playerPos, candidate.transform.position);
                if (dist > candidate.InteractRange || dist >= closestDist) continue;

                closestDist = dist;
                bestPortal = candidate;
            }
        }

        bestPortal?.RequestToggle();
    }

    /// <summary><c>Alt + G</c> 입력 시 현재 장착 무기를 바닥에 버리도록 요청한다.</summary>
    private void OnDropWeaponStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || IsDead || _isInputLocked || _weaponInventory == null) return;
        if (_weaponInventory.ActiveSlot == EWeaponSlotType.Unarmed) return;

        _fpsAdsController?.ExitAds();
        _weaponController?.CancelGrenadeAim();
        _weaponController?.SetFireInput(false);
        _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        _weaponInventory.TryDropCurrentWeapon();
    }

    /// <summary>마우스 우클릭 입력 시작 시 수류탄 조준 취소 또는 1인칭 정조준을 전환한다.</summary>
    private void OnAimStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || _isInputLocked) return;

        if (_weaponController != null && _weaponController.IsGrenadeAiming)
        {
            _weaponController.CancelGrenadeAim();
            return;
        }

        if (_fpsAdsController == null) return;

        _isAimHeld = true;

        if (_fpsAdsController.InputMode == EAdsInputMode.Toggle)
        {
            if (_fpsAdsController.IsAdsActive)
            {
                _fpsAdsController.ExitAds();
            }
            else if (CanUseAds())
            {
                _fpsAdsController.EnterAds(CurrentEquippedWeaponData);
            }
            return;
        }

        if (CanUseAds())
        {
            _fpsAdsController.EnterAds(CurrentEquippedWeaponData);
        }
    }

    /// <summary>마우스 우클릭 입력 해제 시 홀드 방식이면 3인칭 시점으로 복귀한다.</summary>
    private void OnAimCanceled(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _isAimHeld = false;
        if (_fpsAdsController != null && _fpsAdsController.InputMode == EAdsInputMode.Hold)
        {
            _fpsAdsController.ExitAds();
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>소유자 사망 통보 수신 시 3초 시체 포커스 및 킬러 관전 시퀀스를 시작한다.</summary>
    private void HandleOwnerDiedWithKiller(NetworkObject killerNetworkObject)
    {
        if (!IsOwner) return;
        _isAimHeld = false;
        _fpsAdsController?.ForceExitAds();
        _deathCamController?.BeginDeathSequence(this, killerNetworkObject);
    }

    /// <summary>장착 무기 상태가 서버에 의해 변경되면 진행 중인 1인칭 정조준을 해제한다.</summary>
    private void HandleOwnerEquippedWeaponChanged(EquippedWeaponState state)
    {
        if (!IsOwner) return;
        _fpsAdsController?.ExitAds();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>현재 플레이어가 1인칭 정조준(ADS)에 진입하거나 유지할 수 있는 상태인지 판정한다.</summary>
    private bool CanUseAds()
    {
        if (!IsOwner || IsDead || IsRagdollIdle) return false;
        if (_deathCamController != null && _deathCamController.IsDeathCamActive) return false;
        if (_weaponInventory == null) return false;

        EWeaponSlotType activeSlot = _weaponInventory.ActiveSlot;
        if (activeSlot != EWeaponSlotType.Primary && activeSlot != EWeaponSlotType.Secondary) return false;

        WeaponDataSO weaponData = _weaponInventory.EquippedWeaponData;
        if (weaponData == null) return false;
        if (weaponData.Category != EWeaponCategory.Primary && weaponData.Category != EWeaponCategory.Secondary) return false;

        if (_weaponController != null && _weaponController.IsReloading) return false;
        if (_propAnim != null && _propAnim.IsSwappingWeapon) return false;
        if (_playerAnimComponent != null && _playerAnimComponent.IsReloadAnimationActive) return false;

        if (enableWallRetract)
        {
            float retractLimit = IsAdsActive ? adsExitWallRetractThreshold : adsEnterWallRetractThreshold;
            if (_currentWallRetract > retractLimit) return false;
        }

        return true;
    }

    /// <summary><see cref="_aimHits"/> 버퍼에서 본인을 제외한 가장 가까운 벽/지형 충돌 거리를 찾는다.</summary>
    private bool TryFindClosestWallDistance(int hitCount, out float closestDistance)
    {
        bool found = false;
        closestDistance = float.MaxValue;

        for (int i = 0; i < hitCount; i++)
        {
            RaycastHit hit = _aimHits[i];
            if (hit.collider == null || hit.transform == null || hit.transform.IsChildOf(transform)) continue;
            if (hit.collider.GetComponentInParent<DroppedMagazine>() != null) continue;

            if (hit.distance < closestDistance)
            {
                closestDistance = hit.distance;
                found = true;
            }
        }

        return found;
    }

    /// <summary>카메라 추적 앵커의 월드 위치와 수직 회전을 갱신한다.</summary>
    private void UpdateCameraAnchorPosition()
    {
        _cameraAnchor.SetPositionAndRotation(GetCameraTargetPosition(), Quaternion.identity);
    }
}
