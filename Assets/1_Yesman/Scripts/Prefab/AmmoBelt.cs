using UnityEngine;

/// <summary><see cref="GunScript"/>가 발사될 때마다 탄띠를 한 칸씩 이동시키고, 남은 탄약 수만큼 총알을 보여줌.</summary>
[RequireComponent(typeof(GunScript))]
public class AmmoBelt : MonoBehaviour
{
    /// <summary>탄창의 자식인 탄띠 총알. 0번이 총에 먼저 들어가는 총알.</summary>
    [SerializeField]
    private Transform[] bullets;

    /// <summary>탄띠가 총알 한 칸만큼 이동하는 시간 (초).</summary>
    [SerializeField]
    private float feedDuration = 0.06f;

    private GunScript gun;

    /// <summary><see cref="bullets"/>의 원래 로컬 위치.</summary>
    private Vector3[] restPositions;

    /// <summary>탄띠 이동을 시작한 뒤 지난 시간 (초). 이동 중이 아니면 음수.</summary>
    private float feedElapsed = -1f;

    /// <summary>탄띠 이동이 끝날 때 남아 있을 탄약 수. 모르면 null.</summary>
    private int? feedRemainingAmmo;

    private void Awake()
    {
        gun = GetComponent<GunScript>();
        restPositions = new Vector3[bullets.Length];

        for (int i = 0; i < bullets.Length; i++)
        {
            restPositions[i] = bullets[i].localPosition;
        }
    }

    private void OnEnable()
    {
        gun.Fired += HandleFired;
        gun.LoadedAmmoSet += HandleLoadedAmmoSet;
        gun.MagazineDetached += HandleMagazineDetached;
        gun.MagazineAttached += HandleMagazineAttached;
    }

    private void OnDisable()
    {
        gun.Fired -= HandleFired;
        gun.LoadedAmmoSet -= HandleLoadedAmmoSet;
        gun.MagazineDetached -= HandleMagazineDetached;
        gun.MagazineAttached -= HandleMagazineAttached;
    }

    private void Update()
    {
        if (feedElapsed < 0f)
        {
            return;
        }

        feedElapsed += Time.deltaTime;

        if (feedElapsed >= feedDuration)
        {
            ResetBelt(feedRemainingAmmo);
            return;
        }

        float t = feedElapsed / feedDuration;

        // 0번 총알은 움직이지 않고, 나머지는 앞 총알의 원래 위치로 이동
        for (int i = 1; i < bullets.Length; i++)
        {
            bullets[i].localPosition = Vector3.Lerp(restPositions[i], restPositions[i - 1], t);
        }
    }

    /// <summary>탄띠 한 칸 이동을 시작함.</summary>
    private void HandleFired(int? remainingAmmo)
    {
        // 이전 이동이 끝나기 전에 다시 쏘면 먼저 원위치로 되돌림
        if (feedElapsed >= 0f)
        {
            ResetBelt(feedRemainingAmmo);
        }

        feedRemainingAmmo = remainingAmmo;
        feedElapsed = 0f;
    }

    private void HandleLoadedAmmoSet(int loadedAmmo)
    {
        ResetBelt(loadedAmmo);
    }

    /// <summary>떨어진 탄창 복제본의 총알을 모두 숨김.</summary>
    private void HandleMagazineDetached(GameObject droppedMagazine)
    {
        foreach (Transform bullet in bullets)
        {
            droppedMagazine.transform.GetChild(bullet.GetSiblingIndex()).gameObject.SetActive(false);
        }
    }

    /// <summary>새 탄창의 총알을 모두 보여줌.</summary>
    private void HandleMagazineAttached()
    {
        ResetBelt(bullets.Length);
    }

    /// <summary>탄띠를 보간 없이 원위치로 되돌리고, <paramref name="visibleAmmo"/>가 있으면 앞에서부터 그 수만큼만 총알을 보여줌.</summary>
    private void ResetBelt(int? visibleAmmo)
    {
        feedElapsed = -1f;

        for (int i = 0; i < bullets.Length; i++)
        {
            bullets[i].localPosition = restPositions[i];

            if (visibleAmmo.HasValue)
            {
                bullets[i].gameObject.SetActive(i < visibleAmmo.Value);
            }
        }
    }
}
