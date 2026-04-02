namespace Imrdy.Core.Sound;

/// <summary>
/// Abstraction for WAV sound playback.
/// </summary>
public interface ISoundPlayer : IDisposable
{
    /// <summary>
    /// Plays a WAV file. Preempts any currently playing sound.
    /// </summary>
    void Play(string wavPath);

    /// <summary>
    /// Stops any currently playing sound.
    /// </summary>
    void Stop();
}
