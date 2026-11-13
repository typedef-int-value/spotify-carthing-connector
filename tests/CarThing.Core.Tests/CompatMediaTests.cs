using System.Text.Json.Nodes;
using CarThing.Core;
using CarThing.Connector;

internal static class CompatMediaTests
{
    private static JsonObject Object(string json) => JsonNode.Parse(json)!.AsObject();
    private static void Check(bool success, string message)
    {
        if (!success) throw new Exception("Compat media: " + message);
    }

    public static async Task RunAsync()
    {
        Check(MediaCommands.RepeatTarget("com.spotify.superbird.set_repeat", Object("{\"repeat_mode\":\"TRACK\"}")) == 1, "repeat track");
        Check(MediaCommands.RepeatTarget("com.spotify.superbird.set_repeat", Object("{\"repeat_mode\":\"CONTEXT\"}")) == 2, "repeat context");
        Check(MediaCommands.RepeatTarget("com.spotify.superbird.set_repeat", Object("{\"repeat_mode\":\"NONE\"}")) == 0, "repeat off");
        Check(MediaCommands.RepeatTarget("com.spotify.set_repeat", Object("{\"repeat\":2}")) == 2, "legacy repeat");
        Check(MediaCommands.PodcastSpeed(Object("{\"playback_speed\":1.5}")) == 1.5f, "speed is not play/pause");
        foreach (Action invalid in new Action[]
        {
            () => MediaCommands.RepeatTarget("com.spotify.superbird.set_repeat", Object("{\"repeat_mode\":\"BAD\"}")),
            () => MediaCommands.RepeatTarget("com.spotify.set_repeat", Object("{\"repeat\":3}")),
            () => MediaCommands.PodcastSpeed(Object("{\"playback_speed\":0}")),
            () => MediaCommands.PodcastSpeed(Object("{\"playback_speed\":1e999}")),
            () => MediaCommands.SavedTarget(Object("{\"saved\":1}")),
            () => MediaCommands.IntegerArgument(Object("{\"index\":1.5}"), "index")
        })
        {
            bool rejected = false;
            try { invalid(); } catch (ArgumentException) { rejected = true; }
            Check(rejected, "invalid arguments rejected");
        }
        long[] ids = [41, 83, 902];
        var snapshot = new MediaQueueSnapshot(ids, 83);
        Check(snapshot.Resolve(-1, ids, 83) == 41 && snapshot.Resolve(0, ids, 83) == 83 &&
            snapshot.Resolve(1, ids, 83) == 902, "relative indexes map to IDs");
        Check(snapshot.Resolve(int.MaxValue, ids, 83) == null && snapshot.Resolve(int.MinValue, ids, 83) == null,
            "bounds and overflow");
        Check(snapshot.Resolve(1, [41, 83, 903], 83) == null && snapshot.Resolve(1, ids, 41) == null,
            "changed queue or active item rejected");
        JsonObject[] items = [Object("{\"name\":\"Previous\"}"), Object("{\"name\":\"Current\"}"), Object("{\"name\":\"Next\"}")];
        var queue = MediaQueueSnapshot.Build(items, ids, 83)!;
        Check(queue["previous"]![0]!["name"]!.GetValue<string>() == "Previous" &&
            queue["next"]![0]!["name"]!.GetValue<string>() == "Next", "queue split");
        Check(MediaQueueSnapshot.Build(items, ids, -1) == null && MediaQueueSnapshot.Build(items, [1, 1, 2], 1) == null,
            "unknown active item and duplicate IDs rejected");

        var frames = new List<JsonArray>();
        var calls = new List<string>();
        var bridge = new MediaBridge(frame => { frames.Add(frame); return Task.CompletedTask; },
            control: (uri, _) => { calls.Add(uri); return Task.FromResult(true); }, spotifyUiExperiment: true);
        await bridge.HandleAsync(JsonNode.Parse("[1,\"spotify\",{\"authid\":\"test\",\"authmethods\":[\"wampcra\"]}]")!.AsArray());
        await bridge.HandleAsync(JsonNode.Parse("[5,\"signature\",{}]")!.AsArray());
        foreach (string topic in new[] { "com.spotify.play_queue", "com.spotify.podcast_playback_speed", "com.spotify.superbird.player_state" })
            await bridge.HandleAsync(new JsonArray(32, 1, new JsonObject(), topic));
        var state = Object("""
            {"context_title":"My playlist","playback_speed":0,"_podcast_speed":1.5,"_can_rate":true,
             "track":{"name":"Song","artist":{"name":"Artist"},"album":{"name":"Album"},"saved":true,"image_id":"current-cover"}}
            """);
        state["_queue"] = queue;
        await bridge.PublishAsync(state);
        var published = frames[^1][5]!;
        Check(published["context_title"]!.GetValue<string>() == "My playlist" && published["_queue"] == null &&
            published["_can_rate"] == null && published["_podcast_speed"] == null, "private adapter fields excluded");
        Check(frames.Any(f => f[0]!.GetValue<int>() == 36 && f[5]?["next"]?[0]?["name"]?.GetValue<string>() == "Next"), "queue published");
        var queueFrame = frames.Last(f => f[0]!.GetValue<int>() == 36 && f[5]?["next"] is JsonArray);
        Check(queueFrame[5]!["current"]!["image_uri"]?.GetValue<string>() == "current-cover",
            "native now-playing artwork uses current metadata even with an app-provided queue");
        Check(state["_queue"]!["current"]!["image_uri"] == null, "publishing does not mutate adapter queue");
        foreach (string cover in new[] { "", "delayed-cover", "next-track-cover" })
        {
            state["track"]!["image_id"] = cover;
            await bridge.PublishAsync(state);
            queueFrame = frames.Last(f => f[0]!.GetValue<int>() == 36 && f[5]?["next"] is JsonArray);
            Check(queueFrame[5]!["current"]!["image_uri"]!.GetValue<string>() == cover,
                "queue artwork follows missing, delayed and changed current metadata");
            Check(queueFrame[5]!["previous"]![0]!["image_uri"] == null && queueFrame[5]!["next"]![0]!["image_uri"] == null,
                "current artwork is not assigned to adjacent tracks");
        }
        Check(frames.Any(f => f[0]!.GetValue<int>() == 36 && f[5]?["podcast_playback_speed"]?.GetValue<double>() == 1.5), "paused speed retained");
        foreach (string uri in new[] { "com.spotify.superbird.set_repeat", "com.spotify.set_repeat", "com.spotify.set_saved",
            "com.spotify.set_podcast_playback_speed", "com.spotify.skip_to_index_in_queue" })
        {
            await bridge.HandleAsync(new JsonArray(48, 7, new JsonObject(), uri, new JsonArray(), new JsonObject()));
            Check(calls[^1] == uri && frames[^1][0]!.GetValue<int>() == 50, "control routed");
        }
        await bridge.HandleAsync(new JsonArray(48, 8, new JsonObject(), "com.spotify.get_saved", new JsonArray(),
            new JsonObject { ["id"] = MediaCommands.LocalTrackUri("Song", "Artist", "Album") }));
        Check(frames[^1][3]!["saved"]!.GetValue<bool>(), "current heart state");
        state.Remove("_queue");
        state["_can_rate"] = false;
        state.Remove("_podcast_speed");
        await bridge.PublishAsync(state);
        Check(frames.Any(f => f[0]!.GetValue<int>() == 36 && f[5]?["next"] is JsonArray next && next.Count == 0), "queue fallback clears old items");
        await bridge.HandleAsync(new JsonArray(48, 9, new JsonObject(), "com.spotify.get_podcast_playback_speed", new JsonArray(), new JsonObject()));
        Check(frames[^1][0]!.GetValue<int>() == 8, "old speed cleared after source change");
        Console.WriteLine("PASS: repeat/speed/rating validation, relative queue IDs, stale queue rejection, compat routing and state publication.");
    }
}
