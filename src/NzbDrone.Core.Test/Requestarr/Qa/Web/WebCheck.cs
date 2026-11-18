using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;

namespace Requestarr.Core.Test.Qa.Web
{
    // One of the automated web app checks: what it saw, in plain words, and the screenshots it took. Its result is
    // written to QA_CHECKS_DIR/<check>.json for docker/qa_checks.py, passed or not; the screenshots go beside it.
    public sealed class WebCheck
    {
        private readonly string _name;
        private readonly string _folder;
        private readonly List<string> _seen = new();
        private readonly List<string> _screenshots = new();

        public WebCheck(string name, string folder)
        {
            _name = name;
            _folder = folder;
        }

        public void Saw(string line)
        {
            _seen.Add(line);
            TestContext.Progress.WriteLine($"[{_name}] {line}");
        }

        // Saved as <check>-<label>.jpg: lower case, which the checklist's cards take as attachment names.
        public async Task ShotAsync(HeadlessBrowser browser, string label)
        {
            var path = Path.Combine(_folder, "screenshots", $"{_name.Replace('_', '-')}-{label}.jpg");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            await File.WriteAllBytesAsync(path, await browser.ScreenshotAsync());
            _screenshots.Add(path);
            Saw($"screenshot {Path.GetFileName(path)}");
        }

        // Passed when the check ended without a failure.
        public void Write(Exception failure)
        {
            if (failure != null)
            {
                _seen.Add($"Failed: {failure.Message}");
            }

            var result = new { passed = failure == null, evidence = string.Join("\n", _seen), screenshots = _screenshots };
            File.WriteAllText(Path.Combine(_folder, _name + ".json"), JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
