using System.Diagnostics;
using System.IO;
using FluentAssertions;
using NUnit.Framework;
using NzbDrone.Common.EnvironmentInfo;
using Requestarr.Test.Common;

namespace Requestarr.Common.Test.EnvironmentInfo
{
    [TestFixture]
    public class AppFolderInfoFixture : TestBase<AppFolderInfo>
    {
        [Test]
        public void StartupPath_should_not_be_empty()
        {
            Subject.StartUpFolder.Should().NotBeNullOrWhiteSpace();
            Path.IsPathRooted(Subject.StartUpFolder).Should().BeTrue("Path is not rooted");
        }

        [Test]
        public void ApplicationPath_should_not_be_empty()
        {
            Subject.AppDataFolder.Should().NotBeNullOrWhiteSpace();
            Path.IsPathRooted(Subject.AppDataFolder).Should().BeTrue("Path is not rooted");
        }

        [Test]
        public void IsProduction_should_return_false_when_run_within_nunit()
        {
            RuntimeInfo.IsProduction.Should().BeFalse("Process name is " + Process.GetCurrentProcess().ProcessName + " Folder is " + Directory.GetCurrentDirectory());
        }

        [Test]
        public void should_use_path_from_arg_if_provided()
        {
            var args = new StartupContext("-data=\"c:\\users\\test\\\"");

            Mocker.SetConstant<IStartupContext>(args);
            Subject.AppDataFolder.Should().Be("c:\\users\\test\\");
        }
    }
}
