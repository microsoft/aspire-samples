using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http.HttpResults;

namespace Notes.Api;

public sealed record NoteContent(
    [property: Description("The note text: 1-1000 characters, not just whitespace.")]
    [property: Required, StringLength(1000, MinimumLength = 1)] string Text);

public sealed record SeedResult(
    [property: Description("Sample notes that didn't exist yet.")] int Created,
    [property: Description("Sample notes whose text was restored.")] int Replaced,
    [property: Description("Every stored note after seeding, including notes that aren't samples.")] Note[] Notes);

public static partial class NotesApi
{
    private const string IdPattern = "^[a-z0-9-]{1,40}$";
    private const string IdDescription = "The note ID: 1-40 lowercase letters, digits or hyphens.";
    private const string IdRule = "Use 1-40 lowercase letters, digits or hyphens for the note ID.";
    private const string TextRule = "Supply text containing 1-1000 characters, not just whitespace.";

    // Backs the "Seed sample notes" dashboard command. The walkthroughs' 'welcome' and 'automation' notes are never touched.
    private static readonly Note[] SampleNotes =
    [
        new("standup", "Standup at 09:30: Priya demos the Redis REPL, Marco is blocked on the staging certificate."),
        new("release-checklist", "Release 2.4: tag main, run the smoke suite, publish the changelog and post in #releases."),
        new("oncall-handoff", "On-call handoff: the checkout p95 latency alert stays muted until the cache warm-up fix ships."),
        new("design-review", "Design review on Thursday at 14:00: note sharing, offline sync and the 1,000-character limit."),
        new("retro-actions", "Retro actions: add Redis health to the status page and document the Slumber keyboard shortcuts.")
    ];

    [GeneratedRegex(@"\A[a-z0-9-]{1,40}\z", RegexOptions.CultureInvariant)]
    private static partial Regex ValidId();

    public static RouteGroupBuilder MapNotesApi(this IEndpointRouteBuilder routes)
    {
        var logger = CreateLogger(routes);

        // Demo-only, unauthenticated CRUD routes: every caller can read, replace, and delete notes.
        // Use non-sensitive, disposable data and keep this HTTP API private.
        var notes = routes.MapGroup("/notes").WithTags("Notes");
        notes.AddEndpointFilter(async (context, next) =>
        {
            if (context.HttpContext.Request.RouteValues["id"] is string id && !ValidId().IsMatch(id))
            {
                var operation = context.HttpContext.Request.Method switch { "PUT" => "write", "DELETE" => "delete", _ => "read" };
                context.HttpContext.RequestServices.GetRequiredService<NotesTelemetry>().OperationCompleted(operation, "invalid");
                logger.RequestRejected(operation, IdRule);
                return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["id"] = [IdRule] });
            }
            return await next(context);
        });

        notes.MapGet("/", async Task<Ok<Note[]>> (INotesStore store, NotesTelemetry telemetry) =>
            {
                using var operation = telemetry.StartOperation("list");
                var all = await store.ListAsync();
                operation.Activity?.SetTag("notes.count", all.Length);
                operation.Complete("ok");
                telemetry.NotesListed(all.Length);
                logger.NotesListed(all.Length);
                return TypedResults.Ok(all);
            })
            .WithName("ListNotes")
            .WithSummary("List notes")
            .WithDescription("Returns every note in the Redis hash `notes`, ordered by ID. The API has no cache, so values changed in the Redis REPL appear immediately.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notes.MapGet("/{id}", async Task<Results<Ok<Note>, ProblemHttpResult>> (
                [Description(IdDescription)][StringLength(40, MinimumLength = 1)][RegularExpression(IdPattern)] string id,
                INotesStore store,
                NotesTelemetry telemetry) =>
            {
                using var operation = telemetry.StartOperation("read", id);
                if (await store.GetAsync(id) is not { } note)
                {
                    operation.Complete("not_found");
                    logger.NoteNotFound(id);
                    return NotFound(id);
                }

                operation.Activity?.SetTag("note.text.length", note.Text.Length);
                operation.Complete("ok");
                logger.NoteRead(id, note.Text.Length);
                return TypedResults.Ok(note);
            })
            .WithName("GetNote")
            .WithSummary("Read a note")
            .WithDescription("Returns one note. Try it after `HSET notes welcome \"Edited from Redis\"` in the Redis REPL: the new text comes straight from Redis.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notes.MapPut("/{id}", async Task<Results<Created<Note>, Ok<Note>, ValidationProblem>> (
                [Description(IdDescription)][StringLength(40, MinimumLength = 1)][RegularExpression(IdPattern)] string id,
                NoteContent content,
                INotesStore store,
                NotesTelemetry telemetry) =>
            {
                using var operation = telemetry.StartOperation("write", id);
                // JSON without a string "text" property still binds, so check the value here.
                if (content.Text is not { } text || string.IsNullOrWhiteSpace(text) || text.Length > 1000)
                {
                    operation.Complete("invalid");
                    logger.RequestRejected("write", TextRule);
                    return TypedResults.ValidationProblem(new Dictionary<string, string[]> { ["text"] = [TextRule] });
                }

                operation.Activity?.SetTag("note.text.length", text.Length);
                var note = new Note(id, text);
                var created = await store.SetAsync(note);
                operation.Complete(created ? "created" : "replaced", created ? "note.created" : "note.replaced");
                telemetry.TextWritten(text.Length, created);
                if (created)
                {
                    telemetry.StoredNotesChanged(1);
                    logger.NoteCreated(id, text.Length);
                    return TypedResults.Created($"/notes/{id}", note);
                }

                logger.NoteReplaced(id, text.Length);
                return TypedResults.Ok(note);
            })
            .WithName("PutNote")
            .WithSummary("Create or replace a note")
            .WithDescription("Stores the text in the Redis hash `notes` under this ID: `201 Created` for a new note, `200 OK` when it replaces existing text. The request body is limited to 10 KiB and must be `application/json`.")
            .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        notes.MapDelete("/{id}", async Task<Results<NoContent, ProblemHttpResult>> (
                [Description(IdDescription)][StringLength(40, MinimumLength = 1)][RegularExpression(IdPattern)] string id,
                INotesStore store,
                NotesTelemetry telemetry) =>
            {
                using var operation = telemetry.StartOperation("delete", id);
                if (!await store.DeleteAsync(id))
                {
                    operation.Complete("not_found");
                    logger.NoteNotFound(id);
                    return NotFound(id);
                }

                operation.Complete("deleted", "note.deleted");
                telemetry.StoredNotesChanged(-1);
                logger.NoteDeleted(id);
                return TypedResults.NoContent();
            })
            .WithName("DeleteNote")
            .WithSummary("Delete a note")
            .WithDescription("Removes the note's field from the Redis hash `notes`.")
            .ProducesValidationProblem()
            .ProducesProblem(StatusCodes.Status404NotFound)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);

        return notes;
    }

    public static void MapDemoEndpoints(this IEndpointRouteBuilder routes)
    {
        var logger = CreateLogger(routes);

        routes.MapPost("/dev/notes/seed", async Task<Ok<SeedResult>> (INotesStore store, NotesTelemetry telemetry) =>
            {
                using var operation = telemetry.StartOperation("seed");
                var created = 0;
                foreach (var sample in SampleNotes)
                {
                    var isNew = await store.SetAsync(sample);
                    created += isNew ? 1 : 0;
                    telemetry.TextWritten(sample.Text.Length, isNew);
                    operation.Activity?.AddEvent(new ActivityEvent(isNew ? "note.created" : "note.replaced",
                        tags: new ActivityTagsCollection { ["note.id"] = sample.Id, ["note.text.length"] = sample.Text.Length }));
                }

                var all = await store.ListAsync();
                operation.Activity?.SetTag("notes.count", all.Length);
                operation.Complete("ok");
                telemetry.NotesListed(all.Length);
                logger.NotesSeeded(SampleNotes.Length, created, SampleNotes.Length - created, all.Length);
                return TypedResults.Ok(new SeedResult(created, SampleNotes.Length - created, all));
            })
            .WithTags("Demo")
            .WithName("SeedNotes")
            .WithSummary("Seed sample notes")
            .WithDescription("Development only. Writes five realistic team notes (or restores their text) and returns every stored note. The `welcome` and `automation` notes used by the walkthroughs are left alone. Backs the **Seed sample notes** dashboard command.")
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable);
    }

    private static ProblemHttpResult NotFound(string id) => TypedResults.Problem(
        title: "Note not found",
        detail: $"No note has the ID '{id}'. GET /notes lists the stored notes.",
        statusCode: StatusCodes.Status404NotFound);

    private static ILogger CreateLogger(IEndpointRouteBuilder routes) =>
        routes.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(NotesApi).FullName!);
}
