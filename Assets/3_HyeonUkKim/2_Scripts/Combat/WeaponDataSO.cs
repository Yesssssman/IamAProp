using UnityEngine;

/// <summary>무기 슬롯 분류.</summary>
public enum EWeaponCategory
{
    Primary,
    Secondary,
    Melee
}

/// <summary>개별 무기의 전투 수치, 사거리, 탄약 및 재장전 데이터를 정의한다.</summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "IamAProp/Combat/Weapon Data")]
public class WeaponDataSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>UI 및 로그에 표시할 무기 이름.</summary>
    [Header("무기 식별")]
    [SerializeField] private string weaponName = "Weapon";

    /// <summary>이 데이터가 대응하는 장착 무기 모드.</summary>
    [SerializeField] private EWeaponType weaponType = EWeaponType.Ak;

    /// <summary>무기 분류 (주무기, 보조무기, 근접무기).</summary>
    [SerializeField] private EWeaponCategory category = EWeaponCategory.Primary;

    /// <summary>단일 적중 시 적용할 기본 공격력.</summary>
    [Header("전투 성능")]
    [Min(0f)]
    [SerializeField] private float damage = 25f;

    /// <summary>초당 발사 또는 공격 횟수 (RPS).</summary>
    [Min(0.1f)]
    [SerializeField] private float fireRate = 10f;

    /// <summary>공격 입력을 유지할 때 연속 발사되는지 여부.</summary>
    [SerializeField] private bool isAutomatic = true;

    /// <summary>히트스캔 또는 근접 공격 최대 사거리 (미터).</summary>
    [Min(0.1f)]
    [SerializeField] private float range = 100f;

    /// <summary>근접 공격 시 사용할 구체 캐스트 반지름 (0이면 Raycast 사용, 미터).</summary>
    [Min(0f)]
    [SerializeField] private float meleeCastRadius;

    /// <summary>공격 시 탄약을 소모하는지 여부 (근접 무기는 false).</summary>
    [Header("탄약 및 재장전")]
    [SerializeField] private bool usesAmmo = true;

    /// <summary>탄창 최대 장탄 수.</summary>
    [Min(0)]
    [SerializeField] private int maxAmmo = 30;

    /// <summary>초기 및 최대 예비 탄약 수.</summary>
    [Min(0)]
    [SerializeField] private int maxReserveAmmo = 90;

    /// <summary>재장전 완료까지 소요되는 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float reloadTime = 2.5f;

    /// <summary>발사 시 재생할 총성 오디오 클립.</summary>
    [Header("사운드")]
    [SerializeField] private AudioClip shotClip;

    /// <summary>재장전 3단계 오디오 클립 (0: 탄창 빼기, 1: 탄창 결합, 2: 노리쇠 후퇴·전진).</summary>
    [SerializeField] private AudioClip[] reloadClips;

    /// <summary>사격 사운드 재생 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float shotVolume = 1f;

    /// <summary>재장전 사운드 재생 볼륨 배율.</summary>
    [Range(0f, 1f)]
    [SerializeField] private float reloadVolume = 1f;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>무기 이름.</summary>
    public string WeaponName => weaponName;

    /// <summary>대응하는 장착 무기 모드.</summary>
    public EWeaponType WeaponType => weaponType;

    /// <summary>무기 분류.</summary>
    public EWeaponCategory Category => category;

    /// <summary>단일 적중 데미지.</summary>
    public float Damage => damage;

    /// <summary>초당 발사 횟수.</summary>
    public float FireRate => fireRate;

    /// <summary>연속 공격 사이 최소 대기 시간 (초 단위).</summary>
    public float AttackCooldown => fireRate > 0f ? 1f / fireRate : 0f;

    /// <summary>연사 가능 여부.</summary>
    public bool IsAutomatic => isAutomatic;

    /// <summary>최대 사거리 (미터).</summary>
    public float Range => range;

    /// <summary>근접 구체 캐스트 반지름 (미터).</summary>
    public float MeleeCastRadius => meleeCastRadius;

    /// <summary>탄약 소모 여부.</summary>
    public bool UsesAmmo => usesAmmo;

    /// <summary>탄창 최대 용량.</summary>
    public int MaxAmmo => maxAmmo;

    /// <summary>최대 예비 탄약 수.</summary>
    public int MaxReserveAmmo => maxReserveAmmo;

    /// <summary>재장전 소요 시간 (초 단위).</summary>
    public float ReloadTime => reloadTime;

    /// <summary>발사 시 재생할 총성 오디오 클립.</summary>
    public AudioClip ShotClip => shotClip;

    /// <summary>재장전 단계별 오디오 클립 배열.</summary>
    public AudioClip[] ReloadClips => reloadClips;

    /// <summary>사격 사운드 볼륨 배율.</summary>
    public float ShotVolume => shotVolume;

    /// <summary>재장전 사운드 볼륨 배율.</summary>
    public float ReloadVolume => reloadVolume;

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>재장전 단계 번호(<paramref name="step"/>: 1~3)에 해당하는 오디오 클립을 반환한다.</summary>
    public AudioClip GetReloadClip(int step)
    {
        int index = step - 1;
        if (reloadClips == null || index < 0 || index >= reloadClips.Length) return null;
        return reloadClips[index];
    }
}
