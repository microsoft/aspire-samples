namespace Api.Models;

public record CreateTodoRequest(string Title);
public record UpdateTodoRequest(string? Title, bool? Completed);