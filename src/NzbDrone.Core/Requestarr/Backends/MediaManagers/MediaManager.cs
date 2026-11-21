using System;
using System.Collections.Generic;
using System.Linq;
using FluentValidation.Results;
using NzbDrone.Core.ThingiProvider;
using Requestarr.Core.Settings;

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

        private static readonly (string Value, string Name)[] AnimePriorityChoices =
        {
            ("dub", "Dub"),
            ("sub", "Sub"),
            ("first", "Whoever asked first")
        };

        private readonly IInstanceSettingsService _instances;

        protected MediaManagerBase(IInstanceSettingsService instances)
        {
            _instances = instances;
        }

        public string Name => GetType().Name;

        public Type ConfigContract => typeof(BackendSettings);

        public virtual ProviderMessage Message => null;

        public ProviderDefinition Definition { get; set; }

        // Its usual address on the same machine, as Prowlarr's applications start from theirs.
        protected abstract string DefaultUrl { get; }

        public IEnumerable<ProviderDefinition> DefaultDefinitions
        {
            get
            {
                yield return new BackendInstance
                {
                    Implementation = Name,
                    Settings = new BackendSettings { Url = DefaultUrl }
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

        public object RequestAction(string stage, IDictionary<string, string> query) => stage switch
        {
            MediaManagerActions.QualityProfiles => Options(Probe().QualityProfiles.Select(p => (p, p))),
            MediaManagerActions.RootFolders => Options(Probe().RootFolders.Select(f => (f, f))),
            MediaManagerActions.SeriesTypes => Options(SeriesTypeChoices.Select(t => (t, char.ToUpperInvariant(t[0]) + t[1..]))),
            MediaManagerActions.AnimePriorities => Options(AnimePriorityChoices),
            _ => null
        };

        // Reaches it with the settings in the form; a saved instance's own API key when the form's address is its own.
        private InstanceTestResult Probe() =>
            _instances.TestAsync(Instance.Id > 0 ? Instance.Id : null, InstanceInputs.Of(Instance)).GetAwaiter().GetResult();

        // A select's choices in the shape Servarr's provider forms read.
        private static object Options(IEnumerable<(string Value, string Name)> choices) =>
            new { options = choices.Select(c => new { value = c.Value, name = c.Name }).ToList() };
    }
}
