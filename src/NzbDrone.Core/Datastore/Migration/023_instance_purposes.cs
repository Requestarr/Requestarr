using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text.Json.Nodes;
using FluentMigrator;
using NLog;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace NzbDrone.Core.Datastore.Migration
{
    // Requestarr: requests go to a Sonarr or Radarr by what is asked for, never by member. Each type's Default instance
    // (or, with none ticked, its first one turned on) is now for Everything, and every other one for nothing yet; the
    // Default flag goes. Each member's own instance goes too, and each one dropped is written to the log, so an admin can
    // see what changed. Requests already sent keep the instance they went to.
    [Migration(23)]
    public class instance_purposes : NzbDroneMigrationBase
    {
        // InstancePurpose as the settings store it: by name.
        private const string Everything = "everything";
        private const string Nothing = "nothing";

        private static readonly Logger Logger = NzbDroneLogger.GetLogger(typeof(instance_purposes));

        protected override void MainDbUpgrade()
        {
            Execute.WithConnection(SetPurposes);
            Execute.WithConnection(LogDroppedAssignments);
            Delete.Column("BackendId").FromTable("UserMediaAccess");
        }

        private static void SetPurposes(IDbConnection connection, IDbTransaction transaction)
        {
            var rows = Read(connection, transaction);

            foreach (var type in rows.GroupBy(r => r.Implementation))
            {
                var main = type.Where(r => r.Enable && IsDefault(r.Settings)).OrderBy(r => r.Id).FirstOrDefault()
                           ?? type.Where(r => r.Enable).OrderBy(r => r.Id).FirstOrDefault()
                           ?? type.OrderBy(r => r.Id).First();

                foreach (var row in type)
                {
                    row.Settings["purpose"] = row.Id == main.Id ? Everything : Nothing;
                    row.Settings.Remove("isDefault");
                    Write(connection, transaction, row);
                }
            }
        }

        private static bool IsDefault(JsonObject settings) =>
            settings.TryGetPropertyValue("isDefault", out var value) && value is JsonValue flag && flag.TryGetValue(out bool isDefault) && isDefault;

        private static List<Row> Read(IDbConnection connection, IDbTransaction transaction)
        {
            var rows = new List<Row>();

            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT \"Id\", \"Implementation\", \"Enable\", \"Settings\" FROM \"Backends\" WHERE \"Implementation\" IN ('Sonarr', 'Radarr') AND \"Deleted\" IS NULL";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var settings = JsonNode.Parse(reader.GetString(3)) as JsonObject ?? new JsonObject();
                rows.Add(new Row(reader.GetInt32(0), reader.GetString(1), reader.GetBoolean(2), settings));
            }

            return rows;
        }

        private static void Write(IDbConnection connection, IDbTransaction transaction, Row row)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "UPDATE \"Backends\" SET \"Settings\" = @settings WHERE \"Id\" = @id";
            AddParameter(command, "@settings", row.Settings.ToJsonString());
            AddParameter(command, "@id", row.Id);
            command.ExecuteNonQuery();
        }

        // "Member 12 (Alex): shows went to Sonarr 4K", for each member who had an instance of their own.
        private static void LogDroppedAssignments(IDbConnection connection, IDbTransaction transaction)
        {
            using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = "SELECT a.\"UserId\", u.\"DisplayName\", a.\"ContentType\", b.\"Name\" FROM \"UserMediaAccess\" a " +
                                  "LEFT JOIN \"RequestarrUsers\" u ON u.\"Id\" = a.\"UserId\" LEFT JOIN \"Backends\" b ON b.\"Id\" = a.\"BackendId\" WHERE a.\"BackendId\" IS NOT NULL";

            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                var name = reader.IsDBNull(1) ? "unknown" : reader.GetString(1);
                var instance = reader.IsDBNull(3) ? "deleted" : reader.GetString(3);
                Logger.Info("Routing by content: member {0} ({1}) no longer has their own instance for type {2} ({3})", reader.GetInt32(0), name, reader.GetInt32(2), instance);
            }
        }

        private static void AddParameter(IDbCommand command, string name, object value)
        {
            var parameter = command.CreateParameter();
            parameter.ParameterName = name;
            parameter.Value = value;
            command.Parameters.Add(parameter);
        }

        private sealed record Row(int Id, string Implementation, bool Enable, JsonObject Settings);
    }
}
