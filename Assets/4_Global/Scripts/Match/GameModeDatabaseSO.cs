using System;
using Networking.Dto;
using UnityEngine;

/// <summary>로비의 <see cref="EGameMode"/>·<see cref="EGameMap"/>과 인게임 설정·씬·프롭 풀을 매핑한다.</summary>
[CreateAssetMenu(fileName = "GameModeDatabase", menuName = "IamAProp/Match/Game Mode Database")]
public class GameModeDatabaseSO : ScriptableObject
{
    // ***************************************************************
    // Types
    // ***************************************************************

    /// <summary>로비 맵 열거형과 실제 Unity 씬 이름의 매핑 항목.</summary>
    [Serializable]
    public struct MapSceneEntry
    {
        /// <summary>로비에서 선택되는 맵 종류.</summary>
        public EGameMap map;

        /// <summary>BuildSettings에 등록된 인게임 씬 이름.</summary>
        public string sceneName;
    }

    // ***************************************************************
    // Constants
    // ***************************************************************

    /// <summary><see cref="Resources.Load{T}(string)"/> 조회용 기본 리소스 경로.</summary>
    public const string DefaultResourcePath = "GameModeDatabase";

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>지원하는 게임 모드 설정 에셋 목록.</summary>
    [Header("게임 모드 목록")]
    [SerializeField] private GameModeConfigSO[] gameModes;

    /// <summary>일치하는 모드 설정을 찾지 못했을 때 사용할 기본 모드 설정.</summary>
    [SerializeField] private GameModeConfigSO defaultGameMode;

    /// <summary>맵별 인게임 씬 매핑 테이블.</summary>
    [Header("맵 및 씬 매핑")]
    [SerializeField] private MapSceneEntry[] mapScenes;

    /// <summary>매핑된 씬이 없을 때 사용할 기본 인게임 씬 이름.</summary>
    [SerializeField] private string fallbackInGameScene = "InGameScene";

    /// <summary>매치 종료 후 복귀할 로비 씬 이름.</summary>
    [SerializeField] private string lobbySceneName = "LobbyScene";

    /// <summary>플레이어 스폰 및 리스폰에 사용할 기본 프롭 풀.</summary>
    [Header("공통 에셋 참조")]
    [SerializeField] private PropPoolSO defaultPropPool;

    /// <summary>로드아웃 UI 및 서버 무기 검증에 사용할 무기 데이터베이스.</summary>
    [SerializeField] private WeaponDatabaseSO weaponDatabase;

    /// <summary>런타임 캐시 인스턴스.</summary>
    private static GameModeDatabaseSO sCachedInstance;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>매치 종료 후 복귀할 로비 씬 이름.</summary>
    public string LobbySceneName => string.IsNullOrEmpty(lobbySceneName) ? "LobbyScene" : lobbySceneName;

    /// <summary>기본 프롭 랜덤 풀.</summary>
    public PropPoolSO DefaultPropPool => defaultPropPool;

    /// <summary>공통 무기 데이터베이스.</summary>
    public WeaponDatabaseSO WeaponDatabase => weaponDatabase;

    /// <summary>기본 게임 모드 설정.</summary>
    public GameModeConfigSO DefaultGameMode => defaultGameMode;

    // ***************************************************************
    // Static Access
    // ***************************************************************

    /// <summary>도메인 리로드 비활성 환경에서 플레이 시작 시 캐시를 비운다.</summary>
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetStatics()
    {
        sCachedInstance = null;
    }

    /// <summary><c>Resources/GameModeDatabase</c>에서 공유 데이터베이스를 로드해 반환한다.</summary>
    public static GameModeDatabaseSO LoadDefault()
    {
        if (sCachedInstance == null)
        {
            sCachedInstance = Resources.Load<GameModeDatabaseSO>(DefaultResourcePath);
        }
        return sCachedInstance;
    }

    // ***************************************************************
    // Public Query API
    // ***************************************************************

    /// <summary><paramref name="mode"/>에 해당하는 <see cref="GameModeConfigSO"/>를 반환한다.</summary>
    public GameModeConfigSO GetConfig(EGameMode mode)
    {
        if (gameModes != null)
        {
            for (int i = 0; i < gameModes.Length; i++)
            {
                if (gameModes[i] != null && gameModes[i].ModeType == mode)
                {
                    return gameModes[i];
                }
            }
        }

        return defaultGameMode;
    }

    /// <summary>문자열 <paramref name="modeName"/>을 파싱해 해당하는 <see cref="GameModeConfigSO"/>를 반환한다.</summary>
    public GameModeConfigSO GetConfigByName(string modeName)
    {
        if (!string.IsNullOrEmpty(modeName) && Enum.TryParse(modeName, true, out EGameMode parsed))
        {
            return GetConfig(parsed);
        }

        return defaultGameMode;
    }

    /// <summary><paramref name="map"/>에 매핑된 인게임 씬 이름을 반환한다.</summary>
    public string GetSceneName(EGameMap map)
    {
        if (mapScenes != null)
        {
            for (int i = 0; i < mapScenes.Length; i++)
            {
                if (mapScenes[i].map == map && !string.IsNullOrEmpty(mapScenes[i].sceneName))
                {
                    return mapScenes[i].sceneName;
                }
            }
        }

        return string.IsNullOrEmpty(fallbackInGameScene) ? "InGameScene" : fallbackInGameScene;
    }

    /// <summary>문자열 <paramref name="mapName"/>에 매핑된 인게임 씬 이름을 반환한다.</summary>
    public string GetSceneNameByName(string mapName)
    {
        if (!string.IsNullOrEmpty(mapName) && Enum.TryParse(mapName, true, out EGameMap parsed))
        {
            return GetSceneName(parsed);
        }

        return string.IsNullOrEmpty(fallbackInGameScene) ? "InGameScene" : fallbackInGameScene;
    }
}
