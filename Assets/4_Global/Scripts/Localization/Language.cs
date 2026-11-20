using System;
using Networking;
using UnityEngine;

namespace Localization
{
    public enum ELanguage
    {
        English,
        Korean,
    }

    /// <summary>현재 언어 설정과 언어 키 → 문장 변환.</summary>
    public static class Language
    {
        private const string PREFS_KEY = "Language";

        /// <summary><see cref="PlayerPrefs"/>에서 읽은 현재 언어. 아직 읽지 않았으면 null.</summary>
        private static ELanguage? sCurrent;

        /// <summary><see cref="Current"/>가 바뀌었을 때 호출됨.</summary>
        public static event Action Changed;

        /// <summary>현재 언어. 저장된 값이 없으면 시스템 언어를 따름.</summary>
        public static ELanguage Current
        {
            get
            {
                if (sCurrent == null)
                {
                    ELanguage systemLanguage = Application.systemLanguage == SystemLanguage.Korean ? ELanguage.Korean : ELanguage.English;
                    sCurrent = (ELanguage)PlayerPrefs.GetInt(PREFS_KEY, (int)systemLanguage);
                }

                return sCurrent.Value;
            }
            set
            {
                if (Current == value)
                {
                    return;
                }

                sCurrent = value;
                PlayerPrefs.SetInt(PREFS_KEY, (int)value);
                PlayerPrefs.Save();
                Changed?.Invoke();
            }
        }

        /// <summary><paramref name="key"/>의 현재 언어 문장. 없는 키면 키를 그대로 반환.</summary>
        public static string Get(string key)
        {
            return LanguageTable.TryGet(key, Current, out string text) ? text : key;
        }

        public static string Format(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }

        /// <summary>서버·클라이언트 응답 메세지를 사용자에게 보여줄 문장으로 바꿈.</summary>
        public static string GetResponseMessage(string responseMessage)
        {
            return responseMessage switch
            {
                LobbyMessages.INVALID_PASSWORD => Get(LanguageTable.ERROR_INVALID_PASSWORD),
                LobbyMessages.LOBBY_FULL => Get(LanguageTable.ERROR_ROOM_FULL),
                LobbyMessages.LOBBY_NOT_FOUND => Get(LanguageTable.ERROR_ROOM_NOT_FOUND),
                LobbyMessages.LOBBY_IN_GAME => Get(LanguageTable.ERROR_ROOM_IN_GAME),
                LobbyMessages.ALREADY_IN_LOBBY => Get(LanguageTable.ERROR_ALREADY_IN_ROOM),
                LobbyMessages.NOT_ALL_READY => Get(LanguageTable.ERROR_NOT_ALL_READY),
                CommonMessages.CONNECTION_ERROR => Get(LanguageTable.ERROR_CONNECTION),
                AccountMessages.INVALID_CREDENTIALS => Get(LanguageTable.ERROR_INVALID_CREDENTIALS),
                AccountMessages.ACCOUNT_ALREADY_EXISTS => Get(LanguageTable.ERROR_ACCOUNT_ALREADY_EXISTS),
                AccountMessages.NICKNAME_ALREADY_EXISTS => Get(LanguageTable.ERROR_NICKNAME_ALREADY_EXISTS),
                _ => Format(LanguageTable.ERROR_UNKNOWN, responseMessage),
            };
        }
    }
}
