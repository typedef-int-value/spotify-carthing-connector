using System.Text.Json.Nodes;
using CarThing.Core;

internal static class HomeCardTests
{
    public static async Task RunAsync()
    {
        var frames = new List<JsonArray>();
        int resumes = 0;
        var bridge = new MediaBridge(m => { frames.Add(m); return Task.CompletedTask; },
            control: (uri, _) => { if (uri == "com.spotify.superbird.resume") resumes++; return Task.FromResult(true); },
            spotifyUiExperiment: true);
        await bridge.HandleAsync(JsonNode.Parse("[1,\"spotify\",{\"authid\":\"test\",\"authmethods\":[\"wampcra\"]}]")!.AsArray());
        await bridge.HandleAsync(JsonNode.Parse("[5,\"signature\",{}]")!.AsArray());
        await bridge.HandleAsync(JsonNode.Parse("[32,1,{},\"com.spotify.superbird.player_state\"]")!.AsArray());
        var state = JsonNode.Parse("""
            {"context_uri":"spotify:context:fake","context_title":"Test Music",
             "currently_active_application":{"name":"Test Music"},
             "track":{"name":"Song","artist":{"name":"Artist"},"album":{"name":"Album"},"image_id":"cover"}}
            """)!.AsObject();
        await bridge.PublishAsync(state);
        var published = frames[^1][5]!;
        Check(published["context_uri"]!.GetValue<string>().StartsWith("spotify:playlist:") &&
            published["context_title"]!.GetValue<string>() == "Test Music" &&
            published["track"]!["album"]!["name"]!.GetValue<string>() == "Album", "header and album");
        var query = JsonNode.Parse("""
            [48,2,{},"com.spotify.superbird.graphql",[],{"payload":"query{shelf(limit:14 overrides:[]){...on Shelf{items{title id total children{uri title subtitle image_id:imageId}}}...on ShelfError{message}}}"}]
            """)!.AsArray();
        await bridge.HandleAsync(query);
        var card = frames[^1][3]!["data"]!["shelf"]!["items"]![0]!["children"]![0]!;
        Check(card["title"]!.GetValue<string>() == "Song" && card["subtitle"]!.GetValue<string>() == "Artist" &&
            card["image_id"]!.GetValue<string>() == "cover", "home metadata");
        string oldUri = card["uri"]!.GetValue<string>();
        var legacyQuery = JsonNode.Parse("[48,4,{},\"com.spotify.superbird.get_home\",[],{\"limit\":14,\"overrides\":{}}]")!.AsArray();
        await bridge.HandleAsync(legacyQuery);
        Check(frames[^1][0]!.GetValue<int>() == 50, "legacy home response");
        var legacyCategory = frames[^1][3]!["items"]![0]!;
        Check(legacyCategory["uri"] != null && legacyCategory["children"]![0]!["uri"]!.GetValue<string>() == oldUri,
            "legacy and GraphQL expose the same current card");
        async Task Play(string uri) => await bridge.HandleAsync(new JsonArray(48, 3, new JsonObject(),
            "com.spotify.play_uri", new JsonArray(), new JsonObject { ["uri"] = uri }));
        await Play(oldUri);
        Check(resumes == 1 && frames[^1][0]!.GetValue<int>() == 50, "current card resumes");
        state["track"]!["name"] = "Next Song";
        await bridge.PublishAsync(state);
        await Play(oldUri);
        Check(resumes == 1 && frames[^1][0]!.GetValue<int>() == 8, "stale card rejected");
        state["track"] = null;
        await bridge.PublishAsync(state);
        await bridge.HandleAsync(query);
        Check(frames[^1][3]!["data"]!["shelf"]!["items"]!.AsArray().Count == 0, "no media clears home");
        await bridge.HandleAsync(legacyQuery);
        Check(frames[^1][3]!["items"]!.AsArray().Count == 0, "no media clears legacy home");
        Console.WriteLine("PASS: native app header, current home card, resume, stale card rejection, empty media.");
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new Exception("Home card: " + name);
    }
}
