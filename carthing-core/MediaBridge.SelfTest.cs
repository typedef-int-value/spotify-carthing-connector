using System.Buffers.Binary;
using System.Text.Json.Nodes;
using MessagePack;

namespace CarThing.Core;

public sealed partial class MediaBridge
{
    private static async Task SpotifyUiSelfTestAsync()
    {
        foreach (bool enabled in new[] { false, true })
        {
            var sent = new List<JsonArray>();
            var peer = new MediaBridge(m => { sent.Add(m); return Task.CompletedTask; },
                getImage: id => id == "cover-test" ? new JsonObject { ["image_data"] = "YWJj" } : null,
                spotifyUiExperiment: enabled);
            await peer.HandleAsync(Array(1, "spotify", new { authid = "test", authmethods = new[] { "wampcra" } }));
            await peer.HandleAsync(Array(5, "signature", Empty()));
            await peer.HandleAsync(Array(32, 1, Empty(), "com.spotify.superbird.player_state"));
            var state = new JsonObject { ["currently_active_application"] = new JsonObject { ["id"] = "windows.test" } };
            await peer.PublishAsync(state);
            if ((enabled ? sent[^1][5]?["currently_active_application"] != null :
                    sent[^1][5]?["currently_active_application"]?["id"]?.GetValue<string>() != "windows.test") ||
                state["currently_active_application"]?["id"]?.GetValue<string>() != "windows.test")
                throw new Exception("Experiment state leaked into normal mode or mutated source.");
            state["track"] = new JsonObject { ["image_id"] = "cover-test" };
            await peer.PublishAsync(state);
            string expectedImage = "cover-test";
            if (sent[^1][5]?["track"]?["image_id"]?.GetValue<string>() != expectedImage ||
                state["track"]?["image_id"]?.GetValue<string>() != "cover-test")
                throw new Exception("Cover URL conversion or normal-mode preservation failed.");
            var coverFrame = Encode(sent[^1]);
            var coverRoundtrip = JsonNode.Parse(MessagePackSerializer.ConvertToJson(coverFrame.AsMemory(4)));
            if (coverRoundtrip?[5]?["track"]?["image_id"]?.GetValue<string>() != expectedImage)
                throw new Exception("Cover URL MessagePack roundtrip failed.");
            if (enabled)
            {
                string? firstUid = sent[^1][5]?["track"]?["uid"]?.GetValue<string>();
                state["track"]!["name"] = "Next title";
                await peer.PublishAsync(state);
                if (sent[^1][5]?["track"]?["uid"]?.GetValue<string>() == firstUid || state["track"]?["uid"] != null)
                    throw new Exception("Native track identity did not change or mutated source.");
            }
            state["track"]!["image_id"] = "missing";
            await peer.PublishAsync(state);
            if (sent[^1][5]?["track"]?["image_id"]?.GetValue<string>() != "missing")
                throw new Exception("Missing cover did not clear image URL.");
            await peer.HandleAsync(Array(32, 20, Empty(), "com.spotify.play_queue"));
            int queueSubscription = sent[^1][2]!.GetValue<int>();
            state["track"]!["image_id"] = "cover-test";
            int queueStart = sent.Count;
            await peer.PublishAsync(state);
            var queueEvents = sent.Skip(queueStart).Where(m => m[0]?.GetValue<int>() == 36 && m[1]?.GetValue<int>() == queueSubscription).ToList();
            if (queueEvents.Count != (enabled ? 1 : 0)) throw new Exception("Queue mode isolation failed.");
            if (enabled)
            {
                string? requestedImage = queueEvents[0][5]?["current"]?["image_uri"]?.GetValue<string>();
                if (requestedImage != "cover-test") throw new Exception("Native artwork queue reference missing.");
                foreach (string imageRpc in new[] { "get_image", "get_thumbnail_image" })
                {
                    await peer.HandleAsync(Array(48, 21, Empty(), "com.spotify." + imageRpc, new JsonArray(), new { id = requestedImage }));
                    if (sent[^1][3]?["image_data"]?.GetValue<string>() != "YWJj")
                        throw new Exception("Native queue image lookup failed.");
                }
                int noChangeStart = sent.Count;
                await peer.PublishAsync(state);
                if (sent.Count != noChangeStart) throw new Exception("Repeated unchanged queue.");
                state["track"]!["image_id"] = "updated-cover";
                await peer.PublishAsync(state);
                if (!sent.Skip(noChangeStart).Any(m => m[0]?.GetValue<int>() == 36 && m[1]?.GetValue<int>() == queueSubscription &&
                    m[5]?["current"]?["image_uri"]?.GetValue<string>() == "updated-cover"))
                    throw new Exception("Queue cover refresh missing.");
            }
            await peer.HandleAsync(Array(48, 2, Empty(), "com.spotify.superbird.graphql", new JsonArray(), new
            {
                payload = " query{shelf(limit:14 overrides:[]){...on Shelf{items{title id total children{uri title subtitle image_id:imageId}}}...on ShelfError{message}}}"
            }));
            if (sent[^1][0]?.GetValue<int>() != (enabled ? 50 : 8) ||
                (enabled && sent[^1][3]?["data"]?["shelf"]?["items"] is not JsonArray { Count: 0 }))
                throw new Exception("Home query response regression.");
            await peer.HandleAsync(Array(48, 3, Empty(), "com.spotify.superbird.graphql", new JsonArray(), new { payload = "mutation{unknown}" }));
            if (sent[^1][0]?.GetValue<int>() != 8) throw new Exception("Unknown GraphQL request accepted.");
            await peer.HandleAsync(Array(48, 5, Empty(), "com.spotify.superbird.graphql", new JsonArray(), new
            {
                payload = " query{presets(serial:\"8559RQ82Q61S\"){... on Presets{presets{context_uri:contextUri name slot_index:slotIndex description image_url:imageUrl}}... on PresetsError{message}}}"
            }));
            if (sent[^1][0]?.GetValue<int>() != (enabled ? 50 : 8) ||
                (enabled && sent[^1][3]?["data"]?["presets"]?["presets"] is not JsonArray { Count: 4 }))
                throw new Exception("Presets query response regression.");
            await peer.HandleAsync(Array(48, 4, Empty(), "com.spotify.superbird.set_active_app", new JsonArray(), new { app = "spotify" }));
            if (sent[^1][0]?.GetValue<int>() != (enabled ? 50 : 8)) throw new Exception("Active app mode regression.");
            await peer.HandleAsync(Array(48, 6, Empty(), "com.spotify.superbird.presets.get_presets", new JsonArray(), Empty()));
            if (sent[^1][0]?.GetValue<int>() != (enabled ? 50 : 8) ||
                (enabled && sent[^1][3]?["result"] is not JsonArray { Count: 4 }))
                throw new Exception("Preset RPC mode isolation regression.");
            await peer.HandleAsync(Array(48, 7, Empty(), "com.spotify.superbird.presets.set_preset", new JsonArray(),
                new { presets = new[] { new { slot_index = 4, context_uri = "spotify:context:test-save" } } }));
            if (sent[^1][0]?.GetValue<int>() != (enabled ? 50 : 8) ||
                (enabled && sent[^1][3]?["result"]?[3]?["context_uri"]?.GetValue<string>() != "spotify:context:test-save"))
                throw new Exception("Preset save response regression.");
            if (enabled)
            {
                await peer.HandleAsync(Array(48, 8, Empty(), "com.spotify.superbird.presets.set_preset", new JsonArray(),
                    new { presets = new[] { new { slot_index = 1, context_uri = "changed" }, new { slot_index = 5, context_uri = "invalid" } } }));
                if (sent[^1][0]?.GetValue<int>() != 8) throw new Exception("Invalid preset slot accepted.");
                await peer.HandleAsync(Array(48, 9, Empty(), "com.spotify.superbird.presets.get_presets", new JsonArray(), Empty()));
                if (sent[^1][3]?["result"]?[0]?["context_uri"]?.GetValue<string>() != "spotify:playlist:carthing-test-1")
                    throw new Exception("Invalid preset batch changed existing data.");
            }
        }
        Console.WriteLine("PASS: Spotify UI opt-in isolation, source preservation, home response, unknown query rejection, active app.");
    }

    public static async Task SelfTestAsync()
    {
        await SpotifyUiSelfTestAsync();
        var frames = new List<JsonArray>();
        var bridge = new MediaBridge(m => { frames.Add(m); return Task.CompletedTask; });
        await bridge.HandleAsync(Array(1, "spotify", new { authid = "test", authmethods = new[] { "wampcra" } }));
        await bridge.HandleAsync(Array(5, "test-signature", Empty()));
        await bridge.HandleAsync(Array(32, 7, Empty(), "com.spotify.superbird.player_state"));
        await bridge.PublishAsync(new JsonObject { ["is_paused"] = true });
        if (frames.Select(f => f[0]!.GetValue<int>()).SequenceEqual(new[] { 4, 2, 33, 36 }) == false ||
            frames[3][1]!.GetValue<int>() != frames[2][2]!.GetValue<int>() ||
            frames[3][5]?["is_paused"]?.GetValue<bool>() != true)
            throw new Exception("Handshake/subscription/event regression.");
        var encoded = Encode(frames[3]);
        if (BinaryPrimitives.ReadInt32BigEndian(encoded) != encoded.Length - 4)
            throw new Exception("Frame length regression.");
        using var fragmented = new FragmentedStream(encoded);
        using var raw = new MemoryStream();
        using var log = new StreamWriter(Stream.Null);
        var decoded = await ReceiveAsync(fragmented, raw, log, CancellationToken.None);
        if (!JsonNode.DeepEquals(decoded, frames[3])) throw new Exception("MessagePack roundtrip failed.");
        await bridge.HandleAsync(Array(32, 8, Empty(), "com.spotify.session_state"));
        if (frames[^1][5]?["is_logged_in"]?.GetValue<bool>() != true)
            throw new Exception("Local companion session state missing.");
        await bridge.HandleAsync(Array(48, 9, Empty(), "unknown", new JsonArray(), Empty()));
        if (frames[^1][0]!.GetValue<int>() != 8 || frames[^1][2]!.GetValue<int>() != 9)
            throw new Exception("Unknown RPC response did not preserve request ID.");
        await bridge.HandleAsync(Array(34, 10, 1));
        int before = frames.Count;
        await bridge.PublishAsync(new JsonObject { ["is_paused"] = false });
        if (frames.Count != before) throw new Exception("Published after unsubscribe.");
        bool rejected = false;
        try { await new MediaBridge(_ => Task.CompletedTask).HandleAsync(Array(5, "signature", Empty())); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Accepted authentication before HELLO.");
        rejected = false;
        using var invalid = new MemoryStream(new byte[] { 0, 2, 0, 0 });
        try { await ReceiveAsync(invalid, raw, log, CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new Exception("Accepted oversized frame.");
        rejected = false;
        using var truncated = new MemoryStream(encoded[..^1]);
        try { await ReceiveAsync(truncated, raw, log, CancellationToken.None); }
        catch (EndOfStreamException) { rejected = true; }
        if (!rejected) throw new Exception("Accepted truncated frame.");
        var cache = new MediaImageCache();
        var imageBridge = new MediaBridge(m => { frames.Add(m); return Task.CompletedTask; }, cache.Get);
        await imageBridge.HandleAsync(Array(1, "spotify", new { authid = "test", authmethods = new[] { "wampcra" } }));
        await imageBridge.HandleAsync(Array(5, "test-signature", Empty()));
        for (int track = 0; track < 24; track++)
        {
            // Repeat reads of the same thumbnail as ReadAsync does every five seconds.
            for (int read = 0; read < 3; read++)
            {
                string imageId = $"image-{track}";
                cache.Store(imageId, new JsonObject { ["image_data"] = $"payload-{track}", ["width"] = 248, ["height"] = 248 });
                await imageBridge.HandleAsync(Array(48, 100 + track, Empty(), "com.spotify.get_image",
                    new JsonArray(), new JsonObject { ["id"] = imageId }));
                if (frames[^1][0]!.GetValue<int>() != 50 ||
                    frames[^1][3]?["image_data"]?.GetValue<string>() != $"payload-{track}")
                    throw new Exception($"Image cache/RPC regression at track {track + 1}, read {read + 1}.");
                for (int previous = Math.Max(0, track - 7); previous <= track; previous++)
                    if (cache.Get($"image-{previous}") == null)
                        throw new Exception($"Recent image {previous} evicted at track {track}.");
                if (track >= 8 && cache.Get($"image-{track - 8}") != null)
                    throw new Exception("Image cache retained more than eight images.");
            }
        }
        Console.WriteLine("PASS: image cache/RPC across 24 tracks with three thumbnail reads each.");
        await ControlSelfTestAsync();
        Console.WriteLine("PASS: handshake, subscriptions, session state, RPC errors, unsubscribe, fragmented reads, invalid state, oversized/truncated frames.");
    }

    private static async Task ControlSelfTestAsync()
    {
        var frames = new List<JsonArray>();
        int calls = 0;
        bool accepted = true;
        bool fail = false;
        var bridge = new MediaBridge(m => { frames.Add(m); return Task.CompletedTask; }, control: (uri, args) =>
        {
            calls++;
            if (uri == "com.spotify.set_playback_speed") MediaCommands.PlaybackSpeedCommand(args);
            if (uri is "com.spotify.set_shuffle" or "com.spotify.superbird.set_shuffle" or "com.spotify.toggle_shuffle")
                MediaCommands.ShuffleTarget(uri, args, false);
            if (uri.EndsWith(".set_volume")) MediaCommands.ParseVolume(args);
            if (fail) throw new IOException("Simulated endpoint loss.");
            return Task.FromResult(accepted);
        });
        await bridge.HandleAsync(Array(1, "spotify", new { authid = "test", authmethods = new[] { "wampcra" } }));
        await bridge.HandleAsync(Array(5, "test", Empty()));
        string[] commands = ["resume", "pause", "skip_next", "skip_prev",
            "volume.volume_up", "volume.volume_down", "volume.set_volume"];
        foreach (string command in commands)
        {
            await bridge.HandleAsync(Array(48, 201, Empty(), "com.spotify.superbird." + command,
                new JsonArray(), JsonNode.Parse("{\"volume\":0.4}")!));
            if (frames[^1][0]!.GetValue<int>() != 50 || frames[^1][1]!.GetValue<int>() != 201)
                throw new Exception($"Control result regression: {command}");
        }
        if (calls != commands.Length) throw new Exception("Control dispatched more than once.");
        foreach (var (native, target) in new[] { ("com.spotify.skip_next", "com.spotify.superbird.skip_next"),
                     ("com.spotify.skip_previous", "com.spotify.superbird.skip_prev") })
        {
            if (MediaCommands.NormalizePlaybackCommand(native) != target)
                throw new Exception("Native control mapping failed.");
            int previousCalls = calls;
            await bridge.HandleAsync(Array(48, 207, Empty(), native, new JsonArray(), Empty()));
            if (calls != previousCalls + 1 || frames[^1][0]?.GetValue<int>() != 50)
                throw new Exception("Native control dispatch failed.");
        }
        foreach (int speed in new[] { 0, 1 })
        {
            var arguments = new JsonObject { ["playback_speed"] = speed };
            if (MediaCommands.PlaybackSpeedCommand(arguments) != (speed == 0 ? "com.spotify.superbird.pause" : "com.spotify.superbird.resume"))
                throw new Exception("Playback speed mapping failed.");
            await bridge.HandleAsync(Array(48, 208, Empty(), "com.spotify.set_playback_speed", new JsonArray(), arguments));
            if (frames[^1][0]?.GetValue<int>() != 50) throw new Exception("Playback speed dispatch failed.");
        }
        foreach (string badSpeed in new[] { "{}", "{\"playback_speed\":2}", "{\"playback_speed\":\"0\"}" })
        {
            await bridge.HandleAsync(Array(48, 209, Empty(), "com.spotify.set_playback_speed", new JsonArray(), JsonNode.Parse(badSpeed)));
            if (frames[^1][0]?.GetValue<int>() != 8 || frames[^1][4]?.GetValue<string>() != "wamp.error.invalid_argument")
                throw new Exception("Invalid playback speed accepted.");
        }
        foreach (string shuffleRpc in new[] { "com.spotify.set_shuffle", "com.spotify.superbird.set_shuffle", "com.spotify.toggle_shuffle" })
        {
            foreach (bool current in new[] { false, true })
            {
                var args = new JsonObject { ["shuffle"] = current };
                bool expected = shuffleRpc == "com.spotify.toggle_shuffle" ? !current : current;
                if (MediaCommands.ShuffleTarget(shuffleRpc, args, current) != expected)
                    throw new Exception("Shuffle target mapping failed.");
                int countBefore = calls;
                await bridge.HandleAsync(Array(48, 210, Empty(), shuffleRpc, new JsonArray(), args));
                if (calls != countBefore + 1 || frames[^1][0]?.GetValue<int>() != 50)
                    throw new Exception("Shuffle control dispatch failed.");
            }
        }
        foreach (string invalidShuffle in new[] { "{}", "{\"shuffle\":1}", "{\"shuffle\":\"true\"}" })
        {
            await bridge.HandleAsync(Array(48, 211, Empty(), "com.spotify.set_shuffle", new JsonArray(), JsonNode.Parse(invalidShuffle)));
            if (frames[^1][0]?.GetValue<int>() != 8 || frames[^1][4]?.GetValue<string>() != "wamp.error.invalid_argument")
                throw new Exception("Invalid shuffle argument accepted.");
        }
        bool unknownShuffleRejected = false;
        try { MediaCommands.ShuffleTarget("com.spotify.toggle_shuffle", Empty(), null); }
        catch (InvalidOperationException) { unknownShuffleRejected = true; }
        if (!unknownShuffleRejected) throw new Exception("Unknown shuffle state toggled.");
        accepted = false;
        await bridge.HandleAsync(Array(48, 212, Empty(), "com.spotify.set_shuffle", new JsonArray(), new { shuffle = true }));
        if (frames[^1][4]?.GetValue<string>() != "wamp.error.runtime_error")
            throw new Exception("Rejected shuffle control acknowledged.");
        await bridge.HandleAsync(Array(48, 202, Empty(), "com.spotify.superbird.pause", new JsonArray(), Empty()));
        if (frames[^1][0]!.GetValue<int>() != 8 || frames[^1][2]!.GetValue<int>() != 202)
            throw new Exception("Rejected control incorrectly acknowledged.");
        accepted = true;
        fail = true;
        await bridge.HandleAsync(Array(48, 203, Empty(), "com.spotify.superbird.resume", new JsonArray(), Empty()));
        if (frames[^1][4]!.GetValue<string>() != "wamp.error.runtime_error")
            throw new Exception("Control exception did not become RPC error.");
        fail = false;
        foreach (string argument in new[] { "{}", "{\"volume\":-0.1}", "{\"volume\":1.1}", "{\"volume\":\"bad\"}" })
        {
            await bridge.HandleAsync(Array(48, 204, Empty(), "com.spotify.superbird.volume.set_volume",
                new JsonArray(), JsonNode.Parse(argument)!));
            if (frames[^1][4]!.GetValue<string>() != "wamp.error.invalid_argument")
                throw new Exception("Invalid volume accepted.");
        }
        await bridge.HandleAsync(Array(32, 205, Empty(), "com.spotify.superbird.volume.volume_state"));
        var volume = new JsonObject { ["volume"] = 0.4, ["volume_steps"] = 51 };
        await bridge.PublishVolumeAsync(volume);
        if (frames[^1][0]!.GetValue<int>() != 36 || !JsonNode.DeepEquals(frames[^1][5], volume))
            throw new Exception("Volume state event missing.");
        int before = frames.Count;
        await bridge.PublishVolumeAsync(volume);
        if (frames.Count != before) throw new Exception("Duplicate volume event.");
        await bridge.HandleAsync(Array(34, 206, 1));
        before = frames.Count;
        await bridge.PublishVolumeAsync(new JsonObject { ["volume"] = 0.5 });
        if (frames.Count != before) throw new Exception("Volume published after unsubscribe.");
        Console.WriteLine("PASS: seven control routes, rejection, exception, volume validation, volume subscription.");
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], cancellationToken);
    }
}
