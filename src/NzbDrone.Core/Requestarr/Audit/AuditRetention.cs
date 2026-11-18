using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Provisioning;

namespace Requestarr.Core.Audit
{
    // How long the audit log keeps an entry ("Keep the audit log for"): one of a few choices, stored as its words.
    public static class AuditRetention
    {
        public const string NinetyDays = "90 days";
        public const string SixMonths = "6 months";
        public const string OneYear = "1 year";
        public const string TwoYears = "2 years";
        public const string Forever = "forever";
        public const string Default = OneYear;

        public static readonly IReadOnlyList<string> Choices = new[] { NinetyDays, SixMonths, OneYear, TwoYears, Forever };

        // Entries from before this go; null keeps them all.
        public static DateTime? Cutoff(string keepFor, DateTime now) => keepFor switch
        {
            NinetyDays => now.AddDays(-90),
            SixMonths => now.AddMonths(-6),
            TwoYears => now.AddYears(-2),
            Forever => null,
            _ => now.AddYears(-1)
        };

        public static string Problem(string value) =>
            Choices.Contains(value) ? null : $"Keep the audit log for is one of: {string.Join(", ", Choices)}.";
    }

    // Kept in Requestarr's settings (the admin web portal reads and changes it there); provisioning sets it on every
    // start when the file names it.
    public interface IAuditSettings
    {
        // One of AuditRetention.Choices.
        string KeepFor { get; }

        void Apply(AuditLogProvisioning provisioning);
    }

    public class AuditSettings : IAuditSettings
    {
        private readonly IConfigService _config;

        public AuditSettings(IConfigService config)
        {
            _config = config;
        }

        public string KeepFor
        {
            get
            {
                var stored = _config.AuditLogKeepFor;

                return AuditRetention.Choices.Contains(stored) ? stored : AuditRetention.Default;
            }
        }

        // A choice the file gets wrong fails provisioning, which the web app's health check shows, rather than quietly
        // keeping the log for longer or shorter than the admin meant.
        public void Apply(AuditLogProvisioning provisioning)
        {
            if (provisioning?.KeepFor is not { } keepFor)
            {
                return;
            }

            if (AuditRetention.Problem(keepFor) is { } problem)
            {
                throw new ProvisioningException($"auditLog.keepFor: {problem}");
            }

            _config.AuditLogKeepFor = keepFor;
        }
    }
}
