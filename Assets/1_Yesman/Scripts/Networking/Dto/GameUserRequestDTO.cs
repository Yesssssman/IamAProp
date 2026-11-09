using System;

namespace Networking.Dto
{
    /// <summary>회원가입·로그인 요청 DTO. JsonUtility로 직렬화되므로 필드명이 곧 JSON 키.</summary>
    [Serializable]
    public struct GameUserRequestDTO
    {
        /// <summary>JW 서버에서 발급한 게임 코드 (필수, <see cref="LoginManager"/>가 채움).</summary>
        public string gameCode;

        /// <summary>멤버 유형: 0(게임을 통한 순수가입자) / 1(SNS 로그인) (필수).</summary>
        public int memberType;

        /// <summary>액션 유형: JOIN / LOGIN (필수, <see cref="LoginManager"/>가 채움).</summary>
        public string actionType;

        /// <summary>OAuth 인증 제공자 ID (memberType == 1일 때 필수).</summary>
        public string snsProviderId;

        /// <summary>OAuth 인증 제공자 이름: google / kakao / apple (memberType == 1일 때 필수).</summary>
        public string snsProvider;

        /// <summary>멤버 ID (필수).</summary>
        public string memberId;

        /// <summary>멤버 비밀번호 (필수, 6~100자).</summary>
        public string memberPw;

        /// <summary>멤버 이름 (실명) (JOIN일 때 필수).</summary>
        public string memberName;

        /// <summary>멤버 닉네임 (선택).</summary>
        public string nickName;

        /// <summary>이메일 주소 (선택).</summary>
        public string email;

        /// <summary>연락처1 (선택, 최대 4자).</summary>
        public string mobilePhone1;

        /// <summary>연락처2 (선택, 최대 4자).</summary>
        public string mobilePhone2;

        /// <summary>연락처3 (선택, 최대 4자).</summary>
        public string mobilePhone3;

        /// <summary>본인인증 CI (개발 단계에선 선택).</summary>
        public string ci;
    }
}
