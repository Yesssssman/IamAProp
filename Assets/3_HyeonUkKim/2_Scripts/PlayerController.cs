using FishNet.Component.Transforming.Beta;
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

    /// <summary>크로스헤어 레이캐스트 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _aimHits = new RaycastHit[16];

    /// <summary>로컬 플레이어 입력 액션 에셋 인스턴스.</summary>
    private PlayerControls _inputActions;

    /// <summary>발사, 탄약 및 재장전을 처리하는 무기 전투 컴포넌트.</summary>
    private PlayerWeaponController _weaponController;

    /// <summary>4슬롯 무기 보유 및 줍기/버리기를 관리하는 인벤토리 컴포넌트.</summary>
    private PlayerWeaponInventory _weaponInventory;

    /// <summary>래그돌 회전과 무관하게 수직 자세를 유지하는 카메라 추적 앵커.</summary>
    private Transform _cameraAnchor;

    /// <summary>플레이어 기준 위치에서 앵커까지의 월드 수직 오프셋 (미터).</summary>
    private float _tpsViewOffsetY;

    /// <summary>현재 프레임의 2D 이동 입력값.</summary>
    private Vector2 _moveInput;

    /// <summary>다음 네트워크 틱에 소비할 점프 입력 플래그.</summary>
    private bool _jumpRequested;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    protected override void LateUpdate()
    {
        if (IsOwner && _cameraAnchor != null)
        {
            UpdateCameraAnchorPosition();
        }

        base.LateUpdate();
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

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
        _inputActions.Player.Pickup.started += OnPickupStarted;
        _inputActions.Player.DropWeapon.started += OnDropWeaponStarted;

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

        _cameraAnchor = new GameObject("CameraAnchor").transform;
        _cameraAnchor.SetParent(transform, false);
        UpdateCameraAnchorPosition();

        CinemachineCamera cam = FindAnyObjectByType<CinemachineCamera>();
        if (cam != null)
        {
            cam.Follow = _cameraAnchor;
            cam.LookAt = _cameraAnchor;

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
        _moveInput = Vector2.zero;
        _jumpRequested = false;

        if (_cameraAnchor != null)
        {
            Destroy(_cameraAnchor.gameObject);
            _cameraAnchor = null;
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
    }

    /// <inheritdoc/>
    protected override PlayerInputData CollectTickInput()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        bool isAiming = _propAnim != null && _propAnim.CurrentWeaponType != EWeaponType.Hide;
        float aimYaw = cameraYaw;
        if (isAiming)
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
        return new PlayerInputData(_moveInput, cameraYaw, jumpRequested, isAiming, aimYaw);
    }

    /// <inheritdoc/>
    protected override float ComputeOwnerTargetAimPitch()
    {
        bool isAiming = _propAnim != null && _propAnim.CurrentWeaponType != EWeaponType.Hide;
        if (!isAiming) return 0f;

        Vector3 aimPoint = GetAimPoint();
        Vector3 pivotPos = GetFireOrigin();
        Vector3 aimDir = aimPoint - pivotPos;
        float horizontalDist = new Vector2(aimDir.x, aimDir.z).magnitude;
        if (horizontalDist <= 0.0001f && Mathf.Abs(aimDir.y) <= 0.0001f) return 0f;

        float pitch = -Mathf.Atan2(aimDir.y, horizontalDist) * Mathf.Rad2Deg;
        return Mathf.Clamp(pitch, MinAimPitch, MaxAimPitch);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>조준 및 히트스캔 발사의 기준이 되는 월드 원점을 반환한다.</summary>
    public Vector3 GetFireOrigin()
    {
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
        Vector3 pivotPos = GetFireOrigin();
        float minDistance = Vector3.Distance(ray.origin, pivotPos);

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
        if (!IsOwner || !_isGrounded) return;

        _jumpRequested = true;
    }

    /// <summary>공격 입력 시작 시 무기 공격 또는 상태를 갱신한다.</summary>
    private void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

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
        if (!IsOwner) return;

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
                return;
            }

            if (targetSlot == EWeaponSlotType.Unarmed)
            {
                _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
            }

            _weaponController?.SetFireInput(false);
            _weaponInventory.TrySelectSlot(targetSlot);
            return;
        }

        EWeaponType weaponType = (EWeaponType)slotIndex;
        if (weaponType == EWeaponType.Hide)
        {
            _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        }

        _weaponController?.SetFireInput(false);
        _propAnim?.SetWeaponMode(weaponType);
    }

    /// <summary>재장전 입력 시 무기 재장전 시퀀스를 요청한다.</summary>
    private void OnReloadStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        if (_weaponController != null)
        {
            _weaponController.TryStartReload();
        }
        else
        {
            _propAnim?.RequestReload();
        }
    }

    /// <summary><c>G</c> 입력 시 <c>Alt</c> 조합이 아닐 때만 주변 무기 획득을 요청한다.</summary>
    private void OnPickupStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || _weaponInventory == null) return;
        if (Keyboard.current != null && Keyboard.current.altKey.isPressed) return;

        _weaponController?.SetFireInput(false);
        _weaponInventory.TryPickupNearbyWeapon();
    }

    /// <summary><c>Alt + G</c> 입력 시 현재 장착 무기를 바닥에 버리도록 요청한다.</summary>
    private void OnDropWeaponStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner || _weaponInventory == null) return;
        if (_weaponInventory.ActiveSlot == EWeaponSlotType.Unarmed) return;

        _weaponController?.SetFireInput(false);
        _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        _weaponInventory.TryDropCurrentWeapon();
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>카메라 추적 앵커의 월드 위치와 수직 회전을 갱신한다.</summary>
    private void UpdateCameraAnchorPosition()
    {
        Vector3 basePosition = visualRoot != null ? visualRoot.position : transform.position;
        _cameraAnchor.SetPositionAndRotation(basePosition + Vector3.up * _tpsViewOffsetY, Quaternion.identity);
    }
}
