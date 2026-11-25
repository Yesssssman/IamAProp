using UnityEngine;
using UnityEngine.UI;

/// <summary>하단 툴바의 개별 무기 슬롯 아이콘, 단축키 번호 및 선택 포커스 연출을 제어한다.</summary>
public class WeaponSlotUI : MonoBehaviour
{
    // ***************************************************************
    // Constants
    // ***************************************************************

    private const float ScaleSnapEpsilon = 0.0005f;
    private const float ColorSnapEpsilon = 0.002f;

    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>슬롯 모서리에 표시할 단축키 번호 (1~5).</summary>
    [Header("슬롯 기본 설정")]
    [Min(1)]
    [SerializeField] private int slotKeyNumber = 1;

    /// <summary>무기 데이터가 없어도 항상 보유 슬롯으로 취급할지 여부 (1번 맨손 슬롯용).</summary>
    [SerializeField] private bool alwaysOccupied;

    /// <summary><see cref="alwaysOccupied"/> 슬롯에 기본으로 표시할 아이콘 (선택).</summary>
    [SerializeField] private Sprite defaultIcon;

    /// <summary>아이콘이 비어 있을 때 기본 표시할 폴백 이름 (예: Unarmed).</summary>
    [SerializeField] private string defaultFallbackName = string.Empty;

    /// <summary>포커스 확대/축소 보간을 적용할 내부 비주얼 루트.</summary>
    [Header("UI 참조")]
    [SerializeField] private RectTransform scaleTarget;

    /// <summary>선택 상태 테두리 하이라이트용 이미지.</summary>
    [SerializeField] private Image borderImage;

    /// <summary>슬롯 내부 배경 이미지.</summary>
    [SerializeField] private Image backgroundImage;

    /// <summary>좌상단 모서리 단축키 번호 텍스트.</summary>
    [SerializeField] private Text keyNumberText;

    /// <summary>보유 무기 아이콘 이미지.</summary>
    [SerializeField] private Image weaponIconImage;

    /// <summary>무기 아이콘이 없을 때 무기 이름을 표시하는 폴백 텍스트.</summary>
    [SerializeField] private Text fallbackNameText;

    /// <summary>선택 해제 상태의 기본 스케일 배율.</summary>
    [Header("포커스 연출 설정")]
    [Min(0.5f)]
    [SerializeField] private float unselectedScale = 1f;

    /// <summary>선택 포커스 상태의 강조 스케일 배율.</summary>
    [Min(0.5f)]
    [SerializeField] private float selectedScale = 1.15f;

    /// <summary>스케일과 색상 전환 보간 속도.</summary>
    [Min(0.1f)]
    [SerializeField] private float focusLerpSpeed = 12f;

    /// <summary>선택 해제 상태의 배경 색상.</summary>
    [SerializeField] private Color unselectedBackgroundColor = new Color(0.10f, 0.12f, 0.16f, 0.78f);

    /// <summary>선택 포커스 상태의 배경 강조 색상.</summary>
    [SerializeField] private Color selectedBackgroundColor = new Color(0.20f, 0.25f, 0.34f, 0.94f);

    /// <summary>선택 해제 상태의 테두리 색상.</summary>
    [SerializeField] private Color unselectedBorderColor = new Color(1f, 1f, 1f, 0.22f);

    /// <summary>선택 포커스 상태의 테두리 강조 색상.</summary>
    [SerializeField] private Color selectedBorderColor = new Color(1f, 0.80f, 0.28f, 1f);

    /// <summary>빈 슬롯 상태의 단축키 번호 텍스트 색상.</summary>
    [SerializeField] private Color emptyKeyColor = new Color(0.75f, 0.78f, 0.82f, 0.45f);

    /// <summary>무기 보유 상태의 단축키 번호 텍스트 색상.</summary>
    [SerializeField] private Color occupiedKeyColor = new Color(1f, 1f, 1f, 0.92f);

    /// <summary>선택 포커스 상태의 단축키 번호 텍스트 색상.</summary>
    [SerializeField] private Color selectedKeyColor = new Color(1f, 0.88f, 0.45f, 1f);

    /// <summary>현재 슬롯에 표시할 무기 보유 여부.</summary>
    private bool _hasWeapon;

    /// <summary>현재 슬롯의 선택 포커스 여부.</summary>
    private bool _isFocused;

    /// <summary>최소 1회 이상 <see cref="SetWeapon"/>으로 초기화되었는지 여부.</summary>
    private bool _isInitialized;

    /// <summary>스케일 또는 색상 보간 갱신이 필요한지 여부.</summary>
    private bool _isVisualDirty;

    /// <summary>현재 프레임에 적용된 슬롯 스케일 값.</summary>
    private float _currentScale;

    /// <summary>현재 프레임에 적용된 배경 색상.</summary>
    private Color _currentBackgroundColor;

    /// <summary>현재 프레임에 적용된 테두리 색상.</summary>
    private Color _currentBorderColor;

    /// <summary>현재 프레임에 적용된 단축키 텍스트 색상.</summary>
    private Color _currentKeyColor;

    // ***************************************************************
    // Properties
    // ***************************************************************

    /// <summary>슬롯에 할당된 단축키 번호.</summary>
    public int SlotKeyNumber => slotKeyNumber;

    /// <summary>현재 슬롯이 무기를 보유(또는 상시 활성) 중인지 여부.</summary>
    public bool HasWeapon => _hasWeapon;

    /// <summary>현재 슬롯이 선택 포커스 상태인지 여부.</summary>
    public bool IsFocused => _isFocused;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (keyNumberText != null)
        {
            keyNumberText.text = slotKeyNumber.ToString();
        }

        _currentScale = unselectedScale;
        _currentBackgroundColor = unselectedBackgroundColor;
        _currentBorderColor = unselectedBorderColor;
        _currentKeyColor = alwaysOccupied ? occupiedKeyColor : emptyKeyColor;

        if (!_isInitialized)
        {
            SetWeapon(null);
        }

        ApplyVisualState();
    }

    private void Update()
    {
        if (!_isVisualDirty) return;

        float targetScale = _isFocused ? selectedScale : unselectedScale;
        Color targetBg = _isFocused ? selectedBackgroundColor : unselectedBackgroundColor;
        Color targetBorder = _isFocused ? selectedBorderColor : unselectedBorderColor;
        Color targetKey = _isFocused ? selectedKeyColor : (_hasWeapon ? occupiedKeyColor : emptyKeyColor);

        float t = 1f - Mathf.Exp(-focusLerpSpeed * Time.unscaledDeltaTime);
        _currentScale = Mathf.Lerp(_currentScale, targetScale, t);
        _currentBackgroundColor = Color.Lerp(_currentBackgroundColor, targetBg, t);
        _currentBorderColor = Color.Lerp(_currentBorderColor, targetBorder, t);
        _currentKeyColor = Color.Lerp(_currentKeyColor, targetKey, t);

        bool scaleDone = Mathf.Abs(_currentScale - targetScale) <= ScaleSnapEpsilon;
        bool bgDone = Mathf.Abs(_currentBackgroundColor.r - targetBg.r) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBackgroundColor.g - targetBg.g) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBackgroundColor.b - targetBg.b) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBackgroundColor.a - targetBg.a) <= ColorSnapEpsilon;
        bool borderDone = Mathf.Abs(_currentBorderColor.r - targetBorder.r) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBorderColor.g - targetBorder.g) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBorderColor.b - targetBorder.b) <= ColorSnapEpsilon
            && Mathf.Abs(_currentBorderColor.a - targetBorder.a) <= ColorSnapEpsilon;
        bool keyDone = Mathf.Abs(_currentKeyColor.r - targetKey.r) <= ColorSnapEpsilon
            && Mathf.Abs(_currentKeyColor.g - targetKey.g) <= ColorSnapEpsilon
            && Mathf.Abs(_currentKeyColor.b - targetKey.b) <= ColorSnapEpsilon
            && Mathf.Abs(_currentKeyColor.a - targetKey.a) <= ColorSnapEpsilon;

        if (scaleDone && bgDone && borderDone && keyDone)
        {
            _currentScale = targetScale;
            _currentBackgroundColor = targetBg;
            _currentBorderColor = targetBorder;
            _currentKeyColor = targetKey;
            _isVisualDirty = false;
        }

        ApplyVisualState();
    }

    private void OnValidate()
    {
        if (keyNumberText != null)
        {
            keyNumberText.text = slotKeyNumber.ToString();
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary><paramref name="weaponData"/>에 맞춰 슬롯 아이콘과 폴백 이름 표시를 갱신한다.</summary>
    public void SetWeapon(WeaponDataSO weaponData)
    {
        _isInitialized = true;
        _hasWeapon = alwaysOccupied || weaponData != null;

        Sprite icon = null;
        string fallbackName = string.Empty;

        if (weaponData != null)
        {
            icon = weaponData.WeaponIcon;
            fallbackName = weaponData.WeaponName;
        }
        else if (alwaysOccupied)
        {
            icon = defaultIcon;
            fallbackName = defaultFallbackName;
        }

        if (!_hasWeapon)
        {
            if (weaponIconImage != null)
            {
                weaponIconImage.sprite = null;
                weaponIconImage.gameObject.SetActive(false);
            }

            if (fallbackNameText != null)
            {
                fallbackNameText.text = string.Empty;
                fallbackNameText.gameObject.SetActive(false);
            }
        }
        else if (icon != null)
        {
            if (weaponIconImage != null)
            {
                weaponIconImage.sprite = icon;
                weaponIconImage.preserveAspect = true;
                weaponIconImage.gameObject.SetActive(true);
            }

            if (fallbackNameText != null)
            {
                fallbackNameText.text = string.Empty;
                fallbackNameText.gameObject.SetActive(false);
            }
        }
        else
        {
            if (weaponIconImage != null)
            {
                weaponIconImage.sprite = null;
                weaponIconImage.gameObject.SetActive(false);
            }

            if (fallbackNameText != null)
            {
                fallbackNameText.text = fallbackName;
                fallbackNameText.gameObject.SetActive(!string.IsNullOrEmpty(fallbackName));
            }
        }

        _isVisualDirty = true;
    }

    /// <summary>슬롯의 선택 포커스 상태를 전환한다.</summary>
    public void SetFocused(bool isFocused, bool immediate = false)
    {
        _isFocused = isFocused;

        if (immediate)
        {
            _currentScale = _isFocused ? selectedScale : unselectedScale;
            _currentBackgroundColor = _isFocused ? selectedBackgroundColor : unselectedBackgroundColor;
            _currentBorderColor = _isFocused ? selectedBorderColor : unselectedBorderColor;
            _currentKeyColor = _isFocused ? selectedKeyColor : (_hasWeapon ? occupiedKeyColor : emptyKeyColor);
            _isVisualDirty = false;
            ApplyVisualState();
            return;
        }

        _isVisualDirty = true;
    }

    // ***************************************************************
    // Helpers
    // ***************************************************************

    /// <summary>현재 보간된 스케일과 색상을 슬롯 UI 요소에 반영한다.</summary>
    private void ApplyVisualState()
    {
        if (scaleTarget != null)
        {
            scaleTarget.localScale = new Vector3(_currentScale, _currentScale, 1f);
        }

        if (backgroundImage != null)
        {
            backgroundImage.color = _currentBackgroundColor;
        }

        if (borderImage != null)
        {
            borderImage.color = _currentBorderColor;
        }

        if (keyNumberText != null)
        {
            keyNumberText.color = _currentKeyColor;
        }
    }
}
