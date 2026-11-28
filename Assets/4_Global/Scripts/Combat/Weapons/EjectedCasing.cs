using UnityEngine;

/// <summary>로컬 소유자 화면에서 물리 시뮬레이션으로 배출되어 바닥에 튕기고 구른 뒤 풀로 반환되는 탄피 오브젝트.</summary>
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(Collider))]
public class EjectedCasing : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const int IgnoreRaycastLayer = 2; // 히트스캔 및 조준 레이캐스트 제외 레이어
    private const int DefaultTerrainCollisionMask = (1 << 0) | (1 << 3) | (1 << 8); // Default, Ground, Wall

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>배출 후 즉시 비활성화되어 풀로 반환되기까지 유지되는 시간 (초 단위).</summary>
    [Header("수명")]
    [Min(0.1f)]
    [SerializeField] private float lifetime = 3f;

    /// <summary>탄피 배출 초기 최소 속력 (m/s).</summary>
    [Header("배출 물리")]
    [Min(0f)]
    [SerializeField] private float ejectSpeedMin = 1.5f;

    /// <summary>탄피 배출 초기 최대 속력 (m/s).</summary>
    [Min(0f)]
    [SerializeField] private float ejectSpeedMax = 2.0f;

    /// <summary>배출 방향에서 무작위로 퍼지는 최대 원뿔 각도 (도 단위).</summary>
    [Range(0f, 45f)]
    [SerializeField] private float ejectSpreadAngle = 11f;

    /// <summary>배출 직후 로컬 축 기준 초기 회전 각속도 최소값 (rad/s).</summary>
    [SerializeField] private Vector3 angularSpeedMin = new(7f, -2f, -11f);

    /// <summary>배출 직후 로컬 축 기준 초기 회전 각속도 최대값 (rad/s).</summary>
    [SerializeField] private Vector3 angularSpeedMax = new(12f, 2f, -6f);

    /// <summary>탄피 물리 시뮬레이션용 <see cref="Rigidbody"/> 컴포넌트.</summary>
    private Rigidbody _rb;

    /// <summary>배출 후 경과 시간 (초 단위).</summary>
    private float _elapsed;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        gameObject.layer = IgnoreRaycastLayer;

        int terrainMask = LayerMask.GetMask("Default", "Ground", "Wall");
        if (terrainMask == 0)
        {
            terrainMask = DefaultTerrainCollisionMask;
        }

        if (_rb != null)
        {
            _rb.excludeLayers = ~terrainMask;
        }
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;
        if (_elapsed >= lifetime)
        {
            gameObject.SetActive(false);
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="position"/>에서 <paramref name="rotation"/>의 <c>+Z</c> 방향으로 탄피를 물리 배출한다.</summary>
    public void Launch(Vector3 position, Quaternion rotation)
    {
        // 활성 상태 탄피를 재사용할 때 보간(Interpolate) 잔상과 이전 물리 상태를 초기화하기 위해 비활성화 후 재배치한다.
        if (gameObject.activeSelf)
        {
            gameObject.SetActive(false);
        }

        _elapsed = 0f;

        Quaternion randomStartRot = rotation * Quaternion.Euler(
            Random.Range(-15f, 15f),
            Random.Range(-30f, 30f),
            Random.Range(-30f, 30f));
        transform.SetPositionAndRotation(position, randomStartRot);
        gameObject.SetActive(true);

        if (_rb == null)
        {
            _rb = GetComponent<Rigidbody>();
            if (_rb == null) return;
        }

        Vector3 baseDir = rotation * Vector3.forward;
        Vector3 launchDir = Vector3.RotateTowards(
            baseDir,
            Random.onUnitSphere,
            Random.Range(0f, ejectSpreadAngle) * Mathf.Deg2Rad,
            0f);
        float speed = Random.Range(Mathf.Min(ejectSpeedMin, ejectSpeedMax), Mathf.Max(ejectSpeedMin, ejectSpeedMax));

        Vector3 localAngularVel = new Vector3(
            Random.Range(Mathf.Min(angularSpeedMin.x, angularSpeedMax.x), Mathf.Max(angularSpeedMin.x, angularSpeedMax.x)),
            Random.Range(Mathf.Min(angularSpeedMin.y, angularSpeedMax.y), Mathf.Max(angularSpeedMin.y, angularSpeedMax.y)),
            Random.Range(Mathf.Min(angularSpeedMin.z, angularSpeedMax.z), Mathf.Max(angularSpeedMin.z, angularSpeedMax.z)));

        _rb.linearVelocity = launchDir * speed;
        _rb.angularVelocity = rotation * localAngularVel;
    }
}
