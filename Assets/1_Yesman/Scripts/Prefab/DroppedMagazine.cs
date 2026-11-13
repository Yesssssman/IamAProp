using UnityEngine;

/// <summary>떨어진 탄창을 일정 시간 불투명하게 보여준 뒤, 서서히 투명하게 만들고 제거함.</summary>
public class DroppedMagazine : MonoBehaviour
{
    private static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

    /// <summary>불투명하게 보이는 시간 (초).</summary>
    [SerializeField] private float opaqueDuration = 3f;

    /// <summary>투명해지며 사라지는 시간 (초).</summary>
    [SerializeField] private float fadeDuration = 2f;

    /// <summary>페이드 아웃 동안 사용하는 투명 머티리얼.</summary>
    [SerializeField] private Material fadeMaterial;

    private Renderer magazineRenderer;

    /// <summary>페이드 아웃 중의 알파 값을 담는 블록. 페이드 아웃 전에는 null.</summary>
    private MaterialPropertyBlock propertyBlock;

    /// <summary>떨어진 뒤 지난 시간 (초).</summary>
    private float elapsed;

    private void Awake()
    {
        magazineRenderer = GetComponent<Renderer>();
    }

    private void Update()
    {
        elapsed += Time.deltaTime;

        if (elapsed >= opaqueDuration + fadeDuration)
        {
            Destroy(gameObject);
            return;
        }

        if (elapsed < opaqueDuration)
        {
            return;
        }

        if (propertyBlock == null)
        {
            propertyBlock = new MaterialPropertyBlock();
            magazineRenderer.sharedMaterial = fadeMaterial;
        }

        Color color = fadeMaterial.GetColor(BaseColor);
        color.a *= 1f - (elapsed - opaqueDuration) / fadeDuration;
        propertyBlock.SetColor(BaseColor, color);
        magazineRenderer.SetPropertyBlock(propertyBlock);
    }
}
