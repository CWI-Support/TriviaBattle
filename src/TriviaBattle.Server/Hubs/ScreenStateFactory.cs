using Microsoft.Extensions.Options;
using TriviaBattle.Core.Matches;
using TriviaBattle.Core.Snapshots;
using TriviaBattle.Core.Stations;
using TriviaBattle.Server.Display;

namespace TriviaBattle.Server.Hubs;

/// <summary>Bound from "Media" in appsettings.json.</summary>
public class MediaSettings
{
    /// <summary>Video played on every room screen during the Intro phase. Empty = no video.</summary>
    public string IntroVideoUrl { get; set; } = "/media/intro.webm";

    /// <summary>
    /// Gap between the Intro phase starting and the video starting, so every screen has time
    /// to load it. Game:IntroSeconds should be at least this plus the video's length.
    /// </summary>
    public double IntroVideoDelaySeconds { get; set; } = 1.5;
}

/// <summary>
/// Builds the <see cref="ScreenState"/> each screen receives, including any synced media cue.
/// Used both when a screen joins (GameHub) and on every change (SignalRMatchBroadcaster),
/// so the two always agree.
/// </summary>
public class ScreenStateFactory(StationLayout stations, IOptionsMonitor<MediaSettings> media, ThemeService themes)
{
    public ScreenState Create(string roomId, Station? station, MatchSnapshot? match) =>
        new(Room(roomId), station == null ? null : StationInfo.From(station), match, CueFor(match), themes.Current);

    public RoomInfo Room(string roomId) =>
        new(roomId, stations.RoomName(roomId), stations.InRoom(roomId).Select(StationInfo.From).ToList());

    /// <summary>
    /// Synced moments: "play this at server time T". Right now that's only the intro video.
    /// To add another (e.g. a results fanfare), return a cue for that phase here.
    /// </summary>
    private MediaCue? CueFor(MatchSnapshot? match)
    {
        var settings = media.CurrentValue;
        if (match?.Phase != MatchPhase.Intro || string.IsNullOrEmpty(settings.IntroVideoUrl))
            return null;

        var startAtMs = match.PhaseStartedAtMs + (long)(settings.IntroVideoDelaySeconds * 1000);
        return new MediaCue($"intro-{match.MatchId}", settings.IntroVideoUrl, startAtMs);
    }
}
