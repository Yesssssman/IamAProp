using UnityEngine;
using UnityEngine.Rendering;

/// <summary>로컬 소유자 화면에 수류탄 포물선 낙하 궤적(<see cref="LineRenderer"/>)과 착탄 지점 마커를 표시한다.</summary>
public class GrenadeTrajectoryPreview : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const int DefaultCollisionMask = (1 << 0) | (1 << 3) | (1 << 6) | (1 << 8); // Default, Ground, Player, Wall
    private const float ImpactSurfaceOffset = 0.02f; // 착탄 마커와 끝점이 표면에 파묻히지 않도록 띄우는 거리 (미터)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>궤적 곡선을 구성할 최대 샘플 구간 수.</summary>
    [Header("궤적 샘플링")]
    [Range(5, 120)]
    [SerializeField] private int sampleCount = 40;

    /// <summary>각 샘플 포인트 사이의 시뮬레이션 시간 간격 (초 단위).</summary>
    [Range(0.01f, 0.2f)]
    [SerializeField] private float timeStep = 0.05f;

    /// <summary>궤적 충돌 검사에 사용할 구체 캐스트 반지름 (0이면 Raycast 사용, 미터).</summary>
    [Min(0f)]
    [SerializeField] private float castRadius = 0.07f;

    /// <summary>궤적이 차단되는 벽/바닥/플레이어 레이어 마스크.</summary>
    [SerializeField] private LayerMask collisionMask = DefaultCollisionMask;

    /// <summary>궤적 선의 시작·끝 두께 (미터).</summary>
    [Header("선 스타일")]
    [Min(0.002f)]
    [SerializeField] private float lineWidth = 0.03f;

    /// <summary>궤적 시작 지점 색상.</summary>
    [SerializeField] private Color lineStartColor = new Color(1f, 0.88f, 0.25f, 0.95f);

    /// <summary>궤적 끝 지점 색상.</summary>
    [SerializeField] private Color lineEndColor = new Color(1f, 0.35f, 0.12f, 0.4f);

    /// <summary>궤적 선에 사용할 커스텀 머티리얼 (비워두면 기본 언릿 머티리얼 자동 생성).</summary>
    [SerializeField] private Material lineMaterial;

    /// <summary>충돌 지점에 착탄 마커를 표시할지 여부.</summary>
    [Header("착탄 마커")]
    [SerializeField] private bool showImpactMarker = true;

    /// <summary>착탄 마커 지름 크기 (미터).</summary>
    [Min(0.02f)]
    [SerializeField] private float impactMarkerScale = 0.22f;

    /// <summary>착탄 마커 색상 및 투명도.</summary>
    [SerializeField] private Color impactMarkerColor = new Color(1f, 0.32f, 0.12f, 0.85f);

    /// <summary>포물선 궤적을 그리는 <see cref="LineRenderer"/> 컴포넌트.</summary>
    private LineRenderer _lineRenderer;

    /// <summary>런타임에 동적 생성한 궤적 머티리얼 참조.</summary>
    private Material _runtimeLineMaterial;

    /// <summary>착탄 지점을 표시하는 마커 오브젝트.</summary>
    private GameObject _impactMarker;

    /// <summary>런타임에 동적 생성한 착탄 마커 머티리얼 참조.</summary>
    private Material _runtimeMarkerMaterial;

    /// <summary>궤적 샘플 좌표를 저장하는 재사용 배열.</summary>
    private Vector3[] _pointsBuffer;

    /// <summary>구간 충돌 판정 결과를 저장하는 재사용 버퍼.</summary>
    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>현재 궤적 미리보기가 화면에 표시 중인지 여부.</summary>
    public bool IsVisible => _lineRenderer != null && _lineRenderer.enabled;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void OnDisable()
    {
        HideTrajectory();
    }

    private void OnDestroy()
    {
        if (_impactMarker != null)
        {
            Destroy(_impactMarker);
            _impactMarker = null;
        }

        if (_runtimeLineMaterial != null)
        {
            Destroy(_runtimeLineMaterial);
            _runtimeLineMaterial = null;
        }

        if (_runtimeMarkerMaterial != null)
        {
            Destroy(_runtimeMarkerMaterial);
            _runtimeMarkerMaterial = null;
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="startPosition"/>과 초기 속도 <paramref name="initialVelocity"/>로 포물선 궤적 선과 착탄 마커를 갱신한다.</summary>
    public void ShowTrajectory(Vector3 startPosition, Vector3 initialVelocity, Transform ignoreRoot)
    {
        if (_lineRenderer == null)
        {
            Transform existing = transform.Find("GrenadeTrajectoryLine");
            GameObject lineObj = existing != null ? existing.gameObject : new GameObject("GrenadeTrajectoryLine");
            if (existing == null)
            {
                lineObj.transform.SetParent(transform, false);
            }

            _lineRenderer = lineObj.GetComponent<LineRenderer>();
            if (_lineRenderer == null)
            {
                _lineRenderer = lineObj.AddComponent<LineRenderer>();
            }

            _lineRenderer.useWorldSpace = true;
            _lineRenderer.shadowCastingMode = ShadowCastingMode.Off;
            _lineRenderer.receiveShadows = false;
            _lineRenderer.numCapVertices = 4;
            _lineRenderer.numCornerVertices = 4;
            _lineRenderer.alignment = LineAlignment.View;
            _lineRenderer.enabled = false;

            if (lineMaterial != null)
            {
                _lineRenderer.sharedMaterial = lineMaterial;
            }
            else
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null)
                {
                    shader = Shader.Find("Universal Render Pipeline/Unlit");
                }
                if (shader != null)
                {
                    _runtimeLineMaterial = new Material(shader);
                    _lineRenderer.sharedMaterial = _runtimeLineMaterial;
                }
            }
        }

        if (_impactMarker == null && showImpactMarker)
        {
            _impactMarker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            _impactMarker.name = "GrenadeImpactMarker";
            _impactMarker.SetActive(false);

            Collider markerCol = _impactMarker.GetComponent<Collider>();
            if (markerCol != null)
            {
                Destroy(markerCol);
            }

            Renderer renderer = _impactMarker.GetComponent<Renderer>();
            if (renderer != null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null)
                {
                    shader = Shader.Find("Sprites/Default");
                }

                if (shader != null)
                {
                    _runtimeMarkerMaterial = new Material(shader);
                    _runtimeMarkerMaterial.color = impactMarkerColor;
                    if (_runtimeMarkerMaterial.HasProperty("_BaseColor"))
                    {
                        _runtimeMarkerMaterial.SetColor("_BaseColor", impactMarkerColor);
                    }
                    if (_runtimeMarkerMaterial.HasProperty("_Surface"))
                    {
                        _runtimeMarkerMaterial.SetFloat("_Surface", 1f);
                        _runtimeMarkerMaterial.SetFloat("_Blend", 0f);
                        _runtimeMarkerMaterial.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                        _runtimeMarkerMaterial.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                        _runtimeMarkerMaterial.SetFloat("_ZWrite", 0f);
                        _runtimeMarkerMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                        _runtimeMarkerMaterial.renderQueue = (int)RenderQueue.Transparent;
                    }
                    renderer.sharedMaterial = _runtimeMarkerMaterial;
                }

                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
        }

        int maxPoints = Mathf.Max(2, sampleCount + 1);
        if (_pointsBuffer == null || _pointsBuffer.Length < maxPoints)
        {
            _pointsBuffer = new Vector3[maxPoints];
        }

        Vector3 gravity = Physics.gravity;
        Vector3 prevPoint = startPosition;
        _pointsBuffer[0] = startPosition;
        int pointCount = 1;
        bool hasImpact = false;
        Vector3 impactPoint = Vector3.zero;
        Vector3 impactNormal = Vector3.up;

        for (int i = 1; i <= sampleCount; i++)
        {
            float t = i * timeStep;
            Vector3 nextPoint = startPosition + initialVelocity * t + 0.5f * gravity * (t * t);
            Vector3 segment = nextPoint - prevPoint;
            float segmentLength = segment.magnitude;

            if (segmentLength > 0.0001f)
            {
                Vector3 segmentDir = segment / segmentLength;
                int hitCount = castRadius > 0f
                    ? Physics.SphereCastNonAlloc(
                        prevPoint,
                        castRadius,
                        segmentDir,
                        _hitBuffer,
                        segmentLength,
                        collisionMask,
                        QueryTriggerInteraction.Ignore)
                    : Physics.RaycastNonAlloc(
                        prevPoint,
                        segmentDir,
                        _hitBuffer,
                        segmentLength,
                        collisionMask,
                        QueryTriggerInteraction.Ignore);

                bool foundHit = false;
                float closestDist = float.MaxValue;
                RaycastHit closestHit = default;
                for (int h = 0; h < hitCount; h++)
                {
                    RaycastHit hit = _hitBuffer[h];
                    if (hit.collider == null || hit.transform == null) continue;
                    if (ignoreRoot != null && hit.transform.IsChildOf(ignoreRoot)) continue;

                    if (hit.distance < closestDist)
                    {
                        closestDist = hit.distance;
                        closestHit = hit;
                        foundHit = true;
                    }
                }

                if (foundHit)
                {
                    impactNormal = closestHit.normal.sqrMagnitude > 0.0001f ? closestHit.normal.normalized : Vector3.up;
                    impactPoint = closestHit.point + impactNormal * ImpactSurfaceOffset;
                    _pointsBuffer[pointCount++] = impactPoint;
                    hasImpact = true;
                    break;
                }
            }

            _pointsBuffer[pointCount++] = nextPoint;
            prevPoint = nextPoint;
        }

        _lineRenderer.startWidth = lineWidth;
        _lineRenderer.endWidth = lineWidth * 0.65f;
        _lineRenderer.startColor = lineStartColor;
        _lineRenderer.endColor = lineEndColor;
        _lineRenderer.positionCount = pointCount;
        for (int i = 0; i < pointCount; i++)
        {
            _lineRenderer.SetPosition(i, _pointsBuffer[i]);
        }
        _lineRenderer.enabled = true;

        if (_impactMarker != null)
        {
            if (showImpactMarker && hasImpact)
            {
                _impactMarker.transform.SetPositionAndRotation(
                    impactPoint,
                    Quaternion.FromToRotation(Vector3.up, impactNormal));
                _impactMarker.transform.localScale = new Vector3(impactMarkerScale, 0.02f, impactMarkerScale);
                if (!_impactMarker.activeSelf)
                {
                    _impactMarker.SetActive(true);
                }
            }
            else if (_impactMarker.activeSelf)
            {
                _impactMarker.SetActive(false);
            }
        }
    }

    /// <summary>궤적 선과 착탄 마커를 즉시 숨긴다.</summary>
    public void HideTrajectory()
    {
        if (_lineRenderer != null)
        {
            _lineRenderer.enabled = false;
            _lineRenderer.positionCount = 0;
        }

        if (_impactMarker != null && _impactMarker.activeSelf)
        {
            _impactMarker.SetActive(false);
        }
    }
}
