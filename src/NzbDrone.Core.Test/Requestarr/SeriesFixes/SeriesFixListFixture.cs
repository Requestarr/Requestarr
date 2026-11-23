using System.Collections.Generic;
using FluentAssertions;
using Moq;
using NUnit.Framework;
using NzbDrone.Core.Test.Framework;
using Requestarr.Core.Audit;
using Requestarr.Core.SeriesFixes;

namespace Requestarr.Core.Test.SeriesFixes
{
    [TestFixture]
    public class SeriesFixListFixture : CoreTest<SeriesFixList>
    {
        private static SeriesFixInput Input(int tvdbId = 1, string shortName = null) =>
            new(tvdbId, "Show: The Return (2020)", shortName, new List<string>(), new List<string> { " Horizons ", "Horizons" }, "why", true);

        [SetUp]
        public void SetUp()
        {
            Mocker.GetMock<ISeriesFixRepository>().Setup(r => r.Upsert(It.IsAny<SeriesFix>())).Returns<SeriesFix>(f => f);
        }

        [Test]
        public void a_new_fix_gets_a_tag_safe_short_name_and_clean_terms()
        {
            var fix = Subject.Save(null, Input(), Actor.Web("admin")).Fix;

            fix.ShortName.Should().Be("show-the-return-2020");
            fix.MustNotContain.Should().Equal("Horizons");
        }

        [Test]
        public void a_second_fix_for_the_same_series_or_one_with_no_terms_is_refused()
        {
            Mocker.GetMock<ISeriesFixRepository>().Setup(r => r.ForSeries(1)).Returns(new SeriesFix { Id = 5, TvdbId = 1, Series = "Show" });

            Subject.Save(null, Input(), Actor.Web("admin")).Problem.Should().Be("Show already has a fix: change that one.");
            Subject.Save(null, Input(2) with { MustNotContain = new List<string>() }, Actor.Web("admin")).Problem.Should().Contain("at least one term");
        }

        [Test]
        public void a_shipped_fix_can_be_turned_off_but_not_deleted()
        {
            Mocker.GetMock<ISeriesFixRepository>().Setup(r => r.Find(5)).Returns(new SeriesFix { Id = 5, TvdbId = 1, Series = "Pokémon (1997)", Shipped = true });

            Subject.Delete(5, Actor.Web("admin")).Problem.Should().Contain("turn it off instead");
            Subject.Save(5, Input() with { Enabled = false }, Actor.Web("admin")).Fix.Enabled.Should().BeFalse();
            Mocker.GetMock<ISeriesFixRepository>().Verify(r => r.Delete(It.IsAny<int>()), Times.Never());
        }
    }
}
