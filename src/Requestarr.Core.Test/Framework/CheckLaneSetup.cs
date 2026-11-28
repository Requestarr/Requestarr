using System;
using NUnit.Framework;
using Requestarr.Core.Discord;

namespace Requestarr.Core.Test
{
    // The automated run (docker/qa_checks.py) runs its live checks in lanes at the same time, in one Discord server:
    // each lane's test run gets QA_CHECKS_LANE, and every name Requestarr gives a channel, group or role there takes
    // it as a prefix, so no lane finds another's.
    [SetUpFixture]
    public class CheckLaneSetup
    {
        public const string Variable = "QA_CHECKS_LANE";

        [OneTimeSetUp]
        public void UseTheRunsLane() => ChannelLayout.CheckLane = Environment.GetEnvironmentVariable(Variable) ?? string.Empty;
    }
}
