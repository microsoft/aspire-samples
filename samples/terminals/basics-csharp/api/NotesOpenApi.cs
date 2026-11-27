using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Notes.Api;

internal static class NotesOpenApi
{
    public static IServiceCollection AddNotesOpenApi(this IServiceCollection services) => services.AddOpenApi(options =>
    {
        options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info = new OpenApiInfo
            {
                Title = "Terminal Notes API",
                Version = "v1",
                Description = """
                    Plain-text notes for the Aspire terminal samples. Each note is a field of the Redis hash `notes`, so you can
                    create one from the Slumber REST client in the dashboard, change it with `HSET` in the Redis REPL, and read
                    the new value back here: the API has no cache.

                    IDs are 1-40 lowercase letters, digits or hyphens, and text is 1-1000 characters. Errors are returned as
                    [RFC 9457](https://www.rfc-editor.org/rfc/rfc9457) problem details. This local demo has no authentication.
                    """
            };
            document.Tags = new HashSet<OpenApiTag>
            {
                new() { Name = "Notes", Description = "Create, read, replace and delete notes stored in Redis." },
                new() { Name = "Demo", Description = "Development-only operations behind the Aspire dashboard commands." }
            };
            return Task.CompletedTask;
        });

        options.AddSchemaTransformer((schema, context, _) =>
        {
            var type = context.JsonTypeInfo.Type;
            if (type == typeof(Note))
            {
                schema.Examples = [new JsonObject { ["id"] = "welcome", ["text"] = "Edited from Redis" }];
            }
            else if (type == typeof(NoteContent))
            {
                schema.Examples = [new JsonObject { ["text"] = "Hello from Slumber!" }];
            }
            else if (type == typeof(SeedResult))
            {
                schema.Examples =
                [
                    new JsonObject
                    {
                        ["created"] = 4,
                        ["replaced"] = 1,
                        ["notes"] = new JsonArray(
                            new JsonObject { ["id"] = "design-review", ["text"] = "Design review on Thursday at 14:00: note sharing, offline sync and the 1,000-character limit." },
                            new JsonObject { ["id"] = "welcome", ["text"] = "Edited from Redis" })
                    }
                ];
            }
            return Task.CompletedTask;
        });

        // Problem details share one schema, so give each status code its own realistic example and description.
        options.AddOperationTransformer((operation, _, _) =>
        {
            foreach (var parameter in operation.Parameters?.OfType<OpenApiParameter>() ?? [])
            {
                if (parameter.Name == "id")
                {
                    parameter.Example = JsonValue.Create("welcome");
                }
            }

            foreach (var (status, response) in operation.Responses ?? [])
            {
                if (response is OpenApiResponse documented && ResponseDescription(operation.OperationId, status) is { } description)
                {
                    documented.Description = description;
                }
                if (response.Content?.TryGetValue("application/problem+json", out var media) == true &&
                    ProblemExample(status, invalidField: operation.RequestBody is null ? "id" : "text") is { } example)
                {
                    media.Example = example;
                }
            }
            return Task.CompletedTask;
        });
    });

    private static string? ResponseDescription(string? operationId, string status) => (operationId, status) switch
    {
        ("ListNotes", "200") => "The stored notes.",
        ("GetNote", "200") => "The note.",
        ("PutNote", "200") => "Existing text was replaced.",
        ("PutNote", "201") => "The note was created.",
        ("DeleteNote", "204") => "The note was deleted.",
        ("SeedNotes", "200") => "The sample notes were written.",
        ("PutNote", "400") => "The note ID, the JSON body or its text is invalid.",
        (_, "400") => "The note ID is invalid.",
        (_, "404") => "No note has this ID.",
        (_, "415") => "The body is not `application/json`.",
        (_, "503") => "Redis could not be reached.",
        _ => null
    };

    private static JsonObject? ProblemExample(string status, string invalidField) => status switch
    {
        "400" => Problem("https://tools.ietf.org/html/rfc9110#section-15.5.1", "One or more validation errors occurred.", 400, null,
            new JsonObject { [invalidField] = new JsonArray(invalidField == "id"
                ? "Use 1-40 lowercase letters, digits or hyphens for the note ID."
                : "Supply text containing 1-1000 characters, not just whitespace.") }),
        "404" => Problem("https://tools.ietf.org/html/rfc9110#section-15.5.5", "Note not found", 404, "No note has the ID 'welcome'. GET /notes lists the stored notes."),
        "415" => Problem("https://tools.ietf.org/html/rfc9110#section-15.5.16", "Invalid request", 415, "Send the note as application/json."),
        "503" => Problem("https://tools.ietf.org/html/rfc9110#section-15.6.4", "Redis is unavailable", 503, "The notes store couldn't be reached. Check the redis resource in the Aspire dashboard."),
        _ => null
    };

    private static JsonObject Problem(string type, string title, int status, string? detail, JsonObject? errors = null)
    {
        var problem = new JsonObject { ["type"] = type, ["title"] = title, ["status"] = status };
        if (detail is not null)
        {
            problem["detail"] = detail;
        }
        if (errors is not null)
        {
            problem["errors"] = errors;
        }
        problem["traceId"] = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
        return problem;
    }
}
