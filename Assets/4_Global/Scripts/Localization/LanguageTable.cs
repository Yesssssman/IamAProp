using System.Collections.Generic;
using Networking.Dto;

namespace Localization
{
    /// <summary>언어 키와 언어별 문장. 배열 순서는 <see cref="ELanguage"/> 순서.</summary>
    public static class LanguageTable
    {
        // ***************************************************************
        // 공통
        // ***************************************************************

        public const string COMMON_OK = "common.ok";
        public const string COMMON_CANCEL = "common.cancel";
        public const string COMMON_BACK = "common.back";
        public const string COMMON_LOADING = "common.loading";

        // ***************************************************************
        // 로그인·회원가입
        // ***************************************************************

        public const string LOGIN_TITLE = "login.title";
        public const string LOGIN_ID = "login.id";
        public const string LOGIN_ID_PLACEHOLDER = "login.id_placeholder";
        public const string LOGIN_PASSWORD = "login.password";
        public const string LOGIN_PASSWORD_PLACEHOLDER = "login.password_placeholder";
        public const string LOGIN_NOT_A_MEMBER = "login.not_a_member";
        public const string LOGIN_SIGN_UP_NOW = "login.sign_up_now";
        public const string LOGIN_ENTER_ID = "login.enter_id";
        public const string LOGIN_ENTER_PASSWORD = "login.enter_password";
        public const string SIGN_UP_TITLE = "sign_up.title";
        public const string SIGN_UP_NICKNAME = "sign_up.nickname";
        public const string SIGN_UP_NICKNAME_PLACEHOLDER = "sign_up.nickname_placeholder";
        public const string SIGN_UP_ENTER_NICKNAME = "sign_up.enter_nickname";
        public const string SIGN_UP_PASSWORD_CHECK = "sign_up.password_check";
        public const string SIGN_UP_PASSWORD_CHECK_PLACEHOLDER = "sign_up.password_check_placeholder";
        public const string SIGN_UP_PASSWORD_TOO_SHORT = "sign_up.password_too_short";
        public const string SIGN_UP_PASSWORD_MISMATCH = "sign_up.password_mismatch";

        // ***************************************************************
        // 메인
        // ***************************************************************

        public const string MAIN_CREATE_GAME = "main.create_game";
        public const string MAIN_JOIN_GAME = "main.join_game";
        public const string MAIN_SETTINGS = "main.settings";
        public const string MAIN_QUIT = "main.quit";

        // ***************************************************************
        // 게임 만들기
        // ***************************************************************

        public const string CREATE_TITLE = "create.title";
        public const string CREATE_ROOM_NAME = "create.room_name";
        public const string CREATE_ROOM_NAME_PLACEHOLDER = "create.room_name_placeholder";
        public const string CREATE_DEFAULT_ROOM_NAME = "create.default_room_name";
        public const string CREATE_PASSWORD = "create.password";
        public const string CREATE_PASSWORD_PLACEHOLDER = "create.password_placeholder";
        public const string CREATE_CREATE = "create.create";
        public const string CREATE_ENTER_ROOM_NAME = "create.enter_room_name";
        public const string CREATE_ENTER_PASSWORD = "create.enter_password";

        // ***************************************************************
        // 방 설정 (게임 모드·최대 인원·맵)
        // ***************************************************************

        public const string ROOM_SETTINGS_GAME_MODE = "room_settings.game_mode";
        public const string ROOM_SETTINGS_MAX_PLAYERS = "room_settings.max_players";
        public const string ROOM_SETTINGS_MAP = "room_settings.map";

        /// <summary>뒤에 <see cref="EGameMode"/> 이름을 붙여 씀.</summary>
        public const string GAME_MODE_PREFIX = "game_mode.";

        /// <summary>뒤에 <see cref="EGameMap"/> 이름을 붙여 씀.</summary>
        public const string MAP_PREFIX = "map.";

        // ***************************************************************
        // 로비 (참가할 수 있는 게임 목록)
        // ***************************************************************

        public const string LOBBY_TITLE = "lobby.title";
        public const string LOBBY_OWNER = "lobby.owner";
        public const string LOBBY_GAME_MODE = "lobby.game_mode";
        public const string LOBBY_JOIN = "lobby.join";
        public const string LOBBY_RETURN = "lobby.return";
        public const string LOBBY_REFRESH = "lobby.refresh";
        public const string LOBBY_EMPTY = "lobby.empty";
        public const string LOBBY_ENTER_PASSWORD = "lobby.enter_password";

        // ***************************************************************
        // 방 (대기실)
        // ***************************************************************

        public const string ROOM_READY = "room.ready";
        public const string ROOM_CANCEL_READY = "room.cancel_ready";
        public const string ROOM_GAME_START = "room.game_start";
        public const string ROOM_QUIT = "room.quit";
        public const string ROOM_SEND = "room.send";
        public const string ROOM_CHAT_PLACEHOLDER = "room.chat_placeholder";
        public const string ROOM_JOINED = "room.joined";
        public const string ROOM_LEFT = "room.left";
        public const string ROOM_MEMBER_KICKED = "room.member_kicked";
        public const string ROOM_READIED = "room.readied";
        public const string ROOM_CANCELED_READY = "room.canceled_ready";
        public const string ROOM_GAME_START_FAILED = "room.game_start_failed";
        public const string ROOM_CLOSED = "room.closed";
        public const string ROOM_DISCONNECTED = "room.disconnected";
        public const string ROOM_KICKED = "room.kicked";

        // ***************************************************************
        // 설정
        // ***************************************************************

        public const string SETTINGS_TITLE = "settings.title";
        public const string SETTINGS_LANGUAGE = "settings.language";
        public const string SETTINGS_MOUSE_SENSITIVITY = "settings.mouse_sensitivity";
        public const string SETTINGS_VOLUME = "settings.volume";
        public const string SETTINGS_MASTER_VOLUME = "settings.master_volume";
        public const string SETTINGS_MUSIC_VOLUME = "settings.music_volume";
        public const string SETTINGS_EFFECT_VOLUME = "settings.effect_volume";

        // ***************************************************************
        // 오류
        // ***************************************************************

        public const string ERROR_INVALID_PASSWORD = "error.invalid_password";
        public const string ERROR_ROOM_FULL = "error.room_full";
        public const string ERROR_ROOM_NOT_FOUND = "error.room_not_found";
        public const string ERROR_ROOM_IN_GAME = "error.room_in_game";
        public const string ERROR_ALREADY_IN_ROOM = "error.already_in_room";
        public const string ERROR_NOT_ALL_READY = "error.not_all_ready";
        public const string ERROR_CONNECTION = "error.connection";
        public const string ERROR_INVALID_CREDENTIALS = "error.invalid_credentials";
        public const string ERROR_ACCOUNT_ALREADY_EXISTS = "error.account_already_exists";
        public const string ERROR_NICKNAME_ALREADY_EXISTS = "error.nickname_already_exists";
        public const string ERROR_UNKNOWN = "error.unknown";

        private static readonly Dictionary<string, string[]> sTexts = new()
        {
            { COMMON_OK, new[] { "OK", "확인" } },
            { COMMON_CANCEL, new[] { "Cancel", "취소" } },
            { COMMON_BACK, new[] { "Back", "뒤로" } },
            { COMMON_LOADING, new[] { "Loading...", "불러오는 중..." } },

            { LOGIN_TITLE, new[] { "Log In", "로그인" } },
            { LOGIN_ID, new[] { "ID", "아이디" } },
            { LOGIN_ID_PLACEHOLDER, new[] { "Enter your ID", "아이디를 입력하세요" } },
            { LOGIN_PASSWORD, new[] { "PW", "비밀번호" } },
            { LOGIN_PASSWORD_PLACEHOLDER, new[] { "Enter your password", "비밀번호를 입력하세요" } },
            { LOGIN_NOT_A_MEMBER, new[] { "Not a member yet?", "아직 회원이 아니신가요?" } },
            { LOGIN_SIGN_UP_NOW, new[] { "Sign up now!", "지금 가입하세요!" } },
            { LOGIN_ENTER_ID, new[] { "Enter your ID.", "아이디를 입력하세요." } },
            { LOGIN_ENTER_PASSWORD, new[] { "Enter your password.", "비밀번호를 입력하세요." } },
            { SIGN_UP_TITLE, new[] { "Sign Up", "회원가입" } },
            { SIGN_UP_NICKNAME, new[] { "Nickname", "닉네임" } },
            { SIGN_UP_NICKNAME_PLACEHOLDER, new[] { "Enter your nickname", "닉네임을 입력하세요" } },
            { SIGN_UP_ENTER_NICKNAME, new[] { "Enter your nickname.", "닉네임을 입력하세요." } },
            { SIGN_UP_PASSWORD_CHECK, new[] { "PW Check", "비밀번호 확인" } },
            { SIGN_UP_PASSWORD_CHECK_PLACEHOLDER, new[] { "Enter your password again", "비밀번호를 다시 입력하세요" } },
            { SIGN_UP_PASSWORD_TOO_SHORT, new[] { "The password needs at least 6 characters.", "비밀번호는 6자 이상이어야 합니다." } },
            { SIGN_UP_PASSWORD_MISMATCH, new[] { "The passwords do not match.", "비밀번호가 일치하지 않습니다." } },

            { MAIN_CREATE_GAME, new[] { "Create Game", "게임 만들기" } },
            { MAIN_JOIN_GAME, new[] { "Join Game", "게임 참가" } },
            { MAIN_SETTINGS, new[] { "Settings", "설정" } },
            { MAIN_QUIT, new[] { "Quit", "종료" } },

            { CREATE_TITLE, new[] { "Create a new game", "새 게임 만들기" } },
            { CREATE_ROOM_NAME, new[] { "Room Name", "방 이름" } },
            { CREATE_ROOM_NAME_PLACEHOLDER, new[] { "Enter a room name", "방 이름을 입력하세요" } },
            { CREATE_DEFAULT_ROOM_NAME, new[] { "{0}'s game", "{0}의 게임" } },
            { CREATE_PASSWORD, new[] { "Password", "비밀번호" } },
            { CREATE_PASSWORD_PLACEHOLDER, new[] { "Enter a password", "비밀번호를 입력하세요" } },
            { CREATE_CREATE, new[] { "Create", "만들기" } },
            { CREATE_ENTER_ROOM_NAME, new[] { "Enter a room name.", "방 이름을 입력하세요." } },
            { CREATE_ENTER_PASSWORD, new[] { "Enter a password.", "비밀번호를 입력하세요." } },

            { ROOM_SETTINGS_GAME_MODE, new[] { "Game Mode", "게임 모드" } },
            { ROOM_SETTINGS_MAX_PLAYERS, new[] { "Max Players", "최대 인원" } },
            { ROOM_SETTINGS_MAP, new[] { "Map", "맵" } },
            { GAME_MODE_PREFIX + nameof(EGameMode.BattleRoyale), new[] { "Battle Royale", "배틀로얄" } },
            { GAME_MODE_PREFIX + nameof(EGameMode.Deathmatch), new[] { "Deathmatch", "데스매치" } },
            { MAP_PREFIX + nameof(EGameMap.Desert), new[] { "Desert", "사막" } },
            { MAP_PREFIX + nameof(EGameMap.City), new[] { "City", "도시" } },

            { LOBBY_TITLE, new[] { "Join Games", "게임 참가" } },
            { LOBBY_OWNER, new[] { "Owner: {0}", "방장: {0}" } },
            { LOBBY_GAME_MODE, new[] { "Game Mode: {0}", "게임 모드: {0}" } },
            { LOBBY_JOIN, new[] { "Join", "참가" } },
            { LOBBY_RETURN, new[] { "Return to Main", "메인으로" } },
            { LOBBY_REFRESH, new[] { "Refresh", "새로고침" } },
            { LOBBY_EMPTY, new[] { "No games are open.", "열린 게임이 없습니다." } },
            { LOBBY_ENTER_PASSWORD, new[] { "Enter a password", "비밀번호를 입력하세요" } },

            { ROOM_READY, new[] { "Ready", "준비" } },
            { ROOM_CANCEL_READY, new[] { "Cancel Ready", "준비 취소" } },
            { ROOM_GAME_START, new[] { "Game Start", "게임 시작" } },
            { ROOM_QUIT, new[] { "Quit", "나가기" } },
            { ROOM_SEND, new[] { "Send", "보내기" } },
            { ROOM_CHAT_PLACEHOLDER, new[] { "Enter a message", "메세지를 입력하세요" } },
            { ROOM_JOINED, new[] { "{0} joined the game.", "{0}님이 입장했습니다." } },
            { ROOM_LEFT, new[] { "{0} left the game.", "{0}님이 퇴장했습니다." } },
            { ROOM_MEMBER_KICKED, new[] { "{0} was kicked.", "{0}님이 강퇴되었습니다." } },
            { ROOM_READIED, new[] { "{0} readied.", "{0}님이 준비했습니다." } },
            { ROOM_CANCELED_READY, new[] { "{0} canceled ready.", "{0}님이 준비를 취소했습니다." } },
            { ROOM_GAME_START_FAILED, new[] { "Failed to start the game.", "게임을 시작하지 못했습니다." } },
            { ROOM_CLOSED, new[] { "The room was closed.", "방이 닫혔습니다." } },
            { ROOM_DISCONNECTED, new[] { "Disconnected from the room.", "방과의 연결이 끊겼습니다." } },
            { ROOM_KICKED, new[] { "You're kicked by the room owner.", "방장에 의해 강퇴되었습니다." } },

            { SETTINGS_TITLE, new[] { "Settings", "설정" } },
            { SETTINGS_LANGUAGE, new[] { "Languages", "언어" } },
            { SETTINGS_MOUSE_SENSITIVITY, new[] { "Mouse Sensitivity", "마우스 감도" } },
            { SETTINGS_VOLUME, new[] { "Volume", "볼륨" } },
            { SETTINGS_MASTER_VOLUME, new[] { "Master Volume", "전체 볼륨" } },
            { SETTINGS_MUSIC_VOLUME, new[] { "Music Volume", "음악 볼륨" } },
            { SETTINGS_EFFECT_VOLUME, new[] { "Effect Volume", "효과음 볼륨" } },

            { ERROR_INVALID_PASSWORD, new[] { "Wrong password.", "비밀번호가 틀렸습니다." } },
            { ERROR_ROOM_FULL, new[] { "The room is full.", "방이 가득 찼습니다." } },
            { ERROR_ROOM_NOT_FOUND, new[] { "The room no longer exists.", "방이 더 이상 없습니다." } },
            { ERROR_ROOM_IN_GAME, new[] { "The game has already started.", "이미 게임이 시작되었습니다." } },
            { ERROR_ALREADY_IN_ROOM, new[] { "You are already in a room.", "이미 다른 방에 있습니다." } },
            { ERROR_NOT_ALL_READY, new[] { "Not every player is ready.", "모든 플레이어가 준비하지 않았습니다." } },
            { ERROR_CONNECTION, new[] { "Cannot connect to the server.", "서버에 연결할 수 없습니다." } },
            { ERROR_INVALID_CREDENTIALS, new[] { "Wrong ID or password.", "아이디 또는 비밀번호가 틀렸습니다." } },
            { ERROR_ACCOUNT_ALREADY_EXISTS, new[] { "This ID is already in use.", "이미 사용 중인 아이디입니다." } },
            { ERROR_NICKNAME_ALREADY_EXISTS, new[] { "This nickname is already in use.", "이미 사용 중인 닉네임입니다." } },
            { ERROR_UNKNOWN, new[] { "Error: {0}", "오류: {0}" } },
        };

        public static bool TryGet(string key, ELanguage language, out string text)
        {
            if (key != null && sTexts.TryGetValue(key, out string[] texts))
            {
                text = texts[(int)language];
                return true;
            }

            text = null;
            return false;
        }
    }
}
