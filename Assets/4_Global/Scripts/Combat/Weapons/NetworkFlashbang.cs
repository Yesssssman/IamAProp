using System.Collections.Generic;
using FishNet.Object;
using UnityEngine;

/// <summary>서버 권위 반경·엄폐물 검증과 로컬 카메라 정면 시선 판정을 결합한 네트워크 섬광탄 투척체.</summary>
public class NetworkFlashbang : NetworkGrenade
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>섬광탄 효과가 도달하는 최대 유효 반경 (미터).</summary>
    [Header("섬광탄 판정 설정")]
    [Min(0.5f)]
    [SerializeField] private float flashRadius = 15f;

    /// <summary>정면 피격으로 판정할 카메라 시선 기준 최대 각도 (도 단위).</summary>
    [Range(10f, 180f)]
    [SerializeField] private float flashLookAngle = 70f;

    /// <summary>반경 끝자락 또는 비스듬한 시선에서 적용할 최소 섬광 지속 시간 (초 단위).</summary>
    [Min(0.2f)]
    [SerializeField] private float minFlashDuration = 1.5f;

    /// <summary>초근접 정면 응시 시 적용할 최대 섬광 지속 시간 (초 단위).</summary>
    [Min(0.2f)]
    [SerializeField] private float maxFlashDuration = 4.5f;

    /// <summary>비정면 피격 또는 반경 외 플레이어에게 재생할 기본 섬광탄 폭발음 (비워두면 생략).</summary>
    [Header("섬광탄 사운드")]
    [SerializeField] private AudioClip normalPopSfx;

    /// <summary>정면 피격 플레이어에게 재생할 이명 오디오 클립 (비워두면 생략).</summary>
    [SerializeField] private AudioClip tinnitusSfx;

    /// <summary>기본 폭발음 재생 볼륨.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float normalPopVolume = 1f;

    /// <summary>이명 사운드 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float tinnitusVolume = 1f;

    /// <summary>서버에서 중복 판정을 방지하기 위해 기록하는 플레이어 체력 버퍼.</summary>
    private readonly List<PlayerHealth> _candidatePlayers = new(16);

    /// <summary>서버 엄폐물(LoS) 검증을 통과한 플레이어 <c>OwnerId</c> 목록 버퍼.</summary>
    private readonly List<int> _losPassedClientIds = new(16);

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>섬광탄 유효 반경 (미터).</summary>
    public float FlashRadius => flashRadius;

    /// <inheritdoc/>
    protected override float GizmoRadius => flashRadius;

    // ***************************************************************
    // NetworkGrenade
    // ***************************************************************

    /// <summary>서버에서 넓은 반경 내 생존 플레이어와 벽 엄폐물(LoS)을 검증한 뒤 클라이언트에 폭발을 전파한다.</summary>
    [Server]
    protected override void DetonateOnServer(Vector3 explosionPos)
    {
        float safeRadius = Mathf.Max(0.5f, flashRadius);
        int overlapCount = Physics.OverlapSphereNonAlloc(
            explosionPos,
            safeRadius,
            OverlapBuffer,
            DamageLayerMask,
            QueryTriggerInteraction.Ignore);

        _candidatePlayers.Clear();
        _losPassedClientIds.Clear();

        Vector3 coverRayOrigin = explosionPos + Vector3.up * CoverRayHeightOffset;
        for (int i = 0; i < overlapCount; i++)
        {
            Collider col = OverlapBuffer[i];
            if (col == null) continue;

            PlayerHealth playerHealth = col.GetComponentInParent<PlayerHealth>();
            if (playerHealth == null || playerHealth.IsDead) continue;
            if (_candidatePlayers.Contains(playerHealth)) continue;

            _candidatePlayers.Add(playerHealth);

            PlayerController playerController = playerHealth.GetComponent<PlayerController>();
            Vector3 targetPoint = playerController != null
                ? playerController.GetCameraTargetPosition()
                : col.bounds.center;

            Vector3 toTarget = targetPoint - coverRayOrigin;
            float rayDist = toTarget.magnitude;

            // 폭발 중심과 대상 시점 사이에 벽/지형 엄폐물이 있으면 섬광 효과를 차단한다.
            if (rayDist > 0.05f
                && Physics.Raycast(
                    coverRayOrigin,
                    toTarget / rayDist,
                    rayDist,
                    CoverObstacleMask,
                    QueryTriggerInteraction.Ignore))
            {
                continue;
            }

            if (playerHealth.Owner != null && playerHealth.Owner.IsValid)
            {
                _losPassedClientIds.Add(playerHealth.OwnerId);
            }
        }

        OnFlashbangDetonatedClientRpc(explosionPos, safeRadius, _losPassedClientIds.ToArray());
    }

    // ***************************************************************
    // Network RPCs
    // ***************************************************************

    /// <summary>모든 클라이언트에 섬광탄 폭발을 전파하고, LoS 통과 로컬 소유자는 정면 시선 판정 후 섬광 연출을 실행한다.</summary>
    [ObserversRpc]
    private void OnFlashbangDetonatedClientRpc(Vector3 explosionPos, float radius, int[] losPassedClientIds)
    {
        HideVisuals();

        if (ExplosionVfxPrefab != null)
        {
            GameObject vfx = Instantiate(ExplosionVfxPrefab, explosionPos, Quaternion.identity);
            Destroy(vfx, Mathf.Max(0.1f, VfxAutoDestroyTime));
        }

        InvokeExplodedEvents(explosionPos);

        if (EnableDebugVisualization)
        {
            Debug.Log($"[NetworkFlashbang] 섬광 폭발! 위치: {explosionPos}, 반경: {radius:F1}m, LoS 통과 인원: {(losPassedClientIds != null ? losPassedClientIds.Length : 0)}");
            SpawnDebugExplosionSphere(explosionPos, radius);
        }

        bool isLocalFrontFlashed = false;
        PlayerController localPlayer = PlayerController.LocalPlayer;
        if (localPlayer != null
            && localPlayer.IsSpawned
            && localPlayer.IsOwner
            && !localPlayer.IsDead
            && losPassedClientIds != null)
        {
            int localOwnerId = localPlayer.OwnerId;
            bool passedServerLos = false;
            for (int i = 0; i < losPassedClientIds.Length; i++)
            {
                if (losPassedClientIds[i] == localOwnerId)
                {
                    passedServerLos = true;
                    break;
                }
            }

            if (passedServerLos && FlashbangEffectController.Instance != null)
            {
                isLocalFrontFlashed = FlashbangEffectController.Instance.TryApplyFlashHit(
                    explosionPos,
                    radius,
                    flashLookAngle,
                    minFlashDuration,
                    maxFlashDuration,
                    tinnitusSfx,
                    tinnitusVolume);
            }
        }

        // 비정면 피격, 반경 외, 엄폐물 뒤, 사망/관전 플레이어는 실명 없이 기본 폭발음만 재생한다.
        if (!isLocalFrontFlashed && normalPopSfx != null)
        {
            AudioSource.PlayClipAtPoint(normalPopSfx, explosionPos, normalPopVolume);
        }
    }
}
