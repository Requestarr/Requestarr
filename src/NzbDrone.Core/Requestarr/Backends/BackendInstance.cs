using System;
using System.Collections.Generic;
using System.Linq;
using NzbDrone.Core.Annotations;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.Validation;
using Requestarr.Core.Backends.MediaManagers;
using Requestarr.Core.Users;

namespace Requestarr.Core.Backends
{
    public static class BackendImplementations
    {
        public const string Sonarr = "Sonarr";
        public const string Radarr = "Radarr";
        public const string Lidarr = "Lidarr";

        public static string ForContentType(ContentType type) => type switch
        {
            ContentType.Movie => Radarr,
            ContentType.Tv => Sonarr,
            ContentType.Music => Lidarr,
            _ => null
        };
    }

    // One configured Sonarr / Radarr / Lidarr instance (row in Backends), any number per type: a Servarr provider
    // (MediaManagers), as Prowlarr's applications are, its settings a BackendSettings.
    public class BackendInstance : ProviderDefinition
    {
        public BackendInstance()
        {
            Enable = true;
            Settings = new BackendSettings();
        }

        // Its settings, typed: the provider's Settings is always a BackendSettings.
        public BackendSettings Config => (BackendSettings)Settings;

        public DateTime Added { get; set; }

        // Connection state. Never connected (FirstConnected empty): users don't see its content type.
        // Lost (DownSince set): users still see the type; its requests say they're paused.
        public DateTime? FirstConnected { get; set; }
        public DateTime? DownSince { get; set; }
        public string DownAlertMessageId { get; set; }

        // This outage: the alert got its "Announce to users" button; the announcement, if an admin pressed it.
        public bool DownAnnounceOffered { get; set; }
        public string DownAnnounceMessageId { get; set; }

        public bool EverConnected => FirstConnected.HasValue;
    }

    // A Sonarr or Radarr connection's settings, as Servarr provider settings: each field's label and help are its
    // translations (Backend and the field's name), shown by Servarr's provider forms from the schema. The choices of
    // profile, root folder, series type and anime priority come from the media manager itself (its provider actions).
    public class BackendSettings : IProviderConfig
    {
        [FieldDefinition(0, Label = "BackendUrl", HelpText = "BackendUrlHelpText", Type = FieldType.Url)]
        public string Url { get; set; }

        // Where an admin's browser reaches the instance, when Url only works from Requestarr (a container name, say).
        [FieldDefinition(1, Label = "BackendBrowserUrl", HelpText = "BackendBrowserUrlHelpText", Type = FieldType.Url, Advanced = true)]
        public string BrowserUrl { get; set; }

        [FieldDefinition(2, Label = "BackendApiKey", HelpText = "BackendApiKeyHelpText", Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "BackendQualityProfile", HelpText = "BackendQualityProfileHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.QualityProfiles)]
        public string QualityProfile { get; set; }

        [FieldDefinition(4, Label = "BackendRootFolder", HelpText = "BackendRootFolderHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.RootFolders)]
        public string RootFolder { get; set; }

        [FieldDefinition(5, Label = "BackendSeriesType", HelpText = "BackendSeriesTypeHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.SeriesTypes)]
        public string SeriesType { get; set; }

        [FieldDefinition(6, Label = "BackendIsDefault", HelpText = "BackendIsDefaultHelpText", Type = FieldType.Checkbox)]
        public bool IsDefault { get; set; }

        [FieldDefinition(7, Label = "BackendPollingOnly", HelpText = "BackendPollingOnlyHelpText", Type = FieldType.Checkbox, Advanced = true)]
        public bool PollingOnly { get; set; }

        // What a new member gets for this instance's type until an admin changes it. An instance saved before this
        // setting existed reads as Needs approval, what every new member got then.
        [FieldDefinition(8, Label = "BackendDefaultAccess", HelpText = "BackendDefaultAccessHelpText", Type = FieldType.Select, SelectOptions = typeof(AccessState))]
        public AccessState DefaultAccess { get; set; } = AccessState.NeedsApproval;

        // Sonarr only: anime sub/dub (see AnimeDefaults). On unless the instance opts out.
        [FieldDefinition(9, Label = "BackendAnimeEnabled", HelpText = "BackendAnimeEnabledHelpText", Type = FieldType.Checkbox, Section = MediaManagerActions.AnimeSection)]
        public bool AnimeEnabled { get; set; } = true;

        [FieldDefinition(10, Label = "BackendAnimePriority", HelpText = "BackendAnimePriorityHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.AnimePriorities, Section = MediaManagerActions.AnimeSection)]
        public string AnimePriority { get; set; } = "dub";

        [FieldDefinition(11, Label = "BackendDubRequired", HelpText = "BackendDubRequiredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public List<string> DubRequired { get; set; }

        [FieldDefinition(12, Label = "BackendDubIgnored", HelpText = "BackendDubIgnoredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public List<string> DubIgnored { get; set; }

        [FieldDefinition(13, Label = "BackendSubRequired", HelpText = "BackendSubRequiredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public List<string> SubRequired { get; set; }

        [FieldDefinition(14, Label = "BackendSubIgnored", HelpText = "BackendSubIgnoredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public List<string> SubIgnored { get; set; }

        // Checked when it's saved, with its connection tested (InstanceSettingsService), as the settings pages did.
        public NzbDroneValidationResult Validate() => new();
    }

    public interface IBackendRepository : IProviderRepository<BackendInstance>
    {
        BackendInstance FindByName(string name);
    }

    public class BackendRepository : ProviderRepository<BackendInstance>, IBackendRepository
    {
        public BackendRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public BackendInstance FindByName(string name)
        {
            return All().FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
