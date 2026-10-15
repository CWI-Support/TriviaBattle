using Microsoft.Extensions.Logging;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace TriviaBattle.Hardware.Audio;

// Ported from RangerShack (Services/AudioService.cs), where it runs in the venue on the StarTech 7.1 card.
// The engine is unchanged: one MixingSampleProvider, each zone a mono AudioChannel routed to one output
// by MonoToMultichannelProvider, the same device selection and WASAPI exclusive -> shared -> stereo
// fallback. What changed: the zones are passed in (Trivia Battle has four stations, room, attract and
// sub instead of RangerShack's Game1-3/Promo/Sub), the game-specific sequences live in the server
// (RoomSound), stereo test mode switches on by itself when the device has fewer channels than the card
// (RangerShack sets it by hand), and the logger is Microsoft's ILogger. Keep it in step with RangerShack.

/// <summary>One speaker zone: a name, the output it's wired to (0-7 on the StarTech), and its music volume.</summary>
/// <param name="InStereoTest">
/// Whether the zone is heard in stereo test mode, where every zone plays in both ears. Zones that only
/// repeat another zone's sound (attract, sub) are left out: two copies of the same music started
/// separately sound phasey/robotic when they're summed into one pair of headphones.
/// </param>
public sealed record AudioZone(string Name, int ChannelIndex, float Volume, bool InStereoTest = true);

/// <summary>
/// Audio engine: owns independent mono zones routed to specific channels of a single output
/// targeting the StarTech 7.1 USB card. All zones share one clock — no drift or phasing.
/// </summary>
public class AudioService : IDisposable
{
    private readonly ILogger logger;

    /// <summary>Every zone, by name.</summary>
    public IReadOnlyDictionary<string, AudioChannel> Zones { get; }

    /// <summary>True if a working audio output device was opened successfully</summary>
    public bool IsDeviceReady { get; private set; }

    /// <summary>Human-readable description of the active audio device and channel mode</summary>
    public string DeviceStatus { get; private set; } = "Initializing...";

    /// <summary>True when running with 2 outputs (laptop/headphones): every zone plays in both ears.</summary>
    public bool IsStereoTest { get; }

    /// <param name="outputChannels">8 for the StarTech 7.1 card.</param>
    /// <param name="deviceName">Part of the card's Windows name; empty = Windows default (skipping HDMI).</param>
    public AudioService(IReadOnlyList<AudioZone> zones, string deviceName, int outputChannels, int rate,
        float effectsVolume, float duckLevel, int duckFadeMs, ILogger logger)
    {
        this.logger = logger;

        Zones = zones.ToDictionary(
            z => z.Name,
            z => new AudioChannel(z.Name, z.Volume, effectsVolume, duckLevel, duckFadeMs, rate, logger));

        // totalChannels comes from settings (8 for the StarTech). RangerShack is set to 2 by hand on a PC
        // without the card; here that happens by itself when Windows runs the device with fewer channels.
        int totalChannels = Math.Max(2, outputChannels);
        int deviceChannels = DeviceChannelCount(deviceName);
        if (deviceChannels < totalChannels)
        {
            logger.LogWarning($"AudioService: the sound device has {deviceChannels} channel(s), not {totalChannels} — stereo test mode (every speaker in both ears). For the venue, set the StarTech to 7.1 in Windows Sound settings.");
            totalChannels = 2;
        }
        IsStereoTest = totalChannels == 2;

        var mixerFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, totalChannels);
        var mainMixer = new MixingSampleProvider(mixerFormat) { ReadFully = true };

        // In 2-channel (stereo/headphone) mode, broadcast every zone to both ears so
        // all zones are audible for testing. On the real 6/8-ch card, each zone routes
        // to its dedicated physical channel as configured.
        foreach (var zone in zones)
        {
            if (IsStereoTest && !zone.InStereoTest)
                continue;
            mainMixer.AddMixerInput(new MonoToMultichannelProvider(Zones[zone.Name], IsStereoTest ? -1 : Clamp(zone.ChannelIndex, totalChannels), totalChannels));
        }

        // Open the audio device — try configured setup, fall back gracefully if it fails
        LogDevices();
        outputDevice = TryOpenDevice(mainMixer, zones, deviceName, totalChannels, rate, out var statusMsg);
        DeviceStatus = statusMsg;
        IsDeviceReady = outputDevice != null;

        if (outputDevice == null)
            logger.LogWarning("AudioService: no working audio device found — running silent");

        logger.LogInformation($"AudioService initialized — {statusMsg} ({string.Join(", ", zones.Select(z => $"{z.Name}→{z.ChannelIndex}"))})");
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // DEVICE ENUMERATION
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>Logs all detected WASAPI render devices — useful during hardware setup.</summary>
    public void LogDevices()
    {
        using var enumerator = new MMDeviceEnumerator();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).ToList();
        logger.LogInformation($"Audio devices (WASAPI, {devices.Count} found):");
        foreach (var d in devices)
            logger.LogInformation($"  {d.FriendlyName}");
    }

    /// <summary>All available WASAPI render devices, for the operator screen.</summary>
    public static IReadOnlyList<string> GetAvailableDeviceNames()
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active).Select(d => d.FriendlyName).ToList();
        }
        catch (Exception)
        {
            return [];
        }
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // PRIVATE
    // ═══════════════════════════════════════════════════════════════════════════

    private readonly IWavePlayer? outputDevice;
    private bool isDisposed;

    /// <summary>How many channels Windows runs the device with (its shared-mode mix format); 8 if it can't tell.</summary>
    private int DeviceChannelCount(string deviceName)
    {
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            return SelectDevice(enumerator, deviceName, log: false)?.AudioClient.MixFormat.Channels ?? 8;
        }
        catch (Exception)
        {
            return 8;
        }
    }

    /// <summary>
    /// Tries to open a WASAPI device with the given mixer. Falls back through:
    ///   1. Exclusive mode on target device (best for multichannel USB audio — bypasses Windows mixer)
    ///   2. Shared mode on target device
    ///   3. Stereo fallback on the same device (every zone in both channels)
    ///   4. Returns null (silent mode) if all attempts fail
    ///
    /// Many USB audio devices only support multichannel via WASAPI exclusive mode;
    /// shared mode reports 2ch even when the driver is set to 5.1.
    /// </summary>
    private IWavePlayer? TryOpenDevice(ISampleProvider mixer, IReadOnlyList<AudioZone> zones, string deviceName, int channels, int rate, out string status)
    {
        using var enumerator = new MMDeviceEnumerator();
        var targetDevice = SelectDevice(enumerator, deviceName, log: true);
        if (targetDevice == null)
        {
            status = "No audio device — running silent";
            return null;
        }

        // Log the device's actual WASAPI mix format so we can see what shared mode offers
        try
        {
            var mixFmt = targetDevice.AudioClient.MixFormat;
            logger.LogInformation($"WASAPI mix format for '{targetDevice.FriendlyName}': {mixFmt.Channels}ch @ {mixFmt.SampleRate}Hz");
        }
        catch (Exception ex) { logger.LogWarning($"Could not query mix format: {ex.Message}"); }

        // Attempt 1: WASAPI exclusive mode — bypasses Windows audio engine, gives direct hardware access.
        // Required by many USB multichannel devices that only expose 2ch in shared mode.
        var (p1, ok1) = AttemptWasapiOpen(mixer, targetDevice, AudioClientShareMode.Exclusive);
        if (ok1)
        {
            status = $"{targetDevice.FriendlyName} — WASAPI exclusive {channels}ch";
            logger.LogInformation($"AudioService: Opened EXCLUSIVE {channels}ch on '{targetDevice.FriendlyName}'");
            return p1;
        }
        logger.LogWarning($"AudioService: WASAPI exclusive failed on '{targetDevice.FriendlyName}' — trying shared");

        // Attempt 2: WASAPI shared mode
        var (p2, ok2) = AttemptWasapiOpen(mixer, targetDevice, AudioClientShareMode.Shared);
        if (ok2)
        {
            status = $"{targetDevice.FriendlyName} — WASAPI shared {channels}ch";
            logger.LogInformation($"AudioService: Opened SHARED {channels}ch on '{targetDevice.FriendlyName}'");
            return p2;
        }
        logger.LogWarning($"AudioService: WASAPI shared {channels}ch failed — falling back to stereo on same device");

        // Attempt 3: stereo fallback on the SAME target device (not the Windows default).
        // This keeps audio on the correct device even if multichannel negotiation fails.
        if (channels != 2)
        {
            var stereoMixer = BuildStereoFallbackMixer(rate, zones);
            var (p3, ok3) = AttemptWasapiOpen(stereoMixer, targetDevice, AudioClientShareMode.Shared);
            if (ok3)
            {
                status = $"{targetDevice.FriendlyName} — STEREO FALLBACK (every zone in both channels — set the card to 7.1)";
                logger.LogWarning("AudioService: STEREO FALLBACK active — every zone in both channels");
                return p3;
            }
        }

        status = "No audio device — running silent";
        return null;
    }

    /// <summary>
    /// Resolves the target device:
    ///   1. Named device (set in config) — always wins if found
    ///   2. Auto-select: when no name is configured, skip HDMI/Display devices and prefer
    ///      USB/line-out audio. Handles the case where a TV is plugged in and becomes the
    ///      Windows default, breaking audio output on the actual sound card.
    ///   3. Fall back to Windows default only if nothing better is available.
    /// </summary>
    private MMDevice? SelectDevice(MMDeviceEnumerator enumerator, string deviceName, bool log)
    {
        var namedDevice = FindWasapiDevice(enumerator, deviceName, log);
        if (namedDevice != null)
            return namedDevice;

        MMDevice? defaultDevice = null;
        try { defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia); }
        catch (Exception ex) { if (log) logger.LogWarning($"AudioService: no default render endpoint ({ex.Message})"); }

        var preferred = FindNonHdmiDevice(enumerator);
        if (defaultDevice == null)
            return preferred;

        if (!string.IsNullOrWhiteSpace(deviceName))
        {
            // Name was set but device wasn't found — fall back to default and warn loudly
            if (log) logger.LogWarning($"AudioService: configured device '{deviceName}' not found — using Windows default '{defaultDevice.FriendlyName}'.");
            return defaultDevice;
        }

        if (preferred != null && IsHdmi(defaultDevice))
        {
            if (log) logger.LogInformation($"AudioService: system default appears to be HDMI/Display — auto-selected '{preferred.FriendlyName}' instead. Set the device name to pin a specific device.");
            return preferred;
        }
        return defaultDevice;
    }

    private static (IWavePlayer? player, bool ok) AttemptWasapiOpen(ISampleProvider mixer, MMDevice device, AudioClientShareMode mode)
    {
        WasapiOut? wasapi = null;
        try
        {
            bool eventSync = mode == AudioClientShareMode.Exclusive;
            int latency = mode == AudioClientShareMode.Exclusive ? 20 : 100;
            wasapi = new WasapiOut(device, mode, eventSync, latency);
            wasapi.Init(mixer);
            wasapi.Play();
            return (wasapi, true);
        }
        catch
        {
            // Dispose the failed instance immediately so the WASAPI device handle is released.
            // Without this, a failed exclusive-mode open holds the handle and blocks the
            // subsequent shared-mode fallback attempt on the same device.
            wasapi?.Dispose();
            return (null, false);
        }
    }

    /// <summary>Builds a 2-channel fallback mixer: every zone heard in stereo test mode, in both channels.</summary>
    private MixingSampleProvider BuildStereoFallbackMixer(int rate, IReadOnlyList<AudioZone> zones)
    {
        var fmt = WaveFormat.CreateIeeeFloatWaveFormat(rate, 2);
        var mixer = new MixingSampleProvider(fmt) { ReadFully = true };
        foreach (var zone in zones.Where(z => z.InStereoTest))
            mixer.AddMixerInput(new MonoToMultichannelProvider(Zones[zone.Name], -1, 2));
        return mixer;
    }

    private static int Clamp(int channelIndex, int totalChannels)
    {
        if (channelIndex >= 0 && channelIndex < totalChannels) return channelIndex;
        return Math.Clamp(channelIndex, 0, totalChannels - 1);
    }

    private MMDevice? FindWasapiDevice(MMDeviceEnumerator enumerator, string deviceName, bool log)
    {
        if (string.IsNullOrWhiteSpace(deviceName)) return null;
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        var match = devices.FirstOrDefault(d =>
            d.FriendlyName.Contains(deviceName, StringComparison.OrdinalIgnoreCase));
        if (!log) return match;
        if (match != null)
            logger.LogInformation($"Audio device matched: {match.FriendlyName}");
        else
            logger.LogWarning($"Audio device '{deviceName}' not found — will fall through to auto-select or Windows default");
        return match;
    }

    /// <summary>
    /// Returns the first active render device whose name does NOT contain HDMI or Display —
    /// used to auto-skip TV outputs when no device name is configured.
    /// Returns null if no non-HDMI device exists (e.g. headless machine).
    /// </summary>
    private static MMDevice? FindNonHdmiDevice(MMDeviceEnumerator enumerator)
    {
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        return devices.FirstOrDefault(d => !IsHdmi(d));
    }

    private static bool IsHdmi(MMDevice d) =>
        d.FriendlyName.Contains("HDMI", StringComparison.OrdinalIgnoreCase) ||
        d.FriendlyName.Contains("Display", StringComparison.OrdinalIgnoreCase);

    // ═══════════════════════════════════════════════════════════════════════════
    // DISPOSE
    // ═══════════════════════════════════════════════════════════════════════════

    public void Dispose()
    {
        if (isDisposed) return;
        isDisposed = true;

        logger.LogInformation("AudioService disposing");
        outputDevice?.Stop();
        outputDevice?.Dispose();
        foreach (var zone in Zones.Values)
            zone.Dispose();
    }
}

// ═══════════════════════════════════════════════════════════════════════════════
// MONO TO MULTICHANNEL PROVIDER (unchanged from RangerShack)
// ═══════════════════════════════════════════════════════════════════════════════

/// <summary>
/// Routes a mono ISampleProvider to one specific channel slot in an N-channel
/// output stream, with silence in all other slots. Used to wire each AudioChannel
/// into the main 8-channel mixer.
/// </summary>
file sealed class MonoToMultichannelProvider : ISampleProvider
{
    private readonly ISampleProvider source;
    private readonly int targetChannel;
    private readonly int channelCount;
    private float[] monoBuffer = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }

    /// <param name="targetChannel">Channel index to route to, or -1 to broadcast to all channels (stereo test mode).</param>
    public MonoToMultichannelProvider(ISampleProvider monoSource, int targetChannel, int channelCount)
    {
        if (monoSource.WaveFormat.Channels != 1)
            throw new ArgumentException("Source must be mono", nameof(monoSource));
        if (targetChannel != -1 && (targetChannel < 0 || targetChannel >= channelCount))
            throw new ArgumentOutOfRangeException(nameof(targetChannel));

        this.source = monoSource;
        this.targetChannel = targetChannel;
        this.channelCount = channelCount;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(monoSource.WaveFormat.SampleRate, channelCount);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / channelCount;
        if (monoBuffer.Length < frames)
            monoBuffer = new float[frames];

        int read = source.Read(monoBuffer, 0, frames);
        Array.Clear(buffer, offset, count);

        if (targetChannel < 0)
        {
            // Broadcast mode: write to all channels so all zones are audible on stereo headphones
            for (int i = 0; i < read; i++)
                for (int c = 0; c < channelCount; c++)
                    buffer[offset + i * channelCount + c] += monoBuffer[i];
        }
        else
        {
            for (int i = 0; i < read; i++)
                buffer[offset + i * channelCount + targetChannel] = monoBuffer[i];
        }

        // AudioChannel.Read always returns frames, so read * channelCount == count
        return read * channelCount;
    }
}
