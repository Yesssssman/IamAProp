using UnityEngine;

public class PlayerScript : MonoBehaviour
{
    // 손
    [SerializeField]
    private HandScript hand;

    /// <summary>수류탄을 던지는 방향이 프롭의 앞 방향에서 위로 기울어진 각도 (도).</summary>
    [SerializeField]
    private float grenadeThrowPitch = 30f;

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

    /// <summary>오른손에 들고 있는 수류탄의 핀을 뽑아 왼쪽으로 떨어트림.</summary>
    public void RemoveGrenadePin()
    {
        GameObject rightHandItem = hand.GetRightHandItem();

        if (!rightHandItem)
        {
            return;
        }

        GrenadeScript grenadeScript = rightHandItem.GetComponent<GrenadeScript>();

        if (grenadeScript)
        {
            grenadeScript.RemovePin(-transform.right);
        }
    }

    /// <summary>오른손에 들고 있는 수류탄을 앞쪽 위로 던짐.</summary>
    /// <param name="speed">던지는 속도 (m/s).</param>
    public void ThrowGrenade(float speed)
    {
        GameObject rightHandItem = hand.GetRightHandItem();

        if (!rightHandItem)
        {
            return;
        }

        GrenadeScript grenadeScript = rightHandItem.GetComponent<GrenadeScript>();

        if (grenadeScript)
        {
            grenadeScript.Throw(Quaternion.AngleAxis(-grenadeThrowPitch, transform.right) * transform.forward * speed);
        }
    }
}