using UnityEngine;

/// <summary>플레이어의 기본 체력, 이동 및 방어 스탯을 정의한다.</summary>
[CreateAssetMenu(fileName = "PlayerStats", menuName = "IamAProp/Combat/Player Stats")]
public class PlayerStatsSO : ScriptableObject
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>플레이어의 기본 최대 체력.</summary>
    [Header("체력 설정")]
    [Min(1f)]
    [SerializeField] private float maxHealth = 100f;

    /// <summary>프롭 기본 이동 속도에 곱하는 이동 속도 배율.</summary>
    [Header("이동 및 방어 설정")]
    [Min(0.1f)]
    [SerializeField] private float moveSpeedMultiplier = 1f;

    /// <summary>피격 시 경감되는 데미지 비율 (0~1).</summary>
    [Range(0f, 1f)]
    [SerializeField] private float damageReduction;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>기본 최대 체력.</summary>
    public float MaxHealth => maxHealth;

    /// <summary>이동 속도 배율.</summary>
    public float MoveSpeedMultiplier => moveSpeedMultiplier;

    /// <summary>피격 데미지 경감 비율 (0~1).</summary>
    public float DamageReduction => damageReduction;
}
