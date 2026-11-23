// OrderStandards.cs — the eight knightly orders' standards (docs/lore/starforge-lore.md, section 7): one
// atlas for the panel on a Mech's cloth banner and the device on the HUD's Mech strip. The pictures are
// the author's (docs/lore/prompts.md, prompt 10), packed by Tools/pack_standards.py. Presentation only:
// nothing in the simulation reads it, and with the atlas missing the banner keeps its old sigil.
using UnityEngine;

namespace StarForge.View
{
    public static class OrderStandards
    {
        public const int Count = 8;
        /// <summary>A banner's height over its width in the sheet (pack_standards.py prints it).
        /// The atlas holds it squeezed, so whatever draws one stretches it back by this.</summary>
        public const float SourceAspect = 1.297f;

        // The atlas's layout, as Tools/pack_standards.py writes it (keep the two alike): 1024 x 512, four cells
        // across and two down from the top, a banner in the middle of its cell with a gutter of its own edge
        // round it (so no filtered or mip-mapped sample takes the neighbour's colour), white under the cells.
        const string Resource = "MechStandards";   // Assets/StarForge/Resources/MechStandards.png
        const float SheetW = 1024f, SheetH = 512f, CellW = 256f, CellH = 224f, Gutter = 8f;
        const int Across = 4;
        const float IconSpan = 0.84f;              // the HUD's square, of a banner's width: the hammer, the tallest device, fits

        // MechParts.Orders' order, which is the lore's table and the atlas's cells.
        static readonly string[] Names =
        {
            "the Ashen Covenant", "the Order of the Iron Lantern", "the Ninth Vigil", "the Sable Oath",
            "the Order of the Broken Crown", "the Emberguard", "the Wardens of the Long Night", "the Brazen Host",
        };

        static Texture2D atlas;
        static bool looked;
        static readonly Sprite[] icons = new Sprite[Count];

        // Statics outlive a play session in the Editor (domain reload is off); the sprites made in play do not.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void NewSession()
        {
            looked = false;
            atlas = null;
            for (int i = 0; i < Count; i++) icons[i] = null;
        }

        /// <summary>Null when the atlas is not in the project: callers then draw as they did without it.</summary>
        public static Texture2D Atlas
        {
            get
            {
                if (!looked) { looked = true; atlas = Resources.Load<Texture2D>(Resource); }
                return atlas;
            }
        }

        /// <summary>The order's cell, or -1 for no order or a name the atlas has no banner for.</summary>
        public static int IndexOf(string order)
        {
            if (string.IsNullOrEmpty(order)) return -1;
            for (int i = 0; i < Count; i++)
                if (Names[i] == order) return i;
            return -1;
        }

        /// <summary>The order's name as the lore writes it ("the Emberguard"), or null.</summary>
        public static string NameOf(int index) => index >= 0 && index < Count ? Names[index] : null;

        /// <summary>A point in the atlas's white strip: cloth that samples it keeps its vertex colours.</summary>
        public static Vector2 White => new Vector2(0.5f, (SheetH - 2f * CellH) * 0.5f / SheetH);

        // The banner inside the order's cell, in the sheet's pixels with y down from the top.
        static Rect Banner(int index)
        {
            int cx = index % Across, cy = index / Across;
            return new Rect(cx * CellW + Gutter, cy * CellH + Gutter, CellW - 2f * Gutter, CellH - 2f * Gutter);
        }

        /// <summary>The order's banner in the atlas's UVs (v up).</summary>
        public static Rect Uv(int index)
        {
            var b = Banner(index);
            return new Rect(b.x / SheetW, 1f - b.yMax / SheetH, b.width / SheetW, b.height / SheetH);
        }

        /// <summary>The device on its field, cut square out of the banner's middle, for the HUD (the square is
        /// squeezed in the atlas: draw it stretched to fill a square box). Null without the atlas.</summary>
        public static Sprite Icon(int index)
        {
            if (index < 0 || index >= Count) return null;
            var a = Atlas;
            if (a == null) return null;
            if (icons[index] == null)   // Unity's null check: a sprite of an earlier play session is gone
            {
                var b = Banner(index);
                float w = IconSpan * b.width, h = IconSpan / SourceAspect * b.height;   // square on the banner itself
                float sx = a.width / SheetW, sy = a.height / SheetH;                    // the import may have scaled the sheet
                var rect = new Rect((b.x + (b.width - w) * 0.5f) * sx, (SheetH - b.yMax + (b.height - h) * 0.5f) * sy, w * sx, h * sy);
                icons[index] = Sprite.Create(a, rect, new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect);
                icons[index].name = "SF_Order_" + index;
            }
            return icons[index];
        }
    }
}
