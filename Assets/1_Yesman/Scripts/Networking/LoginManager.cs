using System;
using System.IO;
using Networking.Dto;
using UnityEngine;
using UnityEngine.Networking;

namespace Networking
{
    /// <summary>JW 로그인 서버 통신 라이브러리.</summary>
    public static class LoginManager
    {
        // GAME_CODE: 통합 게임 백엔드서버에서 발급받은 IamAProp의 게임 식별자
#if LOCAL
        private const string AUTH_SERVER = "http://127.0.0.1:5050";
        private const string GAME_CODE = "{}";
#elif DEV
        private const string AUTH_SERVER = "http://127.0.0.1/dev"; // PLACEHOLDER: 개발 서버 주소
        private const string GAME_CODE = "{}"; // PLACEHOLDER: 개발 서버 게임 코드
#elif PROD
        private const string AUTH_SERVER = "http://127.0.0.1/prod"; // PLACEHOLDER: 운영 서버 주소
        private const string GAME_CODE = "{}"; // PLACEHOLDER: 운영 서버 게임 코드
#else
        private const string AUTH_SERVER = "http://127.0.0.1:5050";
        private const string GAME_CODE = "858190170F6953FAA83829F20451F719";
#endif

        private const string AUTH_FILE = "iamaprop_auth.json";
        private const string CONTENT_TYPE_JSON = "application/json";

        /// <summary>엑세스 토큰. 메모리에만 존재.</summary>
        private static string sAccessToken;

        /// <summary>리프레쉬 토큰. <see cref="AUTH_FILE"/>에 저장됨.</summary>
        private static string sRefreshToken;

        // ID/PW 로그인 또는 자동 로그인에 성공했을 때 true.
        //
        // Warning: 이 값이 true여도 토큰이 서버에서 유효한지 보장하지 않음. 엑세스 토큰은 1시간이면
        //          만료되므로 모든 요청은 `SendAuthorized`처럼 "엑세스 토큰으로 요청 ->
        //          만료 시 리프레쉬 토큰으로 재발급 후 재요청" 과정을 거쳐야 함.
        private static bool sIsOnline;

        /// <summary>ID/PW 로그인 또는 자동 로그인에 성공했는지 여부.</summary>
        public static bool IsOnline => sIsOnline;

#if LOCAL || DEV
        public static void ResetOnline()
        {
            sIsOnline = false;
        }
#endif

        /// <summary>
        /// 플레이 시작 시 static 상태를 비움.
        ///
        /// 프로젝트 설정의 Enter Play Mode Options에서 도메인 리로드가 꺼져 있어 static 필드가 이전 플레이
        /// 세션의 값을 유지함. 비우지 않으면 이전 세션에서 로그인한 상태가 다음 세션에도 남음.
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            sAccessToken = null;
            sRefreshToken = null;
            sIsOnline = false;
        }

        /// <summary>디스크에 영구히 보존되는 인증 정보 파일 경로.</summary>
        private static string sPersistentDataPath => Path.Combine(Application.persistentDataPath, AUTH_FILE);

        /// <summary>사용자 회원가입. 필수: memberType, memberId, memberPw(6자 이상), memberName.</summary>
        public static async Awaitable<GameUserResponseDTO> SignUp(GameUserRequestDTO request)
        {
            request.gameCode = GAME_CODE;
            request.actionType = "JOIN";

            using (UnityWebRequest req = UnityWebRequest.Post(AUTH_SERVER + "/api/account/gameuser", JsonUtility.ToJson(request), CONTENT_TYPE_JSON))
            {
                await req.SendWebRequest();
                GameUserResponseDTO response = Deserialize<GameUserResponseDTO>(req);

                if (ResponseMessageUtil.IsOk(response.responseMessage))
                {
                    StoreTokens(response);
                    sIsOnline = true;
                }

                return response;
            }
        }

        /// <summary>사용자 로그인. 필수: memberId, memberPw.</summary>
        public static async Awaitable<GameUserResponseDTO> LogIn(GameUserRequestDTO request)
        {
            if (sIsOnline)
            {
                return new GameUserResponseDTO { responseMessage = CommonMessages.ALREADY_ONLINE };
            }

            request.gameCode = GAME_CODE;
            request.actionType = "LOGIN";

            using (UnityWebRequest req = UnityWebRequest.Post(AUTH_SERVER + "/api/account/gameuser", JsonUtility.ToJson(request), CONTENT_TYPE_JSON))
            {
                await req.SendWebRequest();
                GameUserResponseDTO response = Deserialize<GameUserResponseDTO>(req);

                if (ResponseMessageUtil.IsOk(response.responseMessage))
                {
                    StoreTokens(response);
                    sIsOnline = true;
                }

                return response;
            }
        }

        /// <summary>디스크에 저장된 토큰으로 로그인. 성공 시 ID/PW 입력 불필요.</summary>
        public static async Awaitable<GameUserResponseDTO> AutoLogIn()
        {
            if (sIsOnline)
            {
                return new GameUserResponseDTO { responseMessage = CommonMessages.ALREADY_ONLINE };
            }

            if (File.Exists(sPersistentDataPath))
            {
                GameUserTokenRefreshRequestDTO saved = JsonUtility.FromJson<GameUserTokenRefreshRequestDTO>(File.ReadAllText(sPersistentDataPath));
                sAccessToken = saved.accessToken;
                sRefreshToken = saved.refreshToken;
            }

            if (string.IsNullOrEmpty(sAccessToken) && string.IsNullOrEmpty(sRefreshToken))
            {
                return new GameUserResponseDTO { responseMessage = CommonMessages.NO_AUTH_INFO_IN_LOCAL };
            }

            // 게임 시작 API로 토큰 유효성 확인 — 엑세스 토큰 만료 시 재발급 후 재시도
            GameUserResponseDTO response = await SendAuthorized<GameUserResponseDTO>(AUTH_SERVER + "/api/game/gamestart", "");

            if (ResponseMessageUtil.IsOk(response.responseMessage))
            {
                sIsOnline = true;
            }

            return response;
        }

        /// <summary>저장된 리프레쉬 토큰으로 엑세스 토큰을 재발급받아 저장.</summary>
        public static async Awaitable<GameUserResponseDTO> RefreshAccessToken()
        {
            if (string.IsNullOrEmpty(sRefreshToken))
            {
                // 리프레쉬 토큰이 없으면 LogIn으로 ID/PW를 입력해 로그인해야 함
                return new GameUserResponseDTO { responseMessage = CommonMessages.NO_REFRESH_TOKEN };
            }

            string postData = JsonUtility.ToJson(new GameUserTokenRefreshRequestDTO { refreshToken = sRefreshToken });

            using (UnityWebRequest req = UnityWebRequest.Post(AUTH_SERVER + "/api/account/gameusertokenrefresh", postData, CONTENT_TYPE_JSON))
            {
                await req.SendWebRequest();
                GameUserResponseDTO response = Deserialize<GameUserResponseDTO>(req);

                if (ResponseMessageUtil.IsOk(response.responseMessage))
                {
                    StoreTokens(response);
                }

                return response;
            }
        }

        /// <summary>
        /// 엑세스 토큰을 Authorization 헤더에 붙여 <paramref name="url"/>로 POST 요청함.
        /// 토큰이 무효·만료됐으면 재발급 후 한 번 재시도함.
        /// </summary>
        public static async Awaitable<T> SendAuthorized<T>(string url, string postData) where T : ResponseDTO, new()
        {
            T response = await PostAuthorized<T>(url, postData);

            if (!ResponseMessageUtil.IsAccessTokenExpired(response.responseMessage))
            {
                return response;
            }

            GameUserResponseDTO refreshed = await RefreshAccessToken();

            if (!ResponseMessageUtil.IsOk(refreshed.responseMessage))
            {
                return new T { responseCode = refreshed.responseCode, responseMessage = refreshed.responseMessage };
            }

            return await PostAuthorized<T>(url, postData);
        }

        private static async Awaitable<T> PostAuthorized<T>(string url, string postData) where T : ResponseDTO, new()
        {
            using (UnityWebRequest req = UnityWebRequest.Post(url, postData, CONTENT_TYPE_JSON))
            {
                req.SetRequestHeader("Authorization", "Bearer " + sAccessToken);
                await req.SendWebRequest();
                return Deserialize<T>(req);
            }
        }

        /// <summary>새로 발급받은 토큰 쌍을 메모리와 디스크에 저장.</summary>
        private static void StoreTokens(GameUserResponseDTO response)
        {
            sAccessToken = response.accessToken;
            sRefreshToken = response.refreshToken;

            string json = JsonUtility.ToJson(
                new GameUserTokenRefreshRequestDTO { accessToken = sAccessToken, refreshToken = sRefreshToken },
                true
            );
            File.WriteAllText(sPersistentDataPath, json);
        }

        private static T Deserialize<T>(UnityWebRequest req) where T : ResponseDTO, new()
        {
            if (req.result == UnityWebRequest.Result.ConnectionError)
            {
                return new T { responseMessage = CommonMessages.CONNECTION_ERROR };
            }

            T response = null;

            try
            {
                response = JsonUtility.FromJson<T>(req.downloadHandler.text);
            }
            catch (ArgumentException)
            {
                // 프록시 오류 페이지 등 JSON이 아닌 응답
            }

            return response ?? new T { responseCode = req.responseCode.ToString(), responseMessage = CommonMessages.INVALID_RESPONSE };
        }
    }
}