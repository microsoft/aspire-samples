using System.Text.Json;
using System.Text.Json.Serialization;

namespace ImageGallery.Shared.Serialization;

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, UseStringEnumConverter = true)]
[JsonSerializable(typeof(PhotoProcessed))]
[JsonSerializable(typeof(Dictionary<string, string>))]
public sealed partial class SerializationContext : JsonSerializerContext
{
}
