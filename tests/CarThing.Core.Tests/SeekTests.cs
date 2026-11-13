using System.Text.Json.Nodes;
using CarThing.Core;

internal static class SeekTests
{
    public static async Task RunAsync()
    {
        foreach (var (uri, key) in new[]
        {
            ("com.spotify.superbird.seek_to", "position"),
            ("com.spotify.set_playback_position", "position_ms")
        })
        {
            var frames = new List<JsonArray>();
            long actual = -1;
            bool supported = true;
            var bridge = new MediaBridge(m => { frames.Add(m); return Task.CompletedTask; },
                control: (command, arguments) =>
                {
                    if (!supported) return Task.FromResult(false);
                    actual = MediaCommands.ParseSeekPosition(command, arguments, 180000);
                    return Task.FromResult(true);
                });
            await bridge.HandleAsync(JsonNode.Parse("[1,\"spotify\",{\"authid\":\"test\",\"authmethods\":[\"wampcra\"]}]")!.AsArray());
            await bridge.HandleAsync(JsonNode.Parse("[5,\"signature\",{}]")!.AsArray());
            async Task Request(JsonObject arguments) => await bridge.HandleAsync(
                new JsonArray(48, 7, new JsonObject(), uri, new JsonArray(), arguments));
            foreach (var (input, expected) in new[]
            {
                ("45000", 45000L), ("1234.75", 1234L), ("-5000", 0L),
                ("190000", 180000L), ("1e100", 180000L)
            })
            {
                await Request(JsonNode.Parse($"{{\"{key}\":{input}}}")!.AsObject());
                Check(actual == expected && frames[^1][0]!.GetValue<int>() == 50, "seek target and reply");
            }
            foreach (string input in new[] { "null", "true", "\"5000\"", "[]", "{}", "1e999" })
            {
                await Request(JsonNode.Parse($"{{\"{key}\":{input}}}")!.AsObject());
                Check(frames[^1][4]!.GetValue<string>() == "wamp.error.invalid_argument", "invalid input");
            }
            await Request(new JsonObject());
            Check(frames[^1][4]!.GetValue<string>() == "wamp.error.invalid_argument", "missing position");
            supported = false;
            await Request(JsonNode.Parse($"{{\"{key}\":5000}}")!.AsObject());
            Check(frames[^1][4]!.GetValue<string>() == "wamp.error.runtime_error", "unsupported player");
        }
        Console.WriteLine("PASS: both seek routes, millisecond targets, bounds, malformed input, unsupported player.");
    }

    private static void Check(bool success, string name)
    {
        if (!success) throw new Exception("Seek: " + name);
    }
}
