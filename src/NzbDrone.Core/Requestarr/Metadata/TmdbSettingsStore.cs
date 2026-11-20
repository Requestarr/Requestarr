using System;
using System.IO;
using System.Linq;
using NLog;
using NzbDrone.Common.Messaging;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Lifecycle;
using NzbDrone.Core.Localization;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Metadata
{
    // Where the key in use came from.
    public enum TmdbKeySource
    {
        None,
        ProvisioningFile,
        Environment,
        BuiltIn
    }

    // The Movie Database key, first that's set: the provisioning file (dev and QA), the server's environment
    // (REQUESTARR_TMDB_API_KEY, an admin's own key), then Requestarr's own key built into the image (the Image workflow
    // puts it there from a repository secret; a build without it, such as a fork's, has none). Never in the web app, the
    // database or config.xml, and never logged.
    public interface ITmdbSettingsStore
    {
        string ApiKey { get; }
        bool Enabled { get; }
        TmdbKeySource Source { get; }

        // Why the built-in key stopped working (HTTP 401, 429), or null while it works or isn't the one in use.
        string BuiltInProblem { get; }

        void Set(string apiKey);

        // What the last lookup said about the key: null when it worked. True when that changed whether the built-in key
        // has a problem, so the health warning is checked again.
        bool Report(string problem);
    }

    public class TmdbSettingsStore : ITmdbSettingsStore
    {
        public const string KeyVariable = "REQUESTARR_TMDB_API_KEY";

        // Written by the image build next to Requestarr's files; not in the repository.
        public const string BuiltInFile = "the-movie-database-key.txt";

        private readonly object _reportLock = new();
        private string _key;
        private string _builtInProblem;
        private Lazy<string> _builtIn = new(ReadBuiltIn);

        public Func<string, string> Variable { get; set; } = Environment.GetEnvironmentVariable;

        // Reads the built-in key once; swapped in tests.
        public Func<string> BuiltIn
        {
            get => () => _builtIn.Value;
            set => _builtIn = new Lazy<string>(value);
        }

        public string ApiKey => InUse().Key;

        public bool Enabled => ApiKey != null;

        public TmdbKeySource Source => InUse().Source;

        public string BuiltInProblem => Source == TmdbKeySource.BuiltIn ? _builtInProblem : null;

        public void Set(string apiKey)
        {
            _key = Clean(apiKey);
        }

        // Lookups run in parallel, so one at a time decides whether the state changed.
        public bool Report(string problem)
        {
            lock (_reportLock)
            {
                if (Source != TmdbKeySource.BuiltIn || problem == _builtInProblem)
                {
                    return false;
                }

                _builtInProblem = problem;
                return true;
            }
        }

        // The first source that has a key, or (null, None) when none has.
        private (string Key, TmdbKeySource Source) InUse() => new (string Key, TmdbKeySource Source)[]
        {
            (_key, TmdbKeySource.ProvisioningFile),
            (Clean(Variable(KeyVariable)), TmdbKeySource.Environment),
            (Clean(BuiltIn()), TmdbKeySource.BuiltIn)
        }.FirstOrDefault(s => s.Key != null);

        private static string Clean(string key) => string.IsNullOrWhiteSpace(key) ? null : key.Trim();

        // A file that can't be read counts as no built-in key, so it never stops another key from working.
        private static string ReadBuiltIn()
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, BuiltInFile);

                return File.Exists(path) ? File.ReadAllText(path) : null;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }
    }

    public class TmdbKeyChangedEvent : IEvent
    {
    }

    // System, Status: the built-in key was refused or rate limited, so search results show no cast until it works again
    // or the admin sets their own.
    [CheckOn(typeof(TmdbKeyChangedEvent))]
    public class TmdbKeyCheck : HealthCheckBase
    {
        private readonly ITmdbSettingsStore _settings;

        public TmdbKeyCheck(ITmdbSettingsStore settings, ILocalizationService localizationService)
            : base(localizationService)
        {
            _settings = settings;
        }

        public static string Message(string problem) =>
            $"Requestarr's built-in key for The Movie Database stopped working ({problem}), so search results and cards show no cast. " +
            $"To use your own key, set {TmdbSettingsStore.KeyVariable} on the container and restart Requestarr.";

        public override HealthCheck Check() =>
            _settings.BuiltInProblem is { } problem
                ? new HealthCheck(GetType(), HealthCheckResult.Warning, HealthCheckReason.TmdbBuiltInKeyFailing, Message(problem), "#the-movie-database-key")
                : new HealthCheck(GetType());
    }

    // Says once on start where the key in use comes from, never the key itself.
    public class TmdbKeySourceLog : IHandle<ApplicationStartedEvent>
    {
        private readonly ITmdbSettingsStore _settings;
        private readonly Logger _logger;

        public TmdbKeySourceLog(ITmdbSettingsStore settings, Logger logger)
        {
            _settings = settings;
            _logger = logger;
        }

        public void Handle(ApplicationStartedEvent message)
        {
            _logger.Info("Cast from The Movie Database: {0}", _settings.Source switch
            {
                TmdbKeySource.ProvisioningFile => "the provisioning file's key",
                TmdbKeySource.Environment => $"the key in {TmdbSettingsStore.KeyVariable}",
                TmdbKeySource.BuiltIn => "Requestarr's built-in key",
                _ => "no key, so results show the genre instead"
            });
        }
    }
}
