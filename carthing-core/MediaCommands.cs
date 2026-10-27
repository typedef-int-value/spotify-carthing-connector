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
        "com.spotify.superbird.skip_next" or "com.spotify.superbird.skip_prev";

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
