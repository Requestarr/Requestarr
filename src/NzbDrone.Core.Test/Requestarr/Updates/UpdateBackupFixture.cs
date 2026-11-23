using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Dapper;
using FluentAssertions;
using Moq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Http;
using NzbDrone.Core.Configuration;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.HealthCheck;
using NzbDrone.Core.Test.Framework;
using NzbDrone.Test.Common;
using Requestarr.Core.Announcements;
using Requestarr.Core.Backends;
using Requestarr.Core.Updates;

namespace Requestarr.Core.Test.Updates
{
    // Updating on request without a shell: a backup before a newer build touches the database, and an older build on a
    // newer database warns instead of failing.
    [TestFixture]
    public class UpdateBackupFixture : CoreTest<UpdateBackup>
    {
        private string _appData;
        private string _database;

        [SetUp]
        public void Setup()
        {
            _appData = Path.Combine(TempFolder, "appdata");
            Directory.CreateDirectory(_appData);
            _database = Path.Combine(_appData, "requestarr.db");
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.AppDataFolder).Returns(_appData);
            Mocker.GetMock<IAppFolderInfo>().SetupGet(a => a.TempFolder).Returns(Path.Combine(TempFolder, "temp"));
            Mocker.SetConstant<IArchiveService>(new ArchiveService(TestLogger));
            File.WriteAllText(Path.Combine(_appData, "config.xml"), "<Config />");
        }

        private string ConnectionString => $"Data Source={_database};Version=3;Pooling=False";

        private void GivenDatabase(long schema, string lastRun, string backupFolder = null)
        {
            using var connection = new SQLiteConnection(ConnectionString);
            connection.Open();
            connection.Execute("CREATE TABLE \"VersionInfo\" (\"Version\" INTEGER); CREATE TABLE \"Config\" (\"Key\" TEXT, \"Value\" TEXT); INSERT INTO \"VersionInfo\" VALUES (@schema)", new { schema });
            connection.Execute("INSERT INTO \"Config\" VALUES ('lastrunversion', @lastRun), ('backupfolder', @backupFolder)", new { lastRun, backupFolder });
        }

        private List<string> Backups(string folder = null) =>
            Directory.Exists(folder ?? Path.Combine(_appData, "Backups", "update")) ? Directory.GetFiles(folder ?? Path.Combine(_appData, "Backups", "update")).ToList() : new List<string>();

        [Test]
        public void a_database_an_older_build_used_is_backed_up_first_as_an_update_backup_the_web_app_can_restore()
        {
            GivenDatabase(DatabaseOrigin.BuildSchema - 1, "0.0.0.0");

            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);

            var backup = Backups().Single();
            Path.GetFileName(backup).Should().MatchRegex(NzbDrone.Core.Backup.BackupService.BackupFileRegex.ToString());
            using var zip = ZipFile.OpenRead(backup);
            zip.Entries.Select(e => e.Name).Should().BeEquivalentTo("requestarr.db", "config.xml", "INFO");
        }

        [Test]
        public void a_database_from_before_the_version_was_recorded_or_one_a_migration_will_change_is_backed_up_too()
        {
            GivenDatabase(DatabaseOrigin.BuildSchema - 1, null);

            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);

            Backups().Should().HaveCount(1);
        }

        [Test]
        public void the_same_build_again_or_a_new_install_takes_no_backup()
        {
            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);
            Backups().Should().BeEmpty("a new database has nothing to keep");

            File.Delete(_database);
            GivenDatabase(DatabaseOrigin.BuildSchema, BuildInfo.Version.ToString());
            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);
            Backups().Should().BeEmpty();
        }

        [Test]
        public void a_backup_folder_set_in_the_web_app_is_used()
        {
            var folder = Path.Combine(TempFolder, "elsewhere");
            GivenDatabase(DatabaseOrigin.BuildSchema - 1, "0.0.0.0", folder);

            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);

            Backups(Path.Combine(folder, "update")).Should().HaveCount(1);
        }

        [Test]
        public void a_database_a_newer_build_used_is_left_as_it_is_and_said_so_in_plain_words()
        {
            GivenDatabase(DatabaseOrigin.BuildSchema, "999.0");

            Subject.BeforeMigrating(ConnectionString, DatabaseType.SQLite);

            Backups().Should().BeEmpty();
            ExceptionVerification.ExpectedErrors(1);
            NewerDatabase.Message(new DatabaseOrigin(new Version(999, 0), 16)).Should().Contain("System, then Backup, the Update backup from just before that update, then Restore");
        }

        [Test]
        public void two_builds_of_the_same_number_are_the_same_version_whatever_time_they_were_built()
        {
            var build = new Version(0, 7, 5, 16200);
            var sameNumberEarlierInTheDay = new DatabaseOrigin(new Version(0, 7, 5, 36000), DatabaseOrigin.BuildSchema);

            sameNumberEarlierInTheDay.IsNewerThan(build).Should().BeFalse();
            sameNumberEarlierInTheDay.IsOlderThan(build).Should().BeFalse();
            new DatabaseOrigin(new Version(0, 7, 4, 50000), DatabaseOrigin.BuildSchema).IsOlderThan(build).Should().BeTrue();
            (sameNumberEarlierInTheDay with { Schema = DatabaseOrigin.BuildSchema - 1 }).IsOlderThan(build).Should().BeTrue("a migration still to run takes a backup");
            UpdateBackup.Shown(build).Should().Be("0.7.0005");
        }

        [Test]
        public void the_web_app_warns_while_the_database_is_newer_than_this_build()
        {
            var check = new NewerDatabaseCheck(Mocker.GetMock<IMainDatabase>().Object, Mocker.GetMock<IConfigService>().Object, Mocker.GetMock<NzbDrone.Core.Localization.ILocalizationService>().Object);
            Mocker.GetMock<IMainDatabase>().SetupGet(d => d.Migration).Returns((int)DatabaseOrigin.BuildSchema);

            Mocker.GetMock<IConfigService>().SetupGet(c => c.LastRunVersion).Returns("999.0");
            var newer = check.Check();
            newer.Type.Should().Be(HealthCheckResult.Error);
            newer.Message.Should().Contain("last used by a newer Requestarr (");

            Mocker.GetMock<IConfigService>().SetupGet(c => c.LastRunVersion).Returns(BuildInfo.Version.ToString());
            check.Check().Type.Should().Be(HealthCheckResult.Ok);

            Mocker.GetMock<IMainDatabase>().SetupGet(d => d.Migration).Returns((int)DatabaseOrigin.BuildSchema + 1);
            check.Check().Type.Should().Be(HealthCheckResult.Error, "a migration this build doesn't have");
        }

        [Test]
        public void each_start_records_its_version_but_never_lowers_it()
        {
            var config = new Mock<IConfigService>();
            config.SetupProperty(c => c.LastRunVersion, string.Empty);

            new LastRunVersion(config.Object).Handle(null);
            config.Object.LastRunVersion.Should().Be(DatabaseOrigin.Build(BuildInfo.Version).ToString());

            config.Object.LastRunVersion = "999.0";
            new LastRunVersion(config.Object).Handle(null);
            config.Object.LastRunVersion.Should().Be("999.0", "the warning stays until the backup is restored");
        }

        [Test]
        public void each_environment_names_its_own_webhook_connection()
        {
            ArrApiBase.WebhookNameFor("beta").Should().Be("Requestarr Beta (availability)");
            ArrApiBase.WebhookNameFor("qa").Should().Be("Requestarr QA (availability)");
            ArrApiBase.WebhookNameFor("dev").Should().Be("Requestarr Dev (availability)");
            ArrApiBase.WebhookNameFor(ReleaseEnvironment.Production).Should().Be(ArrApiBase.WebhookName);
        }

        [Test]
        public async Task a_connection_under_the_old_name_that_calls_this_requestarr_is_renamed_not_doubled()
        {
            const string Url = "http://requestarr.test:19797/api/v1/webhook/1?token=ours";
            var sent = new List<HttpRequest>();
            var http = new Mock<IHttpClient>();
            http.Setup(h => h.GetAsync<List<JObject>>(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest r) => Response<List<JObject>>(r, "[" +
                    "{\"id\":4,\"name\":\"Requestarr (availability)\",\"implementation\":\"Webhook\",\"fields\":[{\"name\":\"url\",\"value\":\"http://production:9797/hook\"}]}," +
                    $"{{\"id\":5,\"name\":\"Requestarr (availability)\",\"implementation\":\"Webhook\",\"fields\":[{{\"name\":\"url\",\"value\":\"{Url}\"}},{{\"name\":\"method\",\"value\":1}}]}}]"));
            http.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequest>())).Callback<HttpRequest>(sent.Add)
                .ReturnsAsync((HttpRequest r) => new HttpResponse(r, new HttpHeader(), new System.Net.CookieCollection(), "{}", 0, HttpStatusCode.OK));

            var change = await new RadarrApi(http.Object).EnsureWebhookAsync(new BackendSettings { Url = "http://arr.test", ApiKey = "k" }, "Requestarr Beta (availability)", Url);

            change.Should().Be(WebhookChange.Updated);
            sent.Should().ContainSingle(r => r.Method.Method == "PUT" && r.Url.FullUri.Contains("notification/5"), "ours, under the old name, is renamed; the other Requestarr's is left alone");
            JObject.Parse(System.Text.Encoding.UTF8.GetString(sent.Single().ContentData))["name"].Value<string>().Should().Be("Requestarr Beta (availability)");
        }

        [Test]
        public void a_build_that_needs_a_new_compose_file_links_it_for_admins_only()
        {
            var version = new ChangelogVersion(new Version(0, 7, 5), new[] { "**Users:** a line." }, new[] { $"{VersionCards.ComposeLead} the beta's file gains a setting." });

            VersionCards.Collapsed(version, VersionAudience.Admins, 2026).Reply.Embeds[0].Description
                .Should().Contain("This build needs a new compose file").And.Contain("https://github.com/Requestarr/Requestarr/blob/v0.7.0005/deploy/beta/compose.yaml");
            VersionCards.Collapsed(version, VersionAudience.Users, 2026).Reply.Embeds[0].Description.Should().NotContain("compose");
            VersionCards.Full(version, VersionAudience.Admins)[0].Cards.Last().Text.Should().Contain("[The new compose file](https://github.com/Requestarr/Requestarr/blob/v0.7.0005/deploy/beta/compose.yaml)");
        }

        // Beta and production share a Sonarr and Radarr: a beta left on Production never takes over production's
        // connection (same name, another Requestarr's address and token), and removing or testing touches only its own.
        [Test]
        public async Task another_requestarrs_connection_is_never_taken_over_removed_or_tested()
        {
            var sent = new List<HttpRequest>();
            var http = new Mock<IHttpClient>();
            http.Setup(h => h.GetAsync<List<JObject>>(It.IsAny<HttpRequest>()))
                .ReturnsAsync((HttpRequest r) => Response<List<JObject>>(r, "[" +
                    "{\"id\":4,\"name\":\"Requestarr (availability)\",\"implementation\":\"Webhook\",\"fields\":[{\"name\":\"url\",\"value\":\"http://nas:9797/api/v1/webhook/1?token=production\"}]}]"));
            http.Setup(h => h.ExecuteAsync(It.IsAny<HttpRequest>())).Callback<HttpRequest>(sent.Add)
                .ReturnsAsync((HttpRequest r) => new HttpResponse(r, new HttpHeader(), new System.Net.CookieCollection(), "{}", 0, HttpStatusCode.OK));
            var api = new SonarrApi(http.Object);
            var settings = new BackendSettings { Url = "http://arr.test", ApiKey = "k" };

            var ensure = () => api.EnsureWebhookAsync(settings, ArrApiBase.WebhookName, "http://nas:19797/api/v1/webhook/1?token=beta");

            (await ensure.Should().ThrowAsync<WebhookNameTakenException>()).Which.Message.Should().Contain("another Requestarr's connection").And.Contain("install it from the beta image");
            (await api.RemoveWebhookAsync(settings, "beta")).Should().BeFalse();
            await api.Awaiting(a => a.TestWebhookAsync(settings, "beta")).Should().ThrowAsync<BackendException>();
            sent.Should().BeEmpty("production's connection is left exactly as it was");
        }

        private static HttpResponse<T> Response<T>(HttpRequest request, string json)
            where T : new() =>
            new(new HttpResponse(request, new HttpHeader(), new System.Net.CookieCollection(), json, 0, HttpStatusCode.OK));
    }
}
