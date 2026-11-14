using UnityEngine;

public class PlayerScript : MonoBehaviour
{
    // 손
    [SerializeField]
    private HandScript hand;

    // ********************** 인벤토리 전용 함수들 *************************

    // 장착 무기 변경
    public void Equip(GameObject Equipment)
    {
    }

    // ********************** 애니메이션 이벤트 전용 함수들 **********************

    // 오른손에 들고 있는 총에서 탄창을 떨어트림. 떨어진 탄창은 Rigidbody에 의해 시뮬레이션됨.
    public void DropMagazineInGun()
    {
        GameObject rightHandItem = hand.GetRightHandItem();

        if (!rightHandItem)
        {
            return;
        }

        GunScript gunScript = rightHandItem.GetComponent<GunScript>();

        if (gunScript)
        {
            gunScript.DetachMagazine(transform.forward);
        }
    }

    // 왼손의 탄창을 보여주기 시작함
    public void ShowLeftHandMagazine()
    {
        GameObject leftHandItem = hand.GetLeftHandItem();

        if (leftHandItem)
        {
            leftHandItem.SetActive(true);
        }
    }

    // 손에 들린 탄창을 제거하고, 오른손 총기에 있던 탄창을 활성화시킴
    public void EquipMagazineToGun()
    {
        GameObject rightHandItem = hand.GetRightHandItem();

        if (rightHandItem)
        {
            GunScript gunScript = rightHandItem.GetComponent<GunScript>();

            if (gunScript)
            {
                gunScript.AttachMagazine();
            }
        }

        GameObject leftHandItem = hand.GetLeftHandItem();

        if (leftHandItem)
        {
            leftHandItem.SetActive(false);
        }
    }
}