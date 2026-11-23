using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.SeriesFixes;

namespace Requestarr.Core.Test.SeriesFixes
{
    // A fix is built in Sonarr only when its series is requested: its own tag and release profile, created once, reused
    // after, brought back in line with the list, and never anything that isn't Requestarr's.
    [TestFixture]
    public class SeriesFixServiceFixture : CoreTest<SeriesFixService>
    {
        private const int Pokemon = 76703;
        private readonly BackendInstance _sonarr = new() { Id = 1, Name = "Sonarr", Implementation = BackendImplementations.Sonarr, Enable = true, Settings = new BackendSettings() };
        private List<NamedItem> _tags;
        private List<ReleaseProfile> _profiles;
        private SeriesFix _fix;

        [SetUp]
        public void SetUp()
        {
            _fix = new SeriesFix { Id = 1, TvdbId = Pokemon, Series = "Pokémon (1997)", ShortName = "pokemon", MustNotContain = new List<string> { "Horizons" }, Reason = "keeps Horizons out", Enabled = true };
            _tags = new List<NamedItem> { new(3, "pokemon") };
            _profiles = new List<ReleaseProfile> { new() { Id = 7, Name = "Owner's pokemon", Ignored = new List<string> { "Horizons" }, Tags = new List<int> { 3 } } };

            Mocker.GetMock<ISeriesFixRepository>().Setup(r => r.ForSeries(Pokemon)).Returns(() => _fix);
            var sonarr = Mocker.GetMock<ISonarrApi>();
            sonarr.Setup(s => s.GetTagsAsync(It.IsAny<BackendSettings>())).ReturnsAsync(() => _tags.ToList());
            sonarr.Setup(s => s.CreateTagAsync(It.IsAny<BackendSettings>(), It.IsAny<string>())).ReturnsAsync((BackendSettings _, string label) =>
            {
                var tag = new NamedItem(10 + _tags.Count, label);
                _tags.Add(tag);
                return tag;
            });
            sonarr.Setup(s => s.GetReleaseProfilesAsync(It.IsAny<BackendSettings>())).ReturnsAsync(() => _profiles.ToList());
            sonarr.Setup(s => s.SaveReleaseProfileAsync(It.IsAny<BackendSettings>(), It.IsAny<ReleaseProfile>())).ReturnsAsync((BackendSettings _, ReleaseProfile p) =>
            {
                _profiles.RemoveAll(x => x.Id == p.Id && p.Id != 0);
                p.Id = p.Id == 0 ? 20 + _profiles.Count : p.Id;
                _profiles.Add(p);
                return p;
            });
        }

        [Test]
        public async Task a_listed_series_gets_its_own_tag_and_profile_once_and_the_owners_are_left_alone()
        {
            var first = await Subject.BuildAsync(_sonarr, Pokemon);
            var second = await Subject.BuildAsync(_sonarr, Pokemon);

            first.TagId.Should().Be(second.TagId);
            _tags.Select(t => t.Name).Should().Equal("pokemon", "requestarr-fix-pokemon");
            var ours = _profiles.Single(p => p.Name == "Requestarr fix: Pokémon (1997)");
            ours.Ignored.Should().Equal("Horizons");
            ours.Tags.Should().Equal(first.TagId);
            _profiles.Single(p => p.Id == 7).Tags.Should().Equal(3);
            Mocker.GetMock<ISonarrApi>().Verify(s => s.SaveReleaseProfileAsync(It.IsAny<BackendSettings>(), It.IsAny<ReleaseProfile>()), Times.Once());
        }

        [Test]
        public async Task an_edited_fix_brings_its_profile_back_in_line()
        {
            await Subject.BuildAsync(_sonarr, Pokemon);
            _fix.MustNotContain = new List<string> { "Horizons", "Journeys" };

            await Subject.BuildAsync(_sonarr, Pokemon);

            _profiles.Single(p => p.Name == "Requestarr fix: Pokémon (1997)").Ignored.Should().Equal("Horizons", "Journeys");
        }

        [Test]
        public async Task nothing_is_built_for_a_series_without_a_fix_one_turned_off_or_on_radarr()
        {
            (await Subject.BuildAsync(_sonarr, 12345)).Should().BeNull();
            (await Subject.BuildAsync(new BackendInstance { Implementation = BackendImplementations.Radarr, Settings = new BackendSettings() }, Pokemon)).Should().BeNull();
            _fix.Enabled = false;
            (await Subject.BuildAsync(_sonarr, Pokemon)).Should().BeNull();

            Mocker.GetMock<ISonarrApi>().Verify(s => s.CreateTagAsync(It.IsAny<BackendSettings>(), It.IsAny<string>()), Times.Never());
        }

        [Test]
        public async Task a_series_already_in_sonarr_gets_the_tag_added_beside_its_own()
        {
            var applied = await Subject.ApplyToLibraryAsync(_sonarr, Pokemon, 99, new List<int> { 3 });

            Mocker.GetMock<ISonarrApi>().Verify(s => s.SetSeriesTagsAsync(It.IsAny<BackendSettings>(), 99, It.Is<IReadOnlyCollection<int>>(t => t.SequenceEqual(new[] { 3, applied.TagId }))), Times.Once());
        }
    }
}
