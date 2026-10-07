namespace TriviaBattle.Server.Data.Entities;

/// <summary>
/// A returning player, identified by the RFID card they swipe at the kiosk. Table: PlayerProfiles.
/// Guests (no card) don't get a profile and don't appear on leaderboards.
/// </summary>
public class PlayerProfileEntity
{
    public int Id { get; set; }

    /// <summary>The card's id exactly as the reader types it. Unique.</summary>
    public string RfidTag { get; set; } = "";

    public string DisplayName { get; set; } = "";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastSeenAt { get; set; } = DateTime.UtcNow;
}
