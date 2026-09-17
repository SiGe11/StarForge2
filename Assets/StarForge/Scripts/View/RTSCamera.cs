// RTSCamera.cs — the RTS camera: focus point, yaw and distance, with the pitch
// steepening as you zoom out (close-in detail and a readable overview from the
// same control). WASD and the arrow keys pan, Q and E rotate; the commands
// keep to the other letters.
using UnityEngine;
using UnityEngine.InputSystem;
using StarForge.World;

namespace StarForge.View
{
    public sealed class RTSCamera : MonoBehaviour
    {
        public Camera cam;
        public MapInfo map;

        [Header("Framing")]
        public float minDistance = 26f;
        public float maxDistance = 150f;
        public float distance = 72f;
        [Tooltip("Degrees.")] public float yaw = 45f;

        [Header("Speeds")]
        public float panSpeed = 1.1f;          // screen-heights per second at the current zoom
        public float edgeBand = 24f;           // points
        public float rotateSpeed = 90f;
        public float zoomStep = 0.12f;

        /// <summary>Set by the HUD: the pointer is over a panel that eats edge scrolling.</summary>
        public System.Func<Vector2, bool> BlocksEdgeScroll;

        Vector2 focus, focusTarget;
        float distTarget, yawTarget, groundY;
        bool dragging;
        Vector2 dragAnchor;

        public Vector2 Focus => focus;

        void Awake()
        {
            if (cam == null) cam = GetComponentInChildren<Camera>();
            if (map == null) map = FindAnyObjectByType<MapInfo>();
        }

        void Start()
        {
            distTarget = distance;
            yawTarget = yaw;
            if (map != null) JumpTo(map.StartPos(0));
        }

        public void JumpTo(Vector2 p)
        {
            focus = focusTarget = p;
            if (map != null) groundY = map.HeightAt(p);
            Apply();
        }

        public void CenterOn(Vector2 p) => focusTarget = p;

        public void ZoomTo(float dist) => distTarget = Mathf.Clamp(dist, minDistance, maxDistance);

        public static float PitchFor(float dist) =>
            Mathf.Lerp(0.55f, 0.95f, Mathf.Clamp01((dist - 28f) / 110f)) * Mathf.Rad2Deg;

        void Update()
        {
            float dt = Time.unscaledDeltaTime;
            var kb = Keyboard.current;
            var mouse = Mouse.current;

            Vector2 pan = Vector2.zero;
            if (kb != null)
            {
                // WASD pans too, unless a modifier is held (Ctrl/Cmd+Shift+M and the
                // group keys use them).
                bool modifier = kb.ctrlKey.isPressed || kb.leftCommandKey.isPressed || kb.rightCommandKey.isPressed || kb.altKey.isPressed;
                if (kb.leftArrowKey.isPressed || (!modifier && kb.aKey.isPressed)) pan.x -= 1f;
                if (kb.rightArrowKey.isPressed || (!modifier && kb.dKey.isPressed)) pan.x += 1f;
                if (kb.upArrowKey.isPressed || (!modifier && kb.wKey.isPressed)) pan.y += 1f;
                if (kb.downArrowKey.isPressed || (!modifier && kb.sKey.isPressed)) pan.y -= 1f;
                if (kb.qKey.isPressed && !kb.ctrlKey.isPressed) yawTarget -= rotateSpeed * dt;
                if (kb.eKey.isPressed && !kb.ctrlKey.isPressed) yawTarget += rotateSpeed * dt;
            }

            if (mouse != null)
            {
                Vector2 raw = mouse.position.ReadValue();
                Vector2 mp = raw;
                bool inside = Application.isFocused && mp.x >= 0 && mp.y >= 0 && mp.x <= Screen.width && mp.y <= Screen.height;
                // Full screen, the pointer cannot leave the game, but it can sit past
                // the drawn area -- in the strip macOS keeps clear under the camera
                // notch at the top -- and read beyond the screen. That is the edge too.
                if (Application.isFocused && !Application.isEditor && Screen.fullScreenMode != FullScreenMode.Windowed)
                {
                    mp = new Vector2(Mathf.Clamp(mp.x, 0f, Screen.width - 1f), Mathf.Clamp(mp.y, 0f, Screen.height - 1f));
                    inside = true;
                }
                // Edge scrolling ramps from 35% at the inner boundary to full at the
                // edge, so a nudge creeps and a shove sprints. The pointer defaults
                // to (0,0) before it ever moves, which must not read as an edge.
                if (inside && raw != Vector2.zero)
                {
                    float scale = Mathf.Max(1f, Screen.dpi / 110f);
                    float band = edgeBand * scale;
                    // HUD panels eat edge scrolling, except in the last few points at
                    // the very edge: the top bar spans the whole top of the screen, so
                    // otherwise the camera could never be pushed north with the mouse.
                    float hard = 4f * scale;
                    bool atEdge = mp.x <= hard || mp.y <= hard || mp.x >= Screen.width - 1f - hard || mp.y >= Screen.height - 1f - hard;
                    if (atEdge || BlocksEdgeScroll == null || !BlocksEdgeScroll(mp))
                    {
                        pan.x -= EdgeRamp(band - mp.x, band);
                        pan.x += EdgeRamp(mp.x - (Screen.width - band), band);
                        pan.y -= EdgeRamp(band - mp.y, band);
                        pan.y += EdgeRamp(mp.y - (Screen.height - band), band);
                    }
                }

                float scroll = mouse.scroll.ReadValue().y;
                if (Mathf.Abs(scroll) > 0.01f)
                    distTarget *= Mathf.Pow(1f - zoomStep, Mathf.Clamp(scroll / 60f, -3f, 3f));

                // Middle-mouse drag grabs the ground.
                if (mouse.middleButton.wasPressedThisFrame) { dragging = true; dragAnchor = mp; }
                if (mouse.middleButton.wasReleasedThisFrame) dragging = false;
                if (dragging)
                {
                    Vector2 delta = mp - dragAnchor;
                    dragAnchor = mp;
                    float worldPerPixel = distance * 1.1f / Screen.height;
                    Vector2 d = Rotate(-delta * worldPerPixel, yaw);
                    focusTarget += d;
                }
            }

            distTarget = Mathf.Clamp(distTarget, minDistance, maxDistance);
            if (pan != Vector2.zero)
            {
                float speed = distance * panSpeed;
                focusTarget += Rotate(pan.normalized * Mathf.Min(1f, pan.magnitude), yaw) * speed * dt;
            }
            if (map != null)
            {
                focusTarget.x = Mathf.Clamp(focusTarget.x, 6f, map.mapSize - 6f);
                focusTarget.y = Mathf.Clamp(focusTarget.y, 6f, map.mapSize - 6f);
            }

            float k = 1f - Mathf.Exp(-dt * 10f);
            focus = Vector2.Lerp(focus, focusTarget, k);
            distance = Mathf.Lerp(distance, distTarget, k);
            yaw = Mathf.LerpAngle(yaw, yawTarget, k);
            if (map != null) groundY = Mathf.Lerp(groundY, map.HeightAt(focus), 1f - Mathf.Exp(-dt * 3f));
            Apply();
        }

        static float EdgeRamp(float into, float band)
        {
            if (into <= 0f) return 0f;
            return Mathf.Lerp(0.35f, 1f, Mathf.Clamp01(into / band));
        }

        static Vector2 Rotate(Vector2 v, float yawDeg)
        {
            float r = yawDeg * Mathf.Deg2Rad;
            float c = Mathf.Cos(r), s = Mathf.Sin(r);
            // Screen right maps to the camera's right, screen up to its forward.
            return new Vector2(v.x * c + v.y * s, -v.x * s + v.y * c);
        }

        float shake;

        /// <summary>Camera shake, 0..1; the strongest request wins and it decays quickly.</summary>
        public void Shake(float amount) => shake = Mathf.Max(shake, Mathf.Clamp01(amount));

        void Apply()
        {
            var rot = Quaternion.Euler(PitchFor(distance), yaw, 0f);
            Vector3 target = new Vector3(focus.x, groundY, focus.y);
            Vector3 offset = Vector3.zero;
            if (shake > 0.001f)
            {
                float t = Time.unscaledTime * 40f;
                offset = new Vector3(Mathf.PerlinNoise(t, 1.3f) - 0.5f, Mathf.PerlinNoise(2.7f, t) - 0.5f, Mathf.PerlinNoise(t, t * 0.7f) - 0.5f)
                         * (shake * shake * 1.6f);
                shake = Mathf.Max(0f, shake - Time.unscaledDeltaTime * 2.5f);
            }
            transform.SetPositionAndRotation(target - rot * Vector3.forward * distance + offset, rot);
        }

        /// <summary>World-space ground footprint of the view, for the minimap frustum.</summary>
        public bool GroundCorners(Vector3[] into)
        {
            if (cam == null || map == null) return false;
            var vp = new[] { new Vector3(0, 0), new Vector3(1, 0), new Vector3(1, 1), new Vector3(0, 1) };
            var plane = new Plane(Vector3.up, new Vector3(0f, groundY, 0f));
            for (int i = 0; i < 4; i++)
            {
                var ray = cam.ViewportPointToRay(vp[i]);
                if (!plane.Raycast(ray, out float t)) t = 400f;
                into[i] = ray.GetPoint(Mathf.Min(t, 400f));
            }
            return true;
        }
    }
}
