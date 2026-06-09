using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Models;

namespace Api.Extensions;

public static class TodoEndpoints
{
    private const int DefaultTodoLimit = 50;
    private const int MaxTodoLimit = 100;

    public static WebApplication MapTodos(this WebApplication app)
    {
        var group = app.MapGroup("/api");

        // Get todos
        group.MapGet("/todos", async (int? offset, int? limit, TodoDbContext db) =>
        {
            var normalizedOffset = offset ?? 0;
            var normalizedLimit = limit ?? DefaultTodoLimit;

            if (normalizedOffset < 0)
            {
                return ValidationProblem("offset", "Offset must be greater than or equal to 0.");
            }

            if (normalizedLimit is < 1 or > MaxTodoLimit)
            {
                return ValidationProblem("limit", $"Limit must be between 1 and {MaxTodoLimit}.");
            }

            var todos = await db.Todos
                .OrderBy(t => t.Id)
                .Skip(normalizedOffset)
                .Take(normalizedLimit)
                .ToListAsync();

            return Results.Ok(todos);
        });

        // Get todo by id
        group.MapGet("/todos/{id}", async (int id, TodoDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            return todo is not null ? Results.Ok(todo) : Results.NotFound();
        });

        // Create todo
        group.MapPost("/todos", async (CreateTodoRequest request, TodoDbContext db) =>
        {
            var validationResult = ValidateTitle(request.Title, out var title);
            if (validationResult is not null)
            {
                return validationResult;
            }

            var todo = new Todo
            {
                Title = title,
                Completed = false
            };

            db.Todos.Add(todo);
            await db.SaveChangesAsync();

            return Results.Created($"/api/todos/{todo.Id}", todo);
        });

        // Update todo
        group.MapPut("/todos/{id}", async (int id, UpdateTodoRequest request, TodoDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            if (request.Title is not null)
            {
                var validationResult = ValidateTitle(request.Title, out var title);
                if (validationResult is not null)
                {
                    return validationResult;
                }

                todo.Title = title;
            }

            if (request.Completed is not null)
            {
                todo.Completed = request.Completed.Value;
            }

            await db.SaveChangesAsync();
            return Results.Ok(todo);
        });

        // Delete todo
        group.MapDelete("/todos/{id}", async (int id, TodoDbContext db) =>
        {
            var todo = await db.Todos.FindAsync(id);
            if (todo is null)
            {
                return Results.NotFound();
            }

            db.Todos.Remove(todo);
            await db.SaveChangesAsync();

            return Results.Ok(new { message = $"Todo {id} deleted" });
        });

        return app;
    }

    private static IResult? ValidateTitle(string? title, out string normalizedTitle)
    {
        normalizedTitle = title?.Trim() ?? string.Empty;

        if (normalizedTitle.Length == 0)
        {
            return ValidationProblem("title", "Title is required.");
        }

        if (normalizedTitle.Length > Todo.MaxTitleLength)
        {
            return ValidationProblem("title", $"Title must be {Todo.MaxTitleLength} characters or fewer.");
        }

        return null;
    }

    private static IResult ValidationProblem(string key, string message)
    {
        Dictionary<string, string[]> errors = new()
        {
            [key] = [message]
        };

        return Results.ValidationProblem(errors);
    }
}
