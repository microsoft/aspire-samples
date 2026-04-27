using Microsoft.EntityFrameworkCore;
using Api.Data;
using Api.Models;

namespace Api.Extensions;

public static class TodoEndpoints
{
    public static WebApplication MapTodos(this WebApplication app)
    {
        var group = app.MapGroup("/api");

        // Get all todos
        group.MapGet("/todos", async (TodoDbContext db) =>
        {
            return await db.Todos.OrderBy(t => t.Id).ToListAsync();
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
            var todo = new Todo
            {
                Title = request.Title,
                Completed = false
            };

            db.Todos.Add(todo);
            await db.SaveChangesAsync();

            return Results.Created($"/todos/{todo.Id}", todo);
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
                todo.Title = request.Title;
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
}