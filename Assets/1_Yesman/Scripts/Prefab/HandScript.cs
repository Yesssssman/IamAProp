using UnityEngine;

public class HandScript : MonoBehaviour
{
    /// <summary>이 손을 가진 플레이어. 손만 따로 배치한 경우 null.</summary>
    [SerializeField]
    private PlayerScript player;

    // 왼손 위치
    [SerializeField]
    private Transform leftHand;

    // 오른손 위치
    [SerializeField]
    private Transform rightHand;

    // 왼손에 장착한 아이템
    private GameObject _leftHandItem;

    // 오른에 장착한 아이템
    private GameObject _rightHandItem;

    // 왼손에 해당 아이템을 장착
    public void SetLeftHandItem(GameObject item)
    {
        Destroy(_leftHandItem);
        item.transform.SetParent(leftHand);
        _leftHandItem = item;
    }

    // 오른손에 해당 아이템을 장착
    public void SetRightHandItem(GameObject item)
    {
        Destroy(_rightHandItem);
        item.transform.SetParent(rightHand);
        _rightHandItem = item;
    }

    public GameObject GetLeftHandItem()
    {
        return _leftHandItem;
    }

    public GameObject GetRightHandItem()
    {
        return _rightHandItem;
    }

    public Transform GetLeftHand()
    {
        return leftHand;
    }

    public Transform GetRightHand()
    {
        return rightHand;
    }

    // ********************** 애니메이션 이벤트 전용 함수들 **********************

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.DropMagazineInGun"/>으로 전달함.</summary>
    public void DropMagazineInGun()
    {
        if (player)
        {
            player.DropMagazineInGun();
        }
    }

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.ShowLeftHandMagazine"/>으로 전달함.</summary>
    public void ShowLeftHandMagazine()
    {
        if (player)
        {
            player.ShowLeftHandMagazine();
        }
    }

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.EquipMagazineToGun"/>으로 전달함.</summary>
    public void EquipMagazineToGun()
    {
        if (player)
        {
            player.EquipMagazineToGun();
        }
    }
}