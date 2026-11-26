using FishNet.Connection;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;

/// <summary>
/// FishNet 환경에서 경첩 축을 기준으로 부드럽게 열리고 닫히는 문·창문 컨트롤러.
/// 열리는 도중 재입력 시 현재 각도에서 즉시 반대 방향으로 되돌아간다.
/// </summary>
public class NetworkHingedPortal : NetworkBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float ServerRangeMargin = 1.5f; // 서버 거리 검증 시 네트워크 지연 및 문 폭 여유 거리 (미터)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>경첩 회전 기준 로컬 축 (문: Y축, 상향 창문: X축).</summary>
    [SerializeField] private Vector3 hingeAxis = Vector3.up;

    /// <summary>열렸을 때의 목표 회전 각도 (도 단위).</summary>
    [SerializeField] private float openAngle = -95f;

    /// <summary>초당 회전 속도 (도/초).</summary>
    [SerializeField] private float openSpeed = 180f;

    /// <summary>플레이어가 상호작용(<c>E</c>)할 수 있는 최대 거리 (미터).</summary>
    [SerializeField] private float interactRange = 3.2f;

    /// <summary>서버 권한으로 동기화되는 현재 개방 목표 상태.</summary>
    private readonly SyncVar<bool> _isOpen = new(new SyncTypeSettings(0f, Channel.Reliable));

    /// <summary>닫힘 기준 초기 로컬 회전값.</summary>
    private Quaternion _closedLocalRotation;

    /// <summary>현재 적용 중인 경첩 회전 각도 (도 단위).</summary>
    private float _currentAngle;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>플레이어 상호작용 허용 거리 (미터).</summary>
    public float InteractRange => interactRange;

    /// <summary>현재 열림 목표 상태 여부.</summary>
    public bool IsOpen => _isOpen.Value;

    // ***************************************************************
    // MonoBehaviour & NetworkBehaviour
    // ***************************************************************

    private void Awake()
    {
        _closedLocalRotation = transform.localRotation;
        _currentAngle = 0f;
    }

    public override void OnStartServer()
    {
        base.OnStartServer();
        _currentAngle = _isOpen.Value ? openAngle : 0f;
        transform.localRotation = _closedLocalRotation * Quaternion.AngleAxis(_currentAngle, hingeAxis);
    }

    public override void OnStartClient()
    {
        base.OnStartClient();
        _currentAngle = _isOpen.Value ? openAngle : 0f;
        transform.localRotation = _closedLocalRotation * Quaternion.AngleAxis(_currentAngle, hingeAxis);
    }

    private void Update()
    {
        float targetAngle = _isOpen.Value ? openAngle : 0f;
        if (Mathf.Approximately(_currentAngle, targetAngle)) return;

        _currentAngle = Mathf.MoveTowards(_currentAngle, targetAngle, openSpeed * Time.deltaTime);
        transform.localRotation = _closedLocalRotation * Quaternion.AngleAxis(_currentAngle, hingeAxis);
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>문 또는 창문의 개폐 상태 전환을 서버에 요청한다.</summary>
    public void RequestToggle()
    {
        if (!IsSpawned) return;
        RequestToggleServerRpc();
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>서버에서 요청 플레이어의 생존 여부와 거리를 검증한 뒤 개폐 상태를 반전한다.</summary>
    [ServerRpc(RequireOwnership = false)]
    private void RequestToggleServerRpc(NetworkConnection sender = null)
    {
        if (sender == null || sender.FirstObject == null) return;

        PlayerHealth health = sender.FirstObject.GetComponent<PlayerHealth>();
        if (health != null && health.IsDead) return;

        float maxDist = interactRange + ServerRangeMargin;
        Vector3 delta = sender.FirstObject.transform.position - transform.position;
        if (delta.sqrMagnitude > maxDist * maxDist) return;

        _isOpen.Value = !_isOpen.Value;
    }
}
