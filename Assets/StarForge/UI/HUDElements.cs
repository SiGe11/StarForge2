// HUDElements.cs — custom UI Toolkit elements drawn with Painter2D: bars,
// labelled bar charts for the AI inspector, and the minimap.
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace StarForge.UI
{
    static class Paint
    {
        public static void Rect(Painter2D p, float x, float y, float w, float h, Color c)
        {
            if (w <= 0f || h <= 0f) return;
            p.fillColor = c;
            p.BeginPath();
            p.MoveTo(new Vector2(x, y));
            p.LineTo(new Vector2(x + w, y));
            p.LineTo(new Vector2(x + w, y + h));
            p.LineTo(new Vector2(x, y + h));
            p.ClosePath();
            p.Fill();
        }
    }

    /// <summary>A single horizontal fill bar (health, progress).</summary>
    [UxmlElement]
    public partial class BarElement : VisualElement
    {
        float m_Value = 1f;
        Color m_Fill = new Color(0.35f, 0.9f, 0.55f);

        public float Value
        {
            get => m_Value;
            set { float v = Mathf.Clamp01(value); if (Mathf.Abs(v - m_Value) > 1e-4f) { m_Value = v; MarkDirtyRepaint(); } }
        }

        public Color FillColor
        {
            get => m_Fill;
            set { if (value != m_Fill) { m_Fill = value; MarkDirtyRepaint(); } }
        }

        /// <summary>Segment ticks every 10% read as armour plates on health bars; charts turn them off.</summary>
        [UxmlAttribute] public bool Segmented { get; set; } = true;

        public BarElement() => generateVisualContent += Draw;

        void Draw(MeshGenerationContext ctx)
        {
            float w = contentRect.width, h = contentRect.height;
            if (w < 1f || h < 1f) return;
            var p = ctx.painter2D;
            Paint.Rect(p, 0f, 0f, w, h, new Color(0f, 0f, 0f, 0.55f));
            Paint.Rect(p, 1f, 1f, (w - 2f) * m_Value, h - 2f, m_Fill);
            if (!Segmented) return;
            p.strokeColor = new Color(0f, 0f, 0f, 0.35f);
            p.lineWidth = 1f;
            for (int i = 1; i < 10; i++)
            {
                float x = w * i / 10f;
                p.BeginPath();
                p.MoveTo(new Vector2(x, 1f));
                p.LineTo(new Vector2(x, h - 1f));
                p.Stroke();
            }
        }
    }

    /// <summary>Labelled horizontal bar chart built from rows of Labels and bars, so text
    /// is laid out and styled by USS. Values are 0..1; a negative value marks the row
    /// unavailable.</summary>
    [UxmlElement]
    public partial class BarChartElement : VisualElement
    {
        sealed class Row
        {
            public VisualElement root;
            public Label name, value;
            public BarElement bar;
        }

        readonly List<Row> m_Rows = new List<Row>();

        [UxmlAttribute] public Color BarColor { get; set; } = new Color(0.31f, 0.82f, 1f, 0.85f);
        [UxmlAttribute] public Color HighlightColor { get; set; } = new Color(1f, 0.72f, 0.28f, 1f);

        public BarChartElement() => AddToClassList("bar-chart");

        public void SetData(string[] labels, float[] values, string[] valueText, int highlight)
        {
            int n = Mathf.Min(labels?.Length ?? 0, values?.Length ?? 0);
            while (m_Rows.Count < n)
            {
                var r = new Row { root = new VisualElement(), name = new Label(), value = new Label(), bar = new BarElement { Segmented = false } };
                r.root.AddToClassList("chart-row");
                r.name.AddToClassList("chart-label");
                r.bar.AddToClassList("chart-bar");
                r.value.AddToClassList("chart-value");
                r.root.Add(r.name);
                r.root.Add(r.bar);
                r.root.Add(r.value);
                Add(r.root);
                m_Rows.Add(r);
            }
            for (int i = 0; i < m_Rows.Count; i++)
            {
                var r = m_Rows[i];
                bool show = i < n;
                r.root.EnableInClassList("chart-row--hidden", !show);
                if (!show) continue;
                float v = values[i];
                bool locked = v < 0f, hi = i == highlight;
                if (r.name.text != labels[i]) r.name.text = labels[i];
                string vt = valueText != null && i < valueText.Length ? valueText[i] : "";
                if (r.value.text != vt) r.value.text = vt;
                r.bar.Value = locked ? 0f : v;
                r.bar.FillColor = hi ? HighlightColor : BarColor;
                r.root.EnableInClassList("chart-row--highlight", hi);
                r.root.EnableInClassList("chart-row--locked", locked);
            }
        }
    }

    /// <summary>Full-screen, non-interactive layer for screen-space drawing (the drag box).</summary>
    [UxmlElement]
    public partial class OverlayElement : VisualElement
    {
        public Action<Painter2D, float, float> DrawOverlay;

        public OverlayElement()
        {
            pickingMode = PickingMode.Ignore;
            generateVisualContent += ctx =>
            {
                float w = contentRect.width, h = contentRect.height;
                if (w < 1f || h < 1f) return;
                DrawOverlay?.Invoke(ctx.painter2D, w, h);
            };
        }
    }

    /// <summary>Minimap: a terrain image with a Painter2D overlay for units, pings and
    /// the camera footprint. The HUD supplies both through properties.</summary>
    [UxmlElement]
    public partial class MinimapElement : VisualElement
    {
        readonly Image m_Image;
        readonly VisualElement m_Overlay;

        /// <summary>Called when the overlay repaints: painter, width, height.</summary>
        public Action<Painter2D, float, float> DrawOverlay;

        public Texture Texture
        {
            get => m_Image.image;
            set => m_Image.image = value;
        }

        public MinimapElement()
        {
            m_Image = new Image { scaleMode = ScaleMode.StretchToFill, pickingMode = PickingMode.Ignore };
            m_Image.AddToClassList("minimap-image");
            Add(m_Image);
            m_Overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            m_Overlay.AddToClassList("minimap-overlay");
            m_Overlay.generateVisualContent += ctx =>
            {
                float w = m_Overlay.contentRect.width, h = m_Overlay.contentRect.height;
                if (w < 1f || h < 1f) return;
                DrawOverlay?.Invoke(ctx.painter2D, w, h);
            };
            Add(m_Overlay);
        }

        public void RepaintOverlay() => m_Overlay.MarkDirtyRepaint();
    }
}
