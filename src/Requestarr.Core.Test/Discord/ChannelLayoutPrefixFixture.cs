using System.Collections.Generic;
using FluentAssertions;
using NUnit.Framework;
using Requestarr.Core.Discord;
using Requestarr.Core.Removals;

namespace Requestarr.Core.Test.Discord
{
    // Requestarr's Discord names take a prefix only on a local test stack the automated run starts (docker/qa_checks.py).
    [TestFixture]
    public class ChannelLayoutPrefixFixture
    {
        [TestCase("true", "lane8-")]
        [TestCase(null, "")]
        [TestCase("false", "")]
        public void only_a_local_test_stack_takes_a_name_prefix(string devStack, string prefix)
        {
            var variables = new Dictionary<string, string> { [DevFastVotes.DevStackVariable] = devStack, [ChannelLayout.NamePrefixVariable] = "lane8-" };

            ChannelLayout.DevStackPrefix(name => variables.GetValueOrDefault(name)).Should().Be(prefix);
        }
    }
}
