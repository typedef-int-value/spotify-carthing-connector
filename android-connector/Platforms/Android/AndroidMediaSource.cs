using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using CarThing.Core;
using AudioStream = Android.Media.Stream;

namespace CarThing.Connector;

internal sealed class AndroidMediaSource(Context context) : IMediaSource, IDisposable
{
    private readonly MediaSessionManager manager = (MediaSessionManager)context.GetSystemService(Context.MediaSessionService)!;
    private readonly AudioManager audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
    private readonly MediaImageCache images = new();
    private MediaController? current;
    private string imageId = "";
    private string imageKey = "";
    private DateTime nextImageRead;
    private VolumeController? volume;

    private VolumeController Volume => volume ??= new VolumeController(
        () => audio.GetStreamVolume(AudioStream.Music),
        () => audio.GetStreamMaxVolume(AudioStream.Music),
        value => audio.SetStreamVolume(AudioStream.Music, value, (VolumeNotificationFlags)0),
        Android.OS.SystemClock.ElapsedRealtime);

    private MediaController? SelectSession()
    {
        var sessions = manager.GetActiveSessions(MediaAccessService.Component(context));
        var selected = sessions.FirstOrDefault(s => s.PlaybackState?.State == PlaybackStateCode.Playing)
            ?? sessions.FirstOrDefault(s => current != null && s.SessionToken?.Equals(current.SessionToken) == true)
            ?? sessions.FirstOrDefault();
        if (!ReferenceEquals(current, selected)) current?.Dispose();
        current = selected;
        foreach (var session in sessions) if (!ReferenceEquals(session, selected)) session.Dispose();
        return current;
    }

    public Task<JsonObject> ReadAsync()
    {
        var session = SelectSession();
        using var metadata = session?.Metadata;
        using var playback = session?.PlaybackState;
        bool playing = playback?.State == PlaybackStateCode.Playing;
        long actions = (long)(playback?.Actions ?? 0);
        string title = metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? "";
        string artist = metadata?.GetString(MediaMetadata.MetadataKeyArtist) ?? "";
        string album = metadata?.GetString(MediaMetadata.MetadataKeyAlbum) ?? "";
        string package = session?.PackageName ?? "Android";
        string name = package;
        try
        {
            if (session != null)
            {
                using var info = context.PackageManager!.GetApplicationInfo(package, Android.Content.PM.PackageInfoFlags.MetaData);
                name = context.PackageManager.GetApplicationLabel(info);
            }
        }
        catch (Android.Content.PM.PackageManager.NameNotFoundException) { }
        string key = $"{package}\n{title}\n{artist}\n{album}";
        if (key != imageKey || DateTime.UtcNow >= nextImageRead)
        {
            imageKey = key;
            imageId = "";
            nextImageRead = DateTime.UtcNow.AddSeconds(5);
            using var bitmap = metadata?.GetBitmap(MediaMetadata.MetadataKeyArt)
                ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyAlbumArt)
                ?? metadata?.GetBitmap(MediaMetadata.MetadataKeyDisplayIcon);
            if (bitmap != null) CacheImage(bitmap);
        }
        long duration = Math.Max(0, metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0);
        long position = Math.Max(0, playback?.Position ?? 0);
        if (playing && playback?.LastPositionUpdateTime > 0)
            position += (long)(Math.Max(0, Android.OS.SystemClock.ElapsedRealtime() - playback.LastPositionUpdateTime) * playback.PlaybackSpeed);
        if (duration > 0) position = Math.Clamp(position, 0, duration);
        return Task.FromResult(new JsonObject
        {
            ["currently_active_application"] = new JsonObject { ["id"] = package, ["name"] = name },
            ["context_uri"] = "spotify:context:fake", ["context_title"] = album,
            ["is_paused"] = !playing, ["is_paused_bool"] = !playing,
            ["playback_options"] = new JsonObject { ["repeat"] = 0, ["shuffle"] = false },
            ["playback_position"] = position, ["playback_speed"] = playing ? playback?.PlaybackSpeed ?? 1 : 0,
            ["playback_restrictions"] = new JsonObject
            {
                ["can_repeat_context"] = false, ["can_repeat_track"] = false, ["can_seek"] = false,
                ["can_skip_next"] = (actions & PlaybackState.ActionSkipToNext) != 0,
                ["can_skip_prev"] = (actions & PlaybackState.ActionSkipToPrevious) != 0,
                ["can_toggle_shuffle"] = false
            },
            ["track"] = new JsonObject
            {
                ["album"] = new JsonObject { ["name"] = album, ["uri"] = "spotify:album:fake" },
                ["artist"] = new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" },
                ["artists"] = new JsonArray(new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" }),
                ["duration_ms"] = duration, ["image_id"] = imageId, ["image_bytes"] = null,
                ["is_episode"] = false, ["is_podcast"] = false, ["name"] = title,
                ["saved"] = false, ["uid"] = "fake", ["uri"] = "spotify:track:fake"
            }
        });
    }

    private void CacheImage(Bitmap original)
    {
        try
        {
            double scale = Math.Min(1, 248.0 / Math.Max(original.Width, original.Height));
            int width = Math.Max(1, (int)(original.Width * scale));
            int height = Math.Max(1, (int)(original.Height * scale));
            using var resized = Bitmap.CreateScaledBitmap(original, width, height, true)!;
            using var stream = new MemoryStream();
            if (!resized.Compress(Bitmap.CompressFormat.Jpeg!, 85, stream) || stream.Length > 60000) return;
            byte[] bytes = stream.ToArray();
            imageId = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
            images.Store(imageId, new JsonObject
            {
                ["image_data"] = Convert.ToBase64String(bytes), ["width"] = width, ["height"] = height
            });
        }
        catch (Exception ex) { Android.Util.Log.Warn("CarThing", "Cover: " + ex.Message); }
    }

    public JsonObject? GetImage(string id) => images.Get(id);

    public Task<JsonObject?> ReadVolumeAsync()
    {
        var state = Volume.Read();
        if (state == null) return Task.FromResult<JsonObject?>(null);
        var (value, maximum) = state.Value;
        return Task.FromResult<JsonObject?>(new JsonObject
        {
            ["volume"] = maximum == 0 ? 0 : (double)value / maximum,
            ["volume_steps"] = maximum + 1
        });
    }

    public Task<bool> ControlAsync(string uri, JsonObject arguments)
    {
        if (MediaCommands.IsVolumeCommand(uri))
        {
            if (audio.IsVolumeFixed) return Task.FromResult(false);
            if (uri.EndsWith(".set_volume", StringComparison.Ordinal))
                Volume.SetNormalized(MediaCommands.ParseVolume(arguments));
            else Volume.Change(uri.EndsWith(".volume_up", StringComparison.Ordinal) ? 1 : -1);
            return Task.FromResult(true);
        }
        if (uri == "com.spotify.set_playback_speed") uri = MediaCommands.PlaybackSpeedCommand(arguments);
        var session = SelectSession();
        using var playback = session?.PlaybackState;
        using var transport = session?.GetTransportControls();
        if (transport == null) return Task.FromResult(false);
        long actions = (long)(playback?.Actions ?? 0);
        switch (MediaCommands.NormalizePlaybackCommand(uri))
        {
            case "com.spotify.superbird.resume" when (actions & (PlaybackState.ActionPlay | PlaybackState.ActionPlayPause)) != 0:
                transport.Play(); break;
            case "com.spotify.superbird.pause" when (actions & (PlaybackState.ActionPause | PlaybackState.ActionPlayPause)) != 0:
                transport.Pause(); break;
            case "com.spotify.superbird.skip_next" when (actions & PlaybackState.ActionSkipToNext) != 0:
                transport.SkipToNext(); break;
            case "com.spotify.superbird.skip_prev" when (actions & PlaybackState.ActionSkipToPrevious) != 0:
                transport.SkipToPrevious(); break;
            default: return Task.FromResult(false);
        }
        // Android transport calls are asynchronous; acceptance is not a playback-state acknowledgement.
        return Task.FromResult(true);
    }

    public void Dispose()
    {
        current?.Dispose();
        manager.Dispose();
        audio.Dispose();
    }
}
