using System.Collections.Generic;
using DriftCareer.Core;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Surfaces;
using UnityEngine;
using UnityEngine.Rendering;

namespace DriftCareer.Runtime.Track
{
    /// <summary>
    /// Builds the Drift Academy test ground procedurally from <see cref="AcademySite"/>: hangar with an open door,
    /// asphalt lot, painted practice zones, perimeter guardrails with tire walls, lights and cheap scenery.
    /// Static geometry is marked static and grouped to keep draw calls low on mobile.
    /// </summary>
    public static class AcademyBuilder
    {
        public sealed class Result
        {
            public Transform root;
            public Vector3 spawnPosition;
            public Quaternion spawnRotation;
            public Vector3 walkStart;
            public Vector3 exitPoint;
            public Transform drillMarkers;
        }

        private static Vector3 V(Vec2 p, float y = 0f) => new Vector3(p.X, y, p.Y);

        public static Result Build(AcademySite site, Transform parent, bool highQuality)
        {
            var result = new Result();
            var routine = BuildRoutine(site, parent, highQuality, result, null);
            while (routine.MoveNext()) { }
            return result;
        }

        /// <summary>
        /// Same as <see cref="Build"/>, but yields between stages so a loading screen can animate and show real progress.
        /// Progress callback: (0..1, localization key of the stage).
        /// </summary>
        public static System.Collections.IEnumerator BuildRoutine(AcademySite site, Transform parent, bool highQuality, Result r, System.Action<float, string> progress)
        {
            var root = new GameObject("Academy").transform;
            root.SetParent(parent, false);
            r.root = root;
            progress?.Invoke(0.05f, "loading.stage.lighting");
            SetupEnvironment(highQuality);
            yield return null;
            progress?.Invoke(0.18f, "loading.stage.lot");
            BuildGround(site, root);
            yield return null;
            progress?.Invoke(0.35f, "loading.stage.hangar");
            BuildHangar(site, root, highQuality);
            yield return null;
            progress?.Invoke(0.5f, "loading.stage.markings");
            BuildMarkings(site, root);
            r.drillMarkers = BuildDrillMarkers(site, root);
            yield return null;
            progress?.Invoke(0.62f, "loading.stage.perimeter");
            BuildPerimeter(site, root);
            yield return null;
            progress?.Invoke(0.72f, "loading.stage.scenery");
            BuildScenery(site, root, highQuality);
            yield return null;
            if (highQuality)
            {
                progress?.Invoke(0.8f, "loading.stage.reflections");
                // Reflections for car paint: one probe inside the hangar, one over the lot.
                var hc = V(site.HangarCenter, site.hangarHeight * 0.4f);
                GraphicsManager.CreateProbe(root, hc, new Vector3(site.hangarSize.X, site.hangarHeight, site.hangarSize.Y), 128);
                GraphicsManager.CreateProbe(root, new Vector3(0f, 6f, 0f), new Vector3(site.lotSize, 40f, site.lotSize), 128);
                yield return null;
            }
            r.spawnPosition = V(site.SpawnPosition, 0.04f);
            r.spawnRotation = Quaternion.Euler(0f, site.SpawnYawDeg, 0f);
            r.walkStart = V(site.HangarCenter + new Vec2(-site.hangarSize.X * 0.32f, -site.hangarSize.Y * 0.38f));
            r.exitPoint = V(site.DoorCenter + new Vec2(0f, site.exitLaneLength * 0.7f));
        }

        private static void SetupEnvironment(bool highQuality)
        {
            var skyShader = Shader.Find("Skybox/Procedural");
            if (skyShader != null)
            {
                var sky = new Material(skyShader) { name = "DC_Sky" };
                if (sky.HasProperty("_SunSize")) sky.SetFloat("_SunSize", 0.035f);
                if (sky.HasProperty("_AtmosphereThickness")) sky.SetFloat("_AtmosphereThickness", 0.85f);
                if (sky.HasProperty("_SkyTint")) sky.SetColor("_SkyTint", new Color(0.52f, 0.58f, 0.66f));
                if (sky.HasProperty("_GroundColor")) sky.SetColor("_GroundColor", new Color(0.36f, 0.37f, 0.38f));
                if (sky.HasProperty("_Exposure")) sky.SetFloat("_Exposure", 1.15f);
                RenderSettings.skybox = sky;
            }
            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.62f, 0.68f, 0.78f);
            RenderSettings.ambientEquatorColor = new Color(0.5f, 0.5f, 0.5f);
            RenderSettings.ambientGroundColor = new Color(0.25f, 0.24f, 0.23f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.68f, 0.72f, 0.78f);
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 900f;

            var sunGo = new GameObject("Sun");
            var sun = sunGo.AddComponent<Light>();
            sun.type = LightType.Directional;
            sun.color = new Color(1f, 0.96f, 0.9f);
            sun.intensity = 1.25f;
            sun.shadows = highQuality ? LightShadows.Soft : LightShadows.Hard;
            sun.shadowStrength = 0.8f;
            sunGo.transform.rotation = Quaternion.Euler(48f, -35f, 0f);
            RenderSettings.sun = sun;
        }

        private static void BuildGround(AcademySite site, Transform root)
        {
            float size = site.lotSize;
            var asphalt = MaterialLibrary.LitColor("asphalt", Color.white, 0.22f, 0f, MaterialLibrary.AsphaltTexture(), new Vector2(0.5f, 0.5f), MaterialLibrary.AsphaltNormal(), 0.8f);
            var ground = ProceduralMeshes.Spawn("Lot", root, ProceduralMeshes.FlatQuad(size, size), asphalt, Vector3.zero, Quaternion.identity, false);
            ground.GetComponent<MeshRenderer>().receiveShadows = true;
            var col = ground.AddComponent<BoxCollider>();
            col.center = new Vector3(0f, -0.5f, 0f);
            col.size = new Vector3(size, 1f, size);
            ground.AddComponent<SurfaceTag>().surfaceId = SurfaceLibrary.DryAsphalt;
            ground.isStatic = true;

            // Outer grass/runoff ring beyond the guardrails (visual + grass surface).
            var grass = MaterialLibrary.LitColor("grass", new Color(0.3f, 0.38f, 0.22f), 0.1f);
            var outer = ProceduralMeshes.Spawn("Runoff", root, ProceduralMeshes.FlatQuad(size * 3.5f, size * 3.5f), grass, new Vector3(0f, -0.02f, 0f), Quaternion.identity, false);
            var oc = outer.AddComponent<BoxCollider>();
            oc.center = new Vector3(0f, -0.6f, 0f);
            oc.size = new Vector3(size * 3.5f, 1f, size * 3.5f);
            outer.AddComponent<SurfaceTag>().surfaceId = SurfaceLibrary.Grass;
            outer.isStatic = true;
        }

        private static void BuildHangar(AcademySite site, Transform root, bool highQuality)
        {
            var h = new GameObject("Hangar").transform;
            h.SetParent(root, false);
            Vector3 c = V(site.HangarCenter);
            float w = site.hangarSize.X, d = site.hangarSize.Y, height = site.hangarHeight, t = 0.4f;
            var wall = MaterialLibrary.LitColor("hangar_wall", new Color(0.55f, 0.58f, 0.6f), 0.35f, 0.4f);
            var trim = MaterialLibrary.LitColor("hangar_trim", new Color(0.85f, 0.32f, 0.06f), 0.4f);
            var roofMat = MaterialLibrary.LitColor("hangar_roof", new Color(0.4f, 0.42f, 0.44f), 0.3f, 0.5f);
            var concrete = MaterialLibrary.LitColor("concrete", Color.white, 0.35f, 0f, MaterialLibrary.ConcreteTexture(), new Vector2(0.6f, 0.6f), MaterialLibrary.ConcreteNormal(), 0.6f);

            // Floor slab (2 cm threshold at the door).
            var floor = ProceduralMeshes.SpawnBox("Floor", h, c + new Vector3(0f, 0.01f, 0f), new Vector3(w, 0.02f, d), concrete);
            floor.AddComponent<SurfaceTag>().surfaceId = SurfaceLibrary.Concrete;

            ProceduralMeshes.SpawnBox("WallBack", h, c + new Vector3(0f, height * 0.5f, -d * 0.5f), new Vector3(w, height, t), wall);
            ProceduralMeshes.SpawnBox("WallLeft", h, c + new Vector3(-w * 0.5f, height * 0.5f, 0f), new Vector3(t, height, d), wall);
            ProceduralMeshes.SpawnBox("WallRight", h, c + new Vector3(w * 0.5f, height * 0.5f, 0f), new Vector3(t, height, d), wall);
            float side = (w - site.doorWidth) * 0.5f;
            ProceduralMeshes.SpawnBox("WallFrontL", h, c + new Vector3(-(site.doorWidth * 0.5f + side * 0.5f), height * 0.5f, d * 0.5f), new Vector3(side, height, t), wall);
            ProceduralMeshes.SpawnBox("WallFrontR", h, c + new Vector3(site.doorWidth * 0.5f + side * 0.5f, height * 0.5f, d * 0.5f), new Vector3(side, height, t), wall);
            ProceduralMeshes.SpawnBox("Lintel", h, c + new Vector3(0f, (height + site.doorHeight) * 0.5f, d * 0.5f), new Vector3(site.doorWidth, height - site.doorHeight, t), wall);
            ProceduralMeshes.SpawnBox("DoorTrim", h, c + new Vector3(0f, site.doorHeight + 0.15f, d * 0.5f + 0.25f), new Vector3(site.doorWidth + 0.6f, 0.3f, 0.1f), trim, false);
            ProceduralMeshes.SpawnBox("Roof", h, c + new Vector3(0f, height + 0.15f, 0f), new Vector3(w + 0.8f, 0.3f, d + 0.8f), roofMat);
            // Rolled-up door drum above the opening.
            var drum = ProceduralMeshes.Spawn("DoorDrum", h, ProceduralMeshes.Tire(0.45f, 0.01f, site.doorWidth, 16), roofMat,
                c + new Vector3(0f, site.doorHeight + 0.6f, d * 0.5f - 0.6f), Quaternion.identity);
            _ = drum;

            // Interior: ceiling light panels, shelves with tires, workbench, oil drums, stripes.
            var lightPanel = MaterialLibrary.Emissive("hangar_light", new Color(1f, 0.98f, 0.92f), 3f);
            for (int i = -1; i <= 1; i++)
            for (int j = -1; j <= 1; j += 2)
                ProceduralMeshes.Spawn("LightPanel", h, ProceduralMeshes.Box(new Vector3(1.6f, 0.08f, 3.5f)), lightPanel, c + new Vector3(j * w * 0.22f, height - 0.2f, i * d * 0.28f), Quaternion.identity, false);
            for (int k = 0; k < 2; k++)
            {
                var lgo = new GameObject("HangarLight");
                lgo.transform.SetParent(h, false);
                lgo.transform.position = c + new Vector3(0f, height - 1.2f, (k == 0 ? -0.2f : 0.25f) * d);
                var l = lgo.AddComponent<Light>();
                l.type = LightType.Point;
                l.range = 22f;
                l.intensity = 1.6f;
                l.color = new Color(1f, 0.95f, 0.85f);
                l.shadows = highQuality && k == 0 ? LightShadows.Soft : LightShadows.None;
            }
            var shelf = MaterialLibrary.LitColor("shelf", new Color(0.2f, 0.3f, 0.55f), 0.4f, 0.5f);
            var rubber = MaterialLibrary.LitColor("tire_rubber", new Color(0.05f, 0.05f, 0.055f), 0.15f);
            var tireMesh = ProceduralMeshes.Tire(0.32f, 0.2f, 0.22f, 16);
            var combine = new List<CombineInstance>();
            for (int sIdx = 0; sIdx < 3; sIdx++)
            {
                Vector3 sp = c + new Vector3(-w * 0.5f + 0.9f, 0f, -d * 0.3f + sIdx * 4.2f);
                ProceduralMeshes.SpawnBox("Shelf", h, sp + new Vector3(0f, 1.25f, 0f), new Vector3(1.1f, 2.5f, 3.6f), shelf).GetComponent<MeshRenderer>().enabled = false;
                for (int lvl = 0; lvl < 3; lvl++)
                {
                    ProceduralMeshes.SpawnBox("Plank", h, sp + new Vector3(0f, 0.15f + lvl * 0.9f, 0f), new Vector3(1.1f, 0.06f, 3.6f), shelf, false);
                    for (int n = 0; n < 5; n++)
                        combine.Add(new CombineInstance
                        {
                            mesh = tireMesh,
                            transform = Matrix4x4.TRS(sp + new Vector3(0f, 0.5f + lvl * 0.9f, -1.4f + n * 0.7f), Quaternion.Euler(0f, 90f, 0f), Vector3.one)
                        });
                }
            }
            SpawnCombined("ShelfTires", h, combine, rubber);
            var bench = MaterialLibrary.LitColor("bench", new Color(0.35f, 0.25f, 0.18f), 0.3f);
            ProceduralMeshes.SpawnBox("Workbench", h, c + new Vector3(w * 0.5f - 1.2f, 0.45f, -d * 0.25f), new Vector3(1.4f, 0.9f, 4f), bench);
            ProceduralMeshes.SpawnBox("ToolChest", h, c + new Vector3(w * 0.5f - 1.0f, 0.6f, d * 0.05f), new Vector3(0.9f, 1.2f, 1.6f), MaterialLibrary.LitColor("toolchest", new Color(0.8f, 0.08f, 0.06f), 0.6f, 0.3f));
            var drumMat = MaterialLibrary.LitColor("oil_drum", new Color(0.1f, 0.35f, 0.6f), 0.5f, 0.3f);
            for (int i = 0; i < 4; i++)
            {
                var od = ProceduralMeshes.Spawn("OilDrum", h, ProceduralMeshes.CylinderY(0.3f, 0.9f), drumMat, c + new Vector3(w * 0.5f - 0.8f - (i % 2) * 0.7f, 0f, d * 0.3f + (i / 2) * 0.7f), Quaternion.identity);
                var cc = od.AddComponent<CapsuleCollider>();
                cc.radius = 0.3f; cc.height = 0.9f; cc.center = new Vector3(0f, 0.45f, 0f);
            }
            var stripe = MaterialLibrary.UnlitColor("paint_yellow", new Color(0.95f, 0.75f, 0.1f));
            // Parking bay for the car + guide stripes toward the door.
            Vector3 spawn = V(site.SpawnPosition, 0.025f);
            ProceduralMeshes.Spawn("BayL", h, ProceduralMeshes.FlatQuad(0.12f, 6f), stripe, spawn + new Vector3(-1.6f, 0f, 0f), Quaternion.identity, false);
            ProceduralMeshes.Spawn("BayR", h, ProceduralMeshes.FlatQuad(0.12f, 6f), stripe, spawn + new Vector3(1.6f, 0f, 0f), Quaternion.identity, false);
            ProceduralMeshes.Spawn("BayBack", h, ProceduralMeshes.FlatQuad(3.3f, 0.12f), stripe, spawn + new Vector3(0f, 0f, -3f), Quaternion.identity, false);
            var white = MaterialLibrary.UnlitColor("paint_white", new Color(0.92f, 0.92f, 0.9f));
            for (int i = 0; i < 6; i++)
                ProceduralMeshes.Spawn("Chevron", h, ProceduralMeshes.FlatQuad(2.4f, 0.35f), white, spawn + new Vector3(0f, 0f, 4.5f + i * 2.4f), Quaternion.Euler(0f, 0f, 0f), false);
        }

        private static void BuildMarkings(AcademySite site, Transform root)
        {
            var m = new GameObject("Markings").transform;
            m.SetParent(root, false);
            var white = MaterialLibrary.UnlitColor("paint_white", new Color(0.92f, 0.92f, 0.9f));
            var yellow = MaterialLibrary.UnlitColor("paint_yellow", new Color(0.95f, 0.75f, 0.1f));
            var red = MaterialLibrary.UnlitColor("paint_red", new Color(0.85f, 0.12f, 0.1f));
            const float y = 0.012f;
            ProceduralMeshes.Spawn("DonutRing", m, ProceduralMeshes.Ring(site.DonutRadius, 0.25f), white, V(site.DonutCenter, y), Quaternion.identity, false);
            ProceduralMeshes.Spawn("DonutCenter", m, ProceduralMeshes.Ring(0.6f, 0.4f, 24), red, V(site.DonutCenter, y), Quaternion.identity, false);
            ProceduralMeshes.Spawn("Fig8A", m, ProceduralMeshes.Ring(site.FigureEightRadius, 0.25f), yellow, V(site.FigureEightA, y), Quaternion.identity, false);
            ProceduralMeshes.Spawn("Fig8B", m, ProceduralMeshes.Ring(site.FigureEightRadius, 0.25f), yellow, V(site.FigureEightB, y), Quaternion.identity, false);
            for (int i = 0; i < site.SlalomCones; i++)
            {
                var p = site.SlalomStart + site.SlalomDirection * (site.SlalomSpacing * i);
                ProceduralMeshes.Spawn("SlalomDot", m, ProceduralMeshes.Ring(0.5f, 0.3f, 20), white, V(p, y), Quaternion.identity, false);
            }
            // Sandbox outline square.
            float s = site.SandboxRadius;
            var c = V(site.SandboxCenter, y);
            ProceduralMeshes.Spawn("SandN", m, ProceduralMeshes.FlatQuad(s * 2f, 0.25f), white, c + new Vector3(0f, 0f, s), Quaternion.identity, false);
            ProceduralMeshes.Spawn("SandS", m, ProceduralMeshes.FlatQuad(s * 2f, 0.25f), white, c + new Vector3(0f, 0f, -s), Quaternion.identity, false);
            ProceduralMeshes.Spawn("SandE", m, ProceduralMeshes.FlatQuad(0.25f, s * 2f), white, c + new Vector3(s, 0f, 0f), Quaternion.identity, false);
            ProceduralMeshes.Spawn("SandW", m, ProceduralMeshes.FlatQuad(0.25f, s * 2f), white, c + new Vector3(-s, 0f, 0f), Quaternion.identity, false);
            // Exit lane edges.
            var lane = site.ExitLane;
            ProceduralMeshes.Spawn("LaneL", m, ProceduralMeshes.FlatQuad(0.2f, lane.halfExtents.Y * 2f), yellow, V(lane.center + new Vec2(-lane.halfExtents.X, 0f), y), Quaternion.identity, false);
            ProceduralMeshes.Spawn("LaneR", m, ProceduralMeshes.FlatQuad(0.2f, lane.halfExtents.Y * 2f), yellow, V(lane.center + new Vec2(lane.halfExtents.X, 0f), y), Quaternion.identity, false);
        }

        private static Transform BuildDrillMarkers(AcademySite site, Transform root)
        {
            var m = new GameObject("DrillMarkers").transform;
            m.SetParent(root, false);
            var pole = MaterialLibrary.LitColor("pole_red", new Color(0.9f, 0.15f, 0.1f), 0.5f);
            void Pole(string n, Vec2 p) => ProceduralMeshes.Spawn(n, m, ProceduralMeshes.CylinderY(0.06f, 1.8f, 8), pole, V(p), Quaternion.identity);
            Pole("DonutPole", site.DonutCenter);
            Pole("Fig8PoleA", site.FigureEightA);
            Pole("Fig8PoleB", site.FigureEightB);
            return m;
        }

        private static void BuildPerimeter(AcademySite site, Transform root)
        {
            var p = new GameObject("Perimeter").transform;
            p.SetParent(root, false);
            float half = site.lotSize * 0.5f - 1.2f;
            var steel = MaterialLibrary.LitColor("guardrail", new Color(0.72f, 0.74f, 0.76f), 0.6f, 0.8f);
            var rubber = MaterialLibrary.LitColor("tire_rubber", new Color(0.05f, 0.05f, 0.055f), 0.15f);
            var redWhite = MaterialLibrary.LitColor("tire_band", new Color(0.85f, 0.85f, 0.85f), 0.3f);
            var tireMesh = ProceduralMeshes.Tire(0.32f, 0.18f, 0.24f, 12);
            for (int side = 0; side < 4; side++)
            {
                Quaternion rot = Quaternion.Euler(0f, side * 90f, 0f);
                Vector3 n = rot * Vector3.forward;
                Vector3 along = rot * Vector3.right;
                // Rail (static collider) on the lot edge.
                var rail = ProceduralMeshes.Spawn("Rail" + side, p, ProceduralMeshes.Box(new Vector3(site.lotSize, 0.35f, 0.08f)), steel, n * (half + 0.6f) + Vector3.up * 0.55f, rot);
                var rc = rail.AddComponent<BoxCollider>();
                rc.center = new Vector3(0f, -0.4f, 0f);
                rc.size = new Vector3(site.lotSize, 1.5f, 0.6f);
                // Tire wall in front of the rail (one combined mesh per side + one collider).
                var combine = new List<CombineInstance>();
                for (float x = -half; x <= half; x += 0.66f)
                for (int k = 0; k < 2; k++)
                    combine.Add(new CombineInstance { mesh = tireMesh, transform = Matrix4x4.TRS(n * half + along * x + Vector3.up * (0.12f + k * 0.25f), rot * Quaternion.Euler(0f, 0f, 90f), Vector3.one) });
                var wallGo = SpawnCombined("TireWall" + side, p, combine, rubber);
                var wc = wallGo.AddComponent<BoxCollider>();
                wc.center = n * half + Vector3.up * 0.3f;
                wc.size = side % 2 == 0 ? new Vector3(site.lotSize, 0.6f, 0.66f) : new Vector3(0.66f, 0.6f, site.lotSize);
                for (float x = -half; x <= half; x += 4f)
                    ProceduralMeshes.Spawn("Post", p, ProceduralMeshes.CylinderY(0.05f, 0.75f, 6), steel, n * (half + 0.65f) + along * x, Quaternion.identity);
                _ = redWhite;
            }
        }

        private static void BuildScenery(AcademySite site, Transform root, bool highQuality)
        {
            var s = new GameObject("Scenery").transform;
            s.SetParent(root, false);
            float far = site.lotSize * 0.5f + 25f;
            var colors = new[] { new Color(0.75f, 0.2f, 0.1f), new Color(0.1f, 0.35f, 0.6f), new Color(0.2f, 0.5f, 0.3f), new Color(0.8f, 0.65f, 0.2f), new Color(0.5f, 0.5f, 0.52f) };
            var rng = new System.Random(42);
            for (int i = 0; i < 18; i++)
            {
                var mat = MaterialLibrary.LitColor("container_" + (i % colors.Length), colors[i % colors.Length], 0.35f, 0.4f);
                float a = i / 18f * Mathf.PI * 2f;
                Vector3 pos = new Vector3(Mathf.Sin(a) * far * 1.15f, 0f, Mathf.Cos(a) * far * 1.15f);
                int stack = 1 + rng.Next(3);
                for (int k = 0; k < stack; k++)
                    ProceduralMeshes.Spawn("Container", s, ProceduralMeshes.Box(new Vector3(2.4f, 2.6f, 6f)), mat, pos + Vector3.up * (k * 2.6f), Quaternion.Euler(0f, a * Mathf.Rad2Deg + 90f, 0f));
            }
            var lampPole = MaterialLibrary.LitColor("lamp_pole", new Color(0.3f, 0.3f, 0.32f), 0.4f, 0.6f);
            var lampHead = MaterialLibrary.Emissive("lamp_head", new Color(1f, 0.95f, 0.85f), 2f);
            float lh = site.lotSize * 0.5f + 3f;
            for (int i = -2; i <= 2; i++)
            for (int sideIdx = 0; sideIdx < 2; sideIdx++)
            {
                Vector3 pos = new Vector3(i * site.lotSize * 0.22f, 0f, sideIdx == 0 ? lh : -lh * 0.2f + (sideIdx == 1 ? 0 : 0));
                if (sideIdx == 1) pos = new Vector3(sideIdx == 1 ? (i < 0 ? -lh : lh) : 0f, 0f, i * site.lotSize * 0.2f);
                ProceduralMeshes.Spawn("LampPole", s, ProceduralMeshes.CylinderY(0.15f, 12f, 8), lampPole, pos, Quaternion.identity);
                ProceduralMeshes.Spawn("LampHead", s, ProceduralMeshes.Box(new Vector3(1.4f, 0.25f, 0.6f)), lampHead, pos + Vector3.up * 12f, Quaternion.identity, false);
            }
            // Control tower next to the hangar.
            var tower = MaterialLibrary.LitColor("tower", new Color(0.82f, 0.82f, 0.8f), 0.3f);
            Vector3 tp = V(site.HangarCenter + new Vec2(site.hangarSize.X * 0.5f + 12f, 0f));
            ProceduralMeshes.SpawnBox("Tower", s, tp + new Vector3(0f, 6f, 0f), new Vector3(6f, 12f, 6f), tower);
            ProceduralMeshes.Spawn("TowerGlass", s, ProceduralMeshes.Box(new Vector3(7f, 2.5f, 7f)), MaterialLibrary.LitColor("car_glass", new Color(0.05f, 0.07f, 0.09f), 0.95f, 0.2f), tp + new Vector3(0f, 12f, 0f), Quaternion.identity);
            if (!highQuality) return;
            // Distant low-poly hills for a horizon.
            var hill = MaterialLibrary.LitColor("hill", new Color(0.32f, 0.4f, 0.3f), 0.05f);
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f + 0.3f;
                float dist = 650f + (float)rng.NextDouble() * 150f;
                float hh = 50f + (float)rng.NextDouble() * 90f;
                ProceduralMeshes.Spawn("Hill", s, ProceduralMeshes.Cone(160f + (float)rng.NextDouble() * 100f, hh, 7), hill, new Vector3(Mathf.Sin(a) * dist, -2f, Mathf.Cos(a) * dist), Quaternion.Euler(0f, i * 37f, 0f), false);
            }
        }

        private static GameObject SpawnCombined(string name, Transform parent, List<CombineInstance> parts, Material mat)
        {
            var mesh = new Mesh { name = "DC_" + name, indexFormat = IndexFormat.UInt32 };
            mesh.CombineMeshes(parts.ToArray(), true, true);
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterial = mat;
            go.isStatic = true;
            return go;
        }
    }
}
