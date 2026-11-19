using System;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Updates
{
    // Each start records the version that ran, unless the database was last used by a newer one: that stays recorded,
    // so the warning below stays until the backup is restored (or the newer build is back).
    public class LastRunVersion : IHandle<ApplicationStartedEvent>
    {
        private readonly IConfigService _config;

        public LastRunVersion(IConfigService config)
        {
            _config = config;
        }

        public static Version Read(IConfigService config) => Version.TryParse(config.LastRunVersion, out var version) ? version : null;

        public void Handle(ApplicationStartedEvent message)
        {
            if (Read(_config) is not { } stored || stored < BuildInfo.Version)
            {
                _config.LastRunVersion = BuildInfo.Version.ToString();
            }
        }
    }

    // An older build on a newer database: the web app says so on every page's health warnings, in plain words, with
    // how to go back (UpdateBackup took a backup before the update).
    public class NewerDatabaseCheck : HealthCheckBase
    {
        private readonly IMainDatabase _database;
        private readonly IConfigService _config;

        public NewerDatabaseCheck(IMainDatabase database, IConfigService config, ILocalizationService localizationService)
            : base(localizationService)
        {
            _database = database;
            _config = config;
        }

        public override HealthCheck Check()
        {
            var origin = new DatabaseOrigin(LastRunVersion.Read(_config), _database.Migration);

            return origin.IsNewerThan(BuildInfo.Version)
                ? new HealthCheck(GetType(), HealthCheckResult.Error, HealthCheckReason.DatabaseNewerThanBuild, NewerDatabase.Message(origin), "#database-newer-than-this-build")
                : new HealthCheck(GetType());
        }
    }
}
