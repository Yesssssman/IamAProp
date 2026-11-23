using System;
using UnityEngine;
using UnityEngine.UI;

/// <summary>사망 후 관전 모드 전환용 <c>관전하기</c> 버튼 UI의 표시와 클릭 이벤트를 제어한다.</summary>
public class DeathCamUI : MonoBehaviour
{
    // ***************************************************************
    // Fields
    // ***************************************************************

    /// <summary>킬러 관전 모드로 전환하는 <c>관전하기</c> 버튼.</summary>
    [SerializeField] private Button spectateButton;

    /// <summary>표시/숨김을 전환할 UI 콘텐츠 루트 오브젝트.</summary>
    [SerializeField] private GameObject contentRoot;

    // ***************************************************************
    // Events
    // ***************************************************************

    /// <summary><see cref="spectateButton"/> 클릭 시 발생하는 이벤트.</summary>
    public event Action OnSpectateClicked;

    // ***************************************************************
    // MonoBehaviour
    // ***************************************************************

    private void Awake()
    {
        if (contentRoot == null)
        {
            contentRoot = gameObject;
        }

        if (spectateButton == null)
        {
            spectateButton = GetComponentInChildren<Button>(true);
        }

        if (spectateButton != null)
        {
            spectateButton.onClick.AddListener(HandleSpectateButtonClicked);
        }

        Hide();
    }

    private void OnDestroy()
    {
        if (spectateButton != null)
        {
            spectateButton.onClick.RemoveListener(HandleSpectateButtonClicked);
        }
    }

    // ***************************************************************
    // Public Control API
    // ***************************************************************

    /// <summary>관전하기 UI를 화면에 표시한다.</summary>
    public void Show()
    {
        if (contentRoot != null)
        {
            contentRoot.SetActive(true);
        }

        if (spectateButton != null)
        {
            spectateButton.interactable = true;
        }
    }

    /// <summary>관전하기 UI를 화면에서 숨긴다.</summary>
    public void Hide()
    {
        if (contentRoot != null)
        {
            contentRoot.SetActive(false);
        }
    }

    // ***************************************************************
    // Event Handlers
    // ***************************************************************

    /// <summary>관전하기 버튼 클릭을 구독자에게 전달한다.</summary>
    private void HandleSpectateButtonClicked()
    {
        OnSpectateClicked?.Invoke();
    }
}
