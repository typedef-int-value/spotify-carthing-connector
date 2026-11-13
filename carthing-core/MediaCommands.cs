using System.Text.Json.Nodes;

namespace CarThing.Core;

public static class MediaCommands
{
    public static string NormalizePlaybackCommand(string uri) => uri switch
    {
        "com.spotify.skip_next" => "com.spotify.superbird.skip_next",
        "com.spotify.skip_previous" => "com.spotify.superbird.skip_prev",
        _ => uri
    };

    public static string PlaybackSpeedCommand(JsonObject arguments)
    {
        if (arguments["playback_speed"] is not JsonValue value ||
            !value.TryGetValue<int>(out int speed) || speed is not (0 or 1))
            throw new ArgumentException("playback_speed must be 0 (pause) or 1 (play).");
        return speed == 0 ? "com.spotify.superbird.pause" : "com.spotify.superbird.resume";
    }

    public static bool IsPlaybackCommand(string uri) => uri == "com.spotify.set_playback_speed" || NormalizePlaybackCommand(uri) is
        "com.spotify.set_shuffle" or "com.spotify.superbird.set_shuffle" or "com.spotify.toggle_shuffle" or
        "com.spotify.superbird.resume" or "com.spotify.superbird.pause" or
        "com.spotify.superbird.skip_next" or "com.spotify.superbird.skip_prev" or
        "com.spotify.superbird.set_repeat" or "com.spotify.set_repeat" or
        "com.spotify.set_saved" or "com.spotify.set_podcast_playback_speed" or
        "com.spotify.skip_to_index_in_queue" || IsSeekCommand(uri);

    public static int RepeatTarget(string uri, JsonObject arguments)
    {
        if (uri == "com.spotify.superbird.set_repeat")
        {
            if (arguments["repeat_mode"] is JsonValue value && value.TryGetValue<string>(out string? mode))
                return mode switch { "NONE" => 0, "TRACK" => 1, "CONTEXT" => 2,
                    _ => throw new ArgumentException("Unsupported repeat_mode.") };
            throw new ArgumentException("repeat_mode must be NONE, TRACK or CONTEXT.");
        }
        int repeat = IntegerArgument(arguments, "repeat");
        return repeat is >= 0 and <= 2 ? repeat : throw new ArgumentException("repeat must be 0, 1 or 2.");
    }

    public static int IntegerArgument(JsonObject arguments, string key) =>
        arguments[key] is JsonValue value && value.TryGetValue<int>(out int result)
            ? result : throw new ArgumentException($"{key} must be an integer.");

    public static bool SavedTarget(JsonObject arguments) =>
        arguments["saved"] is JsonValue value && value.TryGetValue<bool>(out bool result)
            ? result : throw new ArgumentException("saved must be a Boolean.");

    public static float PodcastSpeed(JsonObject arguments)
    {
        if (arguments["playback_speed"] is not JsonValue value ||
            !value.TryGetValue<double>(out double speed) || !double.IsFinite(speed) || speed < 0.5 || speed > 3.5)
            throw new ArgumentException("playback_speed must be between 0.5 and 3.5.");
        return (float)speed;
    }

    public static string LocalTrackUri(string title, string artist, string album)
    {
        string identity = $"{title}\n{artist}\n{album}";
        string key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(identity))).ToLowerInvariant();
        return "spotify:track:" + key[..22];
    }

    public static bool IsSeekCommand(string uri) => uri is
        "com.spotify.superbird.seek_to" or "com.spotify.set_playback_position";

    public static long ParseSeekPosition(string uri, JsonObject arguments, long duration)
    {
        string key = uri switch
        {
            "com.spotify.superbird.seek_to" => "position",
            "com.spotify.set_playback_position" => "position_ms",
            _ => throw new ArgumentException("Unsupported seek command.")
        };
        if (arguments[key] is not JsonValue value)
            throw new ArgumentException($"{key} must be a finite number in milliseconds.");
        if (duration <= 0) throw new ArgumentException("Track duration must be positive.");
        if (value.TryGetValue<long>(out long integer)) return Math.Clamp(integer, 0, duration);
        if (!value.TryGetValue<double>(out double position) || !double.IsFinite(position))
            throw new ArgumentException($"{key} must be a finite number in milliseconds.");
        if (position <= 0) return 0;
        if (position >= duration) return duration;
        return (long)position;
    }

    public static bool ShuffleTarget(string uri, JsonObject arguments, bool? current)
    {
        if (uri == "com.spotify.toggle_shuffle")
            return current.HasValue ? !current.Value : throw new InvalidOperationException("Shuffle state unavailable.");
        if (arguments["shuffle"] is not JsonValue value || !value.TryGetValue<bool>(out bool requested))
            throw new ArgumentException("shuffle must be a Boolean.");
        return requested;
    }

    public static bool IsVolumeCommand(string uri) => uri is
        "com.spotify.superbird.volume.volume_up" or
        "com.spotify.superbird.volume.volume_down" or
        "com.spotify.superbird.volume.set_volume";

    public static float ParseVolume(JsonObject arguments)
    {
        if (arguments["volume"] is not JsonValue value ||
            !value.TryGetValue<double>(out double volume) ||
            !double.IsFinite(volume) || volume < 0 || volume > 1)
            throw new ArgumentException("volume must be a number between 0 and 1.");
        return (float)volume;
    }

}
