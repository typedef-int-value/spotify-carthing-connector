using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

internal sealed class MediaBridge
{
    private readonly Dictionary<int, string> subscriptions = new();
    private readonly Func<JsonArray, Task> send;
    private readonly Func<string, JsonObject?> getImage;
    private int phase;
    private int nextSubscription;
    private int publication;
    private string authId = "";
    private string? lastMedia;
    private static JsonObject Empty() => new();
    private static JsonArray Array(params object?[] values) => JsonSerializer.SerializeToNode(values)!.AsArray();

    internal MediaBridge(Func<JsonArray, Task> send, Func<string, JsonObject?>? getImage = null)
    {
        this.send = send;
        this.getImage = getImage ?? (_ => null);
    }

    internal async Task HandleAsync(JsonArray message)
    {
        int kind = message[0]!.GetValue<int>();
        switch (kind)
        {
            case 1 when phase == 0:
                if (message[1]?.GetValue<string>() != "spotify")
                    throw new InvalidDataException("Unexpected realm.");
                authId = message[2]?["authid"]?.GetValue<string>() ?? "";
                if (message[2]?["authmethods"] is not JsonArray methods ||
                    !methods.Any(m => m?.GetValue<string>() == "wampcra"))
                    throw new InvalidDataException("Expected wampcra.");
                // Match APK beo/ef51: local companion challenge, not Spotify account authentication.
                string challenge = new JsonObject
                {
                    ["nonce"] = "dummy_nounce", ["authid"] = authId,
                    ["timestamp"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                    ["authmethod"] = "wampcra"
                }.ToJsonString();
                await send(Array(4, "wampcra", new JsonObject { ["challenge"] = challenge }));
                phase = 1;
                break;
            case 5 when phase == 1:
                if (string.IsNullOrWhiteSpace(message[1]?.GetValue<string>()))
                    throw new InvalidDataException("Missing AUTHENTICATE response.");
                // APK ze51 case 1 validates presence of signature then welcomes this paired peer.
                await send(Array(2, 1, new JsonObject
                {
                    ["roles"] = new JsonObject { ["broker"] = Empty(), ["dealer"] = Empty() },
                    ["app_version"] = "9.0.10.415", ["authprovider"] = "", ["authid"] = "",
                    ["authrole"] = "", ["authmethod"] = "",
                    ["date_time"] = DateTimeOffset.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ")
                }));
                phase = 2;
                Console.WriteLine("AUTHENTICATE received; WELCOME sent.");
                break;
            case 32 when phase == 2:
                int id = ++nextSubscription;
                string topic = message[3]!.GetValue<string>();
                subscriptions[id] = topic;
                await send(Array(33, message[1], id));
                Console.WriteLine($"Subscribed {id}: {topic}");
                if (topic == "com.spotify.session_state")
                    await send(Array(36, id, ++publication, Empty(), new JsonArray(), new JsonObject
                    {
                        // Stock UI requires a companion session. This describes the local
                        // paired bridge; it does not authenticate any Spotify cloud account.
                        ["is_logged_in"] = true, ["is_offline"] = false,
                        ["is_in_forced_offline_mode"] = false, ["connection_type"] = "unknown"
                    }));
                lastMedia = null;
                break;
            case 34 when phase == 2:
                subscriptions.Remove(message[2]!.GetValue<int>());
                await send(Array(35, message[1]));
                break;
            case 48 when phase == 2:
                string uri = message[3]!.GetValue<string>();
                Console.WriteLine($"CALL: {uri}");
                JsonObject? result = uri switch
                {
                    "com.spotify.get_image" => getImage(message.Count > 5 ? message[5]?["id"]?.GetValue<string>() ?? "" : ""),
                    "com.spotify.superbird.permissions" => new JsonObject { ["can_use_superbird"] = true },
                    // Diagnostic requests are persisted in the local RX log before acknowledgment.
                    "com.spotify.superbird.pitstop.log" or "com.spotify.superbird.crashes.report" or
                    "com.spotify.superbird.instrumentation.log" or "com.spotify.superbird.instrumentation.request" or
                    "com.spotify.superbird.instrumentation.impression" or "com.spotify.superbird.instrumentation.interaction"
                        => Empty(),
                    // This companion exposes current media only, without a browsable library.
                    "com.spotify.get_children_of_item" => new JsonObject
                    {
                        ["limit"] = message.Count > 5 ? message[5]?["limit"]?.DeepClone() ?? JsonValue.Create(0) : JsonValue.Create(0),
                        ["offset"] = message.Count > 5 ? message[5]?["offset"]?.DeepClone() ?? JsonValue.Create(0) : JsonValue.Create(0),
                        ["total"] = 0, ["items"] = new JsonArray()
                    },
                    _ => null
                };
                // APK result payload is at index 3, not wrapped in a positional-arguments array.
                if (result != null) await send(Array(50, message[1], Empty(), result));
                else await send(Array(8, 48, message[1], Empty(), "wamp.error.no_such_procedure"));
                break;
            case 6:
                await send(Array(6, Empty(), "wamp.close.goodbye_and_out"));
                throw new EndOfStreamException("Peer ended the WAMP session.");
            default:
                throw new InvalidDataException($"Unexpected WAMP message {kind} in phase {phase}.");
        }
    }

    internal async Task PublishAsync(JsonObject state)
    {
        if (phase != 2) return;
        string serialized = state.ToJsonString();
        if (serialized == lastMedia) return;
        foreach (var pair in subscriptions.Where(p => p.Value == "com.spotify.superbird.player_state"))
            await send(Array(36, pair.Key, ++publication, Empty(), new JsonArray(), state));
        lastMedia = serialized;
    }

    internal static byte[] Encode(JsonArray message)
    {
        byte[] payload = MessagePackSerializer.ConvertFromJson(message.ToJsonString());
        if (payload.Length > 120000) throw new InvalidDataException("Outbound frame exceeds APK limit.");
        byte[] frame = new byte[4 + payload.Length];
        BinaryPrimitives.WriteInt32BigEndian(frame, payload.Length);
        payload.CopyTo(frame, 4);
        return frame;
    }

    private static async Task<JsonArray> ReceiveAsync(Stream input, Stream raw, StreamWriter log,
        CancellationToken cancellation)
    {
        byte[] header = new byte[4];
        await input.ReadExactlyAsync(header, cancellation);
        int length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length is < 1 or > 120000) throw new InvalidDataException($"Invalid frame length {length}.");
        byte[] payload = new byte[length];
        await input.ReadExactlyAsync(payload, cancellation);
        await raw.WriteAsync(header, cancellation);
        await raw.WriteAsync(payload, cancellation);
        await raw.FlushAsync(cancellation);
        string json = MessagePackSerializer.ConvertToJson(payload,
            MessagePackSerializerOptions.Standard.WithSecurity(MessagePackSecurity.UntrustedData));
        WriteLog(log, "RX", json);
        return JsonNode.Parse(json)!.AsArray();
    }

    public static async Task RunAsync(StreamSocket socket, string stem, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancellation = lifetime.Token;
        using var input = socket.InputStream.AsStreamForRead();
        using var output = socket.OutputStream.AsStreamForWrite();
        await using var raw = File.Create(stem + ".rx.bin");
        await using var transmitted = File.Create(stem + ".tx.bin");
        await using var log = new StreamWriter(stem + ".jsonl") { AutoFlush = true };
        var media = await WindowsMedia.CreateAsync();
        var bridge = new MediaBridge(async message =>
        {
            byte[] frame = Encode(message);
            await output.WriteAsync(frame, cancellation);
            await output.FlushAsync(cancellation);
            await transmitted.WriteAsync(frame, cancellation);
            await transmitted.FlushAsync(cancellation);
            WriteLog(log, "TX", message.ToJsonString());
        }, media.GetImage);
        // One writer and one protocol state owner; no concurrent writes or subscription mutations.
        Task<JsonArray> receive = ReceiveAsync(input, raw, log, cancellation);
        try
        {
            while (!cancellation.IsCancellationRequested)
            {
                var completed = await Task.WhenAny(receive, Task.Delay(1000, cancellation));
                cancellation.ThrowIfCancellationRequested();
                if (completed == receive)
                {
                    await bridge.HandleAsync(await receive);
                    receive = ReceiveAsync(input, raw, log, cancellation);
                }
                if (bridge.subscriptions.Values.Contains("com.spotify.superbird.player_state"))
                    await bridge.PublishAsync(await media.ReadAsync());
            }
        }
        finally
        {
            lifetime.Cancel();
            socket.Dispose();
            try { await receive; } catch (Exception) { /* Observe the stopped reader. */ }
        }
    }

    private static void WriteLog(StreamWriter log, string direction, string json)
    {
        lock (log)
        {
            log.WriteLine($"{DateTimeOffset.UtcNow:O} {direction} {json}");
            log.Flush();
        }
    }

    public static async Task SelfTestAsync()
    {
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
        Console.WriteLine("PASS: handshake, subscriptions, session state, RPC errors, unsubscribe, fragmented reads, invalid state, oversized/truncated frames.");
    }

    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, 2)], cancellationToken);
    }
}
