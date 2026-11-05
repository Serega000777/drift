using System;
using System.Collections.Generic;
using DriftCareer.Core.Vehicle;

namespace DriftCareer.Core.Garage
{
    public enum Aspiration { NaturallyAspirated = 0, Turbo = 1, TwinTurbo = 2, Rotary = 3, RotaryTurbo = 4 }

    /// <summary>
    /// Engine data for swaps. Values follow publicly listed factory ratings where available; every entry carries
    /// its source and a confidence flag. Real engine codes/brands live in <see cref="referenceCode"/> and are shown
    /// in UI only when DC_SHOW_REFERENCE_NAMES is defined (dev builds) until trademark use is cleared.
    /// </summary>
    [Serializable]
    public sealed class EngineData
    {
        public string id;
        public string titleKey;
        public string referenceCode;
        public string layout = "I4";
        public float displacementL;
        public Aspiration aspiration;
        public float powerKw;
        public float powerRpm;
        public float torqueNm;
        public float torqueRpm;
        public float redlineRpm;
        /// <summary>Engine + gearbox mass used for swap weight changes (kg).</summary>
        public float swapMassKg;
        public int price;
        /// <summary>"verified" = matches the cited listing; "approximate" = sources disagree or are enthusiast data.</summary>
        public string confidence = "approximate";
        public string source;

        public float PowerHp => powerKw * 1.341f;
        public bool IsTurbo => aspiration == Aspiration.Turbo || aspiration == Aspiration.TwinTurbo || aspiration == Aspiration.RotaryTurbo;

        /// <summary>Builds a full-throttle torque curve hitting the rated peak torque and power.</summary>
        public Curve TorqueCurve()
        {
            var sheet = new VehicleSpecSheet
            {
                peakPowerKw = powerKw, peakPowerRpm = powerRpm, peakTorqueNm = torqueNm, peakTorqueRpm = torqueRpm,
                redlineRpm = redlineRpm, turbo = IsTurbo
            };
            return SpecToProfileConverter.SynthesizeTorqueCurve(sheet);
        }
    }

    public static class EngineCatalog
    {
        public const string Stock = "";

        public static readonly IReadOnlyList<EngineData> Engines = Build();

        private static EngineData E(string id, string code, string layout, float liters, Aspiration asp, float ps, float pRpm, float nm, float tRpm, float redline, float mass, int price, string confidence, string source)
            => new EngineData
            {
                id = id, titleKey = "engine." + id, referenceCode = code, layout = layout, displacementL = liters, aspiration = asp,
                powerKw = ps * 0.7355f, powerRpm = pRpm, torqueNm = nm, torqueRpm = tRpm, redlineRpm = redline, swapMassKg = mass,
                price = price, confidence = confidence, source = source
            };

        private static List<EngineData> Build()
        {
            const string vazSrc = "Open catalog listings (auto.ru, autospot.ru, drom.ru, cyclowiki): displacement/power; torque/rpm per factory manuals - verify before release";
            const string jdmSrc = "JDM catalogue ratings (PS, kgf*m) as commonly published; swap masses (engine+gearbox) from drive2.ru community data";
            return new List<EngineData>
            {
                // Soviet/Russian classic NA engines (carbureted, OHC I4).
                E("classic_i4_12", "VAZ-2101", "I4", 1.198f, Aspiration.NaturallyAspirated, 64f, 5600f, 87f, 3400f, 6200f, 150f, 0, "approximate", vazSrc),
                E("classic_i4_13", "VAZ-21011", "I4", 1.294f, Aspiration.NaturallyAspirated, 69f, 5600f, 94f, 3400f, 6200f, 150f, 1500, "approximate", vazSrc),
                E("classic_i4_15", "VAZ-2103", "I4", 1.451f, Aspiration.NaturallyAspirated, 77f, 5600f, 106f, 3500f, 6200f, 152f, 3000, "approximate", vazSrc),
                E("classic_i4_16", "VAZ-2106", "I4", 1.568f, Aspiration.NaturallyAspirated, 80f, 5400f, 121f, 3000f, 6000f, 155f, 4500, "approximate", vazSrc),
                E("classic_i4_17_bored", "VAZ-2106 bored 1.7 (street build)", "I4", 1.69f, Aspiration.NaturallyAspirated, 95f, 6000f, 135f, 3800f, 6800f, 155f, 8000, "approximate", "Typical community build, not a factory engine"),
                // Japanese engines popular for drift swaps.
                E("jdm_i4_16_na", "4A-GE", "I4", 1.587f, Aspiration.NaturallyAspirated, 130f, 6600f, 149f, 5200f, 7600f, 170f, 12000, "approximate", jdmSrc),
                E("jdm_i4_18_turbo", "CA18DET", "I4", 1.809f, Aspiration.Turbo, 169f, 6400f, 225f, 4000f, 7000f, 200f, 16000, "approximate", jdmSrc),
                E("jdm_i4_20_turbo", "SR20DET", "I4", 1.998f, Aspiration.Turbo, 205f, 6000f, 274f, 4000f, 7200f, 220f, 22000, "verified", jdmSrc + "; drifted.com SR20DET listing"),
                E("jdm_i6_25_turbo", "RB25DET", "I6", 2.498f, Aspiration.Turbo, 250f, 6400f, 314f, 4000f, 7000f, 320f, 26000, "approximate", jdmSrc),
                E("jdm_i6_26_twin", "RB26DETT", "I6", 2.568f, Aspiration.TwinTurbo, 280f, 6800f, 353f, 4400f, 8000f, 330f, 38000, "approximate", jdmSrc),
                E("jdm_i6_25_jz", "1JZ-GTE", "I6", 2.491f, Aspiration.TwinTurbo, 280f, 6200f, 363f, 4800f, 7200f, 280f, 30000, "verified", jdmSrc + "; Toyota JZ engine reference listing"),
                E("jdm_i6_30_jz", "2JZ-GTE", "I6", 2.997f, Aspiration.TwinTurbo, 280f, 5600f, 431f, 3600f, 6800f, 330f, 42000, "approximate", jdmSrc + "; sources disagree on 2JZ ratings"),
                E("jdm_rotary_13_turbo", "13B-REW", "R2", 1.308f, Aspiration.RotaryTurbo, 255f, 6500f, 294f, 5000f, 8000f, 190f, 34000, "approximate", jdmSrc),
            };
        }

        public static EngineData Find(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            foreach (var e in Engines) if (e.id == id) return e;
            return null;
        }
    }
}
