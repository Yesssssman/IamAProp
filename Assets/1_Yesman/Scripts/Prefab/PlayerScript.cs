using UnityEngine;

public class PlayerScript : MonoBehaviour
{
    // 들고 있는 총 GameObject
    [SerializeField]
    private GameObject Gun;

    // 왼손에 들고 있는 탄창 (재장전 시 보여줌)
    [SerializeField]
    private GameObject Magazine;

    // 왼손의 Transform
    [SerializeField]
    private Transform LeftHand;

    // ********************** 인벤토리 전용 함수들 *************************

    // 장착 무기 변경
    public void Equip(GameObject Equipment)
    {

    }

    // 현재 들고 있는 총의 탄창을 왼손에 스폰함. 기존의 탄창은 버림.
    // 재장전 애니메이션이 아니라 총기를 장비할 때 불려짐.
    public void SpawnMagazineAtHand()
    {
        if (Magazine)
        {
            Destroy(Magazine);
        }

        GunScript gunScript = Gun.GetComponent<GunScript>();

        if (gunScript)
        {
            Magazine = Instantiate(gunScript.GetMagazine(), LeftHand);
            // 초기에는 비활성화
            Magazine.SetActive(false);
        }
    }

    // ********************** 애니메이션 이벤트 전용 함수들 **********************

    // 손에 들고 있는 총에서 탄창을 떨어트림. 떨어진 탄창은 Rigidbody에 의해 시뮬레이션됨.
    public void DropMagazineInGun()
    {
        GunScript gunScript = Gun.GetComponent<GunScript>();

        if (gunScript)
        {
            gunScript.DetachMagazine(transform.forward);
        }
    }

    // 왼손의 탄창을 보여주기 시작함
    public void ShowLeftHandMagazine()
    {
        if (Magazine)
        {
            Magazine.SetActive(true);
        }
    }

    // 손에 들린 탄창을 제거하고, 총기에 있던 탄창을 활성화시킴
    public void EquipMagazineToGun()
    {
        GunScript gunScript = Gun.GetComponent<GunScript>();

        if (gunScript)
        {
            gunScript.AttachMagazine();
        }

        if (Magazine)
        {
            Magazine.SetActive(false);
        }
    }
}