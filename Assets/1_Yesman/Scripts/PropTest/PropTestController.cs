using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>네트워크 없이 로컬 입력으로 프롭의 <see cref="Rigidbody"/> 이동, 회전, 점프, 쓰러짐을 처리함.</summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(PropTestAnim))]
public class PropTestController : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이동, 점프, 쓰러짐, 바닥 체크 수치를 읽어오는 프롭 데이터.</summary>
    [SerializeField] private PropDataSO propData;

    /// <summary>발밑 바닥 체크 구체를 캐스팅하는 기준점.</summary>
    [SerializeField] private Transform groundCheck;

    private Rigidbody rb;

    /// <summary>쓰러짐 상태의 바닥 체크에 사용하는 프롭 콜라이더.</summary>
    private Collider bodyCollider;

    private PropTestAnim propAnim;

    /// <summary>이동 기준 방향을 계산할 카메라 트랜스폼.</summary>
    private Transform cameraTransform;

    private PlayerControls inputActions;

    private Vector2 moveInput;

    /// <summary>다음 물리 프레임에 소비할 점프 입력.</summary>
    private bool jumpRequested;

    private bool isGrounded;

    private bool wasGrounded;

    /// <summary>맨손 정지 상태에서 회전 제약이 풀려 쓰러진 상태인지 여부.</summary>
    private bool isRagdollIdle;

    /// <summary><see cref="Mathf.SmoothDampAngle(float, float, ref float, float, float, float)"/> 내부 각속도 상태값.</summary>
    private float turnSmoothVelocity;

    private const float GroundedMaxRiseSpeed = 0.1f; // 바닥으로 판정하는 최대 상승 속도 (m/s)

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 프롭의 평면 이동 속도 (초당 월드 단위, 월드 공간).</summary>
    public Vector3 PlanarVelocity { get; private set; }

    /// <summary>프롭의 최대 평면 이동 속력 (초당 월드 단위).</summary>
    public float MoveSpeed => propData.MoveSpeed;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = propData.RigidbodyMass;
        rb.linearDamping = propData.RigidbodyLinearDamping;
        rb.angularDamping = propData.RigidbodyAngularDamping;

        bodyCollider = GetComponentInChildren<Collider>();
        propAnim = GetComponent<PropTestAnim>();
    }

    private void OnEnable()
    {
        inputActions = new PlayerControls();
        inputActions.Player.Move.performed += OnMovePerformed;
        inputActions.Player.Move.canceled += OnMoveCanceled;
        inputActions.Player.Jump.started += OnJump;
        inputActions.Player.Attack.started += OnAttackStarted;
        inputActions.Player.Attack.canceled += OnAttackCanceled;
        inputActions.Player.ChgWeapon.started += OnChangeWeapon;
        inputActions.Player.Reload.started += OnReloadStarted;
        inputActions.Enable();

        Cursor.lockState = CursorLockMode.Locked;
        Cursor.visible = false;
    }

    private void OnDisable()
    {
        inputActions.Player.Move.performed -= OnMovePerformed;
        inputActions.Player.Move.canceled -= OnMoveCanceled;
        inputActions.Player.Jump.started -= OnJump;
        inputActions.Player.Attack.started -= OnAttackStarted;
        inputActions.Player.Attack.canceled -= OnAttackCanceled;
        inputActions.Player.ChgWeapon.started -= OnChangeWeapon;
        inputActions.Player.Reload.started -= OnReloadStarted;
        inputActions.Disable();
        inputActions.Dispose();
        inputActions = null;

        moveInput = Vector2.zero;
        jumpRequested = false;
    }

    private void FixedUpdate()
    {
        if (cameraTransform == null && Camera.main != null)
        {
            cameraTransform = Camera.main.transform;
        }

        float delta = Time.fixedDeltaTime;
        float cameraYaw = cameraTransform != null ? cameraTransform.eulerAngles.y : transform.eulerAngles.y;
        bool armed = propAnim.CurrentWeaponType != EWeaponType.Hide;

        isGrounded = Physics.CheckSphere(groundCheck.position, propData.GroundCheckRadius, propData.GroundLayer);
        if (!isGrounded && isRagdollIdle)
        {
            Bounds bounds = bodyCollider.bounds;
            Vector3 bottomCenter = new Vector3(bounds.center.x, bounds.min.y, bounds.center.z);
            isGrounded = Physics.CheckSphere(bottomCenter, propData.GroundCheckRadius, propData.GroundLayer);
        }

        // 점프 직후 몇 프레임은 체크 구체가 아직 바닥에 닿아 있으므로 상승 중에는 착지로 보지 않음
        isGrounded &= rb.linearVelocity.y <= GroundedMaxRiseSpeed;

        if (!wasGrounded && isGrounded)
        {
            propAnim.SetJumpState(false, false);
        }

        wasGrounded = isGrounded;

        Vector3 inputDirection = new Vector3(moveInput.x, 0f, moveInput.y).normalized;
        bool moving = inputDirection.magnitude >= 0.1f;
        float currentYaw = rb.rotation.eulerAngles.y;

        // 쓰러진 뒤 바닥에 튕겨 잠깐 뜨는 동안에도 입력이 없으면 쓰러짐을 유지함
        if (!armed && !moving && !jumpRequested && (isGrounded || isRagdollIdle))
        {
            if (!isRagdollIdle)
            {
                isRagdollIdle = true;
                rb.constraints = RigidbodyConstraints.None;

                Vector2 push = Random.insideUnitCircle.normalized;
                Vector3 force = new Vector3(push.x, 0f, push.y) * propData.PushForce
                    + Vector3.down * propData.DownwardForce;
                rb.AddForce(force, ForceMode.Impulse);
                rb.AddTorque(rb.rotation * Vector3.right * propData.RagdollToppleSpeed);
            }
        }
        else if (isRagdollIdle)
        {
            isRagdollIdle = false;
            rb.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            rb.angularVelocity = Vector3.zero;
            rb.rotation = Quaternion.Euler(0f, currentYaw, 0f);
        }

        float moveAngle = Mathf.Atan2(inputDirection.x, inputDirection.z) * Mathf.Rad2Deg + cameraYaw;

        // 무기를 들면 카메라 방향을, 맨손이면 이동 방향을 바라봄
        if (armed || moving)
        {
            float targetYaw = armed ? cameraYaw : moveAngle;
            currentYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref turnSmoothVelocity,
                propData.RotationSmoothTime, Mathf.Infinity, delta);
            rb.MoveRotation(Quaternion.Euler(0f, currentYaw, 0f));
        }

        PlanarVelocity = Vector3.zero;

        if (moving)
        {
            Vector3 moveDirection = Quaternion.Euler(0f, moveAngle, 0f) * Vector3.forward;
            rb.MovePosition(rb.position + moveDirection * (propData.MoveSpeed * delta));
            PlanarVelocity = moveDirection * propData.MoveSpeed;
        }

        if (jumpRequested && isGrounded)
        {
            rb.AddForce(Vector3.up * propData.JumpForce, ForceMode.Impulse);
            isGrounded = false;
            wasGrounded = false;
            propAnim.SetJumpState(true, true);
        }

        jumpRequested = false;
    }

    private void OnDrawGizmosSelected()
    {
        if (groundCheck == null || propData == null)
        {
            return;
        }

        Gizmos.color = Color.red;
        Gizmos.DrawWireSphere(groundCheck.position, propData.GroundCheckRadius);
    }

    // ***************************************************************
    // Input Event Handlers
    // ***************************************************************

    private void OnMovePerformed(InputAction.CallbackContext context)
    {
        moveInput = context.ReadValue<Vector2>();
    }

    private void OnMoveCanceled(InputAction.CallbackContext context)
    {
        moveInput = Vector2.zero;
    }

    private void OnJump(InputAction.CallbackContext context)
    {
        if (isGrounded)
        {
            jumpRequested = true;
        }
    }

    private void OnAttackStarted(InputAction.CallbackContext context)
    {
        propAnim.SetAttackState(true);
    }

    private void OnAttackCanceled(InputAction.CallbackContext context)
    {
        propAnim.SetAttackState(false);
    }

    /// <summary>숫자키 1~4를 <see cref="EWeaponType"/>으로 바꿔 장착함.</summary>
    private void OnChangeWeapon(InputAction.CallbackContext context)
    {
        int weaponIndex = Mathf.Clamp(Mathf.RoundToInt(context.ReadValue<float>()) - 1, 0, 3);
        propAnim.SetWeaponMode((EWeaponType)weaponIndex);
    }

    private void OnReloadStarted(InputAction.CallbackContext context)
    {
        propAnim.RequestReload();
    }
}
