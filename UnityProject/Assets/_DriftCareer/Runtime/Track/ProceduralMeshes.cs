using System.Collections.Generic;
using UnityEngine;

namespace DriftCareer.Runtime.Track
{
    /// <summary>
    /// Procedurally generated meshes (original work, no third-party assets). Cached by key.
    /// All meshes have their pivot at the bottom center unless stated otherwise.
    /// </summary>
    public static class ProceduralMeshes
    {
        private static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>();

        private static Mesh Cached(string key, System.Func<Mesh> build)
        {
            if (Cache.TryGetValue(key, out var m) && m != null) return m;
            m = build();
            m.name = "DC_" + key;
            Cache[key] = m;
            return m;
        }

        /// <summary>Cone with a square base plate (traffic cone). Pivot at ground.</summary>
        public static Mesh Cone(float radius = 0.16f, float height = 0.7f, int segments = 16)
        {
            return Cached($"cone_{radius}_{height}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                float baseH = 0.03f, baseHalf = radius * 1.35f;
                AddBox(v, t, new Vector3(0, baseH * 0.5f, 0), new Vector3(baseHalf, baseH * 0.5f, baseHalf));
                int start = v.Count;
                v.Add(new Vector3(0, height, 0));
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * radius, baseH, Mathf.Sin(a) * radius));
                }
                for (int i = 0; i < segments; i++) { t.Add(start); t.Add(start + i + 2); t.Add(start + i + 1); }
                return Build(v, t);
            });
        }

        /// <summary>Tire: tube with outer/inner radius and width. Axis = X. Pivot at center.</summary>
        public static Mesh Tire(float outer, float inner, float width, int segments = 24)
        {
            return Cached($"tire_{outer}_{inner}_{width}_{segments}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                float hw = width * 0.5f;
                // outer tread, inner wall, two sidewalls (flat annuli)
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    float c = Mathf.Cos(a), s = Mathf.Sin(a);
                    v.Add(new Vector3(-hw, c * outer, s * outer));
                    v.Add(new Vector3(hw, c * outer, s * outer));
                    v.Add(new Vector3(-hw, c * inner, s * inner));
                    v.Add(new Vector3(hw, c * inner, s * inner));
                }
                for (int i = 0; i < segments; i++)
                {
                    int a = i * 4, b = (i + 1) * 4;
                    Quad(t, a + 0, b + 0, b + 1, a + 1);      // tread
                    Quad(t, a + 3, b + 3, b + 2, a + 2);      // inner
                    Quad(t, a + 2, b + 2, b + 0, a + 0);      // left wall
                    Quad(t, a + 1, b + 1, b + 3, a + 3);      // right wall
                }
                return Build(v, t);
            });
        }

        /// <summary>Wheel rim with spokes. Axis = X, pivot at center. Styles: 0 stock disc .. 5 forged.</summary>
        public static Mesh Rim(float radius, float width, int style)
        {
            return Cached($"rim_{radius}_{width}_{style}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                int spokes = style == 2 ? 10 : (style == 4 ? 12 : (style == 5 ? 6 : (style == 0 ? 8 : 5)));
                float dish = style == 3 ? width * 0.45f : width * 0.25f;
                float barrelOuter = radius, barrelInner = radius * 0.86f;
                AddCylinderX(v, t, barrelOuter, width, 24, 0f);
                // hub
                AddCylinderX(v, t, radius * 0.22f, width * 0.6f, 12, dish * 0.4f);
                // spokes as thin boxes from hub to barrel
                float spokeW = style == 2 ? 0.012f : (style == 5 ? 0.03f : 0.022f);
                for (int i = 0; i < spokes; i++)
                {
                    float a = i * Mathf.PI * 2f / spokes;
                    var dir = new Vector3(0, Mathf.Cos(a), Mathf.Sin(a));
                    var mid = dir * (radius * 0.55f) + new Vector3(dish * 0.5f, 0, 0);
                    AddOrientedBox(v, t, mid, new Vector3(0.012f, radius * 0.34f, spokeW), Quaternion.FromToRotation(Vector3.up, dir));
                }
                if (style == 4) AddCylinderX(v, t, barrelInner, 0.01f, 24, dish);
                return Build(v, t);
            });
        }

        public static Mesh Box(Vector3 size)
        {
            return Cached($"box_{size.x}_{size.y}_{size.z}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                AddBox(v, t, new Vector3(0, size.y * 0.5f, 0), size * 0.5f);
                return Build(v, t);
            });
        }

        /// <summary>Flat ring (annulus) lying on XZ, used for painted circles.</summary>
        public static Mesh Ring(float radius, float lineWidth, int segments = 96)
        {
            return Cached($"ring_{radius}_{lineWidth}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                float r0 = radius - lineWidth * 0.5f, r1 = radius + lineWidth * 0.5f;
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Sin(a) * r0, 0, Mathf.Cos(a) * r0));
                    v.Add(new Vector3(Mathf.Sin(a) * r1, 0, Mathf.Cos(a) * r1));
                }
                for (int i = 0; i < segments; i++) Quad(t, i * 2, i * 2 + 1, i * 2 + 3, i * 2 + 2);
                return Build(v, t);
            });
        }

        /// <summary>Flat quad on XZ centered at origin.</summary>
        public static Mesh FlatQuad(float sizeX, float sizeZ)
        {
            return Cached($"quad_{sizeX}_{sizeZ}", () =>
            {
                var v = new List<Vector3>
                {
                    new Vector3(-sizeX * 0.5f, 0, -sizeZ * 0.5f), new Vector3(-sizeX * 0.5f, 0, sizeZ * 0.5f),
                    new Vector3(sizeX * 0.5f, 0, sizeZ * 0.5f), new Vector3(sizeX * 0.5f, 0, -sizeZ * 0.5f)
                };
                var t = new List<int> { 0, 1, 2, 0, 2, 3 };
                var m = Build(v, t);
                m.uv = new[] { new Vector2(0, 0), new Vector2(0, sizeZ / 4f), new Vector2(sizeX / 4f, sizeZ / 4f), new Vector2(sizeX / 4f, 0) };
                return m;
            });
        }

        /// <summary>Vertical cylinder (bollards, posts). Pivot at ground.</summary>
        public static Mesh CylinderY(float radius, float height, int segments = 12)
        {
            return Cached($"cyl_{radius}_{height}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                for (int i = 0; i <= segments; i++)
                {
                    float a = i * Mathf.PI * 2f / segments;
                    v.Add(new Vector3(Mathf.Cos(a) * radius, 0, Mathf.Sin(a) * radius));
                    v.Add(new Vector3(Mathf.Cos(a) * radius, height, Mathf.Sin(a) * radius));
                }
                for (int i = 0; i < segments; i++) Quad(t, i * 2, i * 2 + 1, i * 2 + 3, i * 2 + 2);
                int top = v.Count;
                v.Add(new Vector3(0, height, 0));
                for (int i = 0; i < segments; i++) { t.Add(top); t.Add(i * 2 + 3); t.Add(i * 2 + 1); }
                return Build(v, t);
            });
        }

        /// <summary>Low-poly UV sphere centered at origin (optionally only the upper hemisphere).</summary>
        public static Mesh Sphere(float radius, int segments = 16, bool hemisphere = false)
        {
            return Cached($"sphere_{radius}_{segments}_{hemisphere}", () =>
            {
                var v = new List<Vector3>(); var t = new List<int>();
                int rings = segments / 2;
                int startRing = hemisphere ? rings / 2 : 0;
                for (int r = startRing; r <= rings; r++)
                {
                    float phi = Mathf.PI * r / rings; // 0 = bottom
                    float y = -Mathf.Cos(phi) * radius, rr = Mathf.Sin(phi) * radius;
                    for (int sIdx = 0; sIdx <= segments; sIdx++)
                    {
                        float th = 2f * Mathf.PI * sIdx / segments;
                        v.Add(new Vector3(Mathf.Cos(th) * rr, y, Mathf.Sin(th) * rr));
                    }
                }
                int stride = segments + 1, count = rings - startRing;
                for (int r = 0; r < count; r++)
                for (int sIdx = 0; sIdx < segments; sIdx++)
                {
                    int a = r * stride + sIdx, b = a + stride;
                    // Clockwise when seen from outside (Unity front faces).
                    t.Add(a); t.Add(b + 1); t.Add(a + 1);
                    t.Add(a); t.Add(b); t.Add(b + 1);
                }
                return Build(v, t);
            });
        }

        // ---------------- helpers ----------------

        public static Mesh Build(List<Vector3> v, List<int> t)
        {
            var m = new Mesh();
            if (v.Count > 65000) m.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            m.SetVertices(v);
            m.SetTriangles(t, 0);
            // Cheap planar UVs (4 m per tile) so tiled textures show on generated boxes.
            var uv = new List<Vector2>(v.Count);
            foreach (var p in v) uv.Add(new Vector2(p.x + p.z * 0.35f, p.z + p.y) * 0.25f);
            m.SetUVs(0, uv);
            m.RecalculateNormals();
            m.RecalculateBounds();
            return m;
        }

        public static void Quad(List<int> t, int a, int b, int c, int d)
        {
            t.Add(a); t.Add(b); t.Add(c);
            t.Add(a); t.Add(c); t.Add(d);
        }

        /// <summary>Axis-aligned box with separate vertices per face (flat shading).</summary>
        public static void AddBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 h)
        {
            AddOrientedBox(v, t, c, h, Quaternion.identity);
        }

        public static void AddOrientedBox(List<Vector3> v, List<int> t, Vector3 c, Vector3 h, Quaternion r)
        {
            Vector3[] n = { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
            foreach (var face in n)
            {
                Vector3 u = Mathf.Abs(face.y) > 0.5f ? Vector3.right : Vector3.up;
                Vector3 w = Vector3.Cross(face, u);
                Vector3 fc = Vector3.Scale(face, h);
                Vector3 uu = Vector3.Scale(u, h), ww = Vector3.Scale(w, h);
                int i = v.Count;
                v.Add(c + r * (fc - uu - ww));
                v.Add(c + r * (fc + uu - ww));
                v.Add(c + r * (fc + uu + ww));
                v.Add(c + r * (fc - uu + ww));
                t.Add(i); t.Add(i + 1); t.Add(i + 2);
                t.Add(i); t.Add(i + 2); t.Add(i + 3);
            }
        }

        /// <summary>Closed cylinder along X centered at (offsetX,0,0).</summary>
        public static void AddCylinderX(List<Vector3> v, List<int> t, float radius, float width, int segments, float offsetX)
        {
            int start = v.Count;
            float hw = width * 0.5f;
            for (int i = 0; i <= segments; i++)
            {
                float a = i * Mathf.PI * 2f / segments;
                float c = Mathf.Cos(a) * radius, s = Mathf.Sin(a) * radius;
                v.Add(new Vector3(offsetX - hw, c, s));
                v.Add(new Vector3(offsetX + hw, c, s));
            }
            for (int i = 0; i < segments; i++) Quad(t, start + i * 2, start + i * 2 + 2, start + i * 2 + 3, start + i * 2 + 1);
            int capL = v.Count; v.Add(new Vector3(offsetX - hw, 0, 0));
            int capR = v.Count; v.Add(new Vector3(offsetX + hw, 0, 0));
            for (int i = 0; i < segments; i++)
            {
                t.Add(capL); t.Add(start + (i + 1) * 2); t.Add(start + i * 2);
                t.Add(capR); t.Add(start + i * 2 + 1); t.Add(start + (i + 1) * 2 + 1);
            }
        }

        /// <summary>Creates a child GameObject with a mesh renderer.</summary>
        public static GameObject Spawn(string name, Transform parent, Mesh mesh, Material mat, Vector3 localPos, Quaternion localRot, bool castShadows = true)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = castShadows ? UnityEngine.Rendering.ShadowCastingMode.On : UnityEngine.Rendering.ShadowCastingMode.Off;
            return go;
        }

        public static GameObject SpawnBox(string name, Transform parent, Vector3 center, Vector3 size, Material mat, bool collider = true, bool castShadows = true)
        {
            var go = Spawn(name, parent, Box(size), mat, center - new Vector3(0, size.y * 0.5f, 0), Quaternion.identity, castShadows);
            if (collider)
            {
                var bc = go.AddComponent<BoxCollider>();
                bc.center = new Vector3(0, size.y * 0.5f, 0);
                bc.size = size;
            }
            return go;
        }
    }
}
