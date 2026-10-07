namespace TriviaBattle.Server.Data.Entities;

/// <summary>A question category players pick at the kiosk, e.g. "Geography". Table: Categories.</summary>
public class CategoryEntity
{
    public int Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Inactive categories are hidden from the kiosk but kept for match history.</summary>
    public bool IsActive { get; set; } = true;

    public List<QuestionEntity> Questions { get; set; } = new();
}
