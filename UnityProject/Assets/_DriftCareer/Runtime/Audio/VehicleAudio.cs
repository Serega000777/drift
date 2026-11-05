using DriftCareer.Runtime.Vehicle;
using UnityEngine;

namespace DriftCareer.Runtime.Audio
{
    /// <summary>
    /// Fully procedural car audio (no sample assets => no licensing risk):
    /// engine (firing-frequency harmonics + load noise + limiter + turbo whine + lift-off crackle),
    /// tire squeal (resonant filtered noise driven by slip power), impacts and tire blowout bangs.
    /// Synthesis runs on the audio thread in OnAudioFilterRead; the main thread only writes volatile params.
    /// </summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class VehicleAudio : MonoBehaviour
    {
        public int cylinders = 4;
        public float engineVolume = 0.55f;
        public float tireVolume = 0.45f;
        public float effectsVolume = 0.8f;

        private VehicleController _car;
        private AudioSource _source;
        private volatile float _rpm = 900f;
        private volatile float _throttle;
        private volatile float _squeal;
        private volatile float _squealPitch = 1f;
        private volatile float _impact;
        private volatile float _bang;
        private volatile bool _running;
        private int _sampleRate = 48000;
        private double _phase;
        private double _turboPhase;
        private float _noiseState;
        private float _bp1, _bp2;
        private float _lpEngine;
        private float _crackleTimer;
        private float _prevThrottle;
        private uint _rng = 2463534242u;

        public void Initialize(VehicleController car, float masterVolume, float engine, float effects)
        {
            _car = car;
            _source = GetComponent<AudioSource>();
            _sampleRate = AudioSettings.outputSampleRate > 0 ? AudioSettings.outputSampleRate : 48000;
            var clip = AudioClip.Create("dc_silence", 1024, 1, _sampleRate, false);
            clip.SetData(new float[1024], 0);
            _source.clip = clip;
            _source.loop = true;
            _source.spatialBlend = 0.35f;
            _source.dopplerLevel = 0f;
            _source.playOnAwake = false;
            SetVolumes(masterVolume, engine, effects);
            _source.Play();
            car.Impact += (impulse, p, c) => { if (impulse > 800f) _impact = Mathf.Max(_impact, Mathf.Clamp01(impulse / 20000f)); };
            car.Health.TireBlown += w => _bang = 1f;
        }

        public void SetVolumes(float master, float engine, float effects, float tires = 1f)
        {
            if (_source != null) _source.volume = Mathf.Clamp01(master);
            engineVolume = 0.55f * Mathf.Clamp01(engine);
            tireVolume = 0.45f * Mathf.Clamp01(tires);
            effectsVolume = 0.8f * Mathf.Clamp01(effects);
        }

        private void Update()
        {
            if (_car == null || _car.Sim == null) return;
            var t = _car.Telemetry;
            _running = _car.Sim.EngineRunning;
            _rpm = Mathf.Max(0f, t.engineRpm);
            _throttle = t.throttle;
            float slip = 0f;
            for (int i = 0; i < 4; i++) slip = Mathf.Max(slip, t.wheels[i].grounded ? t.wheels[i].slipPower : 0f);
            _squeal = Mathf.Clamp01((slip - 9000f) / 60000f);
            _squealPitch = 0.85f + Mathf.Clamp01(t.speedMs / 30f) * 0.3f;
            if (_source != null && !_source.isPlaying) _source.Play();
        }

        private float Noise()
        {
            _rng ^= _rng << 13; _rng ^= _rng >> 17; _rng ^= _rng << 5;
            return (_rng / 4294967295f) * 2f - 1f;
        }

        private void OnAudioFilterRead(float[] data, int channels)
        {
            float sr = _sampleRate;
            float rpm = _rpm, throttle = _throttle, squeal = _squeal, pitch = _squealPitch;
            bool running = _running;
            float firing = rpm / 60f * cylinders * 0.5f;
            double inc = firing / sr;
            float load = 0.35f + 0.65f * throttle;
            float turboFreq = 2200f + rpm * 0.9f;
            double tInc = turboFreq / sr;
            float bpFreq = 1100f * pitch;
            float f = 2f * Mathf.Sin(Mathf.PI * bpFreq / sr);
            const float q = 0.12f;
            if (_prevThrottle > 0.6f && throttle < 0.15f && rpm > 4500f) _crackleTimer = 0.45f;
            _prevThrottle = throttle;

            for (int i = 0; i < data.Length; i += channels)
            {
                float s = 0f;
                if (running)
                {
                    _phase += inc;
                    if (_phase > 1.0) _phase -= 1.0;
                    float ph = (float)_phase * Mathf.PI * 2f;
                    // Harmonic stack + slightly clipped saw gives a rough four-cylinder character.
                    float tone = Mathf.Sin(ph) * 0.55f + Mathf.Sin(ph * 2f) * 0.3f * load + Mathf.Sin(ph * 0.5f) * 0.25f + Mathf.Sin(ph * 3f) * 0.12f * load;
                    float saw = ((float)_phase * 2f - 1f) * 0.25f * load;
                    _noiseState = _noiseState * 0.97f + Noise() * 0.03f;
                    float rough = _noiseState * 2.5f * load * (0.5f + 0.5f * Mathf.Sin(ph));
                    float engine = tone + saw + rough;
                    engine = engine / (1f + Mathf.Abs(engine) * 0.6f);
                    _lpEngine += (engine - _lpEngine) * Mathf.Clamp01(0.08f + firing / sr * 4f);
                    s += _lpEngine * engineVolume * (0.45f + 0.55f * load);
                    _turboPhase += tInc;
                    if (_turboPhase > 1.0) _turboPhase -= 1.0;
                    s += Mathf.Sin((float)_turboPhase * Mathf.PI * 2f) * 0.015f * throttle * Mathf.Clamp01((rpm - 3000f) / 3000f) * engineVolume;
                    if (_crackleTimer > 0f)
                    {
                        _crackleTimer -= 1f / sr;
                        if (Noise() > 0.995f) s += Noise() * 0.6f * effectsVolume;
                    }
                }
                if (squeal > 0.001f)
                {
                    // State-variable band-pass on white noise.
                    float n = Noise();
                    float hp = n - _bp2 - q * _bp1;
                    _bp1 += f * hp;
                    _bp2 += f * _bp1;
                    s += _bp1 * squeal * tireVolume * 1.6f;
                }
                if (_impact > 0.001f)
                {
                    s += Noise() * _impact * effectsVolume;
                    _impact *= 0.9993f;
                }
                if (_bang > 0.001f)
                {
                    s += (Noise() * 0.7f + Mathf.Sin(i * 0.02f) * 0.3f) * _bang * effectsVolume * 1.5f;
                    _bang *= 0.9985f;
                }
                s = Mathf.Clamp(s, -1f, 1f);
                for (int c = 0; c < channels; c++) data[i + c] = s;
            }
        }
    }
}
