using Imrdy.Core.Sound;

namespace Imrdy.Windows.Sound;

/// <summary>
/// Implements ISoundPlayer using System.Media.SoundPlayer.
/// Uses PlayAsync() to avoid blocking the STA/UI thread.
/// Preempt semantics: new Play() stops the previous sound.
/// </summary>
internal sealed class WinFormsSoundPlayer : ISoundPlayer
{
    private System.Media.SoundPlayer? _player;
    private bool _disposed;

    public void Play(string wavPath)
    {
        if (_disposed)
        {
            return;
        }

        Stop();

        try
        {
            _player = new System.Media.SoundPlayer(wavPath);
            _player.Play();
        }
        catch (Exception)
        {
            // Best-effort playback — don't crash on bad WAV files
            _player?.Dispose();
            _player = null;
        }
    }

    public void Stop()
    {
        if (_player is not null)
        {
            try
            {
                _player.Stop();
            }
            catch (Exception)
            {
                // Ignore stop errors
            }

            _player.Dispose();
            _player = null;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
    }
}
