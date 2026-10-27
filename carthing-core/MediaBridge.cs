using System.Buffers.Binary;
using System.Text.Json;
using System.Text.Json.Nodes;
using MessagePack;
namespace CarThing.Core;

public sealed partial class MediaBridge
{
    private readonly Dictionary<int, string> subscriptions = new();
    private readonly Func<JsonArray, Task> send;
    private readonly Func<string, JsonObject?> getImage;
    private readonly Func<string, JsonObject, Task<bool>> control;
    private int phase;
    private int nextSubscription;
    private int publication;
    private string authId = "";
    private string? lastMedia;
    private string? lastVolume;
    private string? lastQueue;
    private readonly bool spotifyUiExperiment;
    private JsonArray experimentPresets = new();
    private static JsonObject Empty() => new();
    private static JsonArray Array(params object?[] values) => JsonSerializer.SerializeToNode(values)!.AsArray();

    public MediaBridge(Func<JsonArray, Task> send, Func<string, JsonObject?>? getImage = null,
        Func<string, JsonObject, Task<bool>>? control = null, bool spotifyUiExperiment = false)
    {
        this.send = send;
        this.getImage = getImage ?? (_ => null);
        this.control = control ?? ((_, _) => Task.FromResult(false));
        this.spotifyUiExperiment = spotifyUiExperiment;
        if (spotifyUiExperiment)
            for (int slot = 1; slot <= 4; slot++)
                experimentPresets.Add(new JsonObject
                {
                    ["slot_index"] = slot, ["context_uri"] = $"spotify:playlist:carthing-test-{slot}",
                    ["name"] = $"PC Preset {slot}", ["description"] = "Local protocol test",
                    ["image_url"] = ""
                });
    }

    public async Task HandleAsync(JsonArray message)
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
                lastVolume = null;
                lastQueue = null;
                break;
            case 34 when phase == 2:
                subscriptions.Remove(message[2]!.GetValue<int>());
                await send(Array(35, message[1]));
                break;
            case 48 when phase == 2:
                string uri = message[3]!.GetValue<string>();
                Console.WriteLine($"CALL: {uri}");
                if (spotifyUiExperiment && uri is "com.spotify.superbird.presets.get_presets" or "com.spotify.superbird.presets.set_preset")
                {
                    try
                    {
                        if (uri.EndsWith("set_preset", StringComparison.Ordinal))
                            UpdateExperimentPresets(message.Count > 5 ? message[5] as JsonObject : null);
                        await send(Array(50, message[1], Empty(), new JsonObject { ["result"] = experimentPresets.DeepClone() }));
                    }
                    catch (ArgumentException ex)
                    {
                        Console.WriteLine($"EXPERIMENT preset: {ex.Message}");
                        await send(Array(8, 48, message[1], Empty(), "wamp.error.invalid_argument"));
                    }
                    break;
                }
                if (spotifyUiExperiment && uri == "com.spotify.superbird.graphql")
                {
                    string payload = message.Count > 5 ? message[5]?["payload"]?.GetValue<string>() ?? "" : "";
                    Console.WriteLine($"EXPERIMENT GraphQL: {payload}");
                    // Support only observed home/preset queries. Preserve unknown queries as errors.
                    string compact = string.Concat(payload.Where(c => !char.IsWhiteSpace(c)));
                    if (compact == "query{shelf(limit:14overrides:[]){...onShelf{items{titleidtotalchildren{urititlesubtitleimage_id:imageId}}}...onShelfError{message}}}")
                        await send(Array(50, message[1], Empty(), new JsonObject
                        {
                            ["data"] = new JsonObject { ["shelf"] = new JsonObject { ["items"] = new JsonArray() } }
                        }));
                    else if (System.Text.RegularExpressions.Regex.IsMatch(compact,
                        "^query\\{presets\\(serial:\"[A-Za-z0-9]+\"\\)\\{\\.\\.\\.onPresets\\{presets\\{context_uri:contextUrinameslot_index:slotIndexdescriptionimage_url:imageUrl\\}\\}\\.\\.\\.onPresetsError\\{message\\}\\}\\}$"))
                        await send(Array(50, message[1], Empty(), new JsonObject
                        {
                            ["data"] = new JsonObject { ["presets"] = new JsonObject { ["presets"] = experimentPresets.DeepClone() } }
                        }));
                    else await send(Array(8, 48, message[1], Empty(), "wamp.error.no_such_procedure"));
                    break;
                }
                if (spotifyUiExperiment && uri == "com.spotify.superbird.set_active_app")
                {
                    string? app = message.Count > 5 ? message[5]?["app"]?.GetValue<string>() : null;
                    if (app == "spotify")
                    {
                        lastMedia = null;
                        await send(Array(50, message[1], Empty(), Empty()));
                    }
                    else await send(Array(8, 48, message[1], Empty(), "wamp.error.invalid_argument"));
                    break;
                }
                if (MediaCommands.IsPlaybackCommand(uri) || MediaCommands.IsVolumeCommand(uri))
                {
                    string? error = null;
                    try
                    {
                        var arguments = message.Count > 5 ? message[5] as JsonObject : Empty();
                        if (arguments == null) throw new ArgumentException("Expected keyword argument map.");
                        if (!await control(uri, arguments)) error = "wamp.error.runtime_error";
                    }
                    catch (ArgumentException ex)
                    {
                        error = "wamp.error.invalid_argument";
                        Console.Error.WriteLine($"Control {uri}: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        error = "wamp.error.runtime_error";
                        Console.Error.WriteLine($"Control {uri}: {ex.Message}");
                    }
                    Console.WriteLine($"Control {uri}: {error ?? "accepted"}");
                    if (error == null) await send(Array(50, message[1], Empty(), Empty()));
                    else await send(Array(8, 48, message[1], Empty(), error));
                    lastMedia = null;
                    lastVolume = null;
                    break;
                }
                JsonObject? result = uri switch
                {
                    "com.spotify.get_image" or "com.spotify.get_thumbnail_image" => getImage(message.Count > 5 ? message[5]?["id"]?.GetValue<string>() ?? "" : ""),
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

    private void UpdateExperimentPresets(JsonObject? arguments)
    {
        if (arguments?["presets"] is not JsonArray { Count: > 0 } updates)
            throw new ArgumentException("Expected nonempty presets array.");
        var next = experimentPresets.DeepClone().AsArray();
        var slots = new HashSet<int>();
        foreach (var update in updates)
        {
            if (update is not JsonObject item || item["slot_index"] is not JsonValue slotValue ||
                !slotValue.TryGetValue<int>(out int slot) || slot is < 1 or > 4 || !slots.Add(slot) ||
                item["context_uri"] is not JsonValue uriValue || !uriValue.TryGetValue<string>(out string? context) ||
                string.IsNullOrWhiteSpace(context))
                throw new ArgumentException("Expected unique slot_index 1..4 and nonempty context_uri.");
            next[slot - 1]!["context_uri"] = context;
        }
        // Session-local test data only; commit after the entire request validates.
        experimentPresets = next;
    }

    public async Task PublishAsync(JsonObject state)
    {
        if (phase != 2) return;
        if (spotifyUiExperiment)
        {
            state = state.DeepClone().AsObject();
            // APK cpn.F (Spotify) uses null; cpn.E (external media) supplies ActiveApp.
            state["currently_active_application"] = null;
            if (state["track"] is JsonObject track)
            {
                string identity = $"{track["name"]}\n{track["artist"]?["name"]}\n{track["album"]?["name"]}";
                string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
                track["uid"] = key;
                track["uri"] = "spotify:track:" + key[..22]; // Synthetic local identity, not a Spotify catalog ID.
            }
            JsonObject? current = state["track"] is JsonObject item ? new JsonObject
            {
                ["uid"] = item["uid"]?.DeepClone(), ["uri"] = item["uri"]?.DeepClone(),
                ["name"] = item["name"]?.DeepClone(), ["image_uri"] = item["image_id"]?.DeepClone(),
                ["artist_name"] = item["artist"]?["name"]?.DeepClone(),
                ["artists"] = item["artists"]?.DeepClone() ?? new JsonArray(), ["provider"] = "context"
            } : null;
            var queue = new JsonObject { ["previous"] = new JsonArray(), ["current"] = current, ["next"] = new JsonArray() };
            string serializedQueue = queue.ToJsonString();
            if (serializedQueue != lastQueue)
            {
                foreach (var pair in subscriptions.Where(p => p.Value == "com.spotify.play_queue"))
                    await send(Array(36, pair.Key, ++publication, Empty(), new JsonArray(), queue));
                lastQueue = serializedQueue;
            }
        }
        string serialized = state.ToJsonString();
        if (serialized == lastMedia) return;
        foreach (var pair in subscriptions.Where(p => p.Value == "com.spotify.superbird.player_state"))
            await send(Array(36, pair.Key, ++publication, Empty(), new JsonArray(), state));
        lastMedia = serialized;
    }

    public async Task PublishVolumeAsync(JsonObject state)
    {
        if (phase != 2) return;
        string serialized = state.ToJsonString();
        if (serialized == lastVolume) return;
        foreach (var pair in subscriptions.Where(p => p.Value == "com.spotify.superbird.volume.volume_state"))
            await send(Array(36, pair.Key, ++publication, Empty(), new JsonArray(), state));
        lastVolume = serialized;
    }

    public static byte[] Encode(JsonArray message)
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

    /// <summary>Runs one connected peer. The caller owns streams and supplies a writable capture stem.</summary>
    /// <param name="closeTransport">Closes the platform socket to unblock pending reads on shutdown.</param>
    public static async Task RunAsync(Stream input, Stream output, IMediaSource media,
        string stem, bool spotifyMode, Action closeTransport, CancellationToken cancellation)
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        cancellation = lifetime.Token;
        await using var raw = File.Create(stem + ".rx.bin");
        await using var transmitted = File.Create(stem + ".tx.bin");
        await using var log = new StreamWriter(stem + ".jsonl") { AutoFlush = true };
        var bridge = new MediaBridge(async message =>
        {
            byte[] frame = Encode(message);
            await output.WriteAsync(frame, cancellation);
            await output.FlushAsync(cancellation);
            await transmitted.WriteAsync(frame, cancellation);
            await transmitted.FlushAsync(cancellation);
            WriteLog(log, "TX", message.ToJsonString());
        }, media.GetImage, media.ControlAsync, spotifyMode);
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
                if (bridge.subscriptions.Values.Contains("com.spotify.superbird.volume.volume_state"))
                {
                    JsonObject? volume = null;
                    try { volume = await media.ReadVolumeAsync(); }
                    catch (Exception ex) { Console.Error.WriteLine($"Volume state: {ex.Message}"); }
                    if (volume != null) await bridge.PublishVolumeAsync(volume);
                }
            }
        }
        finally
        {
            lifetime.Cancel();
            closeTransport();
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

}
