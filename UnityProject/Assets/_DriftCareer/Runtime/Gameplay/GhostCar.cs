using DriftCareer.Core.Gameplay;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.Gameplay
{
    /// <summary>Translucent, collision-free car following a recorded run (ghost tandem lead).</summary>
    public sealed class GhostCar : MonoBehaviour
    {
        private GameObject _body;

        public static GhostCar Create(Transform parent)
        {
            var go = new GameObject("GhostCar");
            go.transform.SetParent(parent, false);
            var g = go.AddComponent<GhostCar>();
            var mat = MaterialLibrary.Transparent("ghost_car", new Color(0.3f, 0.85f, 1f, 0.35f));
            g._body = new GameObject("Body");
            g._body.transform.SetParent(go.transform, false);
            var lower = Vehicle.CarRig.Extrude(new[]
            {
                new Vector2(2.2f, 0.2f), new Vector2(2.24f, 0.52f), new Vector2(2.05f, 0.7f), new Vector2(0.85f, 0.83f),
                new Vector2(-1.65f, 0.87f), new Vector2(-2.17f, 0.82f), new Vector2(-2.26f, 0.5f), new Vector2(-2.2f, 0.2f)
            }, 1.72f, 1f);
            ProceduralMeshes.Spawn("Lower", g._body.transform, lower, mat, Vector3.zero, Quaternion.identity, false);
            var cabin = Vehicle.CarRig.Extrude(new[] { new Vector2(0.85f, 0.82f), new Vector2(-0.1f, 1.27f), new Vector2(-1.05f, 1.29f), new Vector2(-1.75f, 0.87f) }, 1.38f, 1f);
            ProceduralMeshes.Spawn("Cabin", g._body.transform, cabin, mat, Vector3.zero, Quaternion.identity, false);
            go.SetActive(false);
            return g;
        }

        public void Show(GhostRecording rec)
        {
            gameObject.SetActive(true);
            if (rec != null && rec.frames.Count > 0) SetPose(rec.frames[0]);
        }

        public void Hide() => gameObject.SetActive(false);

        public void SetPose(GhostFrame f)
        {
            transform.SetPositionAndRotation(new Vector3(f.x, f.y, f.z), Quaternion.Euler(0f, f.yawDeg, 0f));
        }
    }
}
