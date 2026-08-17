using System;
using System.Threading.Tasks;
using Windows.Media.Core;
using Windows.Media.Playback;
using Windows.Media.SpeechSynthesis;

namespace HololensHermes.Services
{
    /// <summary>
    /// Speaks changes in navigation state while preventing repeated frame-by-frame
    /// announcements. Visual holograms remain useful, but speech supplies an
    /// equivalent non-visual cue for route changes and safety pauses.
    /// </summary>
    public sealed class GuidanceFeedbackService : IDisposable
    {
        private readonly SpeechSynthesizer synthesizer = new SpeechSynthesizer();
        private readonly MediaPlayer player = new MediaPlayer();
        private string lastPrompt = string.Empty;
        private DateTimeOffset lastAnnouncementUtc = DateTimeOffset.MinValue;

        public async Task AnnounceIfChangedAsync(string prompt, bool urgent)
        {
            if (string.IsNullOrWhiteSpace(prompt))
                return;

            var now = DateTimeOffset.UtcNow;
            var minimumInterval = urgent ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(4);
            if (string.Equals(lastPrompt, prompt, StringComparison.Ordinal) &&
                now - lastAnnouncementUtc < minimumInterval)
            {
                return;
            }

            lastPrompt = prompt;
            lastAnnouncementUtc = now;
            try
            {
                var stream = await synthesizer.SynthesizeTextToStreamAsync(prompt);
                player.Source = MediaSource.CreateFromStream(stream, stream.ContentType);
                player.Play();
            }
            catch
            {
                // Guidance remains visually available if speech synthesis is unavailable.
            }
        }

        public void Dispose()
        {
            player.Dispose();
            synthesizer.Dispose();
        }
    }
}
