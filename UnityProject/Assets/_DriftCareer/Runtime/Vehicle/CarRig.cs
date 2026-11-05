using System.Collections.Generic;
using DriftCareer.Core.Garage;
using DriftCareer.Core.Vehicle;
using DriftCareer.Runtime.Track;
using UnityEngine;

namespace DriftCareer.Runtime.Vehicle
{
    /// <summary>
    /// Visual rig of a car: body, parts, wheels, door, cockpit anchors and lights.
    /// Built procedurally for the placeholder car; a custom FBX/GLB car fills the same slots via the import tool.
    /// Visuals follow physics, never the reverse.
    /// </summary>
    public sealed class CarRig : MonoBehaviour
    {
        public Transform bodyRoot;
        public Transform partsRoot;
        public Transform doorPivot;
        public Transform cockpitAnchor;
        public Transform hoodAnchor;
        public Transform bumperAnchor;
        public Transform steeringWheel;
        public Transform[] wheelRoots = new Transform[4];
        public Transform[] exhausts = new Transform[0];
        public Renderer[] brakeLights = new Renderer[0];
        public Renderer[] paintedRenderers = new Renderer[0];
        public Material paintMaterial;
        public Material secondaryMaterial;
        public float doorOpenAngle = -62f;
        public bool proceduralBody = true;

        private VehicleController _vehicle;
        private readonly List<GameObject> _wheelVisuals = new List<GameObject>();
        private Material _brakeOff, _brakeOn;
        private float _wheelSpin;
        private readonly float[] _spin = new float[4];

        public VehicleController Vehicle => _vehicle;

        public void Bind(VehicleController vehicle) => _vehicle = vehicle;

        public void SetDoorOpen(float t01)
        {
            if (doorPivot != null) doorPivot.localRotation = Quaternion.Euler(0f, Mathf.Lerp(0f, doorOpenAngle, Mathf.SmoothStep(0f, 1f, t01)), 0f);
        }

        private void LateUpdate()
        {
            if (_vehicle == null || _vehicle.Sim == null) return;
            var sim = _vehicle.Sim;
            for (int i = 0; i < 4; i++)
            {
                var root = wheelRoots[i];
                if (root == null) continue;
                root.localPosition = _vehicle.WheelCenterLocal(i);
                var w = sim.Wheel(i);
                _spin[i] = Mathf.Repeat(_spin[i] + w.angularVelocity * Mathf.Rad2Deg * Time.deltaTime, 360f);
                float steerDeg = sim.WheelSteerAngle(i) * Mathf.Rad2Deg;
                root.localRotation = Quaternion.Euler(0f, steerDeg, 0f);
                if (root.childCount > 0)
                {
                    var spinner = root.GetChild(0);
                    spinner.localRotation = Quaternion.Euler(_spin[i], 0f, 0f);
                    // Deflated tire: squash the visual so the rim sits on the ground.
                    float deflation = _vehicle.Health.tires[i].Deflation;
                    spinner.localScale = new Vector3(1f, 1f - 0.18f * deflation, 1f - 0.18f * deflation);
                }
            }
            if (steeringWheel != null)
                steeringWheel.localRotation = Quaternion.Euler(18f, 0f, -sim.CenterSteerAngleRad * Mathf.Rad2Deg * 9f);
            bool braking = _vehicle.Telemetry.brake > 0.05f || _vehicle.Telemetry.handbrake > 0.5f;
            if (_brakeOn != null)
                foreach (var r in brakeLights) if (r != null) r.sharedMaterial = braking ? _brakeOn : _brakeOff;
        }

        // ------------------------------------------------------------------ builder

        /// <summary>Builds the procedural placeholder coupe for a physics profile and visual setup.</summary>
        public static CarRig BuildPlaceholder(Transform root, VehiclePhysicsProfile profile, VisualSetup visual, string bodyStyle = "coupe")
        {
            var rig = root.gameObject.AddComponent<CarRig>();
            rig.bodyRoot = new GameObject("Body").transform;
            rig.bodyRoot.SetParent(root, false);
            rig._sedan = bodyStyle == "sedan";
            rig.BuildBody(profile);
            rig.BuildWheels(profile, visual);
            rig.ApplyVisual(visual, profile);
            return rig;
        }

        private static readonly Vector2[] LowerProfile =
        {
            new Vector2(2.2f, 0.2f), new Vector2(2.24f, 0.52f), new Vector2(2.05f, 0.7f), new Vector2(0.85f, 0.83f),
            new Vector2(-1.65f, 0.87f), new Vector2(-2.17f, 0.82f), new Vector2(-2.26f, 0.5f), new Vector2(-2.2f, 0.2f),
        };

        // Boxy three-box classic sedan silhouette.
        private static readonly Vector2[] SedanLowerProfile =
        {
            new Vector2(2.05f, 0.22f), new Vector2(2.1f, 0.58f), new Vector2(2.0f, 0.78f), new Vector2(0.75f, 0.84f),
            new Vector2(-1.35f, 0.86f), new Vector2(-2.0f, 0.86f), new Vector2(-2.08f, 0.6f), new Vector2(-2.02f, 0.22f),
        };

        private static readonly Vector2[] SedanCabinProfile =
        {
            new Vector2(0.75f, 0.83f), new Vector2(0.15f, 1.38f), new Vector2(-0.95f, 1.4f), new Vector2(-1.4f, 0.85f),
        };

        private bool _sedan;

        private static readonly Vector2[] CabinProfile =
        {
            new Vector2(0.85f, 0.82f), new Vector2(-0.1f, 1.27f), new Vector2(-1.05f, 1.29f), new Vector2(-1.75f, 0.87f),
        };

        private void BuildBody(VehiclePhysicsProfile p)
        {
            float lengthScale = p.chassis.length / 4.4f;
            float width = p.chassis.width;
            paintMaterial = MaterialLibrary.CarPaint("#C8102E", PaintFinish.Metallic);
            secondaryMaterial = MaterialLibrary.CarPaint("#111111", PaintFinish.Gloss);
            var glass = MaterialLibrary.LitColor("car_glass", new Color(0.05f, 0.07f, 0.09f, 1f), 0.95f, 0.2f);
            var trim = MaterialLibrary.LitColor("car_trim", new Color(0.06f, 0.06f, 0.065f), 0.35f);

            float bodyLengthScale = _sedan ? p.chassis.length / 4.1f : lengthScale;
            var lower = ProceduralMeshes.Spawn("Lower", bodyRoot, Extrude(_sedan ? SedanLowerProfile : LowerProfile, width, bodyLengthScale), paintMaterial, Vector3.zero, Quaternion.identity);
            var cabin = ProceduralMeshes.Spawn("Cabin", bodyRoot, Extrude(_sedan ? SedanCabinProfile : CabinProfile, width * (_sedan ? 0.86f : 0.8f), bodyLengthScale, true), glass, Vector3.zero, Quaternion.identity);
            var roof = ProceduralMeshes.Spawn("Roof", bodyRoot, ProceduralMeshes.Box(new Vector3(width * (_sedan ? 0.82f : 0.74f), 0.03f, (_sedan ? 1.05f : 0.95f) * bodyLengthScale)), paintMaterial,
                new Vector3(0f, _sedan ? 1.38f : 1.27f, (_sedan ? -0.4f : -0.57f) * bodyLengthScale), Quaternion.identity);
            _ = cabin;
            paintedRenderers = new[] { lower.GetComponent<Renderer>(), roof.GetComponent<Renderer>() };

            // Door on the driver side (left-hand drive) with a pivot at its front edge.
            doorPivot = new GameObject("DoorPivot").transform;
            doorPivot.SetParent(bodyRoot, false);
            doorPivot.localPosition = new Vector3(-width * 0.5f - 0.005f, 0f, 0.55f * lengthScale);
            var door = ProceduralMeshes.Spawn("Door", doorPivot, ProceduralMeshes.Box(new Vector3(0.04f, 0.55f, 1.15f * lengthScale)), paintMaterial,
                new Vector3(0f, 0.3f, -0.58f * lengthScale), Quaternion.identity);
            var doorGlass = ProceduralMeshes.Spawn("DoorGlass", doorPivot, ProceduralMeshes.Box(new Vector3(0.02f, 0.36f, 0.9f * lengthScale)), glass,
                new Vector3(0.12f, 0.86f, -0.6f * lengthScale), Quaternion.Euler(0f, 0f, -12f));
            paintedRenderers = new[] { paintedRenderers[0], paintedRenderers[1], door.GetComponent<Renderer>() };
            _ = doorGlass;

            // Interior: seat, dashboard, steering wheel and the cockpit camera anchor.
            var seatMat = MaterialLibrary.LitColor("car_seat", new Color(0.1f, 0.1f, 0.11f), 0.2f);
            ProceduralMeshes.Spawn("Seat", bodyRoot, ProceduralMeshes.Box(new Vector3(0.5f, 0.45f, 0.55f)), seatMat, new Vector3(-0.37f, 0.35f, -0.45f), Quaternion.identity);
            ProceduralMeshes.Spawn("SeatBack", bodyRoot, ProceduralMeshes.Box(new Vector3(0.5f, 0.65f, 0.12f)), seatMat, new Vector3(-0.37f, 0.6f, -0.78f), Quaternion.Euler(-12f, 0f, 0f));
            ProceduralMeshes.Spawn("Dash", bodyRoot, ProceduralMeshes.Box(new Vector3(width * 0.78f, 0.22f, 0.35f)), trim, new Vector3(0f, 0.72f, 0.55f), Quaternion.identity);
            steeringWheel = new GameObject("SteeringWheel").transform;
            steeringWheel.SetParent(bodyRoot, false);
            steeringWheel.localPosition = new Vector3(-0.37f, 0.92f, 0.32f);
            var rimMesh = ProceduralMeshes.Tire(0.18f, 0.16f, 0.03f, 20);
            ProceduralMeshes.Spawn("Rim", steeringWheel, rimMesh, trim, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), false);
            cockpitAnchor = new GameObject("CockpitAnchor").transform;
            cockpitAnchor.SetParent(bodyRoot, false);
            cockpitAnchor.localPosition = new Vector3(-0.37f, 1.08f, -0.42f);
            hoodAnchor = new GameObject("HoodAnchor").transform;
            hoodAnchor.SetParent(bodyRoot, false);
            hoodAnchor.localPosition = new Vector3(0f, 1.0f, 0.45f);
            bumperAnchor = new GameObject("BumperAnchor").transform;
            bumperAnchor.SetParent(bodyRoot, false);
            bumperAnchor.localPosition = new Vector3(0f, 0.55f, 2.3f * lengthScale);

            // Lights.
            var head = MaterialLibrary.Emissive("car_headlight", new Color(1f, 0.97f, 0.9f), 2.5f);
            _brakeOff = MaterialLibrary.Emissive("car_tail", new Color(0.5f, 0.02f, 0.02f), 0.8f);
            _brakeOn = MaterialLibrary.Emissive("car_brake", new Color(1f, 0.05f, 0.03f), 6f);
            for (int s = -1; s <= 1; s += 2)
            {
                ProceduralMeshes.Spawn("Headlight", bodyRoot, ProceduralMeshes.Box(new Vector3(0.42f, 0.09f, 0.05f)), head,
                    new Vector3(s * width * 0.33f, 0.6f, 2.21f * lengthScale), Quaternion.identity, false);
            }
            var lights = new List<Renderer>();
            for (int s = -1; s <= 1; s += 2)
                lights.Add(ProceduralMeshes.Spawn("Taillight", bodyRoot, ProceduralMeshes.Box(new Vector3(0.38f, 0.1f, 0.05f)), _brakeOff,
                    new Vector3(s * width * 0.34f, 0.7f, -2.2f * lengthScale), Quaternion.identity, false).GetComponent<Renderer>());
            brakeLights = lights.ToArray();
            exhausts = new Transform[1];
            exhausts[0] = new GameObject("Exhaust").transform;
            exhausts[0].SetParent(bodyRoot, false);
            exhausts[0].localPosition = new Vector3(width * 0.3f, 0.25f, -2.25f * lengthScale);
            ProceduralMeshes.Spawn("Pipe", exhausts[0], ProceduralMeshes.Tire(0.045f, 0.035f, 0.14f, 12), trim, Vector3.zero, Quaternion.Euler(0f, 90f, 0f), false);

            partsRoot = new GameObject("Parts").transform;
            partsRoot.SetParent(bodyRoot, false);
        }

        private void BuildWheels(VehiclePhysicsProfile p, VisualSetup visual)
        {
            for (int i = 0; i < 4; i++)
            {
                var name = ((WheelId)i).ToString();
                var root = new GameObject("Wheel_" + name).transform;
                root.SetParent(transform, false);
                wheelRoots[i] = root;
                var spinner = new GameObject("Spinner").transform;
                spinner.SetParent(root, false);
            }
            RebuildWheels(p, visual);
        }

        /// <summary>Rebuilds tires and rims (rim style/size/colors) - used by the garage try-on.</summary>
        public void RebuildWheels(VehiclePhysicsProfile p, VisualSetup visual)
        {
            if (!proceduralBody) return; // model cars keep their own wheel meshes
            foreach (var go in _wheelVisuals) if (go != null) Destroy(go);
            _wheelVisuals.Clear();
            var rubber = MaterialLibrary.LitColor("tire_rubber", new Color(0.05f, 0.05f, 0.055f), 0.15f);
            var rimMat = MaterialLibrary.CarPaint(visual.rimColor, PaintFinish.Metallic);
            var caliper = MaterialLibrary.CarPaint(visual.caliperColor, PaintFinish.Gloss);
            var rimPart = VisualPartCatalog.Find(visual.PartFor(PartSlot.Rims));
            int style = rimPart != null ? rimPart.style : 0;
            for (int i = 0; i < 4; i++)
            {
                var axle = VehicleSimulation.IsFront(i) ? p.front : p.rear;
                float outer = axle.wheelRadius;
                float rimR = Mathf.Min(visual.rimSizeInch * 0.0254f * 0.5f, outer - 0.05f);
                float width = 0.225f;
                bool left = VehicleSimulation.IsLeft(i);
                var spinner = wheelRoots[i].GetChild(0);
                var tire = ProceduralMeshes.Spawn("Tire", spinner, ProceduralMeshes.Tire(outer, rimR, width), rubber, Vector3.zero, Quaternion.identity);
                var rim = ProceduralMeshes.Spawn("Rim", spinner, ProceduralMeshes.Rim(rimR, width * 0.9f, style), rimMat,
                    Vector3.zero, left ? Quaternion.Euler(0f, 180f, 0f) : Quaternion.identity);
                var cal = ProceduralMeshes.Spawn("Caliper", wheelRoots[i], ProceduralMeshes.Box(new Vector3(0.06f, 0.14f, 0.2f)), caliper,
                    new Vector3(left ? 0.06f : -0.06f, rimR * 0.45f, 0f), Quaternion.identity, false);
                _wheelVisuals.Add(tire); _wheelVisuals.Add(rim); _wheelVisuals.Add(cal);
                wheelRoots[i].localPosition = new Vector3((left ? -0.5f : 0.5f) * axle.trackWidth, outer, (VehicleSimulation.IsFront(i) ? 0.5f : -0.5f) * p.chassis.wheelbase);
            }
        }

        /// <summary>Applies paint and body-kit parts. Cheap enough to call on every garage change (live try-on).</summary>
        public void ApplyVisual(VisualSetup v, VehiclePhysicsProfile p)
        {
            v = VisualPartCatalog.Sanitize(v);
            if (!proceduralBody) return; // model cars keep their own materials in MVP
            var paint = MaterialLibrary.CarPaint(v.paintColor, v.paintFinish);
            var secondary = MaterialLibrary.CarPaint(v.secondaryColor, PaintFinish.Gloss);
            foreach (var r in paintedRenderers) if (r != null) r.sharedMaterial = paint;
            if (paintMaterial != null) Destroy(paintMaterial);
            if (secondaryMaterial != null) Destroy(secondaryMaterial);
            paintMaterial = paint;
            secondaryMaterial = secondary;
            if (!proceduralBody || partsRoot == null) return;

            for (int i = partsRoot.childCount - 1; i >= 0; i--) Destroy(partsRoot.GetChild(i).gameObject);
            float ls = p.chassis.length / 4.4f, w = p.chassis.width;
            var trim = MaterialLibrary.LitColor("car_trim", new Color(0.06f, 0.06f, 0.065f), 0.35f);
            var carbon = MaterialLibrary.LitColor("car_carbon", new Color(0.09f, 0.09f, 0.1f), 0.7f, 0.3f);

            int Style(PartSlot s) => VisualPartCatalog.Find(v.PartFor(s))?.style ?? 0;
            Transform P(string n, Vector3 size, Material m, Vector3 pos, Vector3 euler = default)
                => ProceduralMeshes.Spawn(n, partsRoot, ProceduralMeshes.Box(size), m, pos, Quaternion.Euler(euler)).transform;

            switch (Style(PartSlot.FrontBumper))
            {
                case 1: P("LipStreet", new Vector3(w * 0.95f, 0.05f, 0.25f), trim, new Vector3(0f, 0.15f, 2.15f * ls)); break;
                case 2: P("Splitter", new Vector3(w * 1.02f, 0.025f, 0.38f), carbon, new Vector3(0f, 0.13f, 2.25f * ls)); break;
                case 3:
                    P("LipAggressive", new Vector3(w * 1.0f, 0.08f, 0.3f), paint, new Vector3(0f, 0.14f, 2.18f * ls));
                    for (int s = -1; s <= 1; s += 2) P("Canard", new Vector3(0.22f, 0.02f, 0.12f), carbon, new Vector3(s * w * 0.47f, 0.4f, 2.17f * ls), new Vector3(0f, 0f, s * -12f));
                    break;
            }
            switch (Style(PartSlot.RearBumper))
            {
                case 1:
                    for (int k = -2; k <= 2; k++) P("DiffuserFin", new Vector3(0.02f, 0.14f, 0.38f), carbon, new Vector3(k * 0.22f, 0.13f, -2.12f * ls));
                    P("Diffuser", new Vector3(w * 0.8f, 0.02f, 0.4f), carbon, new Vector3(0f, 0.2f, -2.12f * ls));
                    break;
                case 2: P("TowHook", new Vector3(0.05f, 0.05f, 0.2f), MaterialLibrary.LitColor("tow_red", new Color(0.9f, 0.1f, 0.1f), 0.5f), new Vector3(-0.3f, 0.25f, -2.3f * ls)); break;
            }
            int skirts = Style(PartSlot.SideSkirts);
            if (skirts > 0)
                for (int s = -1; s <= 1; s += 2) P("Skirt", new Vector3(skirts == 2 ? 0.12f : 0.07f, 0.1f, 2.5f * ls), skirts == 2 ? carbon : paint, new Vector3(s * (w * 0.5f + 0.02f), 0.15f, -0.05f));
            switch (Style(PartSlot.Spoiler))
            {
                case 1: P("Ducktail", new Vector3(w * 0.85f, 0.06f, 0.22f), paint, new Vector3(0f, 0.9f, -2.05f * ls), new Vector3(-14f, 0f, 0f)); break;
                case 2:
                    P("WingPlate", new Vector3(w * 0.95f, 0.025f, 0.32f), carbon, new Vector3(0f, 1.18f, -1.95f * ls), new Vector3(-6f, 0f, 0f));
                    for (int s = -1; s <= 1; s += 2)
                    {
                        P("WingStand", new Vector3(0.03f, 0.3f, 0.12f), trim, new Vector3(s * w * 0.3f, 1.0f, -1.95f * ls));
                        P("Endplate", new Vector3(0.015f, 0.18f, 0.36f), carbon, new Vector3(s * w * 0.475f, 1.18f, -1.95f * ls));
                    }
                    break;
                case 3:
                    P("WingPlate", new Vector3(w * 0.98f, 0.025f, 0.34f), carbon, new Vector3(0f, 1.24f, -1.98f * ls), new Vector3(-8f, 0f, 0f));
                    for (int s = -1; s <= 1; s += 2) P("SwanNeck", new Vector3(0.025f, 0.38f, 0.06f), trim, new Vector3(s * w * 0.26f, 1.06f, -1.88f * ls), new Vector3(25f, 0f, 0f));
                    break;
            }
            switch (Style(PartSlot.Hood))
            {
                case 1: for (int s = -1; s <= 1; s += 2) P("HoodVent", new Vector3(0.3f, 0.02f, 0.35f), trim, new Vector3(s * 0.32f, 0.79f, 1.35f * ls), new Vector3(-6f, 0f, 0f)); break;
                case 2: P("CarbonHood", new Vector3(w * 0.86f, 0.015f, 1.15f * ls), carbon, new Vector3(0f, 0.785f, 1.45f * ls), new Vector3(-5.5f, 0f, 0f)); break;
            }
            int fenders = Style(PartSlot.Fenders);
            if (fenders > 0)
                for (int i = 0; i < 4; i++)
                {
                    bool front = VehicleSimulation.IsFront(i), left = VehicleSimulation.IsLeft(i);
                    float x = (left ? -1f : 1f) * (w * 0.5f + (fenders == 2 ? 0.07f : 0.035f));
                    P("Flare", new Vector3(fenders == 2 ? 0.14f : 0.08f, 0.12f, 0.85f), fenders == 2 ? carbon : paint,
                        new Vector3(x, 0.6f, (front ? 0.5f : -0.5f) * p.chassis.wheelbase));
                }
            switch (Style(PartSlot.Livery))
            {
                case 1:
                    for (int s = -1; s <= 1; s += 2) P("Stripe", new Vector3(0.16f, 0.006f, 4.3f * ls), secondary, new Vector3(s * 0.14f, 0.84f, 0f));
                    break;
                case 2: for (int s = -1; s <= 1; s += 2) P("Arrow", new Vector3(0.006f, 0.08f, 3.2f * ls), secondary, new Vector3(s * (w * 0.5f + 0.004f), 0.55f, 0f), new Vector3(0f, 0f, 0f)); break;
                case 3: for (int s = -1; s <= 1; s += 2) P("TwoTone", new Vector3(0.006f, 0.26f, 4.2f * ls), secondary, new Vector3(s * (w * 0.5f + 0.004f), 0.33f, 0f)); break;
                case 4:
                    var white = MaterialLibrary.LitColor("livery_white", Color.white, 0.6f);
                    for (int s = -1; s <= 1; s += 2)
                        ProceduralMeshes.Spawn("Number", partsRoot, ProceduralMeshes.Tire(0.25f, 0.001f, 0.006f, 24), white, new Vector3(s * (w * 0.5f + 0.006f), 0.55f, -0.1f), Quaternion.identity, false);
                    break;
            }
            if (!string.IsNullOrEmpty(v.underglowColor))
            {
                var glow = MaterialLibrary.Emissive("underglow_" + v.underglowColor, MaterialLibrary.ParseHex(v.underglowColor, Color.cyan), 3f);
                P("Underglow", new Vector3(w * 0.7f, 0.01f, 3.4f * ls), glow, new Vector3(0f, 0.12f, 0f));
            }
        }

        /// <summary>
        /// Builds a rig around a player's 3D model: the model is offset so its wheel centers line up with the physics
        /// wheel positions; wheel meshes are re-parented to the suspension/spin pivots. Physics never reads the mesh.
        /// </summary>
        public static CarRig BuildFromModel(Transform root, GameObject prefab, Core.Garage.ModelAnalysis a, VehiclePhysicsProfile p)
        {
            var rig = root.gameObject.AddComponent<CarRig>();
            rig.proceduralBody = false;
            rig.bodyRoot = new GameObject("Body").transform;
            rig.bodyRoot.SetParent(root, false);
            var model = Instantiate(prefab, rig.bodyRoot);
            model.name = "Model";
            Vector3 avg = Vector3.zero;
            float avgR = 0f;
            for (int i = 0; i < 4; i++)
            {
                avg += new Vector3(a.wheelCenters[i * 3], a.wheelCenters[i * 3 + 1], a.wheelCenters[i * 3 + 2]);
                avgR += a.wheelRadii[i];
            }
            avg /= 4f; avgR /= 4f;
            model.transform.localPosition = new Vector3(-avg.x, avgR - avg.y, -avg.z);
            for (int i = 0; i < 4; i++)
            {
                var wr = new GameObject("Wheel_" + (WheelId)i).transform;
                wr.SetParent(root, false);
                rig.wheelRoots[i] = wr;
                var spinner = new GameObject("Spinner").transform;
                spinner.SetParent(wr, false);
                string path = a.wheelPaths != null && i < a.wheelPaths.Length ? a.wheelPaths[i] : null;
                var wheel = string.IsNullOrEmpty(path) ? null : model.transform.Find(path);
                if (wheel != null)
                {
                    wheel.SetParent(spinner, true);
                    wheel.localPosition = Vector3.zero;
                }
            }
            float h = Mathf.Max(1f, a.heightM);
            rig.cockpitAnchor = new GameObject("CockpitAnchor").transform;
            rig.cockpitAnchor.SetParent(rig.bodyRoot, false);
            rig.cockpitAnchor.localPosition = new Vector3(-0.37f, h * 0.8f, -0.35f);
            rig.hoodAnchor = new GameObject("HoodAnchor").transform;
            rig.hoodAnchor.SetParent(rig.bodyRoot, false);
            rig.hoodAnchor.localPosition = new Vector3(0f, h * 0.75f, 0.5f);
            rig.bumperAnchor = new GameObject("BumperAnchor").transform;
            rig.bumperAnchor.SetParent(rig.bodyRoot, false);
            rig.bumperAnchor.localPosition = new Vector3(0f, 0.55f, Mathf.Max(1.8f, a.lengthM * 0.5f));
            rig.doorPivot = new GameObject("DoorPivot").transform;
            rig.doorPivot.SetParent(rig.bodyRoot, false);
            rig.partsRoot = new GameObject("Parts").transform;
            rig.partsRoot.SetParent(rig.bodyRoot, false);
            return rig;
        }

        /// <summary>Extrudes a side profile (x = car Z, y = height) across the car width. Profile must be star-shaped.</summary>
        public static Mesh Extrude(Vector2[] profile, float width, float lengthScale, bool closeToBeltline = false)
        {
            var v = new List<Vector3>(); var t = new List<int>();
            int n = profile.Length;
            float hw = width * 0.5f;
            Vector2 c = Vector2.zero;
            foreach (var pnt in profile) c += pnt;
            c /= n;
            for (int side = -1; side <= 1; side += 2)
            {
                int center = v.Count;
                v.Add(new Vector3(side * hw, c.y, c.x * lengthScale));
                for (int i = 0; i < n; i++) v.Add(new Vector3(side * hw, profile[i].y, profile[i].x * lengthScale));
                for (int i = 0; i < n; i++)
                {
                    int a = center + 1 + i, b = center + 1 + (i + 1) % n;
                    if (side < 0) { t.Add(center); t.Add(a); t.Add(b); }
                    else { t.Add(center); t.Add(b); t.Add(a); }
                }
            }
            for (int i = 0; i < n; i++)
            {
                var p0 = profile[i]; var p1 = profile[(i + 1) % n];
                int s = v.Count;
                v.Add(new Vector3(-hw, p0.y, p0.x * lengthScale));
                v.Add(new Vector3(hw, p0.y, p0.x * lengthScale));
                v.Add(new Vector3(hw, p1.y, p1.x * lengthScale));
                v.Add(new Vector3(-hw, p1.y, p1.x * lengthScale));
                t.Add(s); t.Add(s + 1); t.Add(s + 2);
                t.Add(s); t.Add(s + 2); t.Add(s + 3);
            }
            var m = ProceduralMeshes.Build(v, t);
            m.name = "DC_Extrude";
            return m;
        }
    }
}
