using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using DriftCareer.Core.Localization;
using NUnit.Framework;

namespace DriftCareer.Tests.EditMode
{
    /// <summary>
    /// Static check: every literal localization key used by runtime UI code exists in both RU and EN tables.
    /// Catches typos and forgotten translations without running Unity.
    /// </summary>
    public class LocalizationCoverageTests
    {
        private static readonly Regex KeyCall = new Regex(
            @"(?:\bT|S\.T|Services\.T|Loc\.Format|Loc\.Get|T\(|CyclerRow\([^,]+,|ToggleRow\([^,]+,|MenuButton\([^,]+,|SetProgress\([^,]+,)\s*\(?\s*""([a-z][a-z0-9_]*(?:\.[A-Za-z0-9_]+)+)""",
            RegexOptions.Compiled);

        private static string FindDir(string relative)
        {
            // dotnet runs from tools/CoreTests/bin/..., Unity from the project root: walk up from both.
            foreach (var start in new[] { TestContext.CurrentContext.TestDirectory, Directory.GetCurrentDirectory() })
            {
                var dir = string.IsNullOrEmpty(start) ? null : new DirectoryInfo(start);
                while (dir != null)
                {
                    foreach (var candidate in new[] { Path.Combine(dir.FullName, relative), Path.Combine(dir.FullName, "UnityProject", relative) })
                        if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            return null;
        }

        [Test]
        public void AllLiteralKeysInRuntimeCode_AreTranslated()
        {
            string runtime = FindDir("Assets/_DriftCareer/Runtime");
            string locDir = FindDir("Assets/_DriftCareer/Resources/DriftCareer/Localization");
            if (runtime == null || locDir == null) Assert.Ignore("source folders not found (packaged build)");
            var loc = new LocalizationService();
            loc.LoadTable("ru", File.ReadAllText(Path.Combine(locDir, "ru.json")));
            loc.LoadTable("en", File.ReadAllText(Path.Combine(locDir, "en.json")));
            var missing = new SortedSet<string>();
            int checkedKeys = 0;
            foreach (var file in Directory.GetFiles(runtime, "*.cs", SearchOption.AllDirectories))
                foreach (Match m in KeyCall.Matches(File.ReadAllText(file)))
                {
                    string key = m.Groups[1].Value;
                    checkedKeys++;
                    if (!loc.HasIn("ru", key) || !loc.HasIn("en", key)) missing.Add(key + "  (" + Path.GetFileName(file) + ")");
                }
            TestContext.WriteLine("checked key usages: " + checkedKeys);
            Assert.Greater(checkedKeys, 150);
            Assert.AreEqual(0, missing.Count, "Missing translations:\n" + string.Join("\n", missing));
        }
    }
}
