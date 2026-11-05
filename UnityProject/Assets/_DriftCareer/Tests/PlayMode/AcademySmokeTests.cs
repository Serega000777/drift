using System.Collections;
using DriftCareer.Core.Assists;
using DriftCareer.Core.Catalog;
using DriftCareer.Core.Layout;
using DriftCareer.Runtime;
using DriftCareer.Runtime.Track;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace DriftCareer.Tests.PlayMode
{
    /// <summary>
    /// Smoke tests in a real Unity physics scene (run from Window > General > Test Runner > PlayMode).
    /// They cover acceptance tests 1, 2, 4, 6, 12 and 13 from docs/TEST_PLAN.md.
    /// </summary>
    public class AcademySmokeTests
    {
        private TrackSession _session;
        private GameServices _services;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            Time.fixedDeltaTime = 0.01f;
            _services = GameServices.Create();
            _session = TrackSession.BuildAcademy(_services, 0.4f);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_session != null) _session.Unload();
            yield return null;
        }

        [UnityTest]
        public IEnumerator CarSpawnsInHangar_SettlesOnFloor_WithoutExplosion()
        {
            var car = _session.Car;
            car.SetFrozen(false);
            yield return new WaitForSeconds(2f);
            Assert.IsTrue(car.Telemetry.IsFinite);
            Assert.IsTrue(car.Telemetry.anyWheelGrounded, "wheels must touch the hangar floor");
            float y = car.transform.position.y;
            Assert.That(y, Is.InRange(-0.05f, 0.25f), "car rests on the floor (root is at ground level)");
            Assert.Less(car.Velocity.magnitude, 0.3f, "parked car stays still");
            Assert.IsTrue(_session.Site.HangarZone.Contains(new Core.Vec2(car.transform.position.x, car.transform.position.z)));
        }

        [UnityTest]
        public IEnumerator Throttle_DrivesCarForward_AndTelemetryStaysFinite()
        {
            var car = _session.Car;
            car.SetFrozen(false);
            car.InputEnabled = true;
            float startZ = car.transform.position.z;
            float t = 0f;
            while (t < 4f)
            {
                car.Input = new DriverInput { throttle = 0.6f };
                t += Time.deltaTime;
                yield return null;
                Assert.IsTrue(car.Telemetry.IsFinite);
            }
            Assert.Greater(car.transform.position.z - startZ, 5f, "car moved toward the hangar door");
            Assert.AreEqual(0, car.Telemetry.nonFiniteRecoveries);
        }

        [UnityTest]
        public IEnumerator Layout_SaveAndReload_RoundTrips()
        {
            var ed = _session.Layout;
            ed.ApplyPreset(LayoutPresets.Create("slalom", _services.Site));
            ed.Layout.name = "smoke_test_layout";
            _services.SaveLayout(ed.Layout);
            var back = _services.LoadLayoutFile(GameServices.LayoutsDir + "/smoke_test_layout.json");
            Assert.AreEqual(ed.Count, back.placements.Count);
            _services.Files.Delete(GameServices.LayoutsDir + "/smoke_test_layout.json");
            yield return null;
            Assert.AreEqual(ed.Count, _session.Obstacles.LiveCount, "scene obstacles mirror the layout");
        }

        [Test]
        public void ComingSoonContent_CannotLaunch()
        {
            foreach (var t in _services.Catalog.Tracks)
                if (!t.IsPlayable) Assert.AreNotEqual(LaunchBlockReason.None, _services.Catalog.CanLaunch(t.id, "free_drive"), t.id);
            Assert.AreEqual(LaunchBlockReason.None, _services.Catalog.CanLaunch("academy", "free_drive"));
        }
    }
}
