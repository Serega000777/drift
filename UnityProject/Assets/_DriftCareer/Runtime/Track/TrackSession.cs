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
            if (repair) Deformer.Repair();
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
            var scene = SceneManager.CreateScene("Track_academy_" + DateTime.UtcNow.Ticks);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            var root = new GameObject("TrackSession");
            var session = root.AddComponent<TrackSession>();
            session.Scene = scene;
            session.TrackId = "academy";
            session.Site = services.Site;
            bool hq = qualityScale >= 0.6f;
            session.World = AcademyBuilder.Build(services.Site, root.transform, hq);
            Skidmarks.Create(root.transform);

            // Obstacles + layout model.
            session.Obstacles = ObstacleField.Create(root.transform, services.Obstacles);
            var rules = services.Site.CreateRules(services.Defaults.obstacleBudget);
            session.Layout = new LayoutEditorModel(services.Obstacles, rules, services.LoadLastLayoutOrDefault());
            session.Layout.Changed += () => session.Obstacles.Sync(session.Layout.Layout);
            session.Obstacles.Sync(session.Layout.Layout);

            // Car.
            var carGo = new GameObject("PlayerCar");
            carGo.transform.SetParent(root.transform, false);
            carGo.layer = 2; // Ignore Raycast: wheel/camera rays never hit the car itself.
            carGo.AddComponent<Rigidbody>();
            var car = carGo.AddComponent<VehicleController>();
            var tuning = services.Profile.TuningFor(services.Profile.selectedVehicleId);
            var profile = TuningApplier.Apply(services.BaseProfile, tuning);
            car.Initialize(profile, services.Settings.health, services.AssistSettings(), services.Surfaces);
            var rig = CarRig.BuildPlaceholder(carGo.transform, profile, tuning.visual);
            rig.Bind(car);
            AddBodyColliders(carGo, profile);
            SetLayerRecursive(carGo, 2);
            car.ApplyProfile(profile); // re-apply mass/inertia after colliders exist
            car.ResetTo(session.World.spawnPosition, session.World.spawnRotation, true);
            session.Car = car;
            session.Rig = rig;
            session.Effects = carGo.AddComponent<VehicleEffects>();
            session.Effects.Initialize(car, rig, qualityScale);
            session.Deformer = carGo.AddComponent<DamageDeformer>();
            session.Deformer.Initialize(car, rig.bodyRoot);
            carGo.AddComponent<AudioSource>();
            session.Audio = carGo.AddComponent<VehicleAudio>();
            var s = services.Settings;
            session.Audio.Initialize(car, s.masterVolume, s.engineVolume, s.effectsVolume);

            session.Drift = new DriftSession(services.Site);
            session.Drift.Bind(car, session.Obstacles);
            if (previous.IsValid() && previous != scene) SceneManager.SetActiveScene(scene);
            return session;
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
