using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace DriftCareer.EditorTools
{
    /// <summary>
    /// Release build hardening: IL2CPP + high managed stripping (harder to decompile, smaller), no dev-only defines,
    /// no full stack traces in player logs, and a secret scan of the project before shipping.
    /// </summary>
    public static class ReleaseHardening
    {
        private static readonly NamedBuildTarget[] Targets = { NamedBuildTarget.Android, NamedBuildTarget.iOS, NamedBuildTarget.Standalone };
        private static readonly string[] DevDefines = { "DC_SHOW_REFERENCE_NAMES", "DC_DEV_TOOLS" };

        private static readonly Regex[] SecretPatterns =
        {
            new Regex(@"AKIA[0-9A-Z]{16}"),                                         // AWS access key
            new Regex(@"sk-ant-[A-Za-z0-9_\-]{20,}"),                              // Anthropic key
            new Regex(@"sk-[A-Za-z0-9]{32,}"),                                     // generic "sk-" API keys
            new Regex(@"AIza[0-9A-Za-z_\-]{35}"),                                  // Google API key
            new Regex(@"ghp_[A-Za-z0-9]{36}"),                                     // GitHub token
            new Regex(@"-----BEGIN (RSA |EC |OPENSSH )?PRIVATE KEY-----"),
            new Regex(@"(?i)(api[_-]?key|secret|password|client[_-]?secret)\s*[:=]\s*""[^""]{12,}"""),
        };

        [MenuItem("Drift Career/4. Release Hardening (IL2CPP, stripping, secret scan)", priority = 4)]
        public static void Harden()
        {
            var log = new StringBuilder();
            foreach (var t in Targets)
            {
                PlayerSettings.SetScriptingBackend(t, ScriptingImplementation.IL2CPP);
                PlayerSettings.SetManagedStrippingLevel(t, ManagedStrippingLevel.High);
                string defines = PlayerSettings.GetScriptingDefineSymbols(t);
                var list = new List<string>(defines.Split(';'));
                int removed = list.RemoveAll(d => System.Array.IndexOf(DevDefines, d.Trim()) >= 0);
                if (removed > 0) PlayerSettings.SetScriptingDefineSymbols(t, string.Join(";", list));
                log.AppendLine($"{t.TargetName}: IL2CPP, stripping High, dev defines removed: {removed}");
            }
            PlayerSettings.SetStackTraceLogType(LogType.Log, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Warning, StackTraceLogType.None);
            PlayerSettings.SetStackTraceLogType(LogType.Error, StackTraceLogType.ScriptOnly);
            PlayerSettings.SetStackTraceLogType(LogType.Exception, StackTraceLogType.ScriptOnly);
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            log.AppendLine("Player logs: no stack traces for Log/Warning; Development Build and script debugging off.");
            var findings = ScanForSecrets();
            log.AppendLine(findings.Count == 0 ? "Secret scan: clean." : "Secret scan FOUND " + findings.Count + " issue(s):\n  " + string.Join("\n  ", findings));
            Debug.Log("[DriftCareer] Release hardening\n" + log);
            EditorUtility.DisplayDialog("Drift Career", findings.Count == 0 ? "Release settings applied. Secret scan clean." : $"Settings applied, but {findings.Count} possible secret(s) found. See Console.", "OK");
        }

        public static List<string> ScanForSecrets()
        {
            var findings = new List<string>();
            var exts = new HashSet<string> { ".cs", ".json", ".txt", ".asset", ".xml", ".yaml", ".yml", ".uss", ".uxml", ".md" };
            foreach (var file in Directory.GetFiles("Assets", "*.*", SearchOption.AllDirectories))
            {
                if (!exts.Contains(Path.GetExtension(file).ToLowerInvariant())) continue;
                if (file.Replace('\\', '/').Contains("/Editor/ReleaseHardening.cs")) continue; // the patterns themselves
                string text;
                try { text = File.ReadAllText(file); } catch (IOException) { continue; }
                foreach (var p in SecretPatterns)
                    if (p.IsMatch(text)) { findings.Add(file + "  (" + p + ")"); break; }
            }
            return findings;
        }
    }
}
