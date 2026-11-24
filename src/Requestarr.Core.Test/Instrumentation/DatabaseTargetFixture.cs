using System;
using System.Threading;
using FluentAssertions;
using NLog;
using NUnit.Framework;
using NzbDrone.Common.Instrumentation;
using NzbDrone.Core.Datastore.Migration.Framework;
using NzbDrone.Core.Instrumentation;
using Requestarr.Core.Test.Framework;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.Instrumentation
{
    [TestFixture]
    public class DatabaseTargetFixture : DbTest<DatabaseTarget, Log>
    {
        private static string UniqueMessage;
        private Logger _logger;

        protected override MigrationType MigrationType => MigrationType.Log;

        [SetUp]
        public void Setup()
        {
            Mocker.Resolve<ILogRepository, LogRepository>();
            Mocker.Resolve<DatabaseTarget>().Register();

            LogManager.ReconfigExistingLoggers();

            _logger = NzbDroneLogger.GetLogger(this);

            UniqueMessage = "Unique message: " + Guid.NewGuid();
        }

        [Test]
        public void write_log()
        {
            _logger.Info(UniqueMessage);

            Thread.Sleep(1000);

            StoredModel.Message.Should().Be(UniqueMessage);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_long_log()
        {
            var message = string.Empty;
            for (var i = 0; i < 100; i++)
            {
                message += Guid.NewGuid();
            }

            _logger.Info(message);

            Thread.Sleep(1000);

            StoredModel.Message.Should().HaveLength(message.Length);
            StoredModel.Message.Should().Be(message);
            VerifyLog(StoredModel, LogLevel.Info);
        }

        [Test]
        public void write_log_exception()
        {
            var ex = new InvalidOperationException("Fake Exception");

            _logger.Error(ex, UniqueMessage);

            Thread.Sleep(1000);

            VerifyLog(StoredModel, LogLevel.Error);
            StoredModel.Message.Should().Be(UniqueMessage + ": " + ex.Message);
            StoredModel.ExceptionType.Should().Be(ex.GetType().ToString());
            StoredModel.Exception.Should().Be(ex.ToString());

            ExceptionVerification.ExpectedErrors(1);
        }

        [Test]
        public void exception_log_with_no_message_should_use_exceptions_message()
        {
            var ex = new InvalidOperationException("Fake Exception");
            UniqueMessage = string.Empty;

            _logger.Error(ex, UniqueMessage);

            Thread.Sleep(1000);

            StoredModel.Message.Should().Be(ex.Message);

            VerifyLog(StoredModel, LogLevel.Error);

            ExceptionVerification.ExpectedErrors(1);
        }

        [TearDown]
        public void Teardown()
        {
            Mocker.Resolve<DatabaseTarget>().UnRegister();
        }

        private void VerifyLog(Log logItem, LogLevel level)
        {
            logItem.Time.Should().BeWithin(TimeSpan.FromSeconds(2));
            logItem.Logger.Should().Be(GetType().Name);
            logItem.Level.Should().Be(level.Name);
            _logger.Name.Should().EndWith(logItem.Logger);
        }
    }
}
