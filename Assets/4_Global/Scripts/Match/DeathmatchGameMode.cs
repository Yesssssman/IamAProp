using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;

/// <summary>개인전(FFA) 데스매치의 킬/데스 집계, 동점 순위 판정 및 리스폰 규칙을 구현한다.</summary>
public sealed class DeathmatchGameMode : BaseGameMode
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>데스매치 전용 설정 에셋 참조.</summary>
    private DeathmatchConfigSO _dmConfig;

    /// <summary>순위 정렬에 재사용하는 레코드 버퍼.</summary>
    private readonly List<PlayerMatchRecord> _sortBuffer = new(16);

    // ***************************************************************
    // BaseGameMode
    // ***************************************************************

    /// <inheritdoc/>
    public override void Initialize(MatchManager matchManager, GameModeConfigSO config)
    {
        base.Initialize(matchManager, config);
        _dmConfig = config as DeathmatchConfigSO;
    }

    /// <inheritdoc/>
    public override void OnPlayerKilled(PlayerHealth victim, NetworkConnection attacker, NetworkObject killerObject)
    {
        if (Match == null || victim == null || Match.State != MatchState.InProgress) return;

        NetworkConnection victimConn = victim.Owner;
        int victimId = victimConn != null && victimConn.IsValid ? victimConn.ClientId : -1;
        int attackerId = attacker != null && attacker.IsValid ? attacker.ClientId : -1;
        bool isSuicide = attackerId < 0 || attackerId == victimId;

        string victimName = Match.GetPlayerNickName(victimId);
        string killerName = isSuicide ? string.Empty : Match.GetPlayerNickName(attackerId);
        string weaponId = string.Empty;

        if (!isSuicide && killerObject != null)
        {
            PlayerWeaponInventory killerInventory = killerObject.GetComponent<PlayerWeaponInventory>();
            if (killerInventory != null)
            {
                weaponId = killerInventory.EquippedWeaponId ?? string.Empty;
            }
        }

        // 피해자 데스 증가 및 리스폰 예약
        if (victimId >= 0)
        {
            float respawnDelay = Config != null ? Config.RespawnDelaySeconds : 5f;
            float respawnEndTime = Config != null && Config.AllowRespawn
                ? Match.CurrentServerTime + respawnDelay
                : 0f;

            Match.RecordPlayerDeath(victimId, respawnEndTime, penalizeKill: isSuicide && _dmConfig != null && _dmConfig.PenalizeSuicideKill);

            if (Config != null && Config.DropFirearmsOnDeath)
            {
                PlayerWeaponInventory victimInventory = victim.GetComponent<PlayerWeaponInventory>();
                victimInventory?.DropFirearmsOnDeath();
            }

            if (Config != null && Config.AllowRespawn && victimConn != null && victimConn.IsValid)
            {
                Match.ScheduleRespawn(victimConn, respawnEndTime);
            }
        }

        // 킬러 킬 증가 및 마지막 킬 시각 기록
        if (!isSuicide)
        {
            Match.RecordPlayerKill(attackerId, Match.CurrentServerTime);
        }

        Match.BroadcastKillFeed(new KillFeedEntry
        {
            KillerName = killerName,
            VictimName = victimName,
            WeaponId = weaponId,
            IsSuicide = isSuicide
        });
    }

    /// <inheritdoc/>
    public override bool TryEvaluateMatchEnd(out MatchEndResult result)
    {
        result = default;
        if (Match == null || Match.State != MatchState.InProgress) return false;

        // 1. 선택적 목표 킬 수 조기 달성 검사 (TargetKills > 0일 때만)
        int targetKills = _dmConfig != null ? _dmConfig.TargetKills : 0;
        if (targetKills > 0)
        {
            IReadOnlyList<PlayerMatchRecord> records = Match.Records;
            for (int i = 0; i < records.Count; i++)
            {
                if (records[i].Kills >= targetKills)
                {
                    result = BuildMatchEndResult("TARGET_KILLS_REACHED");
                    return true;
                }
            }
        }

        // 2. 제한 시간 만료 검사
        if (Config != null && Config.MatchDurationSeconds > 0f && Match.StateRemainingTime <= 0f)
        {
            result = BuildMatchEndResult("TIME_EXPIRED");
            return true;
        }

        return false;
    }

    // ***************************************************************
    // Static Ranking Utility
    // ***************************************************************

    /// <summary>킬 수 내림차순 → 데스 오름차순 → 해당 킬 수 도달 시각 오름차순으로 순위를 비교한다.</summary>
    public static int CompareRecords(PlayerMatchRecord a, PlayerMatchRecord b)
    {
        int killCmp = b.Kills.CompareTo(a.Kills);
        if (killCmp != 0) return killCmp;

        int deathCmp = a.Deaths.CompareTo(b.Deaths);
        if (deathCmp != 0) return deathCmp;

        int timeCmp = a.LastKillServerTime.CompareTo(b.LastKillServerTime);
        if (timeCmp != 0) return timeCmp;

        return a.ClientId.CompareTo(b.ClientId);
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>현재 매치 레코드를 순위 기준대로 정렬해 우승자 결과를 생성한다.</summary>
    private MatchEndResult BuildMatchEndResult(string reason)
    {
        _sortBuffer.Clear();
        IReadOnlyList<PlayerMatchRecord> records = Match.Records;
        for (int i = 0; i < records.Count; i++)
        {
            _sortBuffer.Add(records[i]);
        }

        if (_sortBuffer.Count == 0)
        {
            return new MatchEndResult
            {
                WinnerClientId = -1,
                WinnerTeamId = 0,
                WinnerName = string.Empty,
                Reason = reason
            };
        }

        _sortBuffer.Sort(CompareRecords);
        PlayerMatchRecord top = _sortBuffer[0];
        return new MatchEndResult
        {
            WinnerClientId = top.ClientId,
            WinnerTeamId = top.TeamId,
            WinnerName = top.NickName,
            Reason = reason
        };
    }
}
