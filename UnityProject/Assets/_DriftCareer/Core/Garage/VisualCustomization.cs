using System;
using System.Collections.Generic;

namespace DriftCareer.Core.Garage
{
    public enum PaintFinish { Gloss = 0, Matte = 1, Metallic = 2, Pearl = 3, Chrome = 4, Satin = 5 }

    public enum PartSlot { FrontBumper = 0, RearBumper = 1, SideSkirts = 2, Spoiler = 3, Hood = 4, Fenders = 5, Rims = 6, Livery = 7 }

    /// <summary>Cosmetic + aero/weight part. Physical deltas are small and documented, never pay-to-win.</summary>
    [Serializable]
    public sealed class VisualPart
    {
        public string id;
        public PartSlot slot;
        public string titleKey;
        public float massDeltaKg;
        public float downforceDelta;   // m^2 (Cl*A)
        public float dragDelta;        // m^2 (Cd*A)
        /// <summary>Procedural style index used by the runtime part builder.</summary>
        public int style;
    }

    /// <summary>Visual setup of a car (paint, rims, kit). Serialized inside the tuning state.</summary>
    [Serializable]
    public sealed class VisualSetup
    {
        public string paintColor = "#C8102E";
        public PaintFinish paintFinish = PaintFinish.Metallic;
        public string secondaryColor = "#111111";
        public string rimColor = "#B8B8B8";
        public string caliperColor = "#E0B000";
        public float rimSizeInch = 17f;
        public float windowTint = 0.35f;
        public string underglowColor = "";
        public Dictionary<PartSlot, string> parts = new Dictionary<PartSlot, string>();

        public string PartFor(PartSlot slot) => parts != null && parts.TryGetValue(slot, out var id) ? id : VisualPartCatalog.StockId(slot);

        public VisualSetup Clone()
        {
            var c = (VisualSetup)MemberwiseClone();
            c.parts = parts != null ? new Dictionary<PartSlot, string>(parts) : new Dictionary<PartSlot, string>();
            return c;
        }
    }

    public static class VisualPartCatalog
    {
        public static readonly IReadOnlyList<VisualPart> Parts = Build();

        public static readonly string[] PaintPalette =
        {
            "#C8102E", "#F2F2F2", "#0B0B0B", "#1E50A0", "#F5C400", "#FF6A00", "#2E8B57", "#6A0DAD",
            "#00B5D8", "#8C8C8C", "#B87333", "#FF2D95", "#3B3B98", "#A8E10C", "#7A0019", "#D4AF37"
        };

        public static string StockId(PartSlot slot) => slot.ToString().ToLowerInvariant() + ".stock";

        private static VisualPart Part(PartSlot slot, string name, int style, float mass = 0f, float down = 0f, float drag = 0f)
            => new VisualPart
            {
                id = slot.ToString().ToLowerInvariant() + "." + name, slot = slot, titleKey = "part." + slot.ToString().ToLowerInvariant() + "." + name,
                style = style, massDeltaKg = mass, downforceDelta = down, dragDelta = drag
            };

        private static List<VisualPart> Build()
        {
            return new List<VisualPart>
            {
                Part(PartSlot.FrontBumper, "stock", 0),
                Part(PartSlot.FrontBumper, "street", 1, -2f, 0.02f, 0.01f),
                Part(PartSlot.FrontBumper, "splitter", 2, 1f, 0.06f, 0.02f),
                Part(PartSlot.FrontBumper, "aggressive", 3, -4f, 0.04f, 0.03f),

                Part(PartSlot.RearBumper, "stock", 0),
                Part(PartSlot.RearBumper, "diffuser", 1, -1f, 0.04f, 0f),
                Part(PartSlot.RearBumper, "cutout", 2, -6f, 0f, 0.01f),

                Part(PartSlot.SideSkirts, "stock", 0),
                Part(PartSlot.SideSkirts, "street", 1, 1f, 0.01f, 0f),
                Part(PartSlot.SideSkirts, "wide", 2, 2f, 0.02f, 0.01f),

                Part(PartSlot.Spoiler, "stock", 0),
                Part(PartSlot.Spoiler, "ducktail", 1, 1.5f, 0.05f, 0.01f),
                Part(PartSlot.Spoiler, "gt_wing", 2, 4f, 0.22f, 0.06f),
                Part(PartSlot.Spoiler, "swan_neck", 3, 3.5f, 0.28f, 0.05f),

                Part(PartSlot.Hood, "stock", 0),
                Part(PartSlot.Hood, "vented", 1, -1f),
                Part(PartSlot.Hood, "carbon", 2, -9f),

                Part(PartSlot.Fenders, "stock", 0),
                Part(PartSlot.Fenders, "flares", 1, 2f, 0f, 0.02f),
                Part(PartSlot.Fenders, "overfenders", 2, 3f, 0f, 0.03f),

                Part(PartSlot.Rims, "stock", 0),
                Part(PartSlot.Rims, "spoke5", 1, -1f),
                Part(PartSlot.Rims, "mesh", 2, 0f),
                Part(PartSlot.Rims, "deep_dish", 3, 1f),
                Part(PartSlot.Rims, "turbofan", 4, 0.5f),
                Part(PartSlot.Rims, "forged6", 5, -3f),

                Part(PartSlot.Livery, "stock", 0),
                Part(PartSlot.Livery, "stripes", 1),
                Part(PartSlot.Livery, "side_arrow", 2),
                Part(PartSlot.Livery, "two_tone", 3),
                Part(PartSlot.Livery, "number", 4),
            };
        }

        public static VisualPart Find(string id)
        {
            foreach (var p in Parts) if (p.id == id) return p;
            return null;
        }

        public static IEnumerable<VisualPart> ForSlot(PartSlot slot)
        {
            foreach (var p in Parts) if (p.slot == slot) yield return p;
        }

        public static bool IsValidColor(string hex)
        {
            if (string.IsNullOrEmpty(hex) || hex[0] != '#' || (hex.Length != 7 && hex.Length != 9)) return false;
            for (int i = 1; i < hex.Length; i++)
            {
                char c = hex[i];
                bool ok = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
                if (!ok) return false;
            }
            return true;
        }

        /// <summary>Removes unknown parts / wrong slots / invalid colors.</summary>
        public static VisualSetup Sanitize(VisualSetup v)
        {
            var c = (v ?? new VisualSetup()).Clone();
            var clean = new Dictionary<PartSlot, string>();
            foreach (var kv in c.parts)
            {
                var part = Find(kv.Value);
                if (part != null && part.slot == kv.Key) clean[kv.Key] = kv.Value;
            }
            c.parts = clean;
            var d = new VisualSetup();
            if (!IsValidColor(c.paintColor)) c.paintColor = d.paintColor;
            if (!IsValidColor(c.secondaryColor)) c.secondaryColor = d.secondaryColor;
            if (!IsValidColor(c.rimColor)) c.rimColor = d.rimColor;
            if (!IsValidColor(c.caliperColor)) c.caliperColor = d.caliperColor;
            if (!string.IsNullOrEmpty(c.underglowColor) && !IsValidColor(c.underglowColor)) c.underglowColor = "";
            c.rimSizeInch = MathUtil.Clamp(MathUtil.Finite(c.rimSizeInch, 17f), 15f, 20f);
            c.windowTint = MathUtil.Clamp01(MathUtil.Finite(c.windowTint, 0.35f));
            if (!Enum.IsDefined(typeof(PaintFinish), c.paintFinish)) c.paintFinish = PaintFinish.Gloss;
            return c;
        }

        /// <summary>Hex "#RRGGBB" or "#RRGGBBAA" -> floats 0..1. Returns false if invalid.</summary>
        public static bool TryParseColor(string hex, out float r, out float g, out float b, out float a)
        {
            r = g = b = 0f; a = 1f;
            if (!IsValidColor(hex)) return false;
            r = Convert.ToInt32(hex.Substring(1, 2), 16) / 255f;
            g = Convert.ToInt32(hex.Substring(3, 2), 16) / 255f;
            b = Convert.ToInt32(hex.Substring(5, 2), 16) / 255f;
            if (hex.Length == 9) a = Convert.ToInt32(hex.Substring(7, 2), 16) / 255f;
            return true;
        }
    }
}
