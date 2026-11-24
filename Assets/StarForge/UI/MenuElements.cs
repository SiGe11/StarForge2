// MenuElements.cs — the menus' drawn pieces, all vector (no textures): gradient shades,
// chamfered frames, the StarForge emblem, the operation map, segmented sliders and the
// win-rate ring. Animated ones repaint only while the controller says they are shown.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StarForge.UI
{
    /// <summary>A gradient (USS has none): left to right, top to bottom, or a vignette,
    /// drawn as a vertex-coloured mesh. <c>power</c> eases the ramp.</summary>
    [UxmlElement]
    public partial class SFShade : VisualElement
    {
        public enum Mode { Horizontal, Vertical, Vignette }

        [UxmlAttribute] public Color From { get; set; } = new Color(0f, 0f, 0f, 0.85f);
        [UxmlAttribute] public Color To { get; set; } = new Color(0f, 0f, 0f, 0f);
        [UxmlAttribute] public Mode Direction { get; set; } = Mode.Horizontal;
        [UxmlAttribute] public float Power { get; set; } = 1f;

        const int Strips = 12, GridX = 14, GridY = 10;
        Vertex[] verts;
        ushort[] tris;

        public SFShade()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        Color32 At(float t)
        {
            t = Mathf.Pow(Mathf.Clamp01(t), Mathf.Max(0.05f, Power));
            return Color.Lerp(From, To, t);
        }

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1f || h < 1f) return;
            if (Direction == Mode.Vignette) { DrawVignette(mgc, w, h); return; }
            int nv = (Strips + 1) * 2, ni = Strips * 6;
            if (verts == null || verts.Length != nv) verts = new Vertex[nv];
            if (tris == null || tris.Length != ni) tris = new ushort[ni];
            for (int i = 0; i <= Strips; i++)
            {
                float t = i / (float)Strips;
                var c = At(t);
                Vector3 a, b;
                if (Direction == Mode.Horizontal) { a = new Vector3(w * t, 0f, Vertex.nearZ); b = new Vector3(w * t, h, Vertex.nearZ); }
                else { a = new Vector3(0f, h * t, Vertex.nearZ); b = new Vector3(w, h * t, Vertex.nearZ); }
                verts[i * 2] = new Vertex { position = a, tint = c };
                verts[i * 2 + 1] = new Vertex { position = b, tint = c };
            }
            for (int i = 0; i < Strips; i++)
            {
                ushort v = (ushort)(i * 2);
                int k = i * 6;
                // Clockwise on screen: top-left, top-right, bottom-right / bottom-right, bottom-left, top-left.
                if (Direction == Mode.Horizontal)
                {
                    tris[k] = v; tris[k + 1] = (ushort)(v + 2); tris[k + 2] = (ushort)(v + 3);
                    tris[k + 3] = (ushort)(v + 3); tris[k + 4] = (ushort)(v + 1); tris[k + 5] = v;
                }
                else
                {
                    tris[k] = v; tris[k + 1] = (ushort)(v + 1); tris[k + 2] = (ushort)(v + 3);
                    tris[k + 3] = (ushort)(v + 3); tris[k + 4] = (ushort)(v + 2); tris[k + 5] = v;
                }
            }
            var md = mgc.Allocate(nv, ni);
            md.SetAllVertices(verts);
            md.SetAllIndices(tris);
        }

        void DrawVignette(MeshGenerationContext mgc, float w, float h)
        {
            int nv = (GridX + 1) * (GridY + 1), ni = GridX * GridY * 6;
            if (verts == null || verts.Length != nv) verts = new Vertex[nv];
            if (tris == null || tris.Length != ni) tris = new ushort[ni];
            for (int y = 0; y <= GridY; y++)
                for (int x = 0; x <= GridX; x++)
                {
                    float u = x / (float)GridX, v = y / (float)GridY;
                    float d = new Vector2((u - 0.5f) * 2f, (v - 0.5f) * 2f).magnitude / 1.4142f;
                    // From is the middle's colour, To the corners'.
                    verts[y * (GridX + 1) + x] = new Vertex { position = new Vector3(w * u, h * v, Vertex.nearZ), tint = At(Mathf.InverseLerp(0.35f, 1f, d)) };
                }
            int k = 0;
            for (int y = 0; y < GridY; y++)
                for (int x = 0; x < GridX; x++)
                {
                    ushort tl = (ushort)(y * (GridX + 1) + x), tr = (ushort)(tl + 1), bl = (ushort)(tl + GridX + 1), br = (ushort)(bl + 1);
                    tris[k++] = tl; tris[k++] = tr; tris[k++] = br;
                    tris[k++] = br; tris[k++] = bl; tris[k++] = tl;
                }
            var md = mgc.Allocate(nv, ni);
            md.SetAllVertices(verts);
            md.SetAllIndices(tris);
        }
    }

    /// <summary>A panel with cut corners (top-left and bottom-right), a hairline edge and a
    /// bright tab along the top: the menus' card. Defaults are the HUD's iron, steel and star blue
    /// (HUDController.UX's dispatch colours).</summary>
    [UxmlElement]
    public partial class SFFrame : VisualElement
    {
        [UxmlAttribute] public float Cut { get; set; } = 18f;
        [UxmlAttribute] public Color Fill { get; set; } = new Color(0.062f, 0.060f, 0.064f, 0.86f);
        [UxmlAttribute] public Color Edge { get; set; } = new Color(0.47f, 0.65f, 0.82f, 0.30f);
        [UxmlAttribute] public Color Accent { get; set; } = new Color(0.66f, 0.83f, 1f, 0.95f);
        [UxmlAttribute] public bool Tab { get; set; } = true;

        public SFFrame() => generateVisualContent += Draw;

        void Draw(MeshGenerationContext mgc)
        {
            var r = new Rect(0f, 0f, resolvedStyle.width, resolvedStyle.height);
            if (r.width < 4f || r.height < 4f) return;
            float c = Mathf.Min(Cut, r.width * 0.25f, r.height * 0.25f);
            var p = mgc.painter2D;
            p.BeginPath();
            p.MoveTo(new Vector2(c, 0.5f));
            p.LineTo(new Vector2(r.width - 0.5f, 0.5f));
            p.LineTo(new Vector2(r.width - 0.5f, r.height - c));
            p.LineTo(new Vector2(r.width - c, r.height - 0.5f));
            p.LineTo(new Vector2(0.5f, r.height - 0.5f));
            p.LineTo(new Vector2(0.5f, c));
            p.ClosePath();
            p.fillColor = Fill;
            p.Fill();
            p.strokeColor = Edge;
            p.lineWidth = 1f;
            p.Stroke();
            if (!Tab) return;
            p.strokeColor = Accent;
            p.lineWidth = 2f;
            p.BeginPath();
            p.MoveTo(new Vector2(c + 2f, 1f));
            p.LineTo(new Vector2(c + Mathf.Min(120f, r.width * 0.3f), 1f));
            p.Stroke();
            // The cut corners' ticks.
            p.lineWidth = 1.5f;
            p.BeginPath();
            p.MoveTo(new Vector2(1f, c + 10f));
            p.LineTo(new Vector2(1f, c));
            p.LineTo(new Vector2(c, 1f));
            p.Stroke();
            p.BeginPath();
            p.MoveTo(new Vector2(r.width - 1f, r.height - c - 10f));
            p.LineTo(new Vector2(r.width - 1f, r.height - c));
            p.LineTo(new Vector2(r.width - c, r.height - 1f));
            p.Stroke();
        }
    }

    /// <summary>The StarForge mark: a forge-star inside a hexagon, a slowly turning broken ring.
    /// The Brightest Star's blue-white with an amber core.</summary>
    [UxmlElement]
    public partial class SFEmblem : VisualElement
    {
        [UxmlAttribute] public Color Ink { get; set; } = new Color(0.70f, 0.86f, 1f, 1f);
        [UxmlAttribute] public Color Core { get; set; } = new Color(1f, 0.71f, 0.28f, 1f);
        float spin;
        IVisualElementScheduledItem tick;

        /// <summary>Turns the ring; the controller sets it only while the emblem's page is up.</summary>
        public bool Animate
        {
            get => tick != null && tick.isActive;
            set
            {
                if (value == Animate) return;
                if (value)
                {
                    if (tick == null) tick = schedule.Execute(() => { spin = (spin + 1.2f) % 360f; MarkDirtyRepaint(); }).Every(33);
                    else tick.Resume();
                }
                else tick?.Pause();
            }
        }

        public SFEmblem()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += Draw;
        }

        static Vector2 Polar(Vector2 c, float r, float deg) =>
            c + new Vector2(Mathf.Cos(deg * Mathf.Deg2Rad), Mathf.Sin(deg * Mathf.Deg2Rad)) * r;

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            float R = Mathf.Min(w, h) * 0.5f - 2f;
            if (R < 4f) return;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            var p = mgc.painter2D;
            p.lineJoin = LineJoin.Miter;

            // Hexagon.
            p.strokeColor = Ink;
            p.lineWidth = Mathf.Max(1.5f, R * 0.06f);
            p.BeginPath();
            for (int i = 0; i < 6; i++)
            {
                var v = Polar(c, R * 0.78f, -90f + 60f * i);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Stroke();

            // Forge star: four long points, four short, the core in amber.
            p.fillColor = new Color(Ink.r, Ink.g, Ink.b, 0.9f);
            p.BeginPath();
            for (int i = 0; i < 16; i++)
            {
                float rr = i % 2 == 1 ? R * 0.16f : (i / 2) % 2 == 0 ? R * 0.62f : R * 0.34f;
                var v = Polar(c, rr, -90f + 22.5f * i);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();
            p.fillColor = Core;
            p.BeginPath();
            for (int i = 0; i < 4; i++)
            {
                var v = Polar(c, R * 0.13f, -90f + 90f * i);
                if (i == 0) p.MoveTo(v); else p.LineTo(v);
            }
            p.ClosePath();
            p.Fill();

            // The broken ring outside, turning.
            p.strokeColor = new Color(Ink.r, Ink.g, Ink.b, 0.55f);
            p.lineWidth = Mathf.Max(1f, R * 0.035f);
            p.lineCap = LineCap.Butt;
            for (int i = 0; i < 3; i++)
            {
                float a0 = spin + 120f * i, a1 = a0 + 78f;
                p.BeginPath();
                p.Arc(c, R * 0.95f, Angle.Degrees(a0), Angle.Degrees(a1));
                p.Stroke();
            }
        }
    }

    /// <summary>The operation map: the battlefield from above (a texture set by the
    /// controller), its grid, the two bases, the ore fields and a sweeping scan line.</summary>
    [UxmlElement]
    public partial class SFMapPreview : VisualElement
    {
        public Vector2 BaseA = new Vector2(0.2f, 0.8f), BaseB = new Vector2(0.8f, 0.2f);
        public readonly List<Vector2> Ore = new List<Vector2>();
        float scan;
        IVisualElementScheduledItem tick;

        public bool Animate
        {
            get => tick != null && tick.isActive;
            set
            {
                if (value == Animate) return;
                if (value)
                {
                    if (tick == null) tick = schedule.Execute(() => { scan = (scan + 0.006f) % 1f; MarkDirtyRepaint(); }).Every(33);
                    else tick.Resume();
                }
                else tick?.Pause();
            }
        }

        public SFMapPreview() => generateVisualContent += Draw;

        static void Circle(Painter2D p, Vector2 c, float r)
        {
            p.BeginPath();
            p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f));
            p.ClosePath();
        }

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 8f || h < 8f) return;
            var p = mgc.painter2D;
            Vector2 At(Vector2 n) => new Vector2(n.x * w, (1f - n.y) * h);

            p.strokeColor = new Color(0.47f, 0.65f, 0.82f, 0.14f);
            p.lineWidth = 1f;
            for (int i = 1; i < 8; i++)
            {
                float x = w * i / 8f, y = h * i / 8f;
                p.BeginPath(); p.MoveTo(new Vector2(x, 0f)); p.LineTo(new Vector2(x, h)); p.Stroke();
                p.BeginPath(); p.MoveTo(new Vector2(0f, y)); p.LineTo(new Vector2(w, y)); p.Stroke();
            }

            p.fillColor = new Color(0.78f, 0.55f, 1f, 0.9f);
            foreach (var o in Ore) { Circle(p, At(o), 2.6f); p.Fill(); }

            // The scan line and its fading wake. Grid, scan and frame in steel and the star's blue; the
            // bases keep the wings' blue and red (the legend's dots).
            float sy = scan * h;
            for (int i = 0; i < 6; i++)
            {
                float y = sy - i * 3f;
                if (y < 0f) break;
                p.strokeColor = new Color(0.66f, 0.83f, 1f, 0.30f * (1f - i / 6f));
                p.lineWidth = i == 0 ? 1.5f : 1f;
                p.BeginPath(); p.MoveTo(new Vector2(0f, y)); p.LineTo(new Vector2(w, y)); p.Stroke();
            }

            Marker(p, At(BaseA), new Color(0.31f, 0.82f, 1f, 1f));
            Marker(p, At(BaseB), new Color(1f, 0.35f, 0.31f, 1f));

            p.strokeColor = new Color(0.47f, 0.65f, 0.82f, 0.45f);
            p.lineWidth = 1f;
            p.BeginPath();
            p.MoveTo(new Vector2(0.5f, 0.5f)); p.LineTo(new Vector2(w - 0.5f, 0.5f)); p.LineTo(new Vector2(w - 0.5f, h - 0.5f));
            p.LineTo(new Vector2(0.5f, h - 0.5f)); p.ClosePath();
            p.Stroke();
        }

        void Marker(Painter2D p, Vector2 c, Color col)
        {
            float pulse = 0.5f + 0.5f * Mathf.Sin(scan * Mathf.PI * 8f);
            p.strokeColor = new Color(col.r, col.g, col.b, 0.35f + 0.4f * pulse);
            p.lineWidth = 2f;
            Circle(p, c, 13f + 4f * pulse);
            p.Stroke();
            p.fillColor = col;
            Circle(p, c, 6f);
            p.Fill();
            p.strokeColor = new Color(0f, 0f, 0f, 0.7f);
            p.lineWidth = 1.5f;
            Circle(p, c, 6f);
            p.Stroke();
        }
    }

    /// <summary>A segmented value bar the pointer drags; the arrows step it through the controller.</summary>
    [UxmlElement]
    public partial class SFSlider : VisualElement
    {
        [UxmlAttribute] public int Segments { get; set; } = 20;
        float value = 0.5f;
        bool dragging;
        public event Action<float> Changed;

        public float Value
        {
            get => value;
            set
            {
                float v = Mathf.Clamp01(value);
                if (Mathf.Abs(v - this.value) < 1e-4f) return;
                this.value = v;
                MarkDirtyRepaint();
                Changed?.Invoke(v);
            }
        }

        /// <summary>Sets the value without telling anyone (loading a setting).</summary>
        public void SetWithoutNotify(float v) { value = Mathf.Clamp01(v); MarkDirtyRepaint(); }

        public SFSlider()
        {
            generateVisualContent += Draw;
            RegisterCallback<PointerDownEvent>(e => { dragging = true; this.CapturePointer(e.pointerId); FromPointer(e.localPosition.x); e.StopPropagation(); });
            RegisterCallback<PointerMoveEvent>(e => { if (dragging) FromPointer(e.localPosition.x); });
            RegisterCallback<PointerUpEvent>(e => { dragging = false; this.ReleasePointer(e.pointerId); });
            RegisterCallback<PointerCaptureOutEvent>(_ => dragging = false);
        }

        void FromPointer(float x)
        {
            float w = contentRect.width;
            if (w > 1f) Value = Mathf.Round(Mathf.Clamp01(x / w) * Segments) / Segments;
        }

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            int n = Mathf.Max(2, Segments);
            if (w < n * 2f || h < 2f) return;
            var p = mgc.painter2D;
            float gap = 2f, sw = (w - gap * (n - 1)) / n;
            int lit = Mathf.RoundToInt(value * n);
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)(n - 1);
                Color on = Color.Lerp(new Color(0.30f, 0.45f, 0.62f, 1f), new Color(0.66f, 0.83f, 1f, 1f), t); // steel to the star's blue
                Paint.Rect(p, i * (sw + gap), 0f, sw, h, i < lit ? on : new Color(1f, 1f, 1f, 0.08f));
            }
        }
    }

    /// <summary>A ring filled to a share (the win rate).</summary>
    [UxmlElement]
    public partial class SFRing : VisualElement
    {
        float value;
        public float Value { get => value; set { this.value = Mathf.Clamp01(value); MarkDirtyRepaint(); } }

        public SFRing() => generateVisualContent += Draw;

        void Draw(MeshGenerationContext mgc)
        {
            float w = contentRect.width, h = contentRect.height;
            float r = Mathf.Min(w, h) * 0.5f - 6f;
            if (r < 4f) return;
            var c = new Vector2(w * 0.5f, h * 0.5f);
            var p = mgc.painter2D;
            p.lineWidth = 7f;
            p.lineCap = LineCap.Butt;
            p.strokeColor = new Color(1f, 1f, 1f, 0.08f);
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(0f), Angle.Degrees(360f)); p.Stroke();
            if (value <= 0.001f) return;
            p.strokeColor = new Color(0.66f, 0.83f, 1f, 1f); // star blue, as a win's badge
            p.BeginPath(); p.Arc(c, r, Angle.Degrees(-90f), Angle.Degrees(-90f + 360f * value)); p.Stroke();
        }
    }
}
