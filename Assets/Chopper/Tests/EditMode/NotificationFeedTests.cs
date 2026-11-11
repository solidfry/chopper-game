using System.Collections.Generic;
using Chopper.Presentation.Flow;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class NotificationFeedTests
    {
        [Test]
        public void Post_ShowsUpToTheLimit_QueuesTheRest_AndTimesOut()
        {
            var feed = new NotificationFeed();
            var hidden = new List<string>();
            feed.Hidden += n => hidden.Add(n.Title);
            for (var i = 0; i < NotificationFeed.MaxShown + 2; i++) feed.Post("N" + i, seconds: 1f + i);

            Assert.That(feed.Visible.Count, Is.EqualTo(NotificationFeed.MaxShown));
            Assert.That(feed.Queued, Is.EqualTo(2));

            feed.Tick(1.01f);
            Assert.That(hidden, Is.EqualTo(new[] { "N0" }), "the shortest goes first");
            Assert.That(feed.Visible.Count, Is.EqualTo(NotificationFeed.MaxShown), "a queued one moves up");
            Assert.That(feed.Queued, Is.EqualTo(1));
        }

        [Test]
        public void SameNotice_CountsUpAndRestarts_InsteadOfStacking()
        {
            var feed = new NotificationFeed();
            feed.Post("Matchmaking failed", "Timeout", NoticeKind.Error, 2f);
            feed.Tick(1.5f);
            feed.Post("matchmaking failed", "Timeout", NoticeKind.Error, 2f);

            Assert.That(feed.Visible.Count, Is.EqualTo(1));
            Assert.That(feed.Visible[0].Count, Is.EqualTo(2));
            Assert.That(feed.Visible[0].Title, Is.EqualTo("MATCHMAKING FAILED"), "titles are upper case");
            feed.Tick(1.5f);
            Assert.That(feed.Visible.Count, Is.EqualTo(1), "the timer restarted");
        }

        [Test]
        public void Dismiss_RemovesAtOnce_AndEmptyPostsAreIgnored()
        {
            var feed = new NotificationFeed();
            feed.Post("");
            Assert.That(feed.Visible.Count, Is.EqualTo(0));

            feed.Post("Pilot set", "VIPER", NoticeKind.Success);
            var id = feed.Visible[0].Id;
            Assert.That(feed.Dismiss(id), Is.True);
            Assert.That(feed.Visible.Count, Is.EqualTo(0));
            Assert.That(feed.Dismiss(id), Is.False);
        }
    }
}
