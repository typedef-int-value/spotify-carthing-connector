using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Windows.ApplicationModel;
using Windows.Graphics.Imaging;
using Windows.Media.Control;
using Windows.Storage.Streams;

internal sealed class WindowsMedia
{
    private readonly GlobalSystemMediaTransportControlsSessionManager manager;
    private readonly MediaImageCache images = new();
    private string thumbnailKey = "";
    private string thumbnailId = "";
    private DateTime nextThumbnailRead;
    private string namedApp = "";
    private string appName = "";

    private WindowsMedia(GlobalSystemMediaTransportControlsSessionManager manager) => this.manager = manager;

    public static async Task<WindowsMedia> CreateAsync() =>
        new(await GlobalSystemMediaTransportControlsSessionManager.RequestAsync());

    public JsonObject? GetImage(string id) => images.Get(id);

    internal static string NormalizePlaybackCommand(string uri) => uri switch
    {
        "com.spotify.skip_next" => "com.spotify.superbird.skip_next",
        "com.spotify.skip_previous" => "com.spotify.superbird.skip_prev",
        _ => uri
    };

    internal static string PlaybackSpeedCommand(JsonObject arguments)
    {
        if (arguments["playback_speed"] is not JsonValue value ||
            !value.TryGetValue<int>(out int speed) || speed is not (0 or 1))
            throw new ArgumentException("playback_speed must be 0 (pause) or 1 (play).");
        return speed == 0 ? "com.spotify.superbird.pause" : "com.spotify.superbird.resume";
    }

    internal static bool IsPlaybackCommand(string uri) => uri == "com.spotify.set_playback_speed" || NormalizePlaybackCommand(uri) is
        "com.spotify.set_shuffle" or "com.spotify.superbird.set_shuffle" or "com.spotify.toggle_shuffle" or
        "com.spotify.superbird.resume" or "com.spotify.superbird.pause" or
        "com.spotify.superbird.skip_next" or "com.spotify.superbird.skip_prev";

    internal static bool ShuffleTarget(string uri, JsonObject arguments, bool? current)
    {
        if (uri == "com.spotify.toggle_shuffle")
            return current.HasValue ? !current.Value : throw new InvalidOperationException("Shuffle state unavailable.");
        if (arguments["shuffle"] is not JsonValue value || !value.TryGetValue<bool>(out bool requested))
            throw new ArgumentException("shuffle must be a Boolean.");
        return requested;
    }

    public async Task<bool> ControlAsync(string uri, JsonObject arguments)
    {
        if (WindowsVolume.IsCommand(uri)) return WindowsVolume.Control(uri, arguments);
        if (uri == "com.spotify.set_playback_speed") uri = PlaybackSpeedCommand(arguments);
        var session = manager.GetCurrentSession();
        if (session == null) return false;
        var playback = session.GetPlaybackInfo();
        var controls = playback.Controls;
        if (uri is "com.spotify.set_shuffle" or "com.spotify.superbird.set_shuffle" or "com.spotify.toggle_shuffle")
        {
            bool target = ShuffleTarget(uri, arguments, playback.IsShuffleActive);
            return controls.IsShuffleEnabled && await session.TryChangeShuffleActiveAsync(target);
        }
        return NormalizePlaybackCommand(uri) switch
        {
            "com.spotify.superbird.resume" => controls.IsPlayEnabled && await session.TryPlayAsync(),
            "com.spotify.superbird.pause" => controls.IsPauseEnabled && await session.TryPauseAsync(),
            "com.spotify.superbird.skip_next" => controls.IsNextEnabled && await session.TrySkipNextAsync(),
            "com.spotify.superbird.skip_prev" => controls.IsPreviousEnabled && await session.TrySkipPreviousAsync(),
            _ => false
        };
    }

    public async Task<JsonObject> ReadAsync()
    {
        var session = manager.GetCurrentSession();
        var properties = session == null ? null : await session.TryGetMediaPropertiesAsync();
        var playback = session?.GetPlaybackInfo();
        var timeline = session?.GetTimelineProperties();
        bool playing = playback?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
        string title = properties?.Title ?? "";
        string artist = properties?.Artist ?? "";
        string album = properties?.AlbumTitle ?? "";
        string app = session?.SourceAppUserModelId ?? "Windows";
        if (namedApp != app)
        {
            namedApp = app;
            try { appName = AppInfo.GetFromAppUserModelId(app)?.DisplayInfo.DisplayName ?? app; }
            catch (Exception) { appName = app; }
        }
        string key = $"{app}\n{title}\n{artist}\n{album}";
        if (thumbnailKey != key || DateTime.UtcNow >= nextThumbnailRead)
        {
            thumbnailKey = key;
            thumbnailId = "";
            nextThumbnailRead = DateTime.UtcNow.AddSeconds(5);
            try
            {
                if (properties?.Thumbnail != null)
                {
                    using var source = await properties.Thumbnail.OpenReadAsync();
                    var decoder = await BitmapDecoder.CreateAsync(source);
                    using var destination = new InMemoryRandomAccessStream();
                    // Encode JPEG to bound Bluetooth payload size regardless of the app's original format.
                    using var bitmap = await decoder.GetSoftwareBitmapAsync();
                    var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, destination);
                    encoder.SetSoftwareBitmap(bitmap);
                    double scale = Math.Min(1, 248.0 / Math.Max(decoder.PixelWidth, decoder.PixelHeight));
                    encoder.BitmapTransform.ScaledWidth = Math.Max(1, (uint)(decoder.PixelWidth * scale));
                    encoder.BitmapTransform.ScaledHeight = Math.Max(1, (uint)(decoder.PixelHeight * scale));
                    await encoder.FlushAsync();
                    if (destination.Size <= 60000)
                    {
                        destination.Seek(0);
                        using var reader = new DataReader(destination);
                        await reader.LoadAsync((uint)destination.Size);
                        byte[] bytes = new byte[destination.Size];
                        reader.ReadBytes(bytes);
                        thumbnailId = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                        images.Store(thumbnailId, new JsonObject
                        {
                            ["image_data"] = Convert.ToBase64String(bytes),
                            ["width"] = encoder.BitmapTransform.ScaledWidth,
                            ["height"] = encoder.BitmapTransform.ScaledHeight
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Thumbnail: {ex.Message}");
            }
        }
        long duration = timeline == null ? 0 : Math.Max(0, (long)(timeline.EndTime - timeline.StartTime).TotalMilliseconds);
        long position = timeline == null ? 0 : Math.Max(0, (long)(timeline.Position - timeline.StartTime).TotalMilliseconds);
        return new JsonObject
        {
            ["currently_active_application"] = new JsonObject { ["id"] = app, ["name"] = appName },
            ["context_uri"] = "spotify:context:fake", ["context_title"] = album,
            ["is_paused"] = !playing, ["is_paused_bool"] = !playing,
            ["playback_options"] = new JsonObject { ["repeat"] = 0, ["shuffle"] = playback?.IsShuffleActive ?? false },
            ["playback_position"] = position, ["playback_speed"] = playing ? playback?.PlaybackRate ?? 1 : 0,
            // Advertise only implemented commands supported by the current Windows session.
            ["playback_restrictions"] = new JsonObject
            {
                ["can_repeat_context"] = false, ["can_repeat_track"] = false, ["can_seek"] = false,
                ["can_skip_next"] = playback?.Controls.IsNextEnabled ?? false,
                ["can_skip_prev"] = playback?.Controls.IsPreviousEnabled ?? false,
                ["can_toggle_shuffle"] = playback?.Controls.IsShuffleEnabled == true && playback.IsShuffleActive.HasValue
            },
            ["track"] = new JsonObject
            {
                ["album"] = new JsonObject { ["name"] = album, ["uri"] = "spotify:album:fake" },
                ["artist"] = new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" },
                ["artists"] = new JsonArray(new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" }),
                ["duration_ms"] = duration, ["image_id"] = thumbnailId, ["image_bytes"] = null,
                ["is_episode"] = false, ["is_podcast"] = false, ["name"] = title,
                ["saved"] = false, ["uid"] = "fake", ["uri"] = "spotify:track:fake"
            }
        };
    }
}

internal sealed class MediaImageCache
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
