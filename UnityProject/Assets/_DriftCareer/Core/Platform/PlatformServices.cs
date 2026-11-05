using System;
using DriftCareer.Core.Persistence;

namespace DriftCareer.Core.Platform
{
    public enum PlatformKind { Mobile = 0, Desktop = 1, Console = 2 }

    /// <summary>Hardware facts gathered by the host (Unity SystemInfo) to pick defaults.</summary>
    public struct DeviceInfo
    {
        public PlatformKind kind;
        public int systemMemoryMb;
        public int graphicsMemoryMb;
        public int processorCount;
        public bool touchSupported;
        public bool gyroSupported;
    }

    public struct PlatformDefaults
    {
        public QualityTier quality;
        public int targetFps;
        public float obstacleBudget;
        public int maxDynamicObstacles;
        public bool showTouchControls;
        public float uiScale;
    }

    /// <summary>Picks quality and budgets per device. Real performance must be measured on hardware.</summary>
    public static class DeviceTierClassifier
    {
        public static QualityTier Classify(DeviceInfo d)
        {
            if (d.kind == PlatformKind.Console) return QualityTier.High;
            if (d.kind == PlatformKind.Desktop) return d.graphicsMemoryMb >= 6000 ? QualityTier.Ultra : (d.graphicsMemoryMb >= 2500 ? QualityTier.High : QualityTier.Medium);
            if (d.systemMemoryMb >= 7000 && d.processorCount >= 8) return QualityTier.High;
            if (d.systemMemoryMb >= 3500) return QualityTier.Medium;
            return QualityTier.Low;
        }

        public static PlatformDefaults DefaultsFor(DeviceInfo d, QualityTier requested)
        {
            var tier = requested == QualityTier.Auto ? Classify(d) : requested;
            var p = new PlatformDefaults
            {
                quality = tier,
                showTouchControls = d.touchSupported && d.kind == PlatformKind.Mobile,
                uiScale = d.kind == PlatformKind.Mobile ? 1.15f : (d.kind == PlatformKind.Console ? 1.1f : 1f),
            };
            switch (tier)
            {
                case QualityTier.Low: p.targetFps = 30; p.obstacleBudget = 140f; p.maxDynamicObstacles = 60; break;
                case QualityTier.Medium: p.targetFps = 60; p.obstacleBudget = 220f; p.maxDynamicObstacles = 120; break;
                case QualityTier.High: p.targetFps = 60; p.obstacleBudget = 320f; p.maxDynamicObstacles = 200; break;
                default: p.targetFps = d.kind == PlatformKind.Desktop ? 120 : 60; p.obstacleBudget = 450f; p.maxDynamicObstacles = 300; break;
            }
            return p;
        }
    }

    /// <summary>Achievements/leaderboards/cloud saves per store (Steam, consoles). Local no-op in MVP.</summary>
    public interface IPlatformServices
    {
        PlatformKind Kind { get; }
        string StoreName { get; }
        bool SupportsCloudSave { get; }
        void UnlockAchievement(string id);
        void SubmitScore(string leaderboardId, long score);
        void Vibrate(float strength01, float seconds);
    }

    public sealed class LocalPlatformServices : IPlatformServices
    {
        private readonly Action<float, float> _vibrate;

        public LocalPlatformServices(PlatformKind kind, Action<float, float> vibrate = null)
        {
            Kind = kind;
            _vibrate = vibrate;
        }

        public PlatformKind Kind { get; }
        public string StoreName => "local";
        public bool SupportsCloudSave => false;
        public void UnlockAchievement(string id) { }
        public void SubmitScore(string leaderboardId, long score) { }
        public void Vibrate(float strength01, float seconds) => _vibrate?.Invoke(strength01, seconds);
    }
}
