using UnityEngine;

/// <summary>손 <see cref="Animator"/>의 재장전 애니메이션 이벤트를 <see cref="PlayerWeaponController"/>로 중계한다.</summary>
public class HandReloadAudioRelay : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>재장전 사운드 재생을 위임받을 상위 무기 컨트롤러.</summary>
    [SerializeField] private PlayerWeaponController weaponController;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (weaponController == null)
        {
            weaponController = GetComponentInParent<PlayerWeaponController>();
        }
    }

    // ***************************************************************
    // Animation Events
    // ***************************************************************

    /// <summary>재장전 애니메이션 이벤트에서 호출되어 단계(<paramref name="step"/>: 1~3) 사운드 재생을 요청한다.</summary>
    public void OnReloadSound(int step)
    {
        if (weaponController == null)
        {
            weaponController = GetComponentInParent<PlayerWeaponController>();
        }

        weaponController?.PlayReloadSound(step);
    }
}
