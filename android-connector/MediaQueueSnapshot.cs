using System.Text.Json.Nodes;

namespace CarThing.Connector;

// A Car Thing queue index is relative to the current item, not an Android queue ID.
internal sealed class MediaQueueSnapshot(long[] ids, long activeId)
{
    public long? Resolve(int offset, long[] currentIds, long currentActiveId)
    {
        if (activeId != currentActiveId || !ids.SequenceEqual(currentIds)) return null;
        int active = Array.IndexOf(ids, activeId);
        long target = (long)active + offset;
        return active >= 0 && target >= 0 && target < ids.Length ? ids[(int)target] : null;
    }

    public static JsonObject? Build(JsonObject[] items, long[] ids, long activeId)
    {
        int active = Array.IndexOf(ids, activeId);
        if (active < 0 || items.Length != ids.Length || ids.Distinct().Count() != ids.Length) return null;
        return new JsonObject
        {
            ["previous"] = new JsonArray(items.Take(active).Select(x => x.DeepClone()).ToArray()),
            ["current"] = items[active].DeepClone(),
            ["next"] = new JsonArray(items.Skip(active + 1).Select(x => x.DeepClone()).ToArray())
        };
    }
}
