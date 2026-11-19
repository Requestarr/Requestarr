using System;
using System.Collections.Generic;
using System.Linq;
using Dapper;
using NzbDrone.Core.Datastore;
using NzbDrone.Core.Messaging.Events;

namespace Requestarr.Core.Users
{
    public interface IRequestarrUserRepository : IBasicRepository<RequestarrUser>
    {
        RequestarrUser FindByChannel(string channelId);

        // Someone known only from an import, seen in the server now: Imported, unless something else (an onboarding,
        // say) changed them meanwhile. True when they changed.
        bool MarkInServer(int userId, DateTime at);
    }

    public class RequestarrUserRepository : BasicRepository<RequestarrUser>, IRequestarrUserRepository
    {
        public RequestarrUserRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public RequestarrUser FindByChannel(string channelId)
        {
            return Query(u => u.PrivateChannelId == channelId).FirstOrDefault();
        }

        public bool MarkInServer(int userId, DateTime at)
        {
            using var conn = _database.OpenConnection();

            return conn.Execute(
                "UPDATE \"RequestarrUsers\" SET \"Status\" = @Imported, \"StatusChanged\" = @At WHERE \"Id\" = @Id AND \"Status\" IN (@Left, @Elsewhere)",
                new { Imported = (int)UserStatus.Imported, At = at, Id = userId, Left = (int)UserStatus.ImportedLeft, Elsewhere = (int)UserStatus.ImportedElsewhere }) > 0;
        }
    }

    public interface IUserIdentityRepository : IBasicRepository<UserIdentity>
    {
        UserIdentity Find(string provider, string externalId);
        List<UserIdentity> GetForUser(int userId);
    }

    public class UserIdentityRepository : BasicRepository<UserIdentity>, IUserIdentityRepository
    {
        public UserIdentityRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public UserIdentity Find(string provider, string externalId)
        {
            return Query(i => i.Provider == provider && i.ExternalId == externalId).SingleOrDefault();
        }

        public List<UserIdentity> GetForUser(int userId)
        {
            return Query(i => i.UserId == userId);
        }
    }

    public interface IUserMediaAccessRepository : IBasicRepository<UserMediaAccess>
    {
        List<UserMediaAccess> GetForUser(int userId);
    }

    public class UserMediaAccessRepository : BasicRepository<UserMediaAccess>, IUserMediaAccessRepository
    {
        public UserMediaAccessRepository(IMainDatabase database, IEventAggregator eventAggregator)
            : base(database, eventAggregator)
        {
        }

        public List<UserMediaAccess> GetForUser(int userId)
        {
            return Query(a => a.UserId == userId);
        }
    }
}
