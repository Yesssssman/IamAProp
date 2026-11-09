using System;

namespace Networking.Dto
{
    /// <summary>JW 서버 응답의 공통 필드. JsonUtility로 역직렬화되므로 필드명이 곧 JSON 키.</summary>
    [Serializable]
    public class ResponseDTO
    {
        /// <summary>HTTP 상태 코드 문자열 (예: "200").</summary>
        public string responseCode;

        /// <summary>응답 메세지 (예: "OK"). <see cref="ResponseMessageUtil"/>로 판별.</summary>
        public string responseMessage;
    }
}
