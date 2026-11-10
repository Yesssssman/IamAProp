using System;

namespace Networking.Dto
{
    /// <summary>회원가입·로그인·토큰 재발급·게임 시작 응답 DTO.</summary>
    [Serializable]
    public class GameUserResponseDTO : ResponseDTO
    {
        /// <summary>엑세스 토큰.</summary>
        public string accessToken;

        /// <summary>리프레쉬 토큰.</summary>
        public string refreshToken;

        /// <summary>토큰 타입 (Bearer).</summary>
        public string tokenType;

        /// <summary>엑세스 토큰 유효기간, 초 단위 (1시간).</summary>
        public long expiresIn;

        /// <summary>리프레쉬 토큰 유효기간, 초 단위 (30일).</summary>
        public long refreshExpiresIn;

        /// <summary>신규 계정 여부.</summary>
        public bool isNewAccount;

        /// <summary>유저 고유 ID.</summary>
        public string userId;

        /// <summary>게임 계정 해시코드.</summary>
        public string gameAccountHashcode;
    }
}
