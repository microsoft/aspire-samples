using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Notes.Api;
using StackExchange.Redis;
using Xunit;

public class NotesTests
{
    [Fact]
    public async Task CrudRoundTripAndDirectStoreChanges()
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        Assert.Empty((await client.GetFromJsonAsync<Note[]>("/notes"))!);

        var created = await client.PutAsJsonAsync("/notes/welcome", new { text = "Hello from Slumber!" });
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.Equal("/notes/welcome", created.Headers.Location!.ToString());
        Assert.Equal(new Note("welcome", "Hello from Slumber!"), await created.Content.ReadFromJsonAsync<Note>());
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync("/notes/welcome", new { text = "Updated" })).StatusCode);

        await factory.Store.SetAsync(new Note("welcome", "Edited from Redis"));
        Assert.Equal(new Note("welcome", "Edited from Redis"), await client.GetFromJsonAsync<Note>("/notes/welcome"));
        await client.PutAsJsonAsync("/notes/aaa", new { text = "First" });
        Assert.Equal(["aaa", "welcome"], (await client.GetFromJsonAsync<Note[]>("/notes"))!.Select(note => note.Id));
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync("/notes/welcome")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/notes/welcome")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync("/notes/welcome")).StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"text\":null}")]
    [InlineData("{\"text\":1}")]
    [InlineData("{\"text\":\"\"}")]
    [InlineData("{\"text\":\"   \"}")]
    [InlineData("[]")]
    [InlineData("{")]
    public async Task InvalidBodiesAreRejected(string body)
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        var response = await client.PutAsync("/notes/welcome", new StringContent(body, Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await factory.Store.ListAsync());
    }

    [Fact]
    public async Task ValidatesExactTextAndIdBoundaries()
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Created,
            (await client.PutAsJsonAsync($"/notes/{new string('a', 40)}", new { text = new string('x', 1000) })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await client.PutAsJsonAsync("/notes/too-long", new { text = new string('x', 1001) })).StatusCode);
        foreach (var id in new[] { "UPPER", "under_score", "a%0A", new string('a', 41) })
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync($"/notes/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.DeleteAsync($"/notes/{id}")).StatusCode);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync($"/notes/{id}", new { text = "No" })).StatusCode);
        }
    }

    [Fact]
    public async Task NonJsonContentIsRejected()
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        var response = await client.PutAsync("/notes/welcome", new StringContent("Hello"));
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Empty(await factory.Store.ListAsync());
    }

    [Fact]
    public async Task RedisFailureIsNotReportedAsAnEmptyCollection()
    {
        await using var factory = new NotesFactory();
        factory.Store.Failure = new RedisConnectionException(ConnectionFailureType.UnableToConnect, "Unavailable");
        using var client = factory.CreateClient();
        var response = await client.GetAsync("/notes");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
    }

    [Fact]
    public async Task SeedWritesSampleNotesAndLeavesWalkthroughNotesAlone()
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        await factory.Store.SetAsync(new Note("welcome", "Edited from Redis"));
        await factory.Store.SetAsync(new Note("standup", "Stale standup notes"));

        var response = await client.PostAsync("/dev/notes/seed", content: null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SeedResult>())!;
        Assert.Equal((4, 1, 6), (result.Created, result.Replaced, result.Notes.Length));
        Assert.Contains(new Note("welcome", "Edited from Redis"), result.Notes);
        Assert.NotEqual("Stale standup notes", (await client.GetFromJsonAsync<Note>("/notes/standup"))!.Text);
    }

    [Fact]
    public async Task OpenApiDocumentDescribesTheNotesRoutes()
    {
        await using var factory = new NotesFactory();
        using var client = factory.CreateClient();
        using var document = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        var paths = document.RootElement.GetProperty("paths");
        Assert.Equal(["get"], paths.GetProperty("/notes").EnumerateObject().Select(operation => operation.Name));
        Assert.Equal(["get", "put", "delete"], paths.GetProperty("/notes/{id}").EnumerateObject().Select(operation => operation.Name));
        Assert.True(paths.TryGetProperty("/dev/notes/seed", out _));
    }

    private sealed class NotesFactory : WebApplicationFactory<Program>
    {
        public FakeNotesStore Store { get; } = new();

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseContentRoot(Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../api")));
            builder.ConfigureAppConfiguration(config => config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:redis"] = "localhost:6379"
            }));
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<INotesStore>();
                services.AddSingleton<INotesStore>(Store);
            });
        }
    }

    private sealed class FakeNotesStore : INotesStore
    {
        private readonly Dictionary<string, string> _notes = new(StringComparer.Ordinal);
        public Exception? Failure { get; set; }
        public Task<Note[]> ListAsync() => Failure is { } error
            ? Task.FromException<Note[]>(error)
            : Task.FromResult(_notes.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => new Note(pair.Key, pair.Value)).ToArray());
        public Task<Note?> GetAsync(string id) =>
            Task.FromResult(_notes.TryGetValue(id, out var text) ? new Note(id, text) : null);
        public Task<bool> SetAsync(Note note)
        {
            var created = !_notes.ContainsKey(note.Id);
            _notes[note.Id] = note.Text;
            return Task.FromResult(created);
        }
        public Task<bool> DeleteAsync(string id) => Task.FromResult(_notes.Remove(id));
    }
}
