using System.Text.Json.Nodes;

namespace CarThing.Core;

public sealed class MediaImageCache
{
    private readonly OrderedDictionary<string, JsonObject> images = new();

    public JsonObject? Get(string id) =>
        images.TryGetValue(id, out var image) ? image.DeepClone().AsObject() : null;

    public void Store(string id, JsonObject image)
    {
        images[id] = image;
        // Preserve insertion order when removed slots are reused by later tracks.
        while (images.Count > 8) images.RemoveAt(0);
    }
}
