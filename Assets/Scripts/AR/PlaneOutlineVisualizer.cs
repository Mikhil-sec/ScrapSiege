using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ScrapSiege.AR
{
    /// <summary>
    /// Draws a thin colored outline around a detected AR plane's actual boundary, so the
    /// player can see how much of the table the app has actually mapped. Attach this to the
    /// ARPlaneManager's Plane Prefab (needs an ARPlane + LineRenderer on the same object).
    ///
    /// The outline doubles as the Scan-phase feedback: it pulses in "still searching" blue
    /// while detection is running, and goes solid amber once PlaneLockController commits to
    /// this plane as the board.
    /// </summary>
    [RequireComponent(typeof(ARPlane))]
    [RequireComponent(typeof(LineRenderer))]
    public class PlaneOutlineVisualizer : MonoBehaviour
    {
        [SerializeField] private Color scanningColor = new Color(0.29f, 0.66f, 1f, 1f);
        [SerializeField] private Color lockedColor = new Color(1f, 0.54f, 0.24f, 1f);
        [SerializeField] private float lineWidth = 0.004f;
        [SerializeField] private float lockedLineWidth = 0.007f;

        [Tooltip("Depth of the scanning pulse, 0 = no pulse.")]
        [SerializeField, Range(0f, 1f)] private float pulseDepth = 0.45f;
        [SerializeField] private float pulseSpeed = 2.2f;

        [Tooltip("Assign a real material asset. Shader.Find at runtime can return null in a " +
                 "stripped release build, which renders the outline magenta or not at all.")]
        [SerializeField] private Material lineMaterial;

        private ARPlane plane;
        private LineRenderer line;
        private bool isLocked;

        private void Awake()
        {
            plane = GetComponent<ARPlane>();
            line = GetComponent<LineRenderer>();

            line.loop = true;
            line.useWorldSpace = false;
            line.numCapVertices = 4;
            line.textureMode = LineTextureMode.Tile;

            if (lineMaterial != null)
                line.material = lineMaterial;
            else if (line.sharedMaterial == null)
                line.material = new Material(Shader.Find("Sprites/Default"));

            ApplyStyle(1f);
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

        private void Update()
        {
            if (isLocked || pulseDepth <= 0f) return;

            // 0..1 triangle-ish wave; keeps the un-committed outline visibly "live" so the
            // player can tell scanning is still running without reading any text.
            float pulse = 1f - pulseDepth * (0.5f - 0.5f * Mathf.Cos(Time.time * pulseSpeed));
            ApplyStyle(pulse);
        }

        /// <summary>Called by PlaneLockController when this plane becomes (or stops being) the board.</summary>
        public void SetLocked(bool locked)
        {
            isLocked = locked;
            ApplyStyle(1f);
        }

        private void ApplyStyle(float alphaScale)
        {
            Color color = isLocked ? lockedColor : scanningColor;
            color.a *= alphaScale;

            line.startColor = color;
            line.endColor = color;
            // The plane's transform carries the XR Origin's scale, so these real-world thicknesses
            // must convert too or the outline renders hairline-thin on device.
            line.widthMultiplier = ScrapSiege.Core.WorldScale.Metres(isLocked ? lockedLineWidth : lineWidth);
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
