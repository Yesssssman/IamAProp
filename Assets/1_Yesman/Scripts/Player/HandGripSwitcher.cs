using UnityEngine;

/// <summary>손 클립의 애니메이션 이벤트로 무기와 탄창을 쥐는 손을 바꿈.</summary>
[RequireComponent(typeof(HandScript))]
public class HandGripSwitcher : MonoBehaviour
{
    private HandScript hand;

    /// <summary>왼손으로 옮겨 쥔 오른손 아이템. 쥐고 있지 않으면 null.</summary>
    private GameObject leftGripItem;

    /// <summary>왼손으로 옮기기 전 <see cref="leftGripItem"/>의 로컬 위치 (오른손 본 기준).</summary>
    private Vector3 rightGripLocalPosition;

    /// <summary>왼손으로 옮기기 전 <see cref="leftGripItem"/>의 로컬 회전 (오른손 본 기준).</summary>
    private Quaternion rightGripLocalRotation;

    /// <summary>왼손으로 옮겨 쥔 탄창의 총기. 쥐고 있지 않으면 null.</summary>
    private GunScript magazineGun;

    /// <summary>왼손으로 옮기기 전 탄창의 부모.</summary>
    private Transform magazineParent;

    /// <summary>왼손으로 옮기기 전 탄창의 로컬 위치.</summary>
    private Vector3 magazineLocalPosition;

    /// <summary>왼손으로 옮기기 전 탄창의 로컬 회전.</summary>
    private Quaternion magazineLocalRotation;

    private void Awake()
    {
        hand = GetComponent<HandScript>();
    }

    // ********************** 애니메이션 이벤트 전용 함수들 **********************

    /// <summary>오른손 아이템을 월드 포즈를 유지한 채 왼손 본의 자식으로 옮김.</summary>
    public void GripWithLeftHand()
    {
        GameObject item = hand.GetRightHandItem();

        if (!item || item == leftGripItem)
        {
            return;
        }

        item.transform.GetLocalPositionAndRotation(out rightGripLocalPosition, out rightGripLocalRotation);
        item.transform.SetParent(hand.GetLeftHand(), true);
        leftGripItem = item;
    }

    /// <summary>
    /// 왼손으로 옮겨 쥔 오른손 아이템을 오른손 본의 자식으로, 옮기기 전의 로컬 포즈로 되돌림.
    ///
    /// 클립은 되돌리는 프레임에서 원래 그립과 맞도록 만들어졌지만 2~3 mm, 0.5° 정도 어긋남.
    /// 월드 포즈를 유지한 채 되돌리면 이 오차가 재장전할 때마다 쌓이므로 원래 그립으로 맞춤.
    /// </summary>
    public void GripWithRightHand()
    {
        GameObject item = hand.GetRightHandItem();

        if (!item || item != leftGripItem)
        {
            return;
        }

        item.transform.SetParent(hand.GetRightHand(), false);
        item.transform.SetLocalPositionAndRotation(rightGripLocalPosition, rightGripLocalRotation);
        leftGripItem = null;
    }

    /// <summary>오른손 총기의 탄창을 월드 포즈를 유지한 채 왼손 본의 자식으로 옮김.</summary>
    public void HoldMagazineInLeftHand()
    {
        GameObject item = hand.GetRightHandItem();
        GunScript gun = item ? item.GetComponent<GunScript>() : null;

        if (!gun || magazineGun)
        {
            return;
        }

        Transform magazine = gun.GetMagazine().transform;
        magazineParent = magazine.parent;
        magazine.GetLocalPositionAndRotation(out magazineLocalPosition, out magazineLocalRotation);
        magazine.SetParent(hand.GetLeftHand(), true);

        magazineGun = gun;
        magazineGun.MagazineDetached += HandleMagazineDetached;
    }

    /// <summary>떨어트린 뒤 숨겨진 탄창을 총기의 원래 자리로 되돌림.</summary>
    private void HandleMagazineDetached(GameObject droppedMagazine)
    {
        magazineGun.MagazineDetached -= HandleMagazineDetached;

        Transform magazine = magazineGun.GetMagazine().transform;
        magazine.SetParent(magazineParent, false);
        magazine.SetLocalPositionAndRotation(magazineLocalPosition, magazineLocalRotation);
        magazineGun = null;
    }
}