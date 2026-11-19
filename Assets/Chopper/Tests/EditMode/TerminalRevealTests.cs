using Chopper.UI;
using NUnit.Framework;

namespace Chopper.Tests
{
    public sealed class TerminalRevealTests
    {
        private const string Text = "SHOT DOWN";
        private const float Speed = 50f;

        private static string At(float seconds, out bool done) => TerminalReveal.Frame(Text, seconds, Speed, "#FF4246", out done);

        [Test]
        public void Frame_OpensEmpty_TypesWithACursor_ThenEndsPlain()
        {
            var opening = At(0.05f, out var done);
            Assert.That(opening, Is.EqualTo("<alpha=#00>SHOT DOWN</alpha>"), "box opening: all hidden, full width");
            Assert.That(done, Is.False);

            // 0.12 s open + 0.06 s at 50 per second = 3 characters typed, cursor on the 4th.
            var typing = At(0.18f, out done);
            Assert.That(typing, Does.StartWith("SHO<mark=#FF4246CC>T</mark><alpha=#00>"));
            Assert.That(done, Is.False);

            var blinking = At(0.12f + Text.Length / Speed + 0.1f, out done);
            Assert.That(blinking, Does.StartWith(Text + "<mark="), "trailing cursor on");
            Assert.That(done, Is.False);

            var end = At(3f, out done);
            Assert.That(end, Is.EqualTo(Text));
            Assert.That(done, Is.True);
        }
    }
}
