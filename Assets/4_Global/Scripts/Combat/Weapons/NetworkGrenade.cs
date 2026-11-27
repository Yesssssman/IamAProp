using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>서버 권위 물리 시뮬레이션, 신관 타이머, 엄폐물 검사 기반 폭발 데미지 및 이펙트 훅을 처리하는 수류탄 투척체.</summary>
[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(SphereCollider))]
public class NetworkGrenade : NetworkBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const int GrenadeLayer = 2; // Ignore Raycast (사격 히트스캔 제외, 지형·플레이어 물리 충돌 유지)
    private const int DefaultDamageLayerMask = (1 << 6) | (1 << 7); // Player, Hitbox
    private const int DefaultCoverObstacleMask = (1 << 0) | (1 << 3) | (1 << 8); // Default, Ground, Wall
    protected const float CoverRayHeightOffset = 0.08f; // 바닥 안착 시 지면 자체에 엄폐 레이가 걸리지 않도록 하는 높이 오프셋 (미터)
    private const float BounceSfxCooldown = 0.12f; // 연속 충돌 시 바운스 사운드 중복 방지 간격 (초)
    private const float DespawnDelayAfterExplode = 0.05f; // 폭발 RPC 전파 보장을 위한 짧은 디스폰 지연 (초)

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>폭발 시 비활성화할 수류탄 모델 비주얼 루트 (비워두면 하위 렌더러 전체 숨김).</summary>
    [Header("비주얼 참조")]
    [SerializeField] private GameObject visualRoot;

    /// <summary>투척 직후 폭발까지 대기하는 기본 신관 시간 (초 단위).</summary>
    [Header("신관 및 폭발 판정")]
    [Min(0.1f)]
    [SerializeField] private float fuseTime = 3f;

    /// <summary>폭발 피해가 적용되는 최대 반경 (미터).</summary>
    [Min(0.1f)]
    [SerializeField] private float explosionRadius = 5f;

    /// <summary>폭발 중심 거리 0m 기준 최대 데미지.</summary>
    [Min(0f)]
    [SerializeField] private float maxDamage = 120f;

    /// <summary>폭발 반경 끝자락 기준 최소 데미지.</summary>
    [Min(0f)]
    [SerializeField] private float minDamage = 25f;

    /// <summary>투척자 본인도 자신의 수류탄 폭발 피해를 받는지 여부.</summary>
    [SerializeField] private bool canDamageThrower = true;

    /// <summary>투척 직후 투척자 콜라이더와의 충돌을 무시하는 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float ignoreThrowerCollisionDuration = 0.2f;

    /// <summary>폭발 반경 내 피격 대상을 탐색할 물리 레이어 마스크.</summary>
    [SerializeField] private LayerMask damageLayerMask = DefaultDamageLayerMask;

    /// <summary>폭발 중심과 피격 대상 사이 엄폐물(벽/지형) 차단을 검사할 레이어 마스크.</summary>
    [SerializeField] private LayerMask coverObstacleMask = DefaultCoverObstacleMask;

    /// <summary>폭발 시 생성할 VFX 프리팹 (비워두면 생략).</summary>
    [Header("VFX / SFX 확장 슬롯")]
    [SerializeField] private GameObject explosionVfxPrefab;

    /// <summary>생성된 폭발 VFX 오브젝트를 자동 파괴할 시간 (초 단위).</summary>
    [Min(0.1f)]
    [SerializeField] private float vfxAutoDestroyTime = 3f;

    /// <summary>폭발 시 재생할 오디오 클립 (비워두면 생략).</summary>
    [SerializeField] private AudioClip explosionSfx;

    /// <summary>투척 시 재생할 오디오 클립 (비워두면 생략).</summary>
    [SerializeField] private AudioClip throwSfx;

    /// <summary>벽이나 바닥에 튕길 때 재생할 오디오 클립 (비워두면 생략).</summary>
    [SerializeField] private AudioClip bounceSfx;

    /// <summary>폭발 사운드 재생 볼륨.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float explosionSfxVolume = 1f;

    /// <summary>투척 사운드 재생 볼륨.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float throwSfxVolume = 1f;

    /// <summary>바운스 사운드 재생 볼륨.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float bounceSfxVolume = 0.8f;

    /// <summary>바운스 사운드를 재생할 최소 충돌 상대 속력 (m/s).</summary>
    [Min(0f)]
    [SerializeField] private float minBounceSoundSpeed = 1.5f;

    /// <summary>VFX 에셋이 없을 때 폭발 위치와 반경을 반투명 구체 및 로그로 시각화할지 여부.</summary>
    [Header("디버그 시각화")]
    [SerializeField] private bool enableDebugVisualization = true;

    /// <summary>임시 디버그 폭발 구체가 유지되는 시간 (초 단위).</summary>
    [Min(0.05f)]
    [SerializeField] private float debugSphereDuration = 0.6f;

    /// <summary>임시 디버그 폭발 구체의 색상 및 투명도.</summary>
    [SerializeField] private Color debugSphereColor = new Color(1f, 0.35f, 0.1f, 0.35f);

    /// <summary>수류탄 물리 시뮬레이션용 <see cref="Rigidbody"/> 컴포넌트.</summary>
    private Rigidbody _rb;

    /// <summary>수류탄 충돌 판정용 <see cref="SphereCollider"/> 컴포넌트.</summary>
    private SphereCollider _sphereCollider;

    /// <summary>서버에서 투척자 킬러 기록에 사용하는 투척자 <see cref="NetworkObject"/>.</summary>
    private NetworkObject _throwerObject;

    /// <summary>서버에서 데미지 공격자 기록에 사용하는 투척자 <see cref="NetworkConnection"/>.</summary>
    private NetworkConnection _throwerConnection;

    /// <summary>충돌 무시 해제 시 복원할 투척자 콜라이더 목록.</summary>
    private readonly List<Collider> _ignoredThrowerColliders = new(8);

    /// <summary>폭발 범위 탐색용 콜라이더 재사용 버퍼.</summary>
    private readonly Collider[] _overlapBuffer = new Collider[32];

    /// <summary>폭발 1회당 중복 피격을 막기 위한 대상 목록 버퍼.</summary>
    private readonly List<IDamageable> _damagedTargets = new(16);

    /// <summary>대상별 폭발 중심에서 가장 가까운 대표 피격 좌표 버퍼.</summary>
    private readonly List<Vector3> _targetPoints = new(16);

    /// <summary>대상별 폭발 중심과의 최단 거리 제곱 버퍼.</summary>
    private readonly List<float> _targetDistSqrs = new(16);

    /// <summary>신관 카운트다운이 시작되었는지 여부 (서버 전용).</summary>
    private bool _isFuseArmed;

    /// <summary>폭발 판정이 이미 실행되었는지 여부.</summary>
    private bool _hasExploded;

    /// <summary>남은 신관 시간 (초 단위, 서버 전용).</summary>
    private float _fuseRemaining;

    /// <summary>투척자 충돌 무시 해제까지 남은 시간 (초 단위, 서버 전용).</summary>
    private float _ignoreCollisionRemaining;

    /// <summary>폭발 RPC 전송 후 디스폰 대기 여부 (서버 전용).</summary>
    private bool _pendingDespawn;

    /// <summary>디스폰까지 남은 지연 시간 (초 단위, 서버 전용).</summary>
    private float _despawnRemaining;

    /// <summary>다음 바운스 사운드 재생 허용 시각 (<see cref="Time.time"/> 기준, 서버 전용).</summary>
    private float _nextBounceSfxTime;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary>이 수류탄이 폭발할 때 월드 좌표를 알리는 클라이언트 이벤트 훅.</summary>
    public event Action<Vector3> OnExploded;

    /// <summary>씬의 어떤 수류탄이든 폭발할 때 월드 좌표를 알리는 전역 이벤트 훅.</summary>
    public static event Action<Vector3> OnAnyGrenadeExploded;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>수류탄 충돌 구체의 월드 반지름 (미터).</summary>
    public float ColliderRadius => _sphereCollider != null ? _sphereCollider.radius * Mathf.Max(transform.lossyScale.x, Mathf.Max(transform.lossyScale.y, transform.lossyScale.z)) : 0.07f;

    /// <summary>기본 신관 시간 (초 단위).</summary>
    public float FuseTime => fuseTime;

    /// <summary>폭발 피해 반경 (미터).</summary>
    public float ExplosionRadius => explosionRadius;

    /// <summary>투척 오디오 클립.</summary>
    public AudioClip ThrowSfx => throwSfx;

    /// <summary>투척 사운드 볼륨.</summary>
    public float ThrowSfxVolume => throwSfxVolume;

    /// <summary>피격 대상 탐색에 사용하는 물리 레이어 마스크.</summary>
    protected LayerMask DamageLayerMask => damageLayerMask;

    /// <summary>엄폐물(벽/지형) 차단을 검사하는 물리 레이어 마스크.</summary>
    protected LayerMask CoverObstacleMask => coverObstacleMask;

    /// <summary>폭발 반경 탐색에 재사용하는 콜라이더 버퍼.</summary>
    protected Collider[] OverlapBuffer => _overlapBuffer;

    /// <summary>폭발 시 생성할 VFX 프리팹.</summary>
    protected GameObject ExplosionVfxPrefab => explosionVfxPrefab;

    /// <summary>생성된 폭발 VFX 자동 파괴 시간 (초 단위).</summary>
    protected float VfxAutoDestroyTime => vfxAutoDestroyTime;

    /// <summary>디버그 시각화 활성화 여부.</summary>
    protected bool EnableDebugVisualization => enableDebugVisualization;

    /// <summary>임시 디버그 폭발 구체 유지 시간 (초 단위).</summary>
    protected float DebugSphereDuration => debugSphereDuration;

    /// <summary>임시 디버그 폭발 구체 색상.</summary>
    protected Color DebugSphereColor => debugSphereColor;

    /// <summary>씬 뷰 기즈모로 표시할 폭발 반경 (미터).</summary>
    protected virtual float GizmoRadius => explosionRadius;

    // ***************************************************************
    // Static Physics Formula
    // ***************************************************************

    /// <summary>조준 방향, 투척 힘 및 상방 보정으로 실제 투척과 궤적 예측이 공유하는 초기 속도 벡터(m/s)를 계산한다.</summary>
    public static Vector3 ComputeThrowVelocity(Vector3 aimDirection, float throwForce, float upwardBias)
    {
        Vector3 dir = aimDirection.sqrMagnitude > 0.0001f ? aimDirection.normalized : Vector3.forward;
        Vector3 launchDir = dir + Vector3.up * upwardBias;
        if (launchDir.sqrMagnitude <= 0.0001f)
        {
            launchDir = Vector3.forward;
        }
        return launchDir.normalized * Mathf.Max(0f, throwForce);
    }

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        _sphereCollider = GetComponent<SphereCollider>();
        gameObject.layer = GrenadeLayer;

        if (_rb != null)
        {
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.linearDamping = 0f;
            // 순수 클라이언트가 OnStartNetwork 이전에 로컬 물리를 실행하지 않도록 키네마틱으로 시작한다.
            _rb.isKinematic = true;
        }
    }

    private void Update()
    {
        if (!IsServerInitialized) return;

        if (_ignoredThrowerColliders.Count > 0)
        {
            _ignoreCollisionRemaining -= Time.deltaTime;
            if (_ignoreCollisionRemaining <= 0f)
            {
                RestoreThrowerCollisions();
            }
        }

        if (_pendingDespawn)
        {
            _despawnRemaining -= Time.deltaTime;
            if (_despawnRemaining <= 0f)
            {
                _pendingDespawn = false;
                if (IsSpawned)
                {
                    ServerManager.Despawn(NetworkObject);
                }
            }
            return;
        }

        if (!_isFuseArmed || _hasExploded) return;

        _fuseRemaining -= Time.deltaTime;
        if (_fuseRemaining > 0f) return;

        // 신관 만료: 서버에서 폭발 판정을 1회 실행하고 디스폰을 예약한다.
        _hasExploded = true;
        _isFuseArmed = false;
        RestoreThrowerCollisions();

        Vector3 explosionPos = transform.position;
        if (_rb != null)
        {
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.isKinematic = true;
        }
        if (_sphereCollider != null)
        {
            _sphereCollider.enabled = false;
        }
        HideVisuals();

        DetonateOnServer(explosionPos);
        _pendingDespawn = true;
        _despawnRemaining = DespawnDelayAfterExplode;
    }

    /// <summary>신관 만료 시 서버에서 폭발 판정과 클라이언트 연출 전파를 수행한다.</summary>
    [Server]
    protected virtual void DetonateOnServer(Vector3 explosionPos)
    {
        float safeRadius = Mathf.Max(0.1f, explosionRadius);
        int overlapCount = Physics.OverlapSphereNonAlloc(
            explosionPos,
            safeRadius,
            _overlapBuffer,
            damageLayerMask,
            QueryTriggerInteraction.Ignore);

        _damagedTargets.Clear();
        _targetPoints.Clear();
        _targetDistSqrs.Clear();

        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = _overlapBuffer[i];
            if (col == null) continue;

            IDamageable damageable = col.GetComponentInParent<IDamageable>();
            if (damageable == null || damageable.IsDead) continue;

            if (!canDamageThrower && _throwerObject != null && col.transform.IsChildOf(_throwerObject.transform))
            {
                continue;
            }

            Vector3 targetPoint = col.bounds.center;
            float distSqr = (targetPoint - explosionPos).sqrMagnitude;

            int existingIdx = _damagedTargets.IndexOf(damageable);
            if (existingIdx >= 0)
            {
                if (distSqr < _targetDistSqrs[existingIdx])
                {
                    _targetDistSqrs[existingIdx] = distSqr;
                    _targetPoints[existingIdx] = targetPoint;
                }
            }
            else
            {
                _damagedTargets.Add(damageable);
                _targetPoints.Add(targetPoint);
                _targetDistSqrs.Add(distSqr);
            }
        }

        Vector3 coverRayOrigin = explosionPos + Vector3.up * CoverRayHeightOffset;
        for (int i = 0; i < _damagedTargets.Count; i++)
        {
            IDamageable target = _damagedTargets[i];
            Vector3 targetPoint = _targetPoints[i];
            Vector3 toTargetFromOrigin = targetPoint - coverRayOrigin;
            float coverRayDist = toTargetFromOrigin.magnitude;

            // 폭발 중심과 대상 사이에 벽/지형 엄폐물이 있으면 피해를 차단한다.
            if (coverRayDist > 0.05f
                && Physics.Raycast(
                    coverRayOrigin,
                    toTargetFromOrigin / coverRayDist,
                    coverRayDist,
                    coverObstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            Vector3 delta = targetPoint - explosionPos;
            float distance = delta.magnitude;
            if (distance > safeRadius) continue;

            float t = Mathf.Clamp01(distance / safeRadius);
            float finalDamage = Mathf.Lerp(maxDamage, minDamage, t);
            if (finalDamage <= 0f) continue;

            Vector3 hitDir = distance > 0.0001f ? delta / distance : Vector3.up;
            DamageHitInfo hitInfo = new DamageHitInfo
            {
                HitDirection = hitDir,
                HitPoint = targetPoint,
                HasHitPoint = false,
                IsMelee = false,
                AttackerObject = _throwerObject
            };
            target.TakeDamage(finalDamage, _throwerConnection, in hitInfo);
        }

        OnExplodeClientRpc(explosionPos);
    }

    private void OnCollisionEnter(Collision collision)
    {
        if (!IsServerInitialized || _hasExploded) return;
        if (Time.time < _nextBounceSfxTime) return;
        if (collision.relativeVelocity.magnitude < minBounceSoundSpeed) return;

        _nextBounceSfxTime = Time.time + BounceSfxCooldown;
        if (bounceSfx != null)
        {
            PlayBounceSfxObserversRpc(transform.position);
        }
    }

    private void OnDrawGizmosSelected()
    {
        if (!enableDebugVisualization) return;

        Gizmos.color = new Color(debugSphereColor.r, debugSphereColor.g, debugSphereColor.b, 0.8f);
        Gizmos.DrawWireSphere(transform.position, GizmoRadius);
    }

    // ***************************************************************
    // NetworkBehaviour
    // ***************************************************************

    public override void OnStartServer()
    {
        _hasExploded = false;
        _pendingDespawn = false;
        _nextBounceSfxTime = 0f;

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.linearDamping = 0f;
        }
    }

    public override void OnStartNetwork()
    {
        if (!IsServerInitialized && _rb != null)
        {
            _rb.isKinematic = true;
        }
    }

    public override void OnStopNetwork()
    {
        RestoreThrowerCollisions();
        _isFuseArmed = false;
        _pendingDespawn = false;
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>서버에서 투척자 정보, 초기 속도/토크, 신관 시간 및 폭발 수치를 초기화하고 투척을 시작한다.</summary>
    [Server]
    public void Initialize(
        NetworkObject throwerObject,
        NetworkConnection throwerConnection,
        Vector3 throwVelocity,
        Vector3 launchTorque,
        float fuseTimeOverride = -1f,
        float maxDamageOverride = -1f,
        float minDamageOverride = -1f,
        float explosionRadiusOverride = -1f)
    {
        _throwerObject = throwerObject;
        _throwerConnection = throwerConnection;
        _hasExploded = false;
        _pendingDespawn = false;

        if (fuseTimeOverride > 0f) fuseTime = fuseTimeOverride;
        if (maxDamageOverride >= 0f) maxDamage = maxDamageOverride;
        if (minDamageOverride >= 0f) minDamage = minDamageOverride;
        if (explosionRadiusOverride > 0f) explosionRadius = explosionRadiusOverride;

        RestoreThrowerCollisions();
        if (_sphereCollider != null && _throwerObject != null && ignoreThrowerCollisionDuration > 0f)
        {
            Collider[] throwerColliders = _throwerObject.GetComponentsInChildren<Collider>(true);
            for (int i = 0; i < throwerColliders.Length; i++)
            {
                Collider col = throwerColliders[i];
                if (col == null) continue;
                Physics.IgnoreCollision(_sphereCollider, col, true);
                _ignoredThrowerColliders.Add(col);
            }
            _ignoreCollisionRemaining = ignoreThrowerCollisionDuration;
        }

        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.linearDamping = 0f;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.AddForce(throwVelocity, ForceMode.VelocityChange);
            _rb.AddTorque(launchTorque, ForceMode.VelocityChange);
        }

        _fuseRemaining = Mathf.Max(0.1f, fuseTime);
        _isFuseArmed = true;

        if (throwSfx != null)
        {
            PlayThrowSfxObserversRpc(transform.position);
        }
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>모든 클라이언트에 폭발 연출(VFX/SFX/이벤트 훅 및 디버그 시각화)을 전파한다.</summary>
    [ObserversRpc]
    private void OnExplodeClientRpc(Vector3 position)
    {
        HideVisuals();

        if (explosionVfxPrefab != null)
        {
            GameObject vfx = Instantiate(explosionVfxPrefab, position, Quaternion.identity);
            Destroy(vfx, Mathf.Max(0.1f, vfxAutoDestroyTime));
        }

        if (explosionSfx != null)
        {
            AudioSource.PlayClipAtPoint(explosionSfx, position, explosionSfxVolume);
        }

        InvokeExplodedEvents(position);

        if (enableDebugVisualization)
        {
            Debug.Log($"[NetworkGrenade] 폭발 발생! 위치: {position}, 반경: {explosionRadius:F1}m");
            SpawnDebugExplosionSphere(position, explosionRadius);
        }
    }

    /// <summary>폭발 이벤트 훅(<see cref="OnExploded"/>, <see cref="OnAnyGrenadeExploded"/>)을 호출한다.</summary>
    protected void InvokeExplodedEvents(Vector3 position)
    {
        OnExploded?.Invoke(position);
        OnAnyGrenadeExploded?.Invoke(position);
    }

    /// <summary>디버그 시각화가 켜져 있을 때 <paramref name="radius"/> 크기의 임시 반투명 구체를 생성한다.</summary>
    protected void SpawnDebugExplosionSphere(Vector3 position, float radius)
    {
        GameObject debugSphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        debugSphere.name = "GrenadeExplosionDebugSphere";
        debugSphere.transform.position = position;
        debugSphere.transform.localScale = Vector3.one * (radius * 2f);

        Collider sphereCol = debugSphere.GetComponent<Collider>();
        if (sphereCol != null)
        {
            Destroy(sphereCol);
        }

        Renderer renderer = debugSphere.GetComponent<Renderer>();
        if (renderer != null)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
            if (shader == null)
            {
                shader = Shader.Find("Sprites/Default");
            }

            if (shader != null)
            {
                Material mat = new Material(shader);
                mat.color = debugSphereColor;
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", debugSphereColor);
                }
                if (mat.HasProperty("_Surface"))
                {
                    mat.SetFloat("_Surface", 1f); // Transparent
                    mat.SetFloat("_Blend", 0f); // Alpha
                    mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                    mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                    mat.SetFloat("_ZWrite", 0f);
                    mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                    mat.renderQueue = (int)RenderQueue.Transparent;
                }
                renderer.sharedMaterial = mat;
                Destroy(mat, Mathf.Max(0.05f, debugSphereDuration) + 0.05f);
            }

            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;
        }

        Destroy(debugSphere, Mathf.Max(0.05f, debugSphereDuration));
    }

    /// <summary>모든 클라이언트에서 투척 사운드를 재생한다.</summary>
    [ObserversRpc]
    private void PlayThrowSfxObserversRpc(Vector3 position)
    {
        if (throwSfx != null)
        {
            AudioSource.PlayClipAtPoint(throwSfx, position, throwSfxVolume);
        }
    }

    /// <summary>모든 클라이언트에서 수류탄 충돌 바운스 사운드를 재생한다.</summary>
    [ObserversRpc]
    private void PlayBounceSfxObserversRpc(Vector3 position)
    {
        if (bounceSfx != null)
        {
            AudioSource.PlayClipAtPoint(bounceSfx, position, bounceSfxVolume);
        }
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>무시해 두었던 투척자 콜라이더와의 충돌을 복원한다.</summary>
    private void RestoreThrowerCollisions()
    {
        if (_sphereCollider != null)
        {
            for (int i = 0; i < _ignoredThrowerColliders.Count; i++)
            {
                Collider col = _ignoredThrowerColliders[i];
                if (col != null)
                {
                    Physics.IgnoreCollision(_sphereCollider, col, false);
                }
            }
        }
        _ignoredThrowerColliders.Clear();
        _ignoreCollisionRemaining = 0f;
    }

    /// <summary>폭발 직후 디스폰 전까지 수류탄 모델 비주얼을 즉시 숨긴다.</summary>
    protected void HideVisuals()
    {
        if (visualRoot != null)
        {
            visualRoot.SetActive(false);
            return;
        }

        Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
        for (int i = 0; i < renderers.Length; i++)
        {
            renderers[i].enabled = false;
        }
    }
}
