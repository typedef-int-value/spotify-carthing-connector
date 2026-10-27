using System.Buffers.Binary;
using System.Text.Json.Nodes;
using CarThing.Core;
using MessagePack;

AutoConnectionPolicyTests.Run();
await MediaBridge.SelfTestAsync();
foreach (bool spotify in new[] { false, true })
{
    using var input = new MemoryStream();
    foreach (string json in new[]
    {
        "[1,\"spotify\",{\"authid\":\"test\",\"authmethods\":[\"wampcra\"]}]",
        "[5,\"signature\",{}]",
        "[32,1,{},\"com.spotify.superbird.player_state\"]",
        "[32,2,{},\"com.spotify.superbird.volume.volume_state\"]",
        "[48,3,{},\"com.spotify.superbird.pause\",[],{}]",
        "[48,4,{},\"com.spotify.get_image\",[],{\"id\":\"cover\"}]"
    })
        input.Write(MediaBridge.Encode(JsonNode.Parse(json)!.AsArray()));
    input.Position = 0;
    using var output = new MemoryStream();
    var media = new FakeMedia();
    string stem = Path.Combine(Path.GetTempPath(), $"carthing-core-{Guid.NewGuid():N}");
    bool closed = false;
    bool ended = false;
    try
    {
        try
        {
            await MediaBridge.RunAsync(input, output, media, stem, spotify,
                () => closed = true, CancellationToken.None);
        }
        catch (EndOfStreamException) { ended = true; }
        var frames = new List<JsonArray>();
        output.Position = 0;
        while (output.Position < output.Length)
        {
            byte[] header = new byte[4];
            output.ReadExactly(header);
            byte[] payload = new byte[BinaryPrimitives.ReadInt32BigEndian(header)];
            output.ReadExactly(payload);
            frames.Add(JsonNode.Parse(MessagePackSerializer.ConvertToJson(payload))!.AsArray());
        }
        var player = frames.First(f => f[0]!.GetValue<int>() == 36 && f[5]?["track"] != null)[5]!;
        if (!ended || !closed || media.Controls != 1 || media.Reads == 0 || media.VolumeReads == 0 ||
            (player["currently_active_application"] == null) != spotify ||
            !frames.Any(f => f[0]!.GetValue<int>() == 50 && f[1]!.GetValue<int>() == 4 &&
                f[3]?["image_data"]?.GetValue<string>() == "YWJj") ||
            !File.ReadAllBytes(stem + ".tx.bin").SequenceEqual(output.ToArray()))
            throw new Exception("Platform adapter/session integration failed.");
        Console.WriteLine($"PASS: stream session, fake media adapter, cover, capture, EOF cleanup; Spotify={spotify}.");
    }
    finally
    {
        foreach (string suffix in new[] { ".rx.bin", ".tx.bin", ".jsonl" }) File.Delete(stem + suffix);
    }
}

sealed class FakeMedia : IMediaSource
{
    public int Reads, VolumeReads, Controls;
    public Task<JsonObject> ReadAsync()
    {
        Reads++;
        return Task.FromResult(new JsonObject
        {
            ["currently_active_application"] = new JsonObject { ["id"] = "test", ["name"] = "Test" },
            ["track"] = new JsonObject { ["name"] = "Track", ["image_id"] = "cover" }
        });
    }
    public Task<JsonObject?> ReadVolumeAsync()
    {
        VolumeReads++;
        return Task.FromResult<JsonObject?>(new JsonObject { ["volume"] = 0.5, ["volume_steps"] = 51 });
    }
    public JsonObject? GetImage(string id) => id == "cover" ? new JsonObject { ["image_data"] = "YWJj" } : null;
    public Task<bool> ControlAsync(string uri, JsonObject arguments)
    {
        if (uri != "com.spotify.superbird.pause") throw new Exception("Unexpected control URI.");
        Controls++;
        return Task.FromResult(true);
    }
}
