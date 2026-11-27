using System;
using UnityEngine;
using Random = UnityEngine.Random;

public class GunScript : MonoBehaviour
{
    // 탄창 GameObject
    [SerializeField]
    private GameObject Magazine;

    /// <summary>떨어트린 탄창을 던지는 속도 (m/s).</summary>
    [SerializeField]
    private float magazineLaunchSpeed = 1f;

    /// <summary>던지는 방향이 launchDirection에서 무작위로 벗어나는 최대 각도 (도).</summary>
    [SerializeField]
    private float magazineLaunchAngle = 45f;

    /// <summary>다른 부모로 옮기기 전 탄창의 부모. 옮기지 않았으면 null.</summary>
    private Transform _magazineRestParent;

    /// <summary>다른 부모로 옮기기 전 탄창의 로컬 위치.</summary>
    private Vector3 _magazineRestLocalPosition;

    /// <summary>다른 부모로 옮기기 전 탄창의 로컬 회전.</summary>
    private Quaternion _magazineRestLocalRotation;

    /// <summary>총이 발사된 직후 호출. 인자는 발사 후 남은 탄약 수이며, 모르면 null.</summary>
    public event Action<int?> Fired;

    /// <summary>장전된 탄약 수가 발사 없이 정해졌을 때 호출. 인자는 장전된 탄약 수.</summary>
    public event Action<int> LoadedAmmoSet;

    /// <summary>탄창을 떨어트린 직후 호출. 인자는 떨어진 탄창 복제본.</summary>
    public event Action<GameObject> MagazineDetached;

    /// <summary>숨겨둔 탄창을 다시 보여준 직후 호출.</summary>
    public event Action MagazineAttached;

    /// <summary>총기의 탄창을 숨기고, 그 자리에서 Rigidbody로 시뮬레이션되는 탄창 복제본을 던짐.</summary>
    /// <param name="launchDirection">탄창을 던지는 기준 방향 (월드 공간).</param>
    public void DetachMagazine(Vector3 launchDirection)
    {
        if (!Magazine.activeSelf)
        {
            return;
        }

        GameObject droppedMagazine = Instantiate(Magazine, Magazine.transform.parent);
        droppedMagazine.transform.SetParent(null, true);

        Rigidbody droppedRigidbody = droppedMagazine.GetComponent<Rigidbody>();
        droppedRigidbody.isKinematic = false;
        Vector3 randomDirection = Vector3.RotateTowards(launchDirection.normalized, Random.onUnitSphere,
            Random.Range(0f, magazineLaunchAngle) * Mathf.Deg2Rad, 0f);
        droppedRigidbody.linearVelocity = randomDirection * magazineLaunchSpeed;
        droppedMagazine.GetComponent<Collider>().enabled = true;
        droppedMagazine.GetComponent<DroppedMagazine>().enabled = true;

        Magazine.SetActive(false);
        ReturnMagazine();
        MagazineDetached?.Invoke(droppedMagazine);
    }

    /// <summary>탄창을 월드 포즈를 유지한 채 <paramref name="holder"/>의 자식으로 옮김.</summary>
    public void HoldMagazine(Transform holder)
    {
        if (!_magazineRestParent)
        {
            _magazineRestParent = Magazine.transform.parent;
            Magazine.transform.GetLocalPositionAndRotation(out _magazineRestLocalPosition,
                out _magazineRestLocalRotation);
        }

        Magazine.transform.SetParent(holder, true);
    }

    /// <summary><see cref="HoldMagazine"/>으로 옮긴 탄창을 총기의 원래 자리로 되돌림.</summary>
    public void ReturnMagazine()
    {
        if (!_magazineRestParent)
        {
            return;
        }

        Magazine.transform.SetParent(_magazineRestParent, false);
        Magazine.transform.SetLocalPositionAndRotation(_magazineRestLocalPosition, _magazineRestLocalRotation);
        _magazineRestParent = null;
    }

    /// <summary>숨겨둔 총기의 탄창을 다시 보여줌.</summary>
    public void AttachMagazine()
    {
        Magazine.SetActive(true);
        MagazineAttached?.Invoke();
    }

    // 탄창 오브젝트 반환
    public GameObject GetMagazine()
    {
        return Magazine;
    }

    /// <summary>총이 발사되었음을 알림.</summary>
    /// <param name="remainingAmmo">발사 후 남은 탄약 수. 모르면 null.</param>
    public void Fire(int? remainingAmmo)
    {
        Fired?.Invoke(remainingAmmo);
    }

    /// <summary>발사 없이 장전된 탄약 수를 알림.</summary>
    public void SetLoadedAmmo(int loadedAmmo)
    {
        LoadedAmmoSet?.Invoke(loadedAmmo);
    }
}
