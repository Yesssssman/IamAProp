using DG.Tweening;
using UnityEngine;

namespace Skyfish811.UI
{
    [DisallowMultipleComponent]
    public sealed class LoadingScreenView : MonoBehaviour
    {
        public const float TransformProgress = 0.9f;

        [SerializeField] private CanvasGroup overlay;
        [SerializeField] private GameObject propStage;
        [SerializeField] private Transform propPivot;
        [SerializeField] private Animator runningProp;
        [SerializeField] private GameObject stationaryProp;
        [SerializeField] private Animator runningChest;
        [SerializeField] private GameObject stationaryChest;
        [SerializeField] private GameObject hourglass;
        [SerializeField] private CanvasGroup hourglassEntrance;
        [SerializeField] private CanvasGroup loadingLabel;
        [SerializeField, Min(0.01f)] private float entranceDropHeight = 2.2f;
        [SerializeField, Min(0.01f)] private float objectDropHeight = 0.18f;
        [SerializeField, Min(0.01f)] private float gravity = 9.81f;
        [SerializeField, Min(0.02f)] private float transformDuration = 0.3f;
        [SerializeField, Min(0.02f)] private float fadeDuration = 0.2f;

        private enum Phase { Hidden, Entering, Loading, Transforming, Transformed, Fading }

        private Phase phase;
        private Tween transition;
        private Vector3 restingScale;
        private Vector3 propGround;
        private Vector3 labelScale;
        private bool transformRequested;
        private bool destinationReady;
        private Animator activeRunner;
        private GameObject activeObject;

        public bool IsVisible => phase != Phase.Hidden;
        public bool HasTransformed => phase == Phase.Transformed || phase == Phase.Fading;

        private void Awake()
        {
            restingScale = propPivot.localScale;
            propGround = propPivot.localPosition;
            if (loadingLabel != null)
                labelScale = loadingLabel.transform.localScale;
            if (hourglass != null)
            {
                foreach (Animator animator in hourglass.GetComponentsInChildren<Animator>(true))
                    animator.updateMode = AnimatorUpdateMode.UnscaledTime;
            }

            HideImmediately();
        }

        public void BeginLoading() => BeginLoading(false);

        public void BeginLoading(bool useChest)
        {
            HideImmediately();
            phase = Phase.Entering;
            bool showChest = useChest && runningChest != null && stationaryChest != null;
            activeRunner = showChest ? runningChest : runningProp;
            activeObject = showChest ? stationaryChest : stationaryProp;
            runningProp.gameObject.SetActive(false);
            stationaryProp.SetActive(!showChest);
            if (runningChest != null)
                runningChest.gameObject.SetActive(false);
            if (stationaryChest != null)
                stationaryChest.SetActive(showChest);

            propPivot.localScale = restingScale;
            propPivot.localPosition = propGround + Vector3.up * entranceDropHeight;
            propStage.SetActive(true);
            var entrance = DOTween.Sequence().SetUpdate(true);
            entrance.Insert(0.04f, FallToGround(entranceDropHeight));
            if (hourglassEntrance != null)
            {
                hourglassEntrance.alpha = 0f;
                entrance.Insert(0f, hourglassEntrance.DOFade(1f, 0.23f));
            }

            if (loadingLabel != null)
            {
                loadingLabel.alpha = 0f;
                loadingLabel.transform.localScale = labelScale * 0.86f;
                entrance.Insert(0.08f, loadingLabel.DOFade(1f, 0.22f));
                entrance.Insert(0.08f, loadingLabel.transform.DOScale(labelScale, 0.22f).SetEase(Ease.OutBack));
            }

            SetHourglassVisible(true);
            overlay.alpha = 1f;
            overlay.blocksRaycasts = true;
            transition = entrance.OnComplete(FinishEntrance);
        }

        public void ReportProgress(float progress)
        {
            if (!(progress >= TransformProgress))
                return;

            if (phase == Phase.Entering)
                transformRequested = true;
            else if (phase == Phase.Loading)
                BeginTransformation();
        }

        private void FinishEntrance()
        {
            transition = null;
            if (transformRequested)
            {
                SetHourglassVisible(false);
                FinishTransformation();
                return;
            }

            phase = Phase.Loading;
            activeObject.SetActive(false);
            activeRunner.gameObject.SetActive(true);
            activeRunner.speed = 1f;
            activeRunner.Play(0, 0, 0f);
        }

        private void BeginTransformation()
        {
            phase = Phase.Transforming;
            activeRunner.speed = 0f;
            SetHourglassVisible(false);

            transition = DOTween.Sequence()
                .SetUpdate(true)
                .Append(propPivot.DOScale(restingScale * 0.78f, transformDuration * 0.45f).SetEase(Ease.InQuad))
                .Join(propPivot.DOLocalMoveY(propGround.y + objectDropHeight, transformDuration * 0.45f).SetEase(Ease.OutQuad))
                .AppendCallback(ShowStationaryProp)
                .Append(FallToGround(objectDropHeight))
                .Join(propPivot.DOScale(restingScale, transformDuration * 0.55f).SetEase(Ease.OutQuad))
                .OnComplete(FinishTransformation);
        }

        private Tween FallToGround(float height)
        {
            // Ease.InQuad reproduces a drop from rest under constant acceleration.
            float duration = Mathf.Sqrt(2f * height / gravity);
            return propPivot.DOLocalMoveY(propGround.y, duration).SetEase(Ease.InQuad);
        }

        public void NotifyDestinationReady()
        {
            if (phase == Phase.Hidden)
                return;

            destinationReady = true;
            ReportProgress(1f);
            TryFade();
        }

        public void HideImmediately()
        {
            transition?.Kill();
            transition = null;
            phase = Phase.Hidden;
            transformRequested = false;
            destinationReady = false;
            overlay.alpha = 0f;
            overlay.blocksRaycasts = false;
            SetHourglassVisible(false);
            propStage.SetActive(false);
        }

        private void ShowStationaryProp()
        {
            activeRunner.gameObject.SetActive(false);
            activeObject.SetActive(true);
        }

        private void FinishTransformation()
        {
            transition = null;
            phase = Phase.Transformed;
            TryFade();
        }

        private void TryFade()
        {
            if (!destinationReady || phase != Phase.Transformed)
                return;

            phase = Phase.Fading;
            transition = overlay.DOFade(0f, fadeDuration).SetUpdate(true).OnComplete(() =>
            {
                transition = null;
                HideImmediately();
            });
        }

        private void SetHourglassVisible(bool visible)
        {
            if (hourglass != null)
                hourglass.SetActive(visible);
        }

        private void OnDisable() => HideImmediately();
    }
}
