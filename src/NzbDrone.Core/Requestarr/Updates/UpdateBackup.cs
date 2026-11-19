using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Reflection;
using Dapper;
using FluentMigrator;
using NLog;
using NzbDrone.Common;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Common.Extensions;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore;

namespace Requestarr.Core.Updates
{
    // Who last used the main database: the Requestarr version that last ran on it (recorded on every start, null before
    // it was recorded) and its newest migration.
    public record DatabaseOrigin(Version LastRunBy, long Schema)
    {
        // The newest migration this build has.
        public static readonly long BuildSchema = typeof(DbFactory).Assembly.GetTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()?.Version ?? 0).Max();

        // The build as people know it (0.7.0005): MAJOR.MINOR.BUILD. A build's fourth part is only the time of day it was
        // made, so two dev or QA builds of the same number are the same here; their schema tells them apart.
        public static Version Build(Version version) => new(version.Major, version.Minor, Math.Max(0, version.Build));

        public bool IsNewerThan(Version build) => (LastRunBy != null && Build(LastRunBy) > Build(build)) || Schema > BuildSchema;

        public bool IsOlderThan(Version build) => LastRunBy == null || Build(LastRunBy) < Build(build) || Schema < BuildSchema;
    }

    public interface IUpdateBackup
    {
        // Before the main database is migrated (after a restore is put in place): a database last used by an older
        // Requestarr is backed up first, as an Update backup the web app lists under System, then Backup; one last used
        // by a newer Requestarr is left as it is and said so, so going back is a restore, never a crash.
        void BeforeMigrating(string connectionString, DatabaseType type);
    }

    public class UpdateBackup : IUpdateBackup
    {
        public const string LastRunKey = "lastrunversion";

        private const string BackupFolderKey = "backupfolder";
        private const string DefaultBackupFolder = "Backups";

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(UpdateBackup));

        private readonly IAppFolderInfo _appFolderInfo;
        private readonly IArchiveService _archive;

        public UpdateBackup(IAppFolderInfo appFolderInfo, IArchiveService archive)
        {
            _appFolderInfo = appFolderInfo;
            _archive = archive;
        }

        public void BeforeMigrating(string connectionString, DatabaseType type)
        {
            // Postgres is backed up by whoever runs it; Servarr's backups are SQLite's.
            if (type != DatabaseType.SQLite)
            {
                return;
            }

            using var connection = new SQLiteConnection(connectionString);
            connection.Open();

            if (Origin(connection) is not { } origin)
            {
                return;
            }

            if (origin.IsNewerThan(BuildInfo.Version))
            {
                Logger.Error(NewerDatabase.Message(origin));
                return;
            }

            if (origin.IsOlderThan(BuildInfo.Version))
            {
                BackUp(connection, origin);
            }
        }

        // A backup that fails (a full disk, a backup folder that's gone) is said in the log, and Requestarr starts all the
        // same: a restart loop would leave no way to fix it in the web app.
        private void BackUp(SQLiteConnection connection, DatabaseOrigin origin)
        {
            var from = origin.LastRunBy != null ? Shown(origin.LastRunBy) : "an earlier version";

            try
            {
                Logger.Info("Backed up the database before updating it from {0} to {1}: {2}", from, Shown(BuildInfo.Version), Backup(connection));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SQLiteException)
            {
                Logger.Error(ex, "Couldn't back up the database before updating it from {0} to {1}; Requestarr carries on. Take a backup now under System, then Backup.", from, Shown(BuildInfo.Version));
            }
        }

        public static string Shown(Version version) => Announcements.Changelog.Display(DatabaseOrigin.Build(version));

        // Null for a new database, which has nothing to back up.
        public static DatabaseOrigin Origin(SQLiteConnection connection)
        {
            if (connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'VersionInfo'") == 0)
            {
                return null;
            }

            var schema = connection.ExecuteScalar<long>("SELECT MAX(\"Version\") FROM \"VersionInfo\"");
            var lastRun = Version.TryParse(Setting(connection, LastRunKey), out var version) ? version : null;

            return new DatabaseOrigin(lastRun, schema);
        }

        private static string Setting(SQLiteConnection connection, string key) =>
            connection.ExecuteScalar<long>("SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = 'Config'") == 0
                ? null
                : connection.QueryFirstOrDefault<string>("SELECT \"Value\" FROM \"Config\" WHERE \"Key\" = @key", new { key });

        // A zip the web app's restore takes: the database (a consistent copy) and config.xml, in the backup folder's
        // "update" folder, named the way the web app lists backups.
        private string Backup(SQLiteConnection connection)
        {
            var configured = Setting(connection, BackupFolderKey);
            var root = string.IsNullOrWhiteSpace(configured) ? DefaultBackupFolder : configured;
            var folder = Path.Combine(Path.IsPathRooted(root) ? root : Path.Combine(_appFolderInfo.AppDataFolder, root), "update");
            var temp = Path.Combine(_appFolderInfo.TempFolder, "requestarr_update_backup");

            if (Directory.Exists(temp))
            {
                Directory.Delete(temp, true);
            }

            Directory.CreateDirectory(temp);
            Directory.CreateDirectory(folder);

            var database = Path.Combine(temp, "requestarr.db");
            connection.Execute($"VACUUM INTO '{database.Replace("'", "''")}'");

            var info = Path.Combine(temp, "INFO");
            File.WriteAllText(info, $"v{BuildInfo.Version}\n{DateTime.Now:yyyy-MM-dd HH:mm:ss}\nTaken before updating the database\n");
            var files = new List<string> { database, info };

            var configPath = _appFolderInfo.GetConfigPath();

            if (File.Exists(configPath))
            {
                var config = Path.Combine(temp, Path.GetFileName(configPath));
                File.Copy(configPath, config);
                files.Add(config);
            }

            var zip = Path.Combine(folder, $"requestarr_backup_v{BuildInfo.Version}_{DateTime.Now:yyyy.MM.dd_HH.mm.ss}.zip");
            _archive.CreateZip(zip, files);
            Directory.Delete(temp, true);

            return zip;
        }
    }

    public static class NewerDatabase
    {
        // The log line and the health check say the same thing, in plain words.
        public static string Message(DatabaseOrigin origin) =>
            $"This database was last used by a newer Requestarr ({(origin.LastRunBy != null ? UpdateBackup.Shown(origin.LastRunBy) : $"database version {origin.Schema}")}) " +
            $"than this one ({UpdateBackup.Shown(BuildInfo.Version)}), so some things may not work. To go back to this version, restore the backup Requestarr " +
            "took when you first moved past it: System, then Backup, the Update backup from just before that update, then Restore. Or go back to the newer build.";
    }
}
