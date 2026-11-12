using UnityEngine;

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
    }

    /// <summary>숨겨둔 총기의 탄창을 다시 보여줌.</summary>
    public void AttachMagazine()
    {
        Magazine.SetActive(true);
    }

    // 탄창 오브젝트 반환
    public GameObject GetMagazine()
    {
        return Magazine;
    }
}
