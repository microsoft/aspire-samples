namespace Api.Models;

public class Todo
{
    public const int MaxTitleLength = 200;

    public int Id { get; set; }
    public required string Title { get; set; }
    public bool Completed { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
