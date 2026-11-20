using System;

namespace Networking.Dto
{
    /// <summary>계정 정보 조회(/api/game/accountinfo) 응답 DTO. IamAProp에서 쓰는 필드만 받음.</summary>
    [Serializable]
    public class AccountInfoResponseDTO : ResponseDTO
    {
        public string gameAccountHashcode;

        public string accountNickName;
    }
}
