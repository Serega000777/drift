using DriftCareer.Core.Surfaces;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Garage
{
    /// <summary>
    /// "Корч": a cheap, aggressive street-drift build. Recipe follows common community practice:
    /// engine swap (turbo I6 if budget allows), welded diff, angle kit, hydraulic handbrake, coilovers lowered,
    /// stripped interior + cage, used rear tires, mismatched/primer panels. Works on any car.
    /// </summary>
    public static class KorchBuilder
    {
        public static VehicleTuningState Apply(VehicleTuningState t, string engineId = "jdm_i6_25_jz")
        {
            t.presetName = "korch";
            t.engineSwapId = EngineCatalog.Find(engineId) != null ? engineId : "jdm_i6_25_jz";
            t.diffType = DifferentialType.Locked;
            t.steeringLockDeg = 62f;
            t.ackermann = 0.05f;
            t.hydraulicHandbrake = true;
            t.handbrakeScale = 1.6f;
            t.rideHeightOffset = -0.04f;
            t.springFront = 1.5f; t.springRear = 1.35f;
            t.damperFront = 1.3f; t.damperRear = 1.2f;
            t.antiRollFront = 1.5f; t.antiRollRear = 0.7f;
            t.camberFrontDeg = -5.5f; t.camberRearDeg = -1f;
            t.toeFrontDeg = -0.1f; t.toeRearDeg = 0.25f;
            t.strippedInterior = true;
            t.rollCage = true;
            t.batteryInTrunk = true;
            t.compoundFront = TireCompound.DriftSemiSlick;
            t.compoundRear = TireCompound.Street;
            t.usedTireWear = 0.35f;
            t.tireWidthFront = 215f; t.tireWidthRear = 205f;
            t.pressureRearBar = 2.8f;
            var v = t.visual;
            v.paintColor = "#7A7D80";
            v.paintFinish = PaintFinish.Matte;
            v.secondaryColor = "#C8102E";
            v.rimColor = "#2A2A2A";
            v.parts[PartSlot.Livery] = "livery.two_tone";
            v.parts[PartSlot.RearBumper] = "rearbumper.cutout";
            v.parts[PartSlot.FrontBumper] = "frontbumper.aggressive";
            v.parts[PartSlot.Fenders] = "fenders.flares";
            v.parts[PartSlot.Hood] = "hood.vented";
            t.trackOffsetFront = 0.03f;
            t.trackOffsetRear = 0.04f;
            return TuningApplier.Sanitize(t);
        }
    }
}
