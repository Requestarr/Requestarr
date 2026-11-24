using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Backends;
using Requestarr.Core.Discord;
using Requestarr.Core.Provisioning;
using Requestarr.Core.Test.Framework;
using Requestarr.Core.Users;

namespace Requestarr.Core.Test.Anime
{
    // Where anime goes and how it's added: a separate Anime Sonarr (a sub or dub instance) takes anime whoever asks,
    // and anime is always added with Anime numbering, whatever the instance's own series type.
    [TestFixture]
    public class AnimeInstancesFixture : AnimeScenario
    {
        private void TwoSonarrs()
        {
            Mocker.Resolve<IBackendService>().SyncFromProvisioning(new BackendsProvisioning
            {
                Radarr = new List<BackendInstanceProvisioning> { new() { Name = "Radarr", Url = "http://radarr-test:7878", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", Default = true } },
                Sonarr = new List<BackendInstanceProvisioning>
                {
                    new() { Name = "Sonarr", Url = "http://sonarr-test:8989", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/media", Default = true },
                    new() { Name = "Sonarr Sub", Url = "http://sonarr-sub:8989", ApiKey = "k", QualityProfile = "HD-1080p", RootFolder = "/data/anime-sub", Anime = new() { Priority = "sub" } }
                }
            });
            Sonarr.RootFolders.Add("/data/anime-sub");
        }

        [Test]
        public async Task anime_goes_to_the_anime_sonarr_whoever_asks()
        {
            TwoSonarrs();
            var repo = Mocker.Resolve<IBackendRepository>();
            var anime = repo.FindByName("Sonarr Sub");
            anime.Config.Purpose = InstancePurpose.Anime;
            repo.Update(anime);

            await RequestAnime(Alice, "d");

            Sonarr.Adds.Select(a => (a.Url, a.RootFolder)).Should().Equal(("http://sonarr-sub:8989", "/data/anime-sub"));
            RequestStore.All().Single().BackendId.Should().Be(anime.Id, "the title is anime, so the Anime Sonarr takes it, not the member");
        }

        [Test]
        public async Task with_the_sonarr_for_all_other_shows_off_regular_shows_are_paused_and_anime_still_goes()
        {
            TwoSonarrs();
            var repo = Mocker.Resolve<IBackendRepository>();
            var anime = repo.FindByName("Sonarr Sub");
            anime.Config.Purpose = InstancePurpose.Anime;
            repo.Update(anime);
            var main = repo.FindByName("Sonarr");
            main.Enable = false;
            repo.Update(main);
            await Mocker.Resolve<IBackendService>().CheckAllAsync();

            var regular = await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{Severance}"));
            regular.LastReply.Content.Should().Contain(DiscordText.RequestsPausedForNow(ContentType.Tv), "nothing hands regular shows to the Anime Sonarr");

            await RequestAnime(Alice, "d");
            RequestStore.All().Single().BackendId.Should().Be(anime.Id);
        }

        [Test]
        public async Task anime_sent_before_an_anime_sonarr_existed_stays_where_it_went()
        {
            TwoSonarrs();
            await RequestAnime(Alice, "d");
            var repo = Mocker.Resolve<IBackendRepository>();
            var anime = repo.FindByName("Sonarr Sub");
            anime.Config.Purpose = InstancePurpose.Anime;
            repo.Update(anime);

            await RequestAnime(Bob, "d");

            RequestStore.All().Should().ContainSingle("Bob follows Alice's request instead of adding the show again")
                .Which.BackendId.Should().Be(repo.FindByName("Sonarr").Id);
            Sonarr.Adds.Should().ContainSingle();
        }

        [Test]
        public void the_setup_guide_warns_about_separate_instances()
        {
            var guide = File.ReadAllText(RepoFiles.Path("docs", "setup-guide.md"));

            guide.Should().Contain("different root folders").And.Contain("appears twice");
        }

        [Test]
        public async Task anime_is_always_added_as_anime_and_other_tv_uses_the_instances_type()
        {
            var repo = Mocker.Resolve<IBackendRepository>();
            var instance = SonarrInstance;
            instance.Config.SeriesType = "standard";
            repo.Update(instance);

            await RequestAnime(Alice, "d");
            await Click(Bob, $"req-tv-all:{Severance}");

            Sonarr.Adds.Single(a => a.Item.TvdbId == OnePiece).SeriesType.Should().Be("anime");
            Sonarr.Adds.Single(a => a.Item.TvdbId == Severance).SeriesType.Should().Be("standard");
        }

        [Test]
        public async Task anime_stays_anime_type_with_sub_dub_turned_off_and_gets_no_tag()
        {
            Priority("dub", enabled: false);

            var card = (await Harness.SendAsync(Search(Alice, "tv", $"tvdb:{OnePiece}"))).LastReply;
            card.Buttons.Select(b => b.CustomId).Should().Contain($"req-tv-all:{OnePiece}");
            await Click(Alice, $"req-tv-all:{OnePiece}");

            Sonarr.Adds.Single().SeriesType.Should().Be("anime");
            Sonarr.Adds.Single().Tags.Should().BeEmpty();
            RequestFor().Language.Should().BeNull();
        }

        [Test]
        public async Task a_series_the_admin_switched_to_standard_is_never_switched_back()
        {
            var tags = await SetUp();
            var id = Sonarr.AddToLibrary(OnePiece, "standard", tags.Sub);

            await AskLanguage(Bob, "d");

            Sonarr.SeriesTypes[id].Should().Be("standard");
            Sonarr.SeriesTags[id].Should().Equal(tags.Dub);
        }
    }
}
