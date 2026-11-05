using System;
using System.Collections.Generic;

namespace DriftCareer.Core
{
    [Serializable]
    public struct CurveKey
    {
        public float x;
        public float y;
        public CurveKey(float x, float y) { this.x = x; this.y = y; }
    }

    /// <summary>
    /// Piecewise-linear curve, clamped at ends. Used for torque curves and tuning tables.
    /// Deterministic and allocation-free on evaluation.
    /// </summary>
    [Serializable]
    public sealed class Curve
    {
        public List<CurveKey> keys = new List<CurveKey>();

        public Curve() { }

        public Curve(params CurveKey[] k)
        {
            keys.AddRange(k);
            Sort();
        }

        public static Curve FromPairs(params float[] xy)
        {
            if (xy.Length % 2 != 0) throw new ArgumentException("Expected x,y pairs");
            var c = new Curve();
            for (int i = 0; i < xy.Length; i += 2) c.keys.Add(new CurveKey(xy[i], xy[i + 1]));
            c.Sort();
            return c;
        }

        public void Sort() => keys.Sort((a, b) => a.x.CompareTo(b.x));

        public float Evaluate(float x)
        {
            int n = keys.Count;
            if (n == 0) return 0f;
            if (x <= keys[0].x) return keys[0].y;
            if (x >= keys[n - 1].x) return keys[n - 1].y;
            for (int i = 1; i < n; i++)
            {
                var b = keys[i];
                if (x <= b.x)
                {
                    var a = keys[i - 1];
                    float t = (x - a.x) / Math.Max(1e-6f, b.x - a.x);
                    return a.y + (b.y - a.y) * t;
                }
            }
            return keys[n - 1].y;
        }

        public float MaxY()
        {
            float m = float.MinValue;
            foreach (var k in keys) if (k.y > m) m = k.y;
            return keys.Count == 0 ? 0f : m;
        }

        public Curve Clone()
        {
            var c = new Curve();
            c.keys.AddRange(keys);
            return c;
        }

        public bool IsValid(out string error)
        {
            if (keys.Count < 2) { error = "Curve needs at least 2 keys"; return false; }
            for (int i = 0; i < keys.Count; i++)
            {
                if (!MathUtil.IsFinite(keys[i].x) || !MathUtil.IsFinite(keys[i].y)) { error = "Curve has non-finite key"; return false; }
                if (i > 0 && keys[i].x <= keys[i - 1].x) { error = "Curve keys must be strictly increasing in x"; return false; }
            }
            error = null;
            return true;
        }
    }
}
