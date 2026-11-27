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

    /// <summary>왼손으로 옮겨 쥔 오른손 아이템. 쥐고 있지 않으면 null.</summary>
    private GameObject _leftGripItem;

    /// <summary>왼손으로 옮기기 전 <see cref="_leftGripItem"/>의 로컬 위치 (오른손 본 기준).</summary>
    private Vector3 _rightGripLocalPosition;

    /// <summary>왼손으로 옮기기 전 <see cref="_leftGripItem"/>의 로컬 회전 (오른손 본 기준).</summary>
    private Quaternion _rightGripLocalRotation;

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
        // 왼손에 옮겨 쥔 탄창은 총기의 자식이 아니라서 함께 파괴되지 않으므로 먼저 되돌림
        ResetGrip();
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

    /// <summary>왼손으로 옮겨 쥔 오른손 아이템과 탄창을 원래 자리로 되돌림.</summary>
    public void ResetGrip()
    {
        GripWithRightHand();

        if (_rightHandItem && _rightHandItem.TryGetComponent(out GunScript gunScript))
        {
            gunScript.ReturnMagazine();
        }
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

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.RemoveGrenadePin"/>으로 전달함.</summary>
    public void RemoveGrenadePin()
    {
        if (player)
        {
            player.RemoveGrenadePin();
        }
    }

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.ThrowGrenade"/>로 전달함.</summary>
    /// <param name="speed">던지는 속도 (m/s). 애니메이션 이벤트의 Float 인자.</param>
    public void ThrowGrenade(float speed)
    {
        if (player)
        {
            player.ThrowGrenade(speed);
        }
    }

    /// <summary>오른손 아이템을 월드 포즈를 유지한 채 왼손 본의 자식으로 옮김.</summary>
    public void GripWithLeftHand()
    {
        if (!_rightHandItem || _rightHandItem == _leftGripItem)
        {
            return;
        }

        _rightHandItem.transform.GetLocalPositionAndRotation(out _rightGripLocalPosition, out _rightGripLocalRotation);
        _rightHandItem.transform.SetParent(leftHand, true);
        _leftGripItem = _rightHandItem;
    }

    /// <summary>
    /// 왼손으로 옮겨 쥔 오른손 아이템을 오른손 본의 자식으로, 옮기기 전의 로컬 포즈로 되돌림.
    ///
    /// 클립은 되돌리는 프레임에서 원래 그립과 맞도록 만들어졌지만 2~3 mm, 0.5° 정도 어긋남.
    /// 월드 포즈를 유지한 채 되돌리면 이 오차가 재장전할 때마다 쌓이므로 원래 그립으로 맞춤.
    /// </summary>
    public void GripWithRightHand()
    {
        if (!_leftGripItem || _rightHandItem != _leftGripItem)
        {
            _leftGripItem = null;
            return;
        }

        _rightHandItem.transform.SetParent(rightHand, false);
        _rightHandItem.transform.SetLocalPositionAndRotation(_rightGripLocalPosition, _rightGripLocalRotation);
        _leftGripItem = null;
    }

    /// <summary>손 클립의 이벤트를 <see cref="PlayerScript.HoldMagazineInLeftHand"/>로 전달함.</summary>
    public void HoldMagazineInLeftHand()
    {
        if (player)
        {
            player.HoldMagazineInLeftHand();
        }
    }
}
