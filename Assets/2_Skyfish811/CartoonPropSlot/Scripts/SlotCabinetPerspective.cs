using UnityEngine;

namespace Skyfish811.PropSlots
{
    [ExecuteAlways]
    public sealed class SlotCabinetPerspective : MonoBehaviour
    {
        [SerializeField] private Camera sceneCamera;
        [SerializeField] private Canvas canvas;
        [SerializeField, Range(0f, 30f)] private float yaw = 15f;
        [SerializeField, Range(0f, 20f)] private float pitch = 9f;
        [SerializeField, Range(0.1f, 2f)] private float thickness = 0.75f;

        private const float PixelScale = 0.01f;
        private const float FieldOfView = 35f;

        // Cabinet outline in the artwork's 620 x 349 reference coordinates.
        private static readonly Vector2[] Outline = {
            new(114, 339), new(103, 330), new(102, 313), new(117, 273),
            new(109, 117), new(113, 93), new(124, 82), new(136, 49),
            new(158, 29), new(183, 20), new(250, 16), new(278, 8),
            new(322, 8), new(347, 15), new(432, 18), new(468, 28),
            new(490, 54), new(497, 83), new(504, 90), new(507, 107),
            new(499, 118), new(502, 268), new(516, 308), new(514, 327),
            new(502, 339)
        };

        private GameObject shell;

        private void OnDisable()
        {
            if (shell) Release(shell);
            shell = null;
        }

        private void Update()
        {
            if (!sceneCamera || !canvas) return;

            var rect = (RectTransform)canvas.transform;
            rect.localScale = Vector3.one * PixelScale;
            canvas.worldCamera = sceneCamera;

            var center = rect.TransformPoint(new Vector3(10f, 0f, 0f));
            var rotation = Quaternion.Euler(pitch, -yaw, 0f);
            sceneCamera.orthographic = false;
            sceneCamera.fieldOfView = FieldOfView;
            float tangent = Mathf.Tan(FieldOfView * 0.5f * Mathf.Deg2Rad);
            float aspect = Mathf.Max(0.1f, sceneCamera.aspect);
            float distance = Mathf.Max(3.65f / tangent, 5.15f / (tangent * aspect)) + 0.6f;
            sceneCamera.transform.SetPositionAndRotation(center - rotation * Vector3.forward * distance, rotation);

            if (!shell) BuildShell();
            shell.transform.localScale = new Vector3(1f, 1f, thickness);
        }

        private void BuildShell()
        {
            shell = new GameObject("Cabinet depth") { hideFlags = HideFlags.HideAndDontSave };
            shell.transform.SetParent(canvas.transform, false);
            var shader = Shader.Find("Sprites/Default");
            if (!shader) return;
            var material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            var mesh = new Mesh { name = "Cabinet sides", hideFlags = HideFlags.HideAndDontSave };
            var vertices = new Vector3[Outline.Length * 4];
            var colors = new Color[vertices.Length];
            var triangles = new int[Outline.Length * 6];
            for (int i = 0; i < Outline.Length; i++)
            {
                Vector2 a = Outline[i];
                Vector2 b = Outline[(i + 1) % Outline.Length];
                Vector2 edge = (b - a).normalized;
                int v = i * 4;
                vertices[v] = Front(a);
                vertices[v + 1] = Front(b);
                vertices[v + 2] = vertices[v] + Vector3.forward * 100f;
                vertices[v + 3] = vertices[v + 1] + Vector3.forward * 100f;
                float light = Mathf.Clamp01(-edge.x * 0.60f + edge.y * 0.23f + 0.43f);
                Color front = Color.Lerp(new Color(0.34f, 0.025f, 0.065f),
                    new Color(0.98f, 0.23f, 0.12f), light);
                colors[v] = colors[v + 1] = front;
                colors[v + 2] = colors[v + 3] = Color.Lerp(front, Color.black, 0.42f);

                int t = i * 6;
                triangles[t] = v;
                triangles[t + 1] = v + 2;
                triangles[t + 2] = v + 1;
                triangles[t + 3] = v + 1;
                triangles[t + 4] = v + 2;
                triangles[t + 5] = v + 3;
            }
            mesh.vertices = vertices;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            shell.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = shell.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.sortingOrder = -1;
        }

        private static Vector3 Front(Vector2 point) => new((point.x - 310f) * 2f, (174.5f - point.y) * 2f, 1f);

        private static void Release(GameObject value)
        {
            var mesh = value.GetComponent<MeshFilter>();
            var renderer = value.GetComponent<MeshRenderer>();
            if (Application.isPlaying)
            {
                if (mesh) Destroy(mesh.sharedMesh);
                if (renderer) Destroy(renderer.sharedMaterial);
                Destroy(value);
            }
            else
            {
                if (mesh) DestroyImmediate(mesh.sharedMesh);
                if (renderer) DestroyImmediate(renderer.sharedMaterial);
                DestroyImmediate(value);
            }
        }
    }
}
