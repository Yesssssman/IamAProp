namespace Networking
{
    /// <summary>계정 API(/api/account) 응답 메세지.</summary>
    public static class AccountMessages
    {
        public const string OK = "OK";
        public const string MANDATORY_PARAM_MISSING = "MANDATORY_PARAM_MISSING";
        public const string INVALID_PARAMETER = "INVALID_PARAMETER";
        public const string UNSUPPORTED_MEMBER_TYPE = "UNSUPPORTED_MEMBER_TYPE";
        public const string UNSUPPORTED_ACTION_TYPE = "UNSUPPORTED_ACTION_TYPE";
        public const string UNSUPPORTED_SNS_PROVIDER = "UNSUPPORTED_SNS_PROVIDER";
        public const string INVALID_JWT_TOKEN = "INVALID_JWT_TOKEN";
        public const string EXPIRED_JWT_TOKEN = "EXPIRED_JWT_TOKEN";
        public const string INVALID_CREDENTIALS = "INVALID_CREDENTIALS";
        public const string USER_ACCESS_DENIED = "USER_ACCESS_DENIED";
        public const string GAME_CODE_NOT_FOUND = "GAME_CODE_NOT_FOUND";
        public const string GAME_ACCOUNT_NOT_FOUND = "GAME_ACCOUNT_NOT_FOUND";
        public const string ACCOUNT_ALREADY_EXISTS = "ACCOUNT_ALREADY_EXISTS";
        public const string NICKNAME_ALREADY_EXISTS = "NICKNAME_ALREADY_EXISTS";
        public const string EXCEPTION_OCCURRED = "EXCEPTION_OCCURRED";
        public const string SERVICE_UNDER_MAINTENANCE = "SERVICE_UNDER_MAINTENANCE";
        public const string SERVICE_BLOCKED = "SERVICE_BLOCKED";
    }

    /// <summary>게임 시작 API(/api/game/gamestart) 응답 메세지. 성공은 "OK".</summary>
    public static class GameStartMessages
    {
        public const string MANDATORY_PARAM_MISSING = "MANDATORY_PARAM_MISSING";
        public const string INVALID_ACCESS_TOKEN = "INVALID_ACCESS_TOKEN";
        public const string ACCESS_TOKEN_EXPIRED = "ACCESS_TOKEN_EXPIRED";
        public const string GAME_NOT_FOUND = "GAME_NOT_FOUND";
        public const string GAME_NOT_AVAILABLE = "GAME_NOT_AVAILABLE";
        public const string GAME_ACCOUNT_NOT_FOUND = "GAME_ACCOUNT_NOT_FOUND";
        public const string GAME_ACCOUNT_NOT_ACTIVE = "GAME_ACCOUNT_NOT_ACTIVE";
        public const string USER_NOT_ACTIVE = "USER_NOT_ACTIVE";
        public const string EXCEPTION_OCCURRED = "EXCEPTION_OCCURRED";
    }

    /// <summary>로비 API(/api/lobby, 대기실 wss) 응답 메세지. 서버에 요청한 명세 기준.</summary>
    public static class LobbyMessages
    {
        public const string LOBBY_NOT_FOUND = "LOBBY_NOT_FOUND";
        public const string LOBBY_FULL = "LOBBY_FULL";
        public const string INVALID_PASSWORD = "INVALID_PASSWORD";
        public const string ALREADY_IN_LOBBY = "ALREADY_IN_LOBBY";
        public const string NOT_LOBBY_HOST = "NOT_LOBBY_HOST";
        public const string NOT_ALL_READY = "NOT_ALL_READY";
        public const string INVALID_LOBBY_TICKET = "INVALID_LOBBY_TICKET";
        public const string LOBBY_IN_GAME = "LOBBY_IN_GAME";
        public const string LOBBY_NOT_IN_GAME = "LOBBY_NOT_IN_GAME";
        public const string GAME_FINISHED = "GAME_FINISHED";         // GameEnded 이유: 방장이 게임 종료
        public const string HOST_START_FAILED = "HOST_START_FAILED"; // GameEnded 이유: 방장의 NetworkManager.StartHost 실패
    }

    /// <summary>서버가 아닌 클라이언트가 만드는 메세지.</summary>
    public static class CommonMessages
    {
        public const string ALREADY_ONLINE = "ALREADY_ONLINE";
        public const string NO_AUTH_INFO_IN_LOCAL = "NO_AUTH_INFO_IN_LOCAL";
        public const string NO_REFRESH_TOKEN = "NO_REFRESH_TOKEN";
        public const string NOT_IN_LOBBY = "NOT_IN_LOBBY";
        public const string RELAY_CONNECTION_FAILED = "RELAY_CONNECTION_FAILED"; // Relay 참가 또는 호스트 접속 실패
        public const string CONNECTION_ERROR = "CONNECTION_ERROR"; // 서버 다운 또는 디바이스 오프라인
        public const string INVALID_RESPONSE = "INVALID_RESPONSE"; // 응답 본문이 JSON이 아님
    }

    public static class ResponseMessageUtil
    {
        /// <summary>서버 응답이 성공(로그인, 토큰 재발급, 방 참가 등)인지 반환.</summary>
        public static bool IsOk(string msg)
        {
            return msg is AccountMessages.OK;
        }

        /// <summary>아이디·비밀번호 오입력에 의한 실패인지 반환.</summary>
        public static bool IsInvalidCredential(string msg)
        {
            return msg is AccountMessages.INVALID_CREDENTIALS;
        }

        /// <summary>서버 연결에 실패했는지 반환 (서버 다운 또는 디바이스 오프라인).</summary>
        public static bool IsServerUnreachable(string msg)
        {
            return msg is CommonMessages.CONNECTION_ERROR;
        }

        /// <summary>엑세스 토큰이 무효·만료되어 재발급이 필요한지 반환.</summary>
        public static bool IsAccessTokenExpired(string msg)
        {
            return msg is GameStartMessages.INVALID_ACCESS_TOKEN or GameStartMessages.ACCESS_TOKEN_EXPIRED;
        }
    }
}
