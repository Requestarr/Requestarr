using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Audit
{
    public class AuditEntry : ModelBase
    {
        public int? ActorUserId { get; set; }
        public string Actor { get; set; }
        public string Action { get; set; }
        public string TargetType { get; set; }
        public string TargetId { get; set; }
        public DateTime Timestamp { get; set; }
        public string Detail { get; set; }

        // When Requestarr wrote it, which the trim goes by. Timestamp is when it happened: for an entry an import wrote,
        // the day it happened in the old channel, sometimes years before.
        public DateTime Recorded { get; set; } = DateTime.UtcNow;
    }

    // Who did something: a Discord member (admin or user), an admin in the web app, or Requestarr itself.
    public record Actor(string Description, int? UserId = null)
    {
        public const string WebPrefix = "web";

        public static readonly Actor System = new("system");

        // The automated checks (QA checks recording their results on the QA checklist) and automated housekeeping.
        public static readonly Actor Automation = new("automation");

        public static Actor Discord(ulong discordUserId, string username, int? userId = null) => new($"discord:{discordUserId} ({username})", userId);

        // The name they signed in with; none when sign-in is off for local addresses.
        public static Actor Web(string username) => new(string.IsNullOrWhiteSpace(username) ? WebPrefix : $"{WebPrefix}:{username}");
    }

    public interface IAuditRepository : IBasicRepository<AuditEntry>
    {
        List<AuditEntry> ForTarget(string targetType, string targetId);

        // Newest first.
        List<AuditEntry> ForTargetTypes(string first, string second);

        List<AuditEntry> ForAction(string action);

        // Deletes the entries recorded before the cutoff, except those of the action kept; how many went.
        int DeleteOlderThan(DateTime cutoff, string kept);
    }

    public class AuditRepository : BasicRepository<AuditEntry>, IAuditRepository
    {
        public AuditRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<AuditEntry> ForTarget(string targetType, string targetId)
        {
            return Query(a => a.TargetType == targetType && a.TargetId == targetId).OrderBy(a => a.Id).ToList();
        }

        public List<AuditEntry> ForTargetTypes(string first, string second)
        {
            return Query(a => a.TargetType == first || a.TargetType == second).OrderByDescending(a => a.Id).ToList();
        }

        public List<AuditEntry> ForAction(string action)
        {
            return Query(a => a.Action == action);
        }

        public int DeleteOlderThan(DateTime cutoff, string kept)
        {
            var sql = Builder().Where<AuditEntry>(a => a.Recorded < cutoff && a.Action != kept).AddDeleteTemplate(typeof(AuditEntry));

            using var conn = _database.OpenConnection();
            return conn.Execute(sql.RawSql, sql.Parameters);
        }
    }

    public interface IAuditService
    {
        void Record(Actor actor, string action, string targetType, string targetId, string detail = null);
    }

    public class AuditService : IAuditService
    {
        private readonly IAuditRepository _repository;

        public AuditService(IAuditRepository repository)
        {
            _repository = repository;
        }

        public void Record(Actor actor, string action, string targetType, string targetId, string detail = null)
        {
            _repository.Insert(new AuditEntry
            {
                ActorUserId = actor.UserId,
                Actor = actor.Description,
                Action = action,
                TargetType = targetType,
                TargetId = targetId,
                Timestamp = DateTime.UtcNow,
                Detail = detail
            });
        }
    }
}
