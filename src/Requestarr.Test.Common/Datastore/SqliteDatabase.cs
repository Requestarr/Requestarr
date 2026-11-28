using System;
using System.IO;
using NUnit.Framework;
using NzbDrone.Core.Datastore.Migration.Framework;

namespace Requestarr.Test.Common.Datastore
{
    public static class SqliteDatabase
    {
        public static string GetCachedDb(MigrationType type)
        {
            // One per test run: runs side by side (the automated run's lanes) share the test folder.
            return Path.Combine(TestContext.CurrentContext.TestDirectory, $"cached_{type}_{Environment.ProcessId}.db");
        }
    }
}
