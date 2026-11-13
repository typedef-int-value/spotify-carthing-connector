using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Android.Content;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using CarThing.Core;
using AudioStream = Android.Media.Stream;
using Android.Support.V4.Media;
using Android.Support.V4.Media.Session;

namespace CarThing.Connector;

internal sealed class AndroidMediaSource(Context context) : IMediaSource, IDisposable
{
    private readonly MediaSessionManager manager = (MediaSessionManager)context.GetSystemService(Context.MediaSessionService)!;
    private readonly AudioManager audio = (AudioManager)context.GetSystemService(Context.AudioService)!;
    private readonly MediaImageCache images = new();
    private MediaController? current;
    private MediaControllerCompat? compat;
    private MediaQueueSnapshot? queueSnapshot;
    private float lastPlaybackSpeed = 1;
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
        bool sameSession = selected != null && current != null && selected.SessionToken?.Equals(current.SessionToken) == true;
        if (!sameSession)
        {
            compat?.Dispose();
            compat = null;
            queueSnapshot = null;
            lastPlaybackSpeed = 1;
            if (selected?.SessionToken is { } nativeToken)
            {
                using var token = MediaSessionCompat.Token.FromToken(nativeToken);
                if (token != null) compat = new MediaControllerCompat(context, token);
            }
        }
        if (!ReferenceEquals(current, selected)) current?.Dispose();
        current = selected;
        foreach (var session in sessions) if (!ReferenceEquals(session, selected)) session.Dispose();
        return current;
    }

    public Task<JsonObject> ReadAsync()
    {
        var session = SelectSession();
        using var metadata = session?.Metadata;
        using var playback = compat?.PlaybackState;
        bool playing = playback?.State == PlaybackStateCompat.StatePlaying;
        long actions = (long)(playback?.Actions ?? 0);
        if (playback?.PlaybackSpeed > 0 && float.IsFinite(playback.PlaybackSpeed)) lastPlaybackSpeed = playback.PlaybackSpeed;
        int repeat = compat?.IsSessionReady == true ? compat.RepeatMode : -1;
        int shuffle = compat?.IsSessionReady == true ? compat.ShuffleMode : -1;
        using var rating = metadata?.GetRating(MediaMetadata.MetadataKeyUserRating);
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
            ["context_uri"] = "spotify:context:fake", ["context_title"] = name,
            ["is_paused"] = !playing, ["is_paused_bool"] = !playing,
            ["playback_options"] = new JsonObject { ["repeat"] = repeat is 1 or 2 ? repeat : 0, ["shuffle"] = shuffle is 1 or 2 },
            ["playback_position"] = position, ["playback_speed"] = playing ? playback?.PlaybackSpeed ?? 1 : 0,
            ["playback_restrictions"] = new JsonObject
            {
                ["can_repeat_context"] = repeat is >= 0 and <= 2 && (actions & PlaybackStateCompat.ActionSetRepeatMode) != 0,
                ["can_repeat_track"] = repeat is >= 0 and <= 2 && (actions & PlaybackStateCompat.ActionSetRepeatMode) != 0,
                ["can_seek"] = (actions & PlaybackState.ActionSeekTo) != 0 && duration > 0,
                ["can_skip_next"] = (actions & PlaybackState.ActionSkipToNext) != 0,
                ["can_skip_prev"] = (actions & PlaybackState.ActionSkipToPrevious) != 0,
                ["can_toggle_shuffle"] = shuffle is >= 0 and <= 2 && (actions & PlaybackStateCompat.ActionSetShuffleMode) != 0
            },
            ["_queue"] = ReadQueue(playback?.ActiveQueueItemId ?? -1),
            ["_can_rate"] = compat?.RatingType == RatingCompat.RatingHeart && (actions & PlaybackStateCompat.ActionSetRating) != 0,
            ["_podcast_speed"] = (actions & PlaybackStateCompat.ActionSetPlaybackSpeed) != 0 ? JsonValue.Create(lastPlaybackSpeed) : null,
            ["track"] = new JsonObject
            {
                ["album"] = new JsonObject { ["name"] = album, ["uri"] = "spotify:album:fake" },
                ["artist"] = new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" },
                ["artists"] = new JsonArray(new JsonObject { ["name"] = artist, ["uri"] = "spotify:artist:fake" }),
                ["duration_ms"] = duration, ["image_id"] = imageId, ["image_bytes"] = null,
                ["is_episode"] = IsEpisode(metadata), ["is_podcast"] = IsEpisode(metadata), ["name"] = title,
                ["saved"] = rating?.HasHeart == true, ["uid"] = "fake", ["uri"] = "spotify:track:fake"
            }
        });
    }

    private static bool IsEpisode(MediaMetadata? metadata) =>
        (metadata?.GetString(MediaMetadata.MetadataKeyMediaId) ?? "").StartsWith("spotify:episode:", StringComparison.Ordinal) ||
        (metadata?.GetString(MediaMetadata.MetadataKeyMediaUri) ?? "").StartsWith("spotify:episode:", StringComparison.Ordinal);

    private JsonObject? ReadQueue(long activeId)
    {
        var queue = compat?.Queue;
        queueSnapshot = null;
        if (queue == null) return null;
        try
        {
            long[] ids = queue.Select(item => item.QueueId).ToArray();
            var items = queue.Select(item =>
            {
                using var description = item.Description;
                string title = description?.Title?.ToString() ?? "";
                string artist = description?.Subtitle?.ToString() ?? "";
                return new JsonObject
                {
                    ["uid"] = item.QueueId.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    ["uri"] = MediaCommands.LocalTrackUri(title, artist, description?.Description?.ToString() ?? ""),
                    ["name"] = title, ["artist_name"] = artist, ["image_uri"] = "", ["provider"] = "context"
                };
            }).ToArray();
            var result = MediaQueueSnapshot.Build(items, ids, activeId);
            if (result != null) queueSnapshot = new MediaQueueSnapshot(ids, activeId);
            return result;
        }
        finally { foreach (var item in queue) item.Dispose(); }
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
        using var playback = compat?.PlaybackState;
        using var transport = compat?.GetTransportControls();
        if (transport == null) return Task.FromResult(false);
        long actions = (long)(playback?.Actions ?? 0);
        if (uri is "com.spotify.set_shuffle" or "com.spotify.superbird.set_shuffle" or "com.spotify.toggle_shuffle")
        {
            int mode = compat!.IsSessionReady ? compat.ShuffleMode : -1;
            if ((actions & PlaybackStateCompat.ActionSetShuffleMode) == 0 || mode is < 0 or > 2) return Task.FromResult(false);
            transport.SetShuffleMode(MediaCommands.ShuffleTarget(uri, arguments, mode != 0) ? 1 : 0);
            return Task.FromResult(true);
        }
        if (uri is "com.spotify.superbird.set_repeat" or "com.spotify.set_repeat")
        {
            int mode = compat!.IsSessionReady ? compat.RepeatMode : -1;
            if ((actions & PlaybackStateCompat.ActionSetRepeatMode) == 0 || mode is < 0 or > 2) return Task.FromResult(false);
            transport.SetRepeatMode(MediaCommands.RepeatTarget(uri, arguments));
            return Task.FromResult(true);
        }
        if (uri == "com.spotify.set_saved")
        {
            if ((actions & PlaybackStateCompat.ActionSetRating) == 0 || compat!.RatingType != RatingCompat.RatingHeart)
                return Task.FromResult(false);
            using var metadata = session?.Metadata;
            string localUri = MediaCommands.LocalTrackUri(metadata?.GetString(MediaMetadata.MetadataKeyTitle) ?? "",
                metadata?.GetString(MediaMetadata.MetadataKeyArtist) ?? "", metadata?.GetString(MediaMetadata.MetadataKeyAlbum) ?? "");
            if (arguments["uri"]?.GetValue<string>() != localUri) return Task.FromResult(false);
            using var rating = RatingCompat.NewHeartRating(MediaCommands.SavedTarget(arguments));
            transport.SetRating(rating);
            return Task.FromResult(true);
        }
        if (uri == "com.spotify.set_podcast_playback_speed")
        {
            if ((actions & PlaybackStateCompat.ActionSetPlaybackSpeed) == 0) return Task.FromResult(false);
            transport.SetPlaybackSpeed(MediaCommands.PodcastSpeed(arguments));
            return Task.FromResult(true);
        }
        if (uri == "com.spotify.skip_to_index_in_queue")
        {
            if ((actions & PlaybackStateCompat.ActionSkipToQueueItem) == 0 || queueSnapshot == null) return Task.FromResult(false);
            int index = MediaCommands.IntegerArgument(arguments, "index");
            var queue = compat!.Queue;
            if (queue == null) return Task.FromResult(false);
            try
            {
                long? id = queueSnapshot.Resolve(index, queue.Select(item => item.QueueId).ToArray(), playback!.ActiveQueueItemId);
                if (!id.HasValue) return Task.FromResult(false);
                transport.SkipToQueueItem(id.Value);
                return Task.FromResult(true);
            }
            finally { foreach (var item in queue) item.Dispose(); }
        }
        if (MediaCommands.IsSeekCommand(uri))
        {
            using var metadata = session?.Metadata;
            long duration = metadata?.GetLong(MediaMetadata.MetadataKeyDuration) ?? 0;
            if ((actions & PlaybackState.ActionSeekTo) == 0 || duration <= 0)
                return Task.FromResult(false);
            transport.SeekTo(MediaCommands.ParseSeekPosition(uri, arguments, duration));
            // Position updates come from the session, not from the requested target.
            return Task.FromResult(true);
        }
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
        compat?.Dispose();
        manager.Dispose();
        audio.Dispose();
    }
}
