using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>로컬 플레이어 입력과 카메라 추적 기준을 <see cref="PropControllerBase"/>에 연결한다.</summary>
[RequireComponent(typeof(PlayerAnim))]
public class PlayerController : PropControllerBase
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이동 기준 방향을 계산할 카메라 트랜스폼.</summary>
    [Header("참조")]
    [SerializeField] private Transform cameraTransform;

    /// <summary>로컬 플레이어 입력 액션 에셋 인스턴스.</summary>
    private PlayerControls _inputActions;

    /// <summary>현재 프레임의 2D 이동 입력값.</summary>
    private Vector2 _moveInput;

    /// <summary>다음 네트워크 틱에 소비할 점프 입력 플래그.</summary>
    private bool _jumpRequested;

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
        _moveInput = Vector2.zero;
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

    // ***************************************************************
    // PropControllerBase
    // ***************************************************************

    /// <inheritdoc/>
    protected override PlayerInputData CollectTickInput()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        bool jumpRequested = _jumpRequested;
        _jumpRequested = false;
        return new PlayerInputData(_moveInput, cameraYaw, jumpRequested);
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

    /// <summary>공격 입력 시작 시 상태를 갱신한다.</summary>
    private void OnAttackStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _EplayerState = EPlayerState.Attack;
        _propAnim?.SetAttackState(true);
    }

    /// <summary>공격 입력 종료 시 상태를 복원한다.</summary>
    private void OnAttackEnd(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _EplayerState = _isGrounded ? EPlayerState.Idle : EPlayerState.Air;
        _propAnim?.SetAttackState(false);
    }

    /// <summary>무기 전환 입력에 따라 무기 모드를 변경한다.</summary>
    private void ChangeWeaponMode(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        int weaponIndex = Mathf.Clamp(Mathf.RoundToInt(context.ReadValue<float>()) - 1, 0, 3);
        _propAnim?.SetWeaponMode((EWeaponType)weaponIndex);
    }

    /// <summary>재장전 입력 시 손 재장전 시퀀스를 요청한다.</summary>
    private void OnReloadStarted(InputAction.CallbackContext context)
    {
        if (!IsOwner) return;

        _propAnim?.RequestReload();
    }
}
