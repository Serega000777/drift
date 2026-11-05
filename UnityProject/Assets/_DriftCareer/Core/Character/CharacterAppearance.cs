using System;
using System.Collections.Generic;
using DriftCareer.Core.Garage;

namespace DriftCareer.Core.Character
{
    public enum BodyType { Slim = 0, Regular = 1, Strong = 2 }

    public enum GearSlot { Head = 0, Top = 1, Bottom = 2, Gloves = 3, Shoes = 4, Accessory = 5 }

    /// <summary>Wearable cosmetic item. No gameplay effect (fair play: cosmetics never change handling).</summary>
    [Serializable]
    public sealed class GearItem
    {
        public string id;
        public GearSlot slot;
        public string titleKey;
        /// <summary>Procedural style index for the runtime builder.</summary>
        public int style;
        public int price;
        public bool twoColors = true;
    }

    [Serializable]
    public struct GearChoice
    {
        public string itemId;
        public string primary;
        public string secondary;
    }

    /// <summary>
    /// Driver avatar built with in-game tools, or a player's own imported model (PC/editor pipeline, see
    /// <see cref="customModelId"/>). Versioned and sanitized like every other save.
    /// </summary>
    [Serializable]
    public sealed class CharacterAppearance
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public BodyType body = BodyType.Regular;
        /// <summary>Height scale 0.9..1.1 (1 = 1.78 m).</summary>
        public float height = 1f;
        public string skinTone = "#C99A78";
        public int hairStyle = 1;
        public string hairColor = "#2B1D14";
        public int face;
        public Dictionary<GearSlot, GearChoice> gear = new Dictionary<GearSlot, GearChoice>();
        /// <summary>Optional imported model id (Resources/DriftCareer/UserCharacters). Empty = procedural avatar.</summary>
        public string customModelId = "";

        public GearChoice Gear(GearSlot slot) => gear != null && gear.TryGetValue(slot, out var g) ? g : CharacterCatalog.DefaultChoice(slot);

        public CharacterAppearance Clone()
        {
            var c = (CharacterAppearance)MemberwiseClone();
            c.gear = gear != null ? new Dictionary<GearSlot, GearChoice>(gear) : new Dictionary<GearSlot, GearChoice>();
            return c;
        }
    }

    public static class CharacterCatalog
    {
        public const float BaseHeightM = 1.78f;
        public static readonly string[] SkinTones = { "#F3D3BE", "#E8B896", "#C99A78", "#A6704E", "#7A4E33", "#4F3222" };
        public static readonly string[] HairColors = { "#0F0D0C", "#2B1D14", "#5A3A22", "#A06A3A", "#D8B26A", "#B8B8B8", "#C8102E", "#1E50A0" };
        public static readonly int HairStyleCount = 6; // 0 bald, 1 short, 2 buzz, 3 long, 4 ponytail, 5 mohawk
        public static readonly int FaceCount = 4;

        public static readonly IReadOnlyList<GearItem> Items = new List<GearItem>
        {
            new GearItem { id = "head.none", slot = GearSlot.Head, titleKey = "gear.head.none", style = 0, twoColors = false },
            new GearItem { id = "head.cap", slot = GearSlot.Head, titleKey = "gear.head.cap", style = 1, price = 300 },
            new GearItem { id = "head.beanie", slot = GearSlot.Head, titleKey = "gear.head.beanie", style = 2, price = 300 },
            new GearItem { id = "head.helmet_open", slot = GearSlot.Head, titleKey = "gear.head.helmet_open", style = 3, price = 1500 },
            new GearItem { id = "head.helmet_full", slot = GearSlot.Head, titleKey = "gear.head.helmet_full", style = 4, price = 3000 },

            new GearItem { id = "top.tshirt", slot = GearSlot.Top, titleKey = "gear.top.tshirt", style = 0 },
            new GearItem { id = "top.hoodie", slot = GearSlot.Top, titleKey = "gear.top.hoodie", style = 1, price = 600 },
            new GearItem { id = "top.jacket", slot = GearSlot.Top, titleKey = "gear.top.jacket", style = 2, price = 1200 },
            new GearItem { id = "top.race_suit", slot = GearSlot.Top, titleKey = "gear.top.race_suit", style = 3, price = 4000 },
            new GearItem { id = "top.mechanic", slot = GearSlot.Top, titleKey = "gear.top.mechanic", style = 4, price = 800 },

            new GearItem { id = "bottom.jeans", slot = GearSlot.Bottom, titleKey = "gear.bottom.jeans", style = 0 },
            new GearItem { id = "bottom.cargo", slot = GearSlot.Bottom, titleKey = "gear.bottom.cargo", style = 1, price = 500 },
            new GearItem { id = "bottom.race_suit", slot = GearSlot.Bottom, titleKey = "gear.bottom.race_suit", style = 2, price = 2000 },
            new GearItem { id = "bottom.shorts", slot = GearSlot.Bottom, titleKey = "gear.bottom.shorts", style = 3, price = 300 },

            new GearItem { id = "gloves.none", slot = GearSlot.Gloves, titleKey = "gear.gloves.none", style = 0, twoColors = false },
            new GearItem { id = "gloves.race", slot = GearSlot.Gloves, titleKey = "gear.gloves.race", style = 1, price = 900 },
            new GearItem { id = "gloves.fingerless", slot = GearSlot.Gloves, titleKey = "gear.gloves.fingerless", style = 2, price = 400 },

            new GearItem { id = "shoes.sneakers", slot = GearSlot.Shoes, titleKey = "gear.shoes.sneakers", style = 0 },
            new GearItem { id = "shoes.race", slot = GearSlot.Shoes, titleKey = "gear.shoes.race", style = 1, price = 1100 },
            new GearItem { id = "shoes.boots", slot = GearSlot.Shoes, titleKey = "gear.shoes.boots", style = 2, price = 700 },

            new GearItem { id = "acc.none", slot = GearSlot.Accessory, titleKey = "gear.acc.none", style = 0, twoColors = false },
            new GearItem { id = "acc.glasses", slot = GearSlot.Accessory, titleKey = "gear.acc.glasses", style = 1, price = 250, twoColors = false },
            new GearItem { id = "acc.hans", slot = GearSlot.Accessory, titleKey = "gear.acc.hans", style = 2, price = 2500, twoColors = false },
            new GearItem { id = "acc.headphones", slot = GearSlot.Accessory, titleKey = "gear.acc.headphones", style = 3, price = 600 },
        };

        public static GearItem Find(string id)
        {
            foreach (var i in Items) if (i.id == id) return i;
            return null;
        }

        public static IEnumerable<GearItem> ForSlot(GearSlot slot)
        {
            foreach (var i in Items) if (i.slot == slot) yield return i;
        }

        public static GearChoice DefaultChoice(GearSlot slot)
        {
            switch (slot)
            {
                case GearSlot.Head: return new GearChoice { itemId = "head.none", primary = "#111111", secondary = "#C8102E" };
                case GearSlot.Top: return new GearChoice { itemId = "top.tshirt", primary = "#1A1A1A", secondary = "#C8102E" };
                case GearSlot.Bottom: return new GearChoice { itemId = "bottom.jeans", primary = "#2A3A5A", secondary = "#111111" };
                case GearSlot.Gloves: return new GearChoice { itemId = "gloves.none", primary = "#111111", secondary = "#C8102E" };
                case GearSlot.Shoes: return new GearChoice { itemId = "shoes.sneakers", primary = "#F2F2F2", secondary = "#C8102E" };
                default: return new GearChoice { itemId = "acc.none", primary = "#111111", secondary = "#111111" };
            }
        }

        /// <summary>Starter items owned by everyone.</summary>
        public static bool IsFree(string itemId) => Find(itemId)?.price == 0;

        public static CharacterAppearance Sanitize(CharacterAppearance a)
        {
            var c = (a ?? new CharacterAppearance()).Clone();
            if (!Enum.IsDefined(typeof(BodyType), c.body)) c.body = BodyType.Regular;
            c.height = MathUtil.Clamp(MathUtil.Finite(c.height, 1f), 0.9f, 1.1f);
            if (!VisualPartCatalog.IsValidColor(c.skinTone)) c.skinTone = SkinTones[2];
            if (!VisualPartCatalog.IsValidColor(c.hairColor)) c.hairColor = HairColors[1];
            c.hairStyle = MathUtil.Clamp(c.hairStyle, 0, HairStyleCount - 1);
            c.face = MathUtil.Clamp(c.face, 0, FaceCount - 1);
            var clean = new Dictionary<GearSlot, GearChoice>();
            foreach (var kv in c.gear)
            {
                var item = Find(kv.Value.itemId);
                if (item == null || item.slot != kv.Key) continue;
                var d = DefaultChoice(kv.Key);
                clean[kv.Key] = new GearChoice
                {
                    itemId = item.id,
                    primary = VisualPartCatalog.IsValidColor(kv.Value.primary) ? kv.Value.primary : d.primary,
                    secondary = VisualPartCatalog.IsValidColor(kv.Value.secondary) ? kv.Value.secondary : d.secondary
                };
            }
            c.gear = clean;
            if (c.customModelId == null || c.customModelId.Length > 48) c.customModelId = "";
            foreach (char ch in c.customModelId)
                if (!((ch >= 'a' && ch <= 'z') || (ch >= '0' && ch <= '9') || ch == '_')) { c.customModelId = ""; break; }
            return c;
        }

        /// <summary>Gear the player still has to buy for this look.</summary>
        public static long MissingCost(CharacterAppearance a, ICollection<string> owned)
        {
            long sum = 0;
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                var item = Find(a.Gear(slot).itemId);
                if (item != null && item.price > 0 && !owned.Contains(item.id)) sum += item.price;
            }
            return sum;
        }

        public static void MarkOwned(CharacterAppearance a, ICollection<string> owned)
        {
            foreach (GearSlot slot in Enum.GetValues(typeof(GearSlot)))
            {
                var id = a.Gear(slot).itemId;
                if (!owned.Contains(id)) owned.Add(id);
            }
        }

        /// <summary>Presets so players start from something good-looking.</summary>
        public static CharacterAppearance Preset(string id)
        {
            var a = new CharacterAppearance();
            void G(GearSlot s, string item, string p, string sec) => a.gear[s] = new GearChoice { itemId = item, primary = p, secondary = sec };
            switch (id)
            {
                case "pro":
                    G(GearSlot.Head, "head.helmet_full", "#F2F2F2", "#C8102E");
                    G(GearSlot.Top, "top.race_suit", "#C8102E", "#111111");
                    G(GearSlot.Bottom, "bottom.race_suit", "#C8102E", "#111111");
                    G(GearSlot.Gloves, "gloves.race", "#111111", "#C8102E");
                    G(GearSlot.Shoes, "shoes.race", "#111111", "#C8102E");
                    G(GearSlot.Accessory, "acc.hans", "#111111", "#111111");
                    break;
                case "street":
                    G(GearSlot.Head, "head.cap", "#111111", "#F5C400");
                    G(GearSlot.Top, "top.hoodie", "#3B3B3B", "#F5C400");
                    G(GearSlot.Bottom, "bottom.cargo", "#2E3B2A", "#111111");
                    G(GearSlot.Shoes, "shoes.sneakers", "#F2F2F2", "#111111");
                    break;
                case "garage":
                    G(GearSlot.Top, "top.mechanic", "#1E50A0", "#F2F2F2");
                    G(GearSlot.Bottom, "bottom.jeans", "#1E2A40", "#111111");
                    G(GearSlot.Gloves, "gloves.fingerless", "#111111", "#111111");
                    G(GearSlot.Shoes, "shoes.boots", "#5A3A22", "#111111");
                    break;
            }
            return a;
        }

        public static readonly string[] PresetIds = { "default", "street", "garage", "pro" };
    }

    public static class CharacterJson
    {
        public static JsonValue Encode(CharacterAppearance a)
        {
            var gear = JsonValue.NewObject();
            foreach (var kv in a.gear)
                gear.Set(kv.Key.ToString(), JsonValue.NewObject().Set("item", kv.Value.itemId).Set("p", kv.Value.primary ?? "").Set("s", kv.Value.secondary ?? ""));
            return JsonValue.NewObject().Set("version", a.version).Set("body", a.body.ToString()).Set("height", a.height).Set("skin", a.skinTone)
                .Set("hair", a.hairStyle).Set("hairColor", a.hairColor).Set("face", a.face).Set("model", a.customModelId ?? "").Set("gear", gear);
        }

        public static CharacterAppearance Decode(JsonValue j)
        {
            var a = new CharacterAppearance
            {
                body = j.GetEnum("body", BodyType.Regular), height = j.GetFloat("height", 1f), skinTone = j.GetString("skin", "#C99A78"),
                hairStyle = j.GetInt("hair", 1), hairColor = j.GetString("hairColor", "#2B1D14"), face = j.GetInt("face"), customModelId = j.GetString("model", "")
            };
            var gear = j.GetObject("gear");
            if (gear != null)
                foreach (var k in gear.Keys)
                    if (Enum.TryParse(k, out GearSlot slot) && gear[k].IsObject)
                        a.gear[slot] = new GearChoice { itemId = gear[k].GetString("item"), primary = gear[k].GetString("p"), secondary = gear[k].GetString("s") };
            return CharacterCatalog.Sanitize(a);
        }
    }
}
