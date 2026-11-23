using Moq;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using NzbDrone.Core.HealthCheck.Checks;
using NzbDrone.Core.Localization;
using Requestarr.Core.Test.Framework;
using Requestarr.Test.Common;

namespace Requestarr.Core.Test.HealthChecks.Checks
{
    [TestFixture]
    public class AppDataLocationFixture : CoreTest<AppDataLocationCheck>
    {
        [SetUp]
        public void Setup()
        {
            Mocker.GetMock<ILocalizationService>()
                  .Setup(s => s.GetLocalizedString(It.IsAny<string>()))
                  .Returns("Some Warning Message");
        }

        [Test]
        public void should_return_warning_when_app_data_is_child_of_startup_folder()
        {
            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.StartUpFolder)
                  .Returns(@"C:\NzbDrone".AsOsAgnostic());

            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.AppDataFolder)
                  .Returns(@"C:\NzbDrone\AppData".AsOsAgnostic());

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_warning_when_app_data_is_same_as_startup_folder()
        {
            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.StartUpFolder)
                  .Returns(@"C:\NzbDrone".AsOsAgnostic());

            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.AppDataFolder)
                  .Returns(@"C:\NzbDrone".AsOsAgnostic());

            Subject.Check().ShouldBeWarning();
        }

        [Test]
        public void should_return_ok_when_no_conflict()
        {
            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.StartUpFolder)
                  .Returns(@"C:\NzbDrone".AsOsAgnostic());

            Mocker.GetMock<IAppFolderInfo>()
                  .Setup(s => s.AppDataFolder)
                  .Returns(@"C:\ProgramData\NzbDrone".AsOsAgnostic());

            Subject.Check().ShouldBeOk();
        }
    }
}
