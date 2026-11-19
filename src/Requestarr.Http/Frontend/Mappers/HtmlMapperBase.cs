using System;
using System.IO;
using System.Text.RegularExpressions;
using NLog;
using NzbDrone.Common.Disk;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.Configuration;
using Requestarr.Core.Announcements;

namespace Requestarr.Http.Frontend.Mappers
{
    public abstract class HtmlMapperBase : StaticResourceMapperBase
    {
        private readonly IDiskProvider _diskProvider;
        private readonly IConfigFileProvider _configFileProvider;
        private readonly IReleaseEnvironment _environment;
        private readonly Lazy<ICacheBreakerProvider> _cacheBreakProviderFactory;
        private static readonly Regex ReplaceRegex = new Regex(@"(?:(?<attribute>href|src)=\"")(?<path>.*?(?<extension>css|js|png|ico|ics|svg|json))(?:\"")(?:\s(?<nohash>data-no-hash))?", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        private string _urlBase;

        // The page as last made, and the icon set and theme it was made for: a change of either (the server's role, the
        // UI theme) makes it again.
        private (string For, string Text) _generated;

        protected HtmlMapperBase(IDiskProvider diskProvider,
                                 IConfigFileProvider configFileProvider,
                                 IReleaseEnvironment environment,
                                 Lazy<ICacheBreakerProvider> cacheBreakProviderFactory,
                                 Logger logger)
            : base(diskProvider, logger)
        {
            _diskProvider = diskProvider;
            _configFileProvider = configFileProvider;
            _environment = environment;
            _cacheBreakProviderFactory = cacheBreakProviderFactory;

            _urlBase = configFileProvider.UrlBase;
        }

        protected abstract string HtmlPath { get; }

        protected override Stream GetContentStream(string filePath)
        {
            var text = GetHtmlText();

            var stream = new MemoryStream();
            var writer = new StreamWriter(stream);
            writer.Write(text);
            writer.Flush();
            stream.Position = 0;
            return stream;
        }

        protected virtual string GetHtmlText()
        {
            var madeFor = $"{_environment.IconSet}:{_configFileProvider.Theme}";

            if (RuntimeInfo.IsProduction && _generated.For == madeFor)
            {
                return _generated.Text;
            }

            var text = WebIcons.Apply(_diskProvider.ReadAllText(HtmlPath), _environment, _configFileProvider);
            var cacheBreakProvider = _cacheBreakProviderFactory.Value;

            text = ReplaceRegex.Replace(text, match =>
            {
                string url;

                if (match.Groups["nohash"].Success)
                {
                    url = match.Groups["path"].Value;
                }
                else
                {
                    url = cacheBreakProvider.AddCacheBreakerToPath(match.Groups["path"].Value);
                }

                return $"{match.Groups["attribute"].Value}=\"{_urlBase}{url}\"";
            });

            text = text.Replace("__URL_BASE__", _urlBase);

            _generated = (madeFor, text);

            return text;
        }
    }
}
