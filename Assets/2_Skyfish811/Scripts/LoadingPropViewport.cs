using UnityEngine;
using UnityEngine.UI;

namespace Skyfish811.UI
{
    [RequireComponent(typeof(RawImage))]
    public sealed class LoadingPropViewport : MonoBehaviour
    {
        [SerializeField] private Camera previewCamera;
        [SerializeField, Min(1f)] private float referenceWidth = 420f;
        [SerializeField] private Vector2 margin = new(44f, 38f);

        private const float Aspect = 16f / 9f;
        private const int MinWidth = 256;
        private const int MaxWidth = 1536;

        private RawImage image;
        private Canvas canvas;
        private RenderTexture target;
        private RenderTexture template;

        private void OnEnable()
        {
            image = GetComponent<RawImage>();
            if (previewCamera == null || image.canvas == null)
                return;

            canvas = image.canvas.rootCanvas;
            template = previewCamera.targetTexture;
            var rect = image.rectTransform;
            rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(1f, 0f);
            RefreshViewport();
        }

        private void LateUpdate() => RefreshViewport();

        private void RefreshViewport()
        {
            if (canvas == null || previewCamera == null)
                return;

            Vector2 available = ((RectTransform)canvas.transform).rect.size;
            if (available.x <= 0f || available.y <= 0f)
                return;

            var inset = Vector2.Min(margin, available * 0.08f);
            float width = Mathf.Min(referenceWidth, (available.x - inset.x) * 0.38f);
            width = Mathf.Min(width, (available.y - inset.y) * 0.38f * Aspect);
            Vector2 size = new(width, width / Aspect);
            var rect = image.rectTransform;
            if ((rect.rect.size - size).sqrMagnitude > 0.01f)
            {
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, size.x);
                rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, size.y);
            }
            var position = new Vector2(-inset.x, inset.y);
            if ((rect.anchoredPosition - position).sqrMagnitude > 0.01f)
                rect.anchoredPosition = position;

            int pixels = Mathf.Clamp(Mathf.CeilToInt(width * canvas.scaleFactor / 16f) * 16, MinWidth, MaxWidth);
            if (target != null && target.width == pixels && target.IsCreated())
                return;

            var descriptor = template != null
                ? template.descriptor
                : new RenderTextureDescriptor(pixels, pixels / 16 * 9, RenderTextureFormat.ARGB32, 24);
            descriptor.width = pixels;
            descriptor.height = pixels / 16 * 9;
            var next = new RenderTexture(descriptor)
            {
                name = "Loading Prop Viewport",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            if (!next.Create())
            {
                Destroy(next);
                return;
            }

            previewCamera.targetTexture = next;
            image.texture = next;
            ReleaseTarget();
            target = next;
        }

        private void OnDisable()
        {
            if (target == null)
                return;

            if (previewCamera != null)
                previewCamera.targetTexture = template;
            if (image != null)
                image.texture = template;
            ReleaseTarget();
        }

        private void ReleaseTarget()
        {
            if (target == null)
                return;

            target.Release();
            Destroy(target);
            target = null;
        }
    }
}
