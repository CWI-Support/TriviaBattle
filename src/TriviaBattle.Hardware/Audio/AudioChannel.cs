using Microsoft.Extensions.Logging;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TriviaBattle.Hardware.Audio;

// Ported from RangerShack (Services/AudioChannel.cs), where it runs in the venue. Only the namespace and
// the logger (Microsoft ILogger instead of RangerShack's LoggingService) changed. Keep it in step with
// RangerShack rather than reworking it here.

/// <summary>
/// A single mono audio zone: owns an independent background loop and a single-layer
/// effect (with ducking). Implements ISampleProvider so it feeds directly into the
/// multichannel mixer — no separate WaveOutEvent per channel.
/// </summary>
public class AudioChannel : ISampleProvider, IDisposable
{
    private readonly string name;
    private readonly ILogger logger;

    // ── Loop state ────────────────────────────────────────────────────────────

    private volatile ISampleProvider? loopChain;    // full chain ending in VolumeSampleProvider
    private volatile VolumeSampleProvider? loopVol; // volume handle (for ducking)
    private int _loopVersion;                        // incremented on each StartLoop/StopLoop to discard stale async loads

    // ── Effect state ──────────────────────────────────────────────────────────

    private volatile ISampleProvider? effectChain;
    private AudioFileReader? effectReader;
    private volatile bool effectDone;               // set by audio thread, cleared by PlayEffect
    private int effectGeneration;                   // guards against stale OnEffectFinished calls
    private readonly object effectLock = new();     // protects generation counter and reader swap

    // ── One-shot (awaitable) ──────────────────────────────────────────────────

    private TaskCompletionSource<bool>? oneShotTcs;
    private CancellationTokenRegistration oneShotCtReg;

    // ── Volume ────────────────────────────────────────────────────────────────

    private float normalVolume;
    private float effectsVolume;
    private float duckLevel;
    private readonly int duckFadeMs;
    private volatile bool isDucking;
    // Tracks whether an explicit DuckTo() is active. -1f = no external duck.
    // Prevents StopDucking() (called from effect EOF) from un-ducking a channel
    // that was intentionally ducked from the outside (e.g. win/lose sequence, briefing).
    private float _externalDuckTarget = -1f;

    // Cancels any in-progress FadeVolumeAsync so a new one doesn't fight the old one.
    private CancellationTokenSource? _fadeCts;
    private readonly object _fadeLock = new();

    // ── Misc ──────────────────────────────────────────────────────────────────

    private float[]? readBuf;   // reused temp buffer to reduce GC pressure on audio thread
    private bool isDisposed;

    public WaveFormat WaveFormat { get; }   // mono IEEE float at configured sample rate

    // ═══════════════════════════════════════════════════════════════════════════
    // CONSTRUCTOR
    // ═══════════════════════════════════════════════════════════════════════════

    public AudioChannel(
        string name,
        float normalVolume,
        float effectsVolume,
        float duckLevel,
        int duckFadeMs,
        int sampleRate,
        ILogger logger)
    {
        this.name = name;
        this.logger = logger;
        this.normalVolume = normalVolume;
        this.effectsVolume = effectsVolume;
        this.duckLevel = duckLevel;
        this.duckFadeMs = duckFadeMs;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        logger.LogDebug($"AudioChannel [{name}] created at {sampleRate}Hz");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // ISampleProvider — called on the audio device thread
    // ═══════════════════════════════════════════════════════════════════════════

    public int Read(float[] buffer, int offset, int count)
    {
        Array.Clear(buffer, offset, count);

        // Mix loop into buffer
        var loop = loopChain;
        if (loop != null)
        {
            try
            {
                loop.Read(buffer, offset, count);
            }
            catch
            {
                loopChain = null; // reader was disposed; clear stale reference
            }
        }

        // Mix effect on top
        var effect = effectChain;
        if (effect != null)
        {
            if (readBuf == null || readBuf.Length < count)
                readBuf = new float[count];

            int read;
            try
            {
                read = effect.Read(readBuf, 0, count);
            }
            catch
            {
                read = 0;
                effectChain = null;
            }

            if (read == 0 && !effectDone)
            {
                // Effect reached EOF — signal cleanup off the audio thread
                effectDone = true;
                int gen;
                lock (effectLock) { gen = effectGeneration; }
                Task.Run(() => OnEffectFinished(gen));
            }
            else if (read > 0)
            {
                for (int i = 0; i < read; i++)
                    buffer[offset + i] += readBuf[i];
            }
        }

        return count; // always return full count — channel always produces output
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // LOOP
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Starts a looping background track. Fire-and-forget.</summary>
    public void StartLoop(string path)
    {
        if (!ValidatePath(path, "loop")) return;
        _ = StartLoopInternalAsync(path, Interlocked.Increment(ref _loopVersion));
    }

    /// <summary>Starts a looping background track and waits until playback begins.</summary>
    public async Task StartLoopAsync(string path)
    {
        if (!ValidatePath(path, "loop")) return;
        await StartLoopInternalAsync(path, Interlocked.Increment(ref _loopVersion));
    }

    private async Task StartLoopInternalAsync(string path, int version)
    {
        try
        {
            // Pre-decode the entire file to a float[] on a background thread so the audio
            // callback thread never performs file I/O, COM calls, or seeks at loop points.
            // Mono normalisation and resampling are applied here so the chain contains only
            // a LoopingFloatProvider → VolumeSampleProvider — no MediaFoundationReader.
            float[] samples = await Task.Run(() =>
            {
                using var reader = new AudioFileReader(path);
                ISampleProvider src = NormalizeToMono(reader);
                src = ResampleIfNeeded(src);
                return ReadAllSamples(src);
            });

            // Discard if a newer StartLoop or StopLoop has superseded this load.
            if (version != _loopVersion) return;

            var loopProvider = new LoopingFloatProvider(samples, WaveFormat);
            var vol = new VolumeSampleProvider(loopProvider) { Volume = normalVolume };

            lock (effectLock)
            {
                if (version != _loopVersion) return; // double-check inside lock
                loopVol = vol;
                loopChain = vol; // volatile write — audio thread sees this immediately
            }

            logger.LogDebug($"[{name}] Loop started ({samples.Length} samples pre-decoded): {Path.GetFileName(path)}");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"[{name}] Failed to start loop: {path}");
        }
    }

    /// <summary>Stops the background loop and releases its resources.</summary>
    public void StopLoop()
    {
        Interlocked.Increment(ref _loopVersion); // invalidates any pending StartLoopInternalAsync
        lock (effectLock)
        {
            loopChain = null;
            loopVol = null;
        }
        isDucking = false;
        _externalDuckTarget = -1f;
        lock (_fadeLock) { _fadeCts?.Cancel(); _fadeCts?.Dispose(); _fadeCts = null; }
        logger.LogDebug($"[{name}] Loop stopped");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // EFFECTS
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Plays an effect at the channel's configured effects volume, ducking the loop while it plays.</summary>
    public void PlayEffect(string path) => PlayEffect(path, effectsVolume);

    /// <summary>Plays an effect at an explicit volume level, ducking the loop while it plays.</summary>
    public void PlayEffect(string path, float volume)
    {
        if (!ValidatePath(path, "effect")) return;
        try
        {
            var reader = new AudioFileReader(path);
            ISampleProvider chain = NormalizeToMono(reader);
            chain = ResampleIfNeeded(chain);
            var vol = new VolumeSampleProvider(chain) { Volume = Math.Clamp(volume, 0f, 3f) };

            AudioFileReader? old;
            lock (effectLock)
            {
                effectGeneration++;
                old = effectReader;
                effectReader = reader;
                effectDone = false;      // must reset before setting chain (audio thread reads both)
                effectChain = vol;
            }
            old?.Dispose();

            StartDucking();
            logger.LogDebug($"[{name}] Effect: {Path.GetFileName(path)} (vol {volume:F2})");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, $"[{name}] Failed to play effect: {path}");
        }
    }

    /// <summary>Plays a one-shot and awaits its completion. Pass volumeOverride to use a specific level instead of effectsVolume.</summary>
    public async Task PlayOneShotAsync(string path, CancellationToken ct, float? volumeOverride = null)
    {
        if (!ValidatePath(path, "one-shot")) return;

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        oneShotCtReg = ct.Register(() =>
        {
            StopEffect();
            tcs.TrySetCanceled(ct);
        });
        oneShotTcs = tcs;

        if (volumeOverride.HasValue)
            PlayEffect(path, volumeOverride.Value);
        else
            PlayEffect(path);

        // If PlayEffect failed silently (e.g. AudioFileReader threw and was caught internally),
        // effectChain/effectReader were never set. OnEffectFinished will never fire, so tcs.Task
        // would wait forever. Detect this and resolve immediately.
        lock (effectLock)
        {
            if (effectReader == null)
                tcs.TrySetResult(false);
        }

        try
        {
            // 3-minute hard cap: if OnEffectFinished never fires (e.g. unhandled audio
            // thread exception), the game flow must not hang forever.
            await tcs.Task.WaitAsync(TimeSpan.FromMinutes(3), ct);
        }
        catch (TimeoutException)
        {
            logger.LogWarning($"[{name}] PlayOneShotAsync timed out — forcing completion");
            StopEffect();
        }
        finally
        {
            oneShotCtReg.Dispose();
            oneShotTcs = null;
        }
    }

    /// <summary>Stops any currently playing effect and restores loop volume.</summary>
    public void StopEffect()
    {
        AudioFileReader? old;
        lock (effectLock)
        {
            effectGeneration++;
            effectChain = null;
            effectDone = false;
            old = effectReader;
            effectReader = null;
        }
        old?.Dispose();
        StopDucking();
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // VOLUME
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Sets the background loop volume. Values above 1.0 amplify the signal (software gain boost).</summary>
    public void SetVolume(float volume)
    {
        normalVolume = Math.Clamp(volume, 0f, 3f);
        var vol = loopVol;
        if (vol != null && !isDucking)
            vol.Volume = normalVolume;
    }

    /// <summary>Sets the effects volume. Values above 1.0 apply a software boost.</summary>
    public void SetEffectsVolume(float volume)
    {
        effectsVolume = Math.Clamp(volume, 0f, 3f);
    }

    /// <summary>Updates the duck level used by internal auto-ducking when effects play.</summary>
    public void SetDuckLevel(float level)
    {
        duckLevel = Math.Clamp(level, 0f, 1f);
    }

    /// <summary>Smoothly ducks the loop to a fraction of its normal volume.</summary>
    public void DuckTo(float levelFraction, int fadeMs)
    {
        var vol = loopVol;
        if (vol == null) return;
        isDucking = true;
        float target = normalVolume * Math.Clamp(levelFraction, 0f, 1f);
        _externalDuckTarget = target;
        _ = FadeVolumeAsync(vol, vol.Volume, target, fadeMs, BeginFade());
    }

    /// <summary>Smoothly restores the loop to its normal volume after a duck.</summary>
    public void RestoreFromDuck(int fadeMs)
    {
        var vol = loopVol;
        _externalDuckTarget = -1f;
        if (vol == null || !isDucking) return;
        isDucking = false;
        _ = FadeVolumeAsync(vol, vol.Volume, normalVolume, fadeMs, BeginFade());
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // STOP ALL
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Stops loop and effect, releases resources, resets state.</summary>
    public void Stop()
    {
        StopLoop();
        StopEffect();
        isDucking = false;
        _externalDuckTarget = -1f;
        lock (_fadeLock) { _fadeCts?.Cancel(); _fadeCts?.Dispose(); _fadeCts = null; }
        oneShotTcs?.TrySetResult(false);
        logger.LogDebug($"[{name}] Stopped");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL — Ducking
    // ═══════════════════════════════════════════════════════════════════════════

    private void StartDucking()
    {
        var vol = loopVol;
        if (vol == null || isDucking) return;
        isDucking = true;
        float target = normalVolume * duckLevel;
        _ = FadeVolumeAsync(vol, vol.Volume, target, duckFadeMs, BeginFade());
    }

    private void StopDucking()
    {
        // Only un-duck if this was an auto-duck from PlayEffect.
        // If DuckTo() is externally active, leave the duck in place — the caller
        // is responsible for restoring via RestoreFromDuck().
        if (_externalDuckTarget >= 0) return;
        var vol = loopVol;
        if (vol == null || !isDucking) return;
        isDucking = false;
        _ = FadeVolumeAsync(vol, vol.Volume, normalVolume, duckFadeMs, BeginFade());
    }

    private CancellationToken BeginFade()
    {
        lock (_fadeLock)
        {
            _fadeCts?.Cancel();
            _fadeCts?.Dispose();
            _fadeCts = new CancellationTokenSource();
            return _fadeCts.Token;
        }
    }

    private static async Task FadeVolumeAsync(VolumeSampleProvider provider, float from, float to, int durationMs, CancellationToken ct)
    {
        const int steps = 20;
        int stepMs = Math.Max(1, durationMs / steps);
        float stepVol = (to - from) / steps;
        for (int i = 0; i < steps; i++)
        {
            if (ct.IsCancellationRequested) return;
            provider.Volume = from + stepVol * i;
            await Task.Delay(stepMs, ct).ConfigureAwait(false);
        }
        if (!ct.IsCancellationRequested)
            provider.Volume = to;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL — Effect completion
    // ═══════════════════════════════════════════════════════════════════════════

    private void OnEffectFinished(int generation)
    {
        AudioFileReader? old;
        lock (effectLock)
        {
            // Guard: a newer effect may have started since this was queued
            if (generation != effectGeneration) return;

            effectChain = null;
            old = effectReader;
            effectReader = null;
        }
        // Dispose BEFORE resolving the TCS, but catch any COM/MF exceptions so the
        // TCS is always resolved — a dispose failure must never hang PlayOneShotAsync.
        try { old?.Dispose(); }
        catch (Exception ex) { logger.LogWarning($"[{name}] Effect reader dispose error (non-fatal): {ex.Message}"); }
        StopDucking();
        oneShotTcs?.TrySetResult(true);
        logger.LogDebug($"[{name}] Effect finished");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // INTERNAL — Format helpers
    // ═══════════════════════════════════════════════════════════════════════════

    private ISampleProvider NormalizeToMono(ISampleProvider source)
    {
        return source.WaveFormat.Channels switch
        {
            1 => source,
            2 => new StereoToMonoSampleProvider(source),
            _ => throw new InvalidOperationException(
                $"[{name}] Unsupported channel count {source.WaveFormat.Channels} in file")
        };
    }

    private ISampleProvider ResampleIfNeeded(ISampleProvider source)
    {
        if (source.WaveFormat.SampleRate == WaveFormat.SampleRate) return source;
        return new WdlResamplingSampleProvider(source, WaveFormat.SampleRate);
    }

    /// <summary>Reads all samples from <paramref name="source"/> into a single float[] array.</summary>
    private static float[] ReadAllSamples(ISampleProvider source)
    {
        // Pre-size for ~1 minute of mono audio to reduce List<T> resizes.
        var list = new List<float>(source.WaveFormat.SampleRate * 60);
        var buf = new float[source.WaveFormat.SampleRate]; // 1-second read buffer
        int read;
        while ((read = source.Read(buf, 0, buf.Length)) > 0)
            list.AddRange(new ReadOnlySpan<float>(buf, 0, read));
        return list.ToArray();
    }

    private static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path);

    private bool ValidatePath(string path, string kind)
    {
        var full = Resolve(path);
        if (File.Exists(full)) return true;
        logger.LogWarning($"[{name}] {kind} file not found: {full}");
        return false;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DISPOSE
    // ═══════════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;
        Stop();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// LOOPING FLOAT PROVIDER
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Plays a pre-decoded float[] buffer as a continuous seamless loop.
/// All operations are plain array copies — no file I/O, no COM calls, and no
/// MediaFoundationReader on the audio callback thread. Loop points are an
/// integer reset rather than IMFSourceReader::SetCurrentPosition, which
/// eliminates the blocking seek that caused audio dropouts with the old
/// LoopingSampleProvider + AudioFileReader approach.
/// </summary>
file sealed class LoopingFloatProvider : ISampleProvider
{
    private readonly float[] samples;
    private int position;

    public WaveFormat WaveFormat { get; }

    public LoopingFloatProvider(float[] samples, WaveFormat format)
    {
        this.samples = samples;
        WaveFormat = format;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        if (samples.Length == 0)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }

        int written = 0;
        while (written < count)
        {
            int available = Math.Min(count - written, samples.Length - position);
            Array.Copy(samples, position, buffer, offset + written, available);
            written += available;
            position += available;
            if (position >= samples.Length)
                position = 0; // seamless loop — pure integer reset, no seek
        }
        return count;
    }
}