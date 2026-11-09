using System;

namespace Networking.Dto
{
    /// <summary>토큰 재발급 요청 DTO. 디스크에 저장하는 인증 정보 파일의 형식으로도 쓰임.</summary>
    [Serializable]
    public struct GameUserTokenRefreshRequestDTO
    {
        public string accessToken;

        public string refreshToken;
    }
}
