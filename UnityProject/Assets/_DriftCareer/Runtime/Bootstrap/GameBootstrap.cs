using UnityEngine;
using UnityEngine.SceneManagement;

namespace DriftCareer.Runtime
{
    /// <summary>Put on a GameObject to keep a scene from auto-starting the game (test scenes, art scenes).</summary>
    public sealed class NoAutoBootstrap : MonoBehaviour { }

    /// <summary>
    /// Single entry point. Press Play in ANY scene (even an empty one) and the game boots itself:
    /// services -> world -> UI. The Bootstrap scene created by "Drift Career > Setup Project" only holds this component.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    public sealed class GameBootstrap : MonoBehaviour
    {
        public static GameBootstrap Instance { get; private set; }
        public GameController Controller { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void AutoStart()
        {
            if (Instance != null) return;
            var scene = SceneManager.GetActiveScene();
            if (scene.name.StartsWith("InitTestScene")) return; // Unity Test Framework scenes
            foreach (var root in scene.GetRootGameObjects())
                if (root.GetComponentInChildren<NoAutoBootstrap>(true) != null) return;
            if (FindAll<GameBootstrap>().Length > 0) return;
            new GameObject("DriftCareer").AddComponent<GameBootstrap>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            DisableSceneCameras();
            var services = GameServices.Create();
            Controller = gameObject.AddComponent<GameController>();
            Controller.Boot(services);
            Debug.Log($"[DriftCareer] Booted. Platform={services.Device.kind} Tier={services.Defaults.quality} Save={Application.persistentDataPath}");
        }

        private static T[] FindAll<T>() where T : Object
        {
#if UNITY_2023_1_OR_NEWER
            return Object.FindObjectsByType<T>(FindObjectsSortMode.None);
#else
            return Object.FindObjectsOfType<T>();
#endif
        }

        /// <summary>The template's SampleScene camera/light would double-render; our rig owns the camera.</summary>
        private static void DisableSceneCameras()
        {
            foreach (var cam in FindAll<Camera>())
            {
                if (cam.GetComponentInParent<GameBootstrap>() != null) continue;
                cam.gameObject.SetActive(false);
            }
            foreach (var l in FindAll<Light>())
                if (l.type == LightType.Directional) l.gameObject.SetActive(false);
        }
    }
}
