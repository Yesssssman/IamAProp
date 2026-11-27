using Networking.Dto;
using UnityEngine;

/// <summary>개인전(FFA) 데스매치 게임 모드의 타이밍, 승리 조건 및 드롭 무기 소멸 설정.</summary>
[CreateAssetMenu(fileName = "DeathmatchConfig", menuName = "IamAProp/Match/Deathmatch Config")]
public class DeathmatchConfigSO : GameModeConfigSO
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>조기 승리 판정에 사용할 목표 킬 수 (0이면 제한 시간 종료 시 킬 수 순위로만 판정).</summary>
    [Header("데스매치 규칙")]
    [Min(0)]
    [SerializeField] private int targetKills = 0;

    /// <summary>자폭·환경사 시 킬 점수 차감 여부.</summary>
    [SerializeField] private bool penalizeSuicideKill = false;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <inheritdoc/>
    public override EGameMode ModeType => EGameMode.Deathmatch;

    /// <summary>조기 승리 목표 킬 수 (0이면 제한 시간 종료 기준).</summary>
    public int TargetKills => targetKills;

    /// <summary>자폭·환경사 시 킬 수 차감 여부.</summary>
    public bool PenalizeSuicideKill => penalizeSuicideKill;

    // ***************************************************************
    // ScriptableObject
    // ***************************************************************

    private void Reset()
    {
        waitingTimeoutSeconds = 15f;
        countdownSeconds = 5f;
        matchDurationSeconds = 300f;
        resultDisplaySeconds = 8f;
        allowRespawn = true;
        respawnDelaySeconds = 5f;
        spawnInvulnerabilitySeconds = 2f;
        randomizePropOnRespawn = true;
        allowSpectateButton = false;
        allowLoadoutSelection = true;
        dropFirearmsOnDeath = true;
        despawnDroppedWeapons = true;
        droppedWeaponLifetimeSeconds = 20f;
        despawnInitialScenePickups = false;
        initialScenePickupLifetimeSeconds = 60f;
        targetKills = 0;
        penalizeSuicideKill = false;
    }

    // ***************************************************************
    // Factory
    // ***************************************************************

    /// <inheritdoc/>
    public override BaseGameMode CreateRuntimeMode()
    {
        return new DeathmatchGameMode();
    }
}
