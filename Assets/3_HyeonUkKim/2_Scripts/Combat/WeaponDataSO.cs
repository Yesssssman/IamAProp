using UnityEngine;

/// <summary>무기 슬롯 분류.</summary>
public enum EWeaponCategory
{
    Primary,
    Secondary,
    Melee
}

/// <summary>개별 무기의 식별자, 장착 비주얼, 애니메이션 오버라이드, 전투 수치 및 사운드 데이터를 정의한다.</summary>
[CreateAssetMenu(fileName = "WeaponData", menuName = "IamAProp/Combat/Weapon Data")]
public class WeaponDataSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>네트워크 동기화 및 데이터베이스 조회에 사용할 고유 무기 식별자.</summary>
    [Header("무기 식별")]
    [SerializeField] private string weaponId = string.Empty;

    /// <summary>UI 및 로그에 표시할 무기 이름.</summary>
    [SerializeField] private string weaponName = "Weapon";

    /// <summary>이 데이터가 대응하는 장착 무기 모드.</summary>
    [SerializeField] private EWeaponType weaponType = EWeaponType.Ak;

    /// <summary>이 무기가 장착되는 키 입력 슬롯 카테고리.</summary>
    [SerializeField] private EWeaponSlotType slotType = EWeaponSlotType.Primary;

    /// <summary>무기 분류 (주무기, 보조무기, 근접무기).</summary>
    [SerializeField] private EWeaponCategory category = EWeaponCategory.Primary;

    /// <summary>오른손 소켓에 장착할 무기 프리팹.</summary>
    [Header("장착 및 월드 비주얼")]
    [SerializeField] private GameObject equippedPrefab;

    /// <summary>바닥에 떨어진 월드 픽업에 표시할 비주얼 프리팹 (비워두면 <see cref="equippedPrefab"/> 사용).</summary>
    [SerializeField] private GameObject worldVisualPrefab;

    /// <summary>장착된 무기 오브젝트의 <see cref="Animator"/>에 할당할 컨트롤러.</summary>
    [SerializeField] private RuntimeAnimatorController weaponAnimController;

    /// <summary>오른손 본 기준 무기 로컬 위치.</summary>
    [SerializeField] private Vector3 gripPosition;

    /// <summary>오른손 본 기준 무기 로컬 오일러 회전각 (도 단위).</summary>
    [SerializeField] private Vector3 gripRotation;

    /// <summary>오른손 본 기준 무기 로컬 스케일.</summary>
    [SerializeField] private Vector3 gripScale = Vector3.one;

    /// <summary>바닥 월드 픽업에 표시할 모델의 로컬 스케일.</summary>
    [SerializeField] private Vector3 worldPickupScale = Vector3.one;

    /// <summary>바닥 월드 픽업의 무기 형태에 맞춘 복합 <see cref="BoxCollider"/> 목록 (비어 있으면 메쉬 바운드로 자동 생성).</summary>
    [SerializeField] private WeaponColliderBox[] worldPickupColliders;

    /// <summary>장착 상태에서 이동 시 조준 달리기 클립(<c>AimRun</c>)을 사용하는지 여부.</summary>
    [SerializeField] private bool usesAimLocomotion = true;

    /// <summary>손 애니메이터의 무기 드로우 상태에 오버라이드할 클립.</summary>
    [Header("손 애니메이션 오버라이드")]
    [SerializeField] private AnimationClip handDrawClip;

    /// <summary>손 애니메이터의 대기/조준 상태에 오버라이드할 클립.</summary>
    [SerializeField] private AnimationClip handIdleClip;

    /// <summary>손 애니메이터의 기본 공격/사격 상태에 오버라이드할 클립.</summary>
    [SerializeField] private AnimationClip handAttackClip;

    /// <summary>손 애니메이터의 2타 근접 공격 상태에 오버라이드할 클립.</summary>
    [SerializeField] private AnimationClip handSecondaryAttackClip;

    /// <summary>손 애니메이터의 재장전 상태 및 왼손 탄창 샘플링에 사용할 클립.</summary>
    [SerializeField] private AnimationClip handReloadClip;

    /// <summary>손 발사 클립의 총 프레임 수.</summary>
    [Header("총기 프레임 동기화")]
    [Min(0f)]
    [SerializeField] private float handShotFrames = 17f;

    /// <summary>총기 발사 클립의 총 프레임 수.</summary>
    [Min(0f)]
    [SerializeField] private float weaponShotFrames = 9f;

    /// <summary>손 재장전 클립의 총 프레임 수.</summary>
    [Min(0f)]
    [SerializeField] private float handReloadTotalFrames;

    /// <summary>손 재장전 중 총기 노리쇠/슬라이드 모션이 시작되는 프레임.</summary>
    [Min(0f)]
    [SerializeField] private float weaponReloadStartFrame;

    /// <summary>총기 재장전 클립의 총 프레임 수.</summary>
    [Min(0f)]
    [SerializeField] private float weaponReloadTotalFrames;

    /// <summary>손 드로우 클립의 총 프레임 수.</summary>
    [Min(0f)]
    [SerializeField] private float handDrawTotalFrames;

    /// <summary>손 드로우 중 총기 노리쇠/슬라이드 모션이 시작되는 프레임.</summary>
    [Min(0f)]
    [SerializeField] private float weaponDrawStartFrame;

    /// <summary>총기 드로우 연동 클립의 총 프레임 수.</summary>
    [Min(0f)]
    [SerializeField] private float weaponDrawTotalFrames;

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

    /// <summary>1발 발사 시 카메라가 위로 튀는 상하 반동 각도 (도 단위).</summary>
    [Header("반동 및 화면 흔들림")]
    [Min(0f)]
    [SerializeField] private float recoilPitch;

    /// <summary>1발 발사 시 카메라 좌우 반동 최소 각도 (도 단위).</summary>
    [SerializeField] private float recoilYawMin;

    /// <summary>1발 발사 시 카메라 좌우 반동 최대 각도 (도 단위).</summary>
    [SerializeField] private float recoilYawMax;

    /// <summary>반동 목표 각도로 튀어 오르는 반응 속도.</summary>
    [Min(0.01f)]
    [SerializeField] private float recoilSnappiness = 20f;

    /// <summary>반동 오프셋이 원래 조준점으로 복귀하는 속도.</summary>
    [Min(0.01f)]
    [SerializeField] private float recoilReturnSpeed = 10f;

    /// <summary>연사 시 누적되는 상하 반동 최대 각도 제한 (도 단위).</summary>
    [Min(0f)]
    [SerializeField] private float maxRecoilPitch = 10f;

    /// <summary>연사 시 누적되는 좌우 반동 최대 절대 각도 제한 (도 단위).</summary>
    [Min(0f)]
    [SerializeField] private float maxRecoilYaw = 5f;

    /// <summary>발사 순간 미세 펄스 화면 흔들림 강도.</summary>
    [Min(0f)]
    [SerializeField] private float shakeIntensity;

    /// <summary>발사 순간 미세 펄스 화면 흔들림 지속 시간 (초 단위).</summary>
    [Min(0f)]
    [SerializeField] private float shakeDuration = 0.05f;

    /// <summary>1인칭 정조준 시 이 무기 전용으로 사용할 시야각 (0 이하면 카메라 기본 ADS FOV 사용, 도 단위).</summary>
    [Header("1인칭 정조준 (ADS)")]
    [Min(0f)]
    [SerializeField] private float adsFovOverride;

    /// <summary>무기 프리팹의 <c>AdsCameraAnchor</c> 로컬 좌표에 추가로 더할 미세 위치 오프셋 (로컬 단위).</summary>
    [SerializeField] private Vector3 adsAnchorLocalOffset;

    /// <summary>무기 프리팹의 <c>AdsCameraAnchor</c> 로컬 회전에 추가로 더할 미세 오일러 각도 (도 단위).</summary>
    [SerializeField] private Vector3 adsAnchorLocalRotationOffset;

    /// <summary>1인칭 정조준 발사 시 이 무기 전용 Z축 후퇴 반동 거리 (0 이하면 카메라 기본값 사용, 미터).</summary>
    [Min(0f)]
    [SerializeField] private float adsKickbackZOverride;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>고유 무기 식별자.</summary>
    public string WeaponId => weaponId;

    /// <summary>무기 이름.</summary>
    public string WeaponName => weaponName;

    /// <summary>대응하는 장착 무기 모드.</summary>
    public EWeaponType WeaponType => weaponType;

    /// <summary>이 무기가 장착되는 키 입력 슬롯 카테고리.</summary>
    public EWeaponSlotType SlotType => slotType;

    /// <summary>무기 분류.</summary>
    public EWeaponCategory Category => category;

    /// <summary>오른손 소켓 장착용 프리팹.</summary>
    public GameObject EquippedPrefab => equippedPrefab;

    /// <summary>월드 픽업 비주얼용 프리팹.</summary>
    public GameObject WorldVisualPrefab => worldVisualPrefab != null ? worldVisualPrefab : equippedPrefab;

    /// <summary>무기 오브젝트 <see cref="Animator"/> 컨트롤러.</summary>
    public RuntimeAnimatorController WeaponAnimController => weaponAnimController;

    /// <summary>오른손 본 기준 무기 로컬 위치.</summary>
    public Vector3 GripPosition => gripPosition;

    /// <summary>오른손 본 기준 무기 로컬 오일러 회전각 (도 단위).</summary>
    public Vector3 GripRotation => gripRotation;

    /// <summary>오른손 본 기준 무기 로컬 스케일.</summary>
    public Vector3 GripScale => gripScale;

    /// <summary>바닥 월드 픽업 모델 스케일.</summary>
    public Vector3 WorldPickupScale => worldPickupScale;

    /// <summary>바닥 월드 픽업용 복합 <see cref="BoxCollider"/> 정의 배열.</summary>
    public WeaponColliderBox[] WorldPickupColliders => worldPickupColliders;

    /// <summary>장착 이동 시 조준 달리기 클립 사용 여부.</summary>
    public bool UsesAimLocomotion => usesAimLocomotion;

    /// <summary>손 드로우 오버라이드 클립.</summary>
    public AnimationClip HandDrawClip => handDrawClip;

    /// <summary>손 대기/조준 오버라이드 클립.</summary>
    public AnimationClip HandIdleClip => handIdleClip;

    /// <summary>손 기본 공격/사격 오버라이드 클립.</summary>
    public AnimationClip HandAttackClip => handAttackClip;

    /// <summary>손 2타 근접 공격 오버라이드 클립.</summary>
    public AnimationClip HandSecondaryAttackClip => handSecondaryAttackClip;

    /// <summary>손 재장전 오버라이드 클립.</summary>
    public AnimationClip HandReloadClip => handReloadClip;

    /// <summary>손 발사 클립 총 프레임 수.</summary>
    public float HandShotFrames => handShotFrames;

    /// <summary>총기 발사 클립 총 프레임 수.</summary>
    public float WeaponShotFrames => weaponShotFrames;

    /// <summary>손 재장전 클립 총 프레임 수.</summary>
    public float HandReloadTotalFrames => handReloadTotalFrames;

    /// <summary>손 재장전 중 총기 모션 시작 프레임.</summary>
    public float WeaponReloadStartFrame => weaponReloadStartFrame;

    /// <summary>총기 재장전 클립 총 프레임 수.</summary>
    public float WeaponReloadTotalFrames => weaponReloadTotalFrames;

    /// <summary>손 드로우 클립 총 프레임 수.</summary>
    public float HandDrawTotalFrames => handDrawTotalFrames;

    /// <summary>손 드로우 중 총기 모션 시작 프레임.</summary>
    public float WeaponDrawStartFrame => weaponDrawStartFrame;

    /// <summary>총기 드로우 연동 클립 총 프레임 수.</summary>
    public float WeaponDrawTotalFrames => weaponDrawTotalFrames;

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

    /// <summary>1발 발사 시 상하 반동 각도 (도 단위).</summary>
    public float RecoilPitch => recoilPitch;

    /// <summary>1발 발사 시 좌우 반동 최소 각도 (도 단위).</summary>
    public float RecoilYawMin => recoilYawMin;

    /// <summary>1발 발사 시 좌우 반동 최대 각도 (도 단위).</summary>
    public float RecoilYawMax => recoilYawMax;

    /// <summary>반동 반응 속도.</summary>
    public float RecoilSnappiness => recoilSnappiness;

    /// <summary>반동 원점 복귀 속도.</summary>
    public float RecoilReturnSpeed => recoilReturnSpeed;

    /// <summary>누적 상하 반동 최대 각도 제한 (도 단위).</summary>
    public float MaxRecoilPitch => maxRecoilPitch;

    /// <summary>누적 좌우 반동 최대 절대 각도 제한 (도 단위).</summary>
    public float MaxRecoilYaw => maxRecoilYaw;

    /// <summary>발사 순간 미세 펄스 화면 흔들림 강도.</summary>
    public float ShakeIntensity => shakeIntensity;

    /// <summary>발사 순간 미세 펄스 화면 흔들림 지속 시간 (초 단위).</summary>
    public float ShakeDuration => shakeDuration;

    /// <summary>1인칭 정조준 시 이 무기 전용 시야각 (0 이하면 기본 ADS FOV 사용).</summary>
    public float AdsFovOverride => adsFovOverride;

    /// <summary><c>AdsCameraAnchor</c> 로컬 좌표 추가 미세 오프셋.</summary>
    public Vector3 AdsAnchorLocalOffset => adsAnchorLocalOffset;

    /// <summary><c>AdsCameraAnchor</c> 로컬 회전 추가 미세 오일러 각도 (도 단위).</summary>
    public Vector3 AdsAnchorLocalRotationOffset => adsAnchorLocalRotationOffset;

    /// <summary>1인칭 정조준 발사 시 이 무기 전용 Z축 후퇴 반동 거리 (0 이하면 기본값 사용, 미터).</summary>
    public float AdsKickbackZOverride => adsKickbackZOverride;

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
