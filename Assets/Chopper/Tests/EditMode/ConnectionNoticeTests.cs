using System;
using Chopper.Presentation.Flow;
using NUnit.Framework;
using Unity.NetCode;

namespace Chopper.Tests
{
    public sealed class ConnectionNoticeTests
    {
        [Test]
        public void EveryReason_HasATitleAndText()
        {
            foreach (NetworkStreamDisconnectReason reason in Enum.GetValues(typeof(NetworkStreamDisconnectReason)))
            foreach (var wasConnected in new[] { false, true })
            {
                Assert.That(ConnectionNotice.Title(wasConnected), Is.Not.Empty);
                Assert.That(ConnectionNotice.Text(reason, wasConnected), Is.Not.Empty, $"{reason}, connected {wasConnected}");
            }
        }

        [Test]
        public void OldServer_SaysVersion_ServerStop_SaysClosed()
        {
            Assert.That(ConnectionNotice.Text(NetworkStreamDisconnectReason.BadProtocolVersion, false), Does.Contain("version"));
            Assert.That(ConnectionNotice.Text(NetworkStreamDisconnectReason.ClosedByRemote, true), Does.Contain("closed the match"));
            Assert.That(ConnectionNotice.Title(false), Is.EqualTo("Cannot connect"));
        }
    }
}
