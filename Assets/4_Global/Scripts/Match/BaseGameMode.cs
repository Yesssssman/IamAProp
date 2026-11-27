using FishNet.Connection;
using FishNet.Object;

/// <summary>서버 권위 게임 모드 규칙을 추상화하는 베이스 전략 클래스.</summary>
public abstract class BaseGameMode
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>이 모드를 소유하고 동기화 상태를 관리하는 <see cref="MatchManager"/>.</summary>
    protected MatchManager Match { get; private set; }

    /// <summary>이 모드에 바인딩된 설정 에셋.</summary>
    protected GameModeConfigSO Config { get; private set; }

    // ***************************************************************
    // Lifecycle
    // ***************************************************************

    /// <summary>모드 실행에 필요한 매치 매니저와 설정을 초기화한다.</summary>
    public virtual void Initialize(MatchManager matchManager, GameModeConfigSO config)
    {
        Match = matchManager;
        Config = config;
    }

    /// <summary>매치가 본 경기(<see cref="MatchState.InProgress"/>)로 진입할 때 호출된다.</summary>
    public virtual void OnMatchStarted() { }

    /// <summary>본 경기 중 매 서버 프레임 호출되어 타이머와 종료 조건을 갱신한다.</summary>
    public virtual void OnServerUpdate(float deltaTime) { }

    /// <summary>플레이어가 접속해 매치에 등록되었을 때 호출된다.</summary>
    public virtual void OnPlayerRegistered(NetworkConnection conn) { }

    /// <summary>플레이어가 서버에서 연결 해제되었을 때 호출된다.</summary>
    public virtual void OnPlayerDisconnected(NetworkConnection conn) { }

    /// <summary>서버에서 <paramref name="attacker"/>가 <paramref name="victim"/>에게 피해를 줄 수 있는지 판정한다.</summary>
    public virtual bool CanDamage(NetworkConnection attacker, PlayerHealth victim)
    {
        return Match != null && Match.State == MatchState.InProgress;
    }

    /// <summary>서버에서 플레이어가 사망했을 때 점수 집계와 리스폰 정책을 처리한다.</summary>
    public virtual void OnPlayerKilled(PlayerHealth victim, NetworkConnection attacker, NetworkObject killerObject) { }

    /// <summary>매치 종료 조건 충족 여부를 판정하고 충족 시 <paramref name="result"/>를 채운다.</summary>
    public virtual bool TryEvaluateMatchEnd(out MatchEndResult result)
    {
        result = default;
        return false;
    }

    /// <summary>매치가 종료 상태로 전환된 직후 호출된다.</summary>
    public virtual void OnMatchEnded(in MatchEndResult result) { }
}
