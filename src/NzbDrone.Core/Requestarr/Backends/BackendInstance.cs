using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
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

    // What a Sonarr or Radarr is for: a request goes to the instance whose purpose matches what is asked for
    // (BackendService.ForRequest), never by member. Everything ("All other shows", "All other movies"; stored under its
    // first name): whatever no other purpose claims, one per type, and while none has it those requests are paused.
    // Anime: Sonarr only, anime shows. Nothing: an extra instance no new request goes to.
    public enum InstancePurpose
    {
        [FieldOption("All other", Hint = "")]
        Everything = 0,

        [FieldOption("Anime", Hint = "")]
        Anime = 1,

        [FieldOption("Nothing yet", Hint = "")]
        Nothing = 2
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

        // Deleted by an admin: gone from every list and picker, kept so its requests still show where they went. Its name
        // says so ("Sonarr (deleted)"), which frees the old name for a new instance.
        public DateTime? Deleted { get; set; }

        public bool IsDeleted => Deleted.HasValue;
    }

    // A Sonarr or Radarr connection's settings, as Servarr provider settings: each field's label and help are its
    // translations (Backend and the field's name), shown by Servarr's provider forms from the schema. The choices of
    // profile, root folder, series type and anime priority come from the media manager itself (its provider actions).
    public class BackendSettings : IProviderConfig
    {
        [FieldDefinition(0, Label = "BackendUrl", HelpText = "BackendUrlHelpText", Type = FieldType.Url)]
        public string Url { get; set; }

        // Where the admin opens it themselves (a domain behind a proxy, say), for the links on requests; empty when it's
        // the same as Url.
        [FieldDefinition(1, Label = "BackendBrowserUrl", HelpText = "BackendBrowserUrlHelpText", Type = FieldType.Url, Advanced = true)]
        public string BrowserUrl { get; set; }

        [FieldDefinition(2, Label = "BackendApiKey", HelpText = "BackendApiKeyHelpText", Privacy = PrivacyLevel.ApiKey)]
        public string ApiKey { get; set; }

        [FieldDefinition(3, Label = "BackendQualityProfile", HelpText = "BackendQualityProfileHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.QualityProfiles)]
        public string QualityProfile { get; set; }

        [FieldDefinition(4, Label = "BackendRootFolder", HelpText = "BackendRootFolderHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.RootFolders)]
        public string RootFolder { get; set; }

        // Sonarr only. Standard unless the admin chooses otherwise; an instance saved without one reads as Standard.
        [FieldDefinition(5, Label = "BackendSeriesType", HelpText = "BackendSeriesTypeHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.SeriesTypes, Section = MediaManagerActions.SonarrSection, Advanced = true)]
        public string SeriesType
        {
            get => _seriesType ?? StandardSeries;
            set => _seriesType = value;
        }

        public const string StandardSeries = "standard";

        private string _seriesType;

        // What it's for (InstancePurpose), Everything unless set: with one instance of a type, it takes all its requests.
        public InstancePurpose Purpose { get; set; } = InstancePurpose.Everything;

        // The purpose as Servarr's provider form picks it: by number. It isn't saved, so the stored settings keep it by name.
        [FieldDefinition(6, Label = "BackendPurpose", HelpText = "BackendPurposeHelpText", Type = FieldType.Select, SelectOptions = typeof(InstancePurpose))]
        [JsonIgnore]
        public int PurposeChoice
        {
            get => (int)Purpose;
            set => Purpose = (InstancePurpose)value;
        }

        // Kept from before Requestarr chose for itself: Sonarr and Radarr report downloads right away once their own
        // Connect test reaches Requestarr, and on the timer otherwise (AvailabilityService). A provisioning file may still
        // set it (dev and QA).
        [FieldDefinition(7, Label = "BackendPollingOnly", Type = FieldType.Checkbox, Hidden = HiddenType.Hidden)]
        public bool PollingOnly { get; set; }

        // What a new member gets for this instance's type until an admin changes it. An instance saved before this
        // setting existed reads as Needs approval, what every new member got then; a new one starts on Approved
        // automatically (MediaManagerBase.DefaultDefinitions).
        public AccessState DefaultAccess { get; set; } = AccessState.NeedsApproval;

        // The default access as Servarr's provider form picks it: by number, as all of Servarr's choices are. It isn't
        // saved, so the stored settings keep the access by name.
        [FieldDefinition(8, Label = "BackendDefaultAccess", HelpText = "BackendDefaultAccessHelpText", Type = FieldType.Select, SelectOptions = typeof(AccessState))]
        [JsonIgnore]
        public int DefaultAccessChoice
        {
            get => (int)DefaultAccess;
            set => DefaultAccess = (AccessState)value;
        }

        // Sonarr only: anime sub/dub (see AnimeDefaults). On unless the instance opts out.
        [FieldDefinition(9, Label = "BackendAnimeEnabled", HelpText = "BackendAnimeEnabledHelpText", Type = FieldType.Checkbox, Section = MediaManagerActions.AnimeSection)]
        public bool AnimeEnabled { get; set; } = true;

        [FieldDefinition(10, Label = "BackendAnimePriority", HelpText = "BackendAnimePriorityHelpText", Type = FieldType.Select, SelectOptionsProviderAction = MediaManagerActions.AnimePriorities, Section = MediaManagerActions.AnimeSection)]
        public string AnimePriority { get; set; } = "dub";

        [FieldDefinition(11, Label = "BackendDubRequired", HelpText = "BackendDubRequiredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public IEnumerable<string> DubRequired { get; set; }

        [FieldDefinition(12, Label = "BackendDubIgnored", HelpText = "BackendDubIgnoredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public IEnumerable<string> DubIgnored { get; set; }

        [FieldDefinition(13, Label = "BackendSubRequired", HelpText = "BackendSubRequiredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public IEnumerable<string> SubRequired { get; set; }

        [FieldDefinition(14, Label = "BackendSubIgnored", HelpText = "BackendSubIgnoredHelpText", Type = FieldType.Tag, Section = MediaManagerActions.AnimeSection, Advanced = true)]
        public IEnumerable<string> SubIgnored { get; set; }

        // Checked when it's saved, with its connection tested (InstanceSettingsService), as the settings pages did.
        public NzbDroneValidationResult Validate() => new();
    }

    // All: every instance that isn't deleted, so a deleted one is gone from every list and picker. Known: deleted ones
    // too, for history (where each request went). Find and Get by ID find deleted ones too.
    public interface IBackendRepository : IProviderRepository<BackendInstance>
    {
        BackendInstance FindByName(string name);
        IEnumerable<BackendInstance> Known();
    }

    public class BackendRepository : ProviderRepository<BackendInstance>, IBackendRepository
    {
        public BackendRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public override IEnumerable<BackendInstance> All() => base.All().Where(b => !b.IsDeleted);

        public IEnumerable<BackendInstance> Known() => base.All();

        // A deleted instance's name is free for a new one.
        public BackendInstance FindByName(string name)
        {
            return All().FirstOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
        }
    }
}
