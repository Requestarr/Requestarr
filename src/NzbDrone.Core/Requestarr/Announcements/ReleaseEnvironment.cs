using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NzbDrone.Core.Configuration;

namespace Requestarr.Core.Announcements
{
    // How a server keeps its "what's new" posts (docs/development.md#version-posts). Set by the environment it
    // runs in, or by the role chosen in the first-run setup; an install that sets nothing is production.
    public enum VersionPostMode
    {
        // Production and staging (beta): every version is posted once, oldest first, and stays for good.
        Standing,

        // QA: every version in QA that staging doesn't have yet, each posted once. Promoted versions drop off; a reset
        // posts the list again.
        NotYetStaged,

        // Dev: only the version running now, posted once; a new version replaces the previous post.
        CurrentOnly
    }

    public interface IReleaseEnvironment
    {
        VersionPostMode VersionPosts { get; }

        // QA: the versions staging already has, which QA no longer shows.
        IReadOnlySet<Version> Staged { get; }

        // The web app's icons and logo for this environment: "dev", "qa", "beta" (staging) or "production".
        string IconSet { get; }

        // Whether the stack or the image says which server this is (dev, QA, and beta's image), so a role saved by
        // hand has no say.
        bool SetByStack { get; }

        // Beta, because it was installed from the beta image (docs/development.md#release-flow, images).
        bool BetaFromImage { get; }

        // How a version is shown: the version this build runs as its build label (0.7.0005; 0.7.0005-dev or -qa for a
        // goal's numbered build; 0.7.0006-dev.4f2a9c1 for a dev build between goals, docs/development.md#versions),
        // every other one as its number.
        string Shown(Version version);
    }

    public class ReleaseEnvironment : IReleaseEnvironment
    {
        // dev, qa or staging: set by the local stacks, and staging by the beta image (the tested image plus this one
        // variable, the one deliberate difference from production's). Unset, a role saved by hand before the beta image
        // had it decides (ServerRole), and an install that sets nothing is production.
        public const string EnvironmentVariable = "REQUESTARR_ENVIRONMENT";

        // QA only: staging's versions ("0.1.0,0.2.0,..."), from the staging branch's changelog when the QA stack starts.
        public const string StagedVersionsVariable = "REQUESTARR_STAGED_VERSIONS";

        // The build's label, set in the image when it is built: its number, then for dev and QA the stack, and for a dev
        // build between goals (a commit without a number) its commit too.
        public const string ReleaseVersionVariable = "REQUESTARR_RELEASE_VERSION";

        private static readonly Regex Label = new(@"^(?<number>\d+\.\d+\.\d+)(?:-(?:dev|qa)|-(?:dev|qa|test)\.[0-9a-f]{7,40})?$");

        private readonly IConfigService _config;

        public ReleaseEnvironment(IConfigService config)
        {
            _config = config;
        }

        public Func<string, string> Variable { get; set; } = Environment.GetEnvironmentVariable;

        // The stack's or image's variable when it sets one; otherwise a role saved by hand, beta running as staging.
        private string Name => SetByStack ? Variable(EnvironmentVariable) : RoleName;

        private string RoleName => _config.ServerRole == Beta ? Staging : null;

        public bool SetByStack => !string.IsNullOrWhiteSpace(Variable(EnvironmentVariable));

        public bool BetaFromImage => string.Equals(Variable(EnvironmentVariable)?.Trim(), Staging, StringComparison.OrdinalIgnoreCase);

        public const string Staging = "staging";

        public VersionPostMode VersionPosts => (Name ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dev" => VersionPostMode.CurrentOnly,
            "qa" => VersionPostMode.NotYetStaged,
            _ => VersionPostMode.Standing
        };

        public string IconSet => IconSetFor(Name);

        // The roles an install could save by hand before the beta image said which it is.
        public const string Production = "production";
        public const string Beta = "beta";

        // Anything that isn't dev, QA or staging gets production's icons.
        public static string IconSetFor(string environment) => (environment ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "dev" => "dev",
            "qa" => "qa",
            "staging" => "beta",
            _ => Production
        };

        public string Shown(Version version)
        {
            var number = Changelog.Display(version);
            var match = Label.Match((Variable(ReleaseVersionVariable) ?? string.Empty).Trim());

            return match.Success && match.Groups["number"].Value == number ? match.Value : number;
        }

        public IReadOnlySet<Version> Staged => (Variable(StagedVersionsVariable) ?? string.Empty)
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(v => Version.TryParse(v, out var parsed) ? parsed : null)
            .Where(v => v != null)
            .ToHashSet();
    }
}
