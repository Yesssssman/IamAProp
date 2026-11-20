/// <summary>프롭 몸통, 손 및 무기 애니메이션을 제어하는 공통 인터페이스.</summary>
public interface IPropAnimController
{
    /// <summary>현재 동기화된 무기 장착 상태.</summary>
    EWeaponType CurrentWeaponType { get; }

    /// <summary>무기 교체(드로우) 애니메이션이 진행 중인지 여부.</summary>
    bool IsSwappingWeapon { get; }

    /// <summary>점프 및 체공 애니메이션 파라미터를 갱신한다.</summary>
    void SetJumpState(bool isJumping, bool inAir);

    /// <summary>공격 애니메이션 파라미터를 갱신한다.</summary>
    void SetAttackState(bool attacking);

    /// <summary>현재 장착 무기의 공격 또는 근접 휘두르기 애니메이션을 재생한다.</summary>
    void TriggerAttack(int attackVariant = 0);

    /// <summary>장착 무기 모드를 변경하고 네트워크에 동기화한다.</summary>
    void SetWeaponMode(EWeaponType weaponType);

    /// <summary>총기 장착 상태에서 손 재장전 애니메이션을 요청한다.</summary>
    void RequestReload();
}
