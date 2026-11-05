using System;
using System.Collections.Generic;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Layout;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Audio;
using DriftCareer.Runtime.Gameplay;
using DriftCareer.Runtime.LayoutEditor;
using DriftCareer.Runtime.Vehicle;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftCareer.Runtime.Track
{
    /// <summary>Everything that exists while a track is loaded: world, car, obstacles, gameplay session.</summary>
    public sealed class TrackSession : MonoBehaviour
    {
        public string TrackId { get; private set; }
        public AcademySite Site { get; private set; }
        public AcademyBuilder.Result World { get; private set; }
        public ObstacleField Obstacles { get; private set; }
        public LayoutEditorModel Layout { get; private set; }
        public VehicleController Car { get; private set; }
        public CarRig Rig { get; private set; }
        public VehicleEffects Effects { get; private set; }
        public VehicleAudio Audio { get; private set; }
        public DamageDeformer Deformer { get; private set; }
        public DriftSession Drift { get; private set; }
        public Scene Scene { get; private set; }

        public void ResetCar(bool repair)
        {
            Car.ResetTo(World.spawnPosition, World.spawnRotation, repair);
            if (repair)
            {
                Deformer.Repair();
                ApplyUsedTires(Car, GameServices.Instance.Profile.TuningFor(GameServices.Instance.Profile.selectedVehicleId));
            }
            Drift.ResetRun();
        }

        /// <summary>Applies a tuned profile and visuals to the live car (garage try-on / test drive).</summary>
        public void ApplyTuning(VehiclePhysicsProfile baseProfile, VehicleTuningState tuning)
        {
            var tuned = TuningApplier.Apply(baseProfile, tuning);
            Car.ApplyProfile(tuned);
            Rig.ApplyVisual(tuning.visual, tuned);
            Rig.RebuildWheels(tuned, tuning.visual);
        }

        public static TrackSession BuildAcademy(GameServices services, float qualityScale)
        {
            TrackSession session = null;
            var routine = BuildAcademyRoutine(services, qualityScale, s => session = s, null);
            while (routine.MoveNext()) { }
            return session;
        }

        /// <summary>Stage-by-stage build (yields between stages) for the loading screen.</summary>
        public static System.Collections.IEnumerator BuildAcademyRoutine(GameServices services, float qualityScale, Action<TrackSession> done, Action<float, string> progress)
        {
            var scene = SceneManager.CreateScene("Track_academy_" + DateTime.UtcNow.Ticks);
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("TrackSession");
            var session = root.AddComponent<TrackSession>();
            session.Scene = scene;
            session.TrackId = "academy";
            session.Site = services.Site;
            bool hq = qualityScale >= 0.6f;
            var world = new AcademyBuilder.Result();
            session.World = world;
            var build = AcademyBuilder.BuildRoutine(services.Site, root.transform, hq, world, (p, key) => progress?.Invoke(p * 0.8f, key));
            while (build.MoveNext()) yield return build.Current;
            Skidmarks.Create(root.transform);

            progress?.Invoke(0.84f, "loading.stage.obstacles");
            session.Obstacles = ObstacleField.Create(root.transform, services.Obstacles);
            var rules = services.Site.CreateRules(services.Defaults.obstacleBudget);
            session.Layout = new LayoutEditorModel(services.Obstacles, rules, services.LoadLastLayoutOrDefault());
            session.Layout.Changed += () => session.Obstacles.Sync(session.Layout.Layout);
            session.Obstacles.Sync(session.Layout.Layout);
            yield return null;

            progress?.Invoke(0.92f, "loading.stage.car");
            session.BuildCar(services, qualityScale);
            session.Drift = new DriftSession(services.Site);
            session.Drift.Bind(session.Car, session.Obstacles);
            yield return null;
            done?.Invoke(session);
        }

        /// <summary>(Re)creates the player car for the selected vehicle (garage car switch).</summary>
        public void BuildCar(GameServices services, float qualityScale)
        {
            Vector3 pos = World.spawnPosition;
            Quaternion rot = World.spawnRotation;
            if (Car != null)
            {
                pos = Car.transform.position;
                rot = Car.transform.rotation;
                Destroy(Car.gameObject);
            }
            var carGo = new GameObject("PlayerCar");
            carGo.transform.SetParent(transform, false);
            carGo.AddComponent<Rigidbody>();
            var car = carGo.AddComponent<VehicleController>();
            var tuning = services.Profile.TuningFor(services.Profile.selectedVehicleId);
            var profile = TuningApplier.Apply(services.BaseProfile, tuning);
            car.Initialize(profile, services.Settings.health, services.AssistSettings(), services.Surfaces);
            string style = services.BodyStyleFor(services.Profile.selectedVehicleId);
            CarRig rig = null;
            if (style.StartsWith("model:"))
            {
                string id = style.Substring(6);
                var prefab = Resources.Load<GameObject>(GameServices.UserCarsResources + "/" + id);
                if (prefab != null && services.UserCars.TryGetValue("user:" + id, out var manifest))
                    rig = CarRig.BuildFromModel(carGo.transform, prefab, manifest.analysis, profile);
            }
            if (rig == null) rig = CarRig.BuildPlaceholder(carGo.transform, profile, tuning.visual, style == "sedan" ? "sedan" : "coupe");
            rig.Bind(car);
            AddBodyColliders(carGo, profile);
            SetLayerRecursive(carGo, 2); // Ignore Raycast: wheel/camera rays never hit the car itself.
            car.ApplyProfile(profile); // re-apply mass/inertia after colliders exist
            car.ResetTo(pos, rot, true);
            ApplyUsedTires(car, tuning);
            Car = car;
            Rig = rig;
            Effects = carGo.AddComponent<VehicleEffects>();
            Effects.Initialize(car, rig, qualityScale * services.Settings.particleDensity);
            Deformer = carGo.AddComponent<DamageDeformer>();
            Deformer.Initialize(car, rig.bodyRoot);
            carGo.AddComponent<AudioSource>();
            Audio = carGo.AddComponent<VehicleAudio>();
            var s = services.Settings;
            Audio.Initialize(car, s.masterVolume, s.engineVolume, s.effectsVolume);
            Drift?.Bind(car, Obstacles);
        }

        private static void ApplyUsedTires(VehicleController car, VehicleTuningState t)
        {
            for (int i = 0; i < 4; i++) car.Health.tires[i].wear = i >= 2 ? t.usedTireWear : t.usedTireWear * 0.5f;
        }

        private static void AddBodyColliders(GameObject car, VehiclePhysicsProfile p)
        {
#if UNITY_6000_0_OR_NEWER
            var mat = new PhysicsMaterial("DC_CarBody") { dynamicFriction = 0.2f, staticFriction = 0.25f, bounciness = 0.05f, frictionCombine = PhysicsMaterialCombine.Minimum };
#else
            var mat = new PhysicMaterial("DC_CarBody") { dynamicFriction = 0.2f, staticFriction = 0.25f, bounciness = 0.05f, frictionCombine = PhysicMaterialCombine.Minimum };
#endif
            var body = car.AddComponent<BoxCollider>();
            body.center = new Vector3(0f, 0.52f, 0f);
            body.size = new Vector3(p.chassis.width, 0.62f, p.chassis.length);
            body.sharedMaterial = mat;
            var cabin = car.AddComponent<BoxCollider>();
            cabin.center = new Vector3(0f, 1.05f, -0.45f * p.chassis.length / 4.4f);
            cabin.size = new Vector3(p.chassis.width * 0.78f, 0.45f, 1.9f);
            cabin.sharedMaterial = mat;
        }

        public static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform c in go.transform) SetLayerRecursive(c.gameObject, layer);
        }

        public void Unload()
        {
            if (Scene.IsValid() && Scene.isLoaded) SceneManager.UnloadSceneAsync(Scene);
        }
    }
}
