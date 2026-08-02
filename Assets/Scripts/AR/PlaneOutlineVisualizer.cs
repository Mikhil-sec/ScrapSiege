using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ScrapSiege.AR
{
    /// <summary>
    /// Draws a thin colored outline around a detected AR plane's actual boundary, so the
    /// player can see how much of the table the app has actually mapped. Attach this to the
    /// ARPlaneManager's Plane Prefab (needs an ARPlane + LineRenderer on the same object).
    /// </summary>
    [RequireComponent(typeof(ARPlane))]
    [RequireComponent(typeof(LineRenderer))]
    public class PlaneOutlineVisualizer : MonoBehaviour
    {
        [SerializeField] private Color outlineColor = new Color(0.2f, 0.6f, 1f, 1f);
        [SerializeField] private float lineWidth = 0.005f;

        private ARPlane plane;
        private LineRenderer line;

        private void Awake()
        {
            plane = GetComponent<ARPlane>();
            line = GetComponent<LineRenderer>();

            line.loop = true;
            line.useWorldSpace = false;
            line.widthMultiplier = lineWidth;
            line.startColor = outlineColor;
            line.endColor = outlineColor;
            line.numCapVertices = 4;

            if (line.sharedMaterial == null)
                line.material = new Material(Shader.Find("Sprites/Default"));
        }

        private void OnEnable()
        {
            plane.boundaryChanged += OnBoundaryChanged;
            UpdateOutline();
        }

        private void OnDisable()
        {
            plane.boundaryChanged -= OnBoundaryChanged;
        }

        private void OnBoundaryChanged(ARPlaneBoundaryChangedEventArgs args) => UpdateOutline();

        private void UpdateOutline()
        {
            var boundary = plane.boundary;
            line.positionCount = boundary.Length;
            for (int i = 0; i < boundary.Length; i++)
            {
                var point = boundary[i];
                line.SetPosition(i, new Vector3(point.x, 0.001f, point.y));
            }
        }
    }
}
