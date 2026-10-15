using Microsoft.Extensions.Configuration;
using TriviaBattle.Server.Audio;

namespace TriviaBattle.Tests.Hardware;

/// <summary>Speaker outputs from config/stations.json (0-based outputs on the room's sound card).</summary>
public class RoomAudioConfigTests
{
    [Fact]
    public void Shipped_stations_json_matches_the_three_amps_and_the_sub()
    {
        var configFile = Path.Combine(RepoRoot(), "src", "TriviaBattle.Server", "config", "stations.json");
        var config = new ConfigurationBuilder().AddJsonFile(configFile).Build();

        var room = RoomAudioMap.Load(config).Find("A")!;

        Assert.Equal([0, 1, 4, 5], room.Stations.Select(s => s.Output)); // green jack, black jack
        Assert.Equal("A1", room.Stations[0].StationId);
        Assert.Equal((6, 7), (room.Room, room.Attract));                  // grey jack
        Assert.Equal(2, room.Sub);                                        // orange jack (centre)
    }

    [Fact]
    public void Two_speakers_on_one_output_is_a_config_error()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rooms:0:Id"] = "A",
            ["Rooms:0:Audio:Room"] = "1",
            ["Rooms:0:Stations:0:Number"] = "1",
            ["Rooms:0:Stations:0:AudioOutput"] = "0",
            ["Rooms:0:Stations:1:Number"] = "2",
            ["Rooms:0:Stations:1:AudioOutput"] = "1", // same as the room speaker
        }).Build();

        var error = Assert.Throws<InvalidOperationException>(() => RoomAudioMap.Load(config));
        Assert.Contains("output 1", error.Message);
    }

    [Fact]
    public void A_speaker_left_out_has_no_output()
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Rooms:0:Id"] = "A",
            ["Rooms:0:Stations:0:Number"] = "1",
        }).Build();

        var room = RoomAudioMap.Load(config).Find("A")!;

        Assert.Null(room.Stations[0].Output);
        Assert.Null(room.Sub);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "TriviaBattle.sln")))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
