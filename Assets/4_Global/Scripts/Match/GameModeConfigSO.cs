using Networking.Dto;
using UnityEngine;

/// <summary>모든 게임 모드가 공유하는 진행 타이밍, 리스폰, 로드아웃 및 드롭 아이템 정책 설정 베이스.</summary>
public abstract class GameModeConfigSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>모든 클라이언트 접속·로딩을 기다리는 최대 대기 시간 (초).</summary>
    [Header("매치 타이밍")]
    [Min(1f)]
    [SerializeField] protected float waitingTimeoutSeconds = 15f;

    /// <summary>게임 시작 전 카운트다운 시간 (초).</summary>
    [Min(0f)]
    [SerializeField] protected float countdownSeconds = 5f;

    /// <summary>본 경기 제한 시간 (초, 0이면 시간 제한 없음).</summary>
    [Min(0f)]
    [SerializeField] protected float matchDurationSeconds = 300f;

    /// <summary>매치 종료 결과창을 표시한 뒤 자동으로 대기실로 복귀하는 시간 (초).</summary>
    [Min(1f)]
    [SerializeField] protected float resultDisplaySeconds = 8f;

    /// <summary>전투 중 사망 시 리스폰을 허용할지 여부.</summary>
    [Header("리스폰 및 스폰 보호")]
    [SerializeField] protected bool allowRespawn = true;

    /// <summary>사망 후 리스폰까지 대기하는 시간 (초).</summary>
    [Min(0f)]
    [SerializeField] protected float respawnDelaySeconds = 5f;

    /// <summary>리스폰 직후 피해를 무시하는 스폰 무적 시간 (초).</summary>
    [Min(0f)]
    [SerializeField] protected float spawnInvulnerabilitySeconds = 2f;

    /// <summary>리스폰할 때마다 새로운 랜덤 프롭 프리팹으로 교체할지 여부.</summary>
    [SerializeField] protected bool randomizePropOnRespawn = true;

    /// <summary>사망 후 3초 경과 시 관전하기 버튼 UI를 표시할지 여부 (배틀로얄 등 비리스폰 모드용).</summary>
    [Header("데스캠 및 로드아웃")]
    [SerializeField] protected bool allowSpectateButton = false;

    /// <summary>카운트다운 및 사망 대기 중 무기 로드아웃 선택 UI를 허용할지 여부.</summary>
    [SerializeField] protected bool allowLoadoutSelection = true;

    /// <summary>사망 시 소지 중인 총기류(주무기·보조무기)를 시체 주변에 드롭할지 여부.</summary>
    [Header("무기 드롭 및 소멸 정책")]
    [SerializeField] protected bool dropFirearmsOnDeath = true;

    /// <summary>바닥에 드롭된 무기를 일정 시간 후 자동 소멸시킬지 여부 (데스매치 전용 활성화 권장).</summary>
    [SerializeField] protected bool despawnDroppedWeapons = false;

    /// <summary><see cref="despawnDroppedWeapons"/>가 켜져 있을 때 드롭 무기가 유지되는 시간 (초).</summary>
    [Min(1f)]
    [SerializeField] protected float droppedWeaponLifetimeSeconds = 20f;

    /// <summary>씬에 미리 배치된 초기 월드 픽업도 일정 시간 후 자동 소멸시킬지 여부.</summary>
    [SerializeField] protected bool despawnInitialScenePickups = false;

    /// <summary><see cref="despawnInitialScenePickups"/>가 켜져 있을 때 초기 픽업이 유지되는 시간 (초).</summary>
    [Min(1f)]
    [SerializeField] protected float initialScenePickupLifetimeSeconds = 60f;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>이 설정이 대응하는 로비 <see cref="EGameMode"/> 종류.</summary>
    public abstract EGameMode ModeType { get; }

    /// <summary>플레이어 로딩 최대 대기 시간 (초).</summary>
    public float WaitingTimeoutSeconds => waitingTimeoutSeconds;

    /// <summary>게임 시작 전 카운트다운 시간 (초).</summary>
    public float CountdownSeconds => countdownSeconds;

    /// <summary>매치 제한 시간 (초, 0이면 무제한).</summary>
    public float MatchDurationSeconds => matchDurationSeconds;

    /// <summary>종료 결과창 자동 복귀 대기 시간 (초).</summary>
    public float ResultDisplaySeconds => resultDisplaySeconds;

    /// <summary>사망 후 리스폰 허용 여부.</summary>
    public bool AllowRespawn => allowRespawn;

    /// <summary>사망 후 리스폰 대기 시간 (초).</summary>
    public float RespawnDelaySeconds => respawnDelaySeconds;

    /// <summary>리스폰 직후 무적 유지 시간 (초).</summary>
    public float SpawnInvulnerabilitySeconds => spawnInvulnerabilitySeconds;

    /// <summary>리스폰 시 새 랜덤 프롭으로 교체할지 여부.</summary>
    public bool RandomizePropOnRespawn => randomizePropOnRespawn;

    /// <summary>사망 후 관전하기 버튼 표시 허용 여부.</summary>
    public bool AllowSpectateButton => allowSpectateButton;

    /// <summary>로드아웃 선택 UI 활성화 여부.</summary>
    public bool AllowLoadoutSelection => allowLoadoutSelection;

    /// <summary>사망 시 주무기·보조무기 드롭 여부.</summary>
    public bool DropFirearmsOnDeath => dropFirearmsOnDeath;

    /// <summary>바닥 드롭 무기 자동 소멸 여부.</summary>
    public bool DespawnDroppedWeapons => despawnDroppedWeapons;

    /// <summary>바닥 드롭 무기 유지 시간 (초).</summary>
    public float DroppedWeaponLifetimeSeconds => droppedWeaponLifetimeSeconds;

    /// <summary>씬 사전 배치 초기 픽업 자동 소멸 여부.</summary>
    public bool DespawnInitialScenePickups => despawnInitialScenePickups;

    /// <summary>씬 사전 배치 초기 픽업 유지 시간 (초).</summary>
    public float InitialScenePickupLifetimeSeconds => initialScenePickupLifetimeSeconds;

    // ***************************************************************
    // Factory
    // ***************************************************************

    /// <summary>이 설정을 실행할 서버 권위 <see cref="BaseGameMode"/> 인스턴스를 생성한다.</summary>
    public abstract BaseGameMode CreateRuntimeMode();
}
