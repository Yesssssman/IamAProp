using System;

/// <summary>인게임 매치의 전체 진행 단계.</summary>
public enum MatchState : byte
{
    WaitingForPlayers = 0,
    Countdown = 1,
    InProgress = 2,
    MatchEnded = 3,
    ReturningToRoom = 4
}

/// <summary>플레이어가 리스폰 또는 매치 시작 시 사용할 슬롯별 무기 로드아웃 선택값.</summary>
[Serializable]
public struct PlayerLoadoutSelection : IEquatable<PlayerLoadoutSelection>
{
    /// <summary>4번 슬롯 주무기 식별자.</summary>
    public string PrimaryWeaponId;

    /// <summary>3번 슬롯 보조무기 식별자.</summary>
    public string SecondaryWeaponId;

    /// <summary>2번 슬롯 근접무기 식별자.</summary>
    public string MeleeWeaponId;

    /// <summary>5번 슬롯 투척무기 식별자.</summary>
    public string ThrowableWeaponId;

    public bool Equals(PlayerLoadoutSelection other)
    {
        return string.Equals(PrimaryWeaponId, other.PrimaryWeaponId, StringComparison.Ordinal)
            && string.Equals(SecondaryWeaponId, other.SecondaryWeaponId, StringComparison.Ordinal)
            && string.Equals(MeleeWeaponId, other.MeleeWeaponId, StringComparison.Ordinal)
            && string.Equals(ThrowableWeaponId, other.ThrowableWeaponId, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return obj is PlayerLoadoutSelection other && Equals(other);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(
            PrimaryWeaponId ?? string.Empty,
            SecondaryWeaponId ?? string.Empty,
            MeleeWeaponId ?? string.Empty,
            ThrowableWeaponId ?? string.Empty);
    }

    public static bool operator ==(PlayerLoadoutSelection left, PlayerLoadoutSelection right) => left.Equals(right);
    public static bool operator !=(PlayerLoadoutSelection left, PlayerLoadoutSelection right) => !left.Equals(right);
}

/// <summary>스코어보드와 결과창에 동기화되는 개별 플레이어의 매치 전적 레코드.</summary>
[Serializable]
public struct PlayerMatchRecord : IEquatable<PlayerMatchRecord>
{
    /// <summary>FishNet 클라이언트 연결 ID.</summary>
    public int ClientId;

    /// <summary>웹 로비 서버 계정 해시코드 (단독 테스트 시 빈 문자열).</summary>
    public string AccountHashcode;

    /// <summary>화면에 표시할 플레이어 닉네임.</summary>
    public string NickName;

    /// <summary>소속 팀 ID (개인전은 0).</summary>
    public byte TeamId;

    /// <summary>누적 킬 수.</summary>
    public int Kills;

    /// <summary>누적 데스 수.</summary>
    public int Deaths;

    /// <summary>모드별 점수 (데스매치는 킬 수와 동일).</summary>
    public int Score;

    /// <summary>현재 생존 여부.</summary>
    public bool IsAlive;

    /// <summary>클라이언트 씬 로드 및 매치 등록 완료 여부.</summary>
    public bool IsLoaded;

    /// <summary>현재 서버에 접속 중인지 여부.</summary>
    public bool IsConnected;

    /// <summary>리스폰 완료 예정 서버 시각 (생존 중이면 0).</summary>
    public float RespawnEndServerTime;

    /// <summary>현재 킬 수에 마지막으로 도달한 서버 시각 (동점 타이브레이커용).</summary>
    public float LastKillServerTime;

    public bool Equals(PlayerMatchRecord other)
    {
        return ClientId == other.ClientId
            && string.Equals(AccountHashcode, other.AccountHashcode, StringComparison.Ordinal)
            && string.Equals(NickName, other.NickName, StringComparison.Ordinal)
            && TeamId == other.TeamId
            && Kills == other.Kills
            && Deaths == other.Deaths
            && Score == other.Score
            && IsAlive == other.IsAlive
            && IsLoaded == other.IsLoaded
            && IsConnected == other.IsConnected
            && RespawnEndServerTime.Equals(other.RespawnEndServerTime)
            && LastKillServerTime.Equals(other.LastKillServerTime);
    }

    public override bool Equals(object obj)
    {
        return obj is PlayerMatchRecord other && Equals(other);
    }

    public override int GetHashCode()
    {
        HashCode hash = new HashCode();
        hash.Add(ClientId);
        hash.Add(AccountHashcode ?? string.Empty);
        hash.Add(NickName ?? string.Empty);
        hash.Add(TeamId);
        hash.Add(Kills);
        hash.Add(Deaths);
        hash.Add(Score);
        hash.Add(IsAlive);
        hash.Add(IsLoaded);
        hash.Add(IsConnected);
        hash.Add(RespawnEndServerTime);
        hash.Add(LastKillServerTime);
        return hash.ToHashCode();
    }

    public static bool operator ==(PlayerMatchRecord left, PlayerMatchRecord right) => left.Equals(right);
    public static bool operator !=(PlayerMatchRecord left, PlayerMatchRecord right) => !left.Equals(right);
}

/// <summary>킬피드 UI에 표시할 단일 처치 이벤트 정보.</summary>
[Serializable]
public struct KillFeedEntry
{
    /// <summary>킬러 닉네임 (자살/환경사 시 빈 문자열).</summary>
    public string KillerName;

    /// <summary>피해자 닉네임.</summary>
    public string VictimName;

    /// <summary>처치에 사용된 무기 식별자.</summary>
    public string WeaponId;

    /// <summary>자폭 또는 환경사 여부.</summary>
    public bool IsSuicide;
}

/// <summary>매치 종료 시 확정된 승자 및 종료 사유.</summary>
public struct MatchEndResult
{
    /// <summary>우승 플레이어의 FishNet ClientId (없으면 -1).</summary>
    public int WinnerClientId;

    /// <summary>우승 팀 ID (개인전은 0).</summary>
    public byte WinnerTeamId;

    /// <summary>우승자 닉네임.</summary>
    public string WinnerName;

    /// <summary>매치 종료 사유 문자열.</summary>
    public string Reason;
}
