using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation;
using FluentValidation.Results;
using NzbDrone.Core.ThingiProvider;
using Requestarr.Core.Settings;
using Requestarr.Core.Users;

namespace Requestarr.Core.Backends.MediaManagers
{
    // The provider actions a media manager's settings ask for their choices, and the sections of Sonarr-only fields.
    public static class MediaManagerActions
    {
        public const string QualityProfiles = "getQualityProfiles";
        public const string RootFolders = "getRootFolders";
        public const string SeriesTypes = "getSeriesTypes";
        public const string AnimePriorities = "getAnimePriorities";
        public const string SonarrSection = "sonarr";
        public const string AnimeSection = "anime";
    }

    // A media manager Requestarr sends requests to: Sonarr for shows, Radarr for movies, as Servarr providers, as
    // Prowlarr's applications are (Applications/IApplication).
    public interface IMediaManager : IProvider
    {
    }

    // What every media manager does, as Prowlarr's ApplicationBase: its default settings, testing the connection and
    // listing its choices, both through the settings rules the pages used (InstanceSettingsService). A provider's
    // Implementation is its class name, Sonarr or Radarr, the instance's type as Requestarr stored it.
    public abstract class MediaManagerBase : IMediaManager
    {
        private static readonly string[] SeriesTypeChoices = { "standard", "anime", "daily" };

        // The same words as the bot's own (DiscordText.AnimePriority).
        private static readonly (string Value, string Name)[] AnimePriorityChoices =
        {
            ("dub", Discord.DiscordText.AnimePriority(Anime.AnimePriority.Dub)),
            ("sub", Discord.DiscordText.AnimePriority(Anime.AnimePriority.Sub)),
            ("first", Discord.DiscordText.AnimePriority(Anime.AnimePriority.First))
        };

        private readonly IInstanceSettingsService _instances;
        private readonly IBackendRepository _repository;

        protected MediaManagerBase(IInstanceSettingsService instances, IBackendRepository repository)
        {
            _instances = instances;
            _repository = repository;
        }

        public string Name => GetType().Name;

        public Type ConfigContract => typeof(BackendSettings);

        public virtual ProviderMessage Message => null;

        public ProviderDefinition Definition { get; set; }

        // No address to start from: inside Requestarr's container, localhost is Requestarr itself, so Prowlarr's usual
        // localhost default could never work. A new one lets new members in without waiting for an admin.
        public IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                yield return new BackendInstance
                {
                    Implementation = Name,
                    Settings = new BackendSettings { DefaultAccess = AccessState.AutoApprove }
                };
            }
        }

        private BackendInstance Instance => (BackendInstance)Definition;

        public ValidationResult Test()
        {
            var problem = Probe().Problem;

            return problem == null
                ? new ValidationResult()
                : new ValidationResult(new[] { new ValidationFailure(nameof(BackendSettings.Url), problem) });
        }

        // A list that needs the instance and can't load says why, under its field, never an empty list.
        public object RequestAction(string stage, IDictionary<string, string> query) => stage switch
        {
            MediaManagerActions.QualityProfiles => Options(Reached().QualityProfiles.Select(p => (p, p))),
            MediaManagerActions.RootFolders => Options(Reached().RootFolders.Select(f => (f, f))),
            MediaManagerActions.SeriesTypes => Options(SeriesTypeChoices.Select(t => (t, char.ToUpperInvariant(t[0]) + t[1..]))),
            MediaManagerActions.AnimePriorities => Options(AnimePriorityChoices),
            _ => null
        };

        // Reaches it with the settings in the form. The form holds the saved API key unless one was typed (Servarr's
        // form fills in the key it masks), so that key is left to the settings rules, which send it only to the saved
        // address.
        private InstanceTestResult Probe()
        {
            var input = InstanceInputs.Of(Instance);
            var saved = Instance.Id > 0 ? _repository.Find(Instance.Id) : null;

            if (saved != null && input.ApiKey == saved.Config.ApiKey)
            {
                input = input with { ApiKey = null };
            }

            return _instances.TestAsync(saved?.Id, input).GetAwaiter().GetResult();
        }

        private InstanceTestResult Reached()
        {
            var probe = Probe();

            return probe.Problem == null ? probe : throw new ValidationException(new[] { new ValidationFailure(nameof(BackendSettings.Url), probe.Problem) });
        }

        // A select's choices in the shape Servarr's provider forms read.
        private static object Options(IEnumerable<(string Value, string Name)> choices) =>
            new { options = choices.Select(c => new { value = c.Value, name = c.Name }).ToList() };
    }
}
