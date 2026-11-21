using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using FluentValidation;
using FluentValidation.Results;
using NLog;
using NzbDrone.Core.Messaging.Events;
using NzbDrone.Core.ThingiProvider;
using NzbDrone.Core.ThingiProvider.Events;
using Requestarr.Core.Audit;
using Requestarr.Core.Settings;

namespace Requestarr.Core.Backends.MediaManagers
{
    public interface IMediaManagerFactory : IProviderFactory<IMediaManager, BackendInstance>
    {
    }

    // The Sonarr and Radarr instances as Servarr providers, as Prowlarr's ApplicationFactory keeps its applications.
    // Adding or changing one goes through the settings rules the pages used (InstanceSettingsService): refused on a
    // server with a provisioning file, checked, its API key kept when left as shown, and written in the change record
    // as the admin who made it (ActingAdmin).
    public class MediaManagerFactory : ProviderFactory<IMediaManager, BackendInstance>, IMediaManagerFactory
    {
        private readonly IInstanceSettingsService _instances;
        private readonly IEventAggregator _eventAggregator;

        public MediaManagerFactory(IBackendRepository repository,
                                   IEnumerable<IMediaManager> providers,
                                   IServiceProvider container,
                                   IEventAggregator eventAggregator,
                                   IInstanceSettingsService instances,
                                   Logger logger)
            : base(repository, providers, container, eventAggregator, logger)
        {
            _instances = instances;
            _eventAggregator = eventAggregator;
        }

        // Lidarr instances have no media manager yet (music comes later): they stay, kept out of this list.
        protected override bool RemovesMissingImplementations => false;

        public override List<BackendInstance> All() =>
            base.All().Where(b => _providers.Any(p => p.Name == b.Implementation)).ToList();

        public override BackendInstance Create(BackendInstance definition)
        {
            var saved = Save(null, definition);
            _eventAggregator.PublishEvent(new ProviderAddedEvent<IMediaManager>(saved));

            return saved;
        }

        public override void Update(BackendInstance definition)
        {
            var saved = Save(definition.Id, definition);
            _eventAggregator.PublishEvent(new ProviderUpdatedEvent<IMediaManager>(saved));
        }

        private BackendInstance Save(int? id, BackendInstance definition)
        {
            var result = _instances.SaveAsync(id, InstanceInputs.Of(definition), ActingAdmin.Current).GetAwaiter().GetResult();

            if (result.Problem != null)
            {
                throw new ValidationException(new[] { new ValidationFailure(string.Empty, result.Problem) });
            }

            return Get(result.Instance.Id);
        }
    }

    // Who is adding or changing a media manager, for the change record: set by the web app's handler for the request
    // (the provider factory's Create and Update take only the definition). Nobody set: the system.
    public static class ActingAdmin
    {
        private static readonly AsyncLocal<Actor> Acting = new();

        public static Actor Current
        {
            get => Acting.Value ?? Actor.System;
            set => Acting.Value = value;
        }
    }
}
