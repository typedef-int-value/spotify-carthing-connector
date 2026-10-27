using CarThing.Core;
using System.Text.Json.Nodes;
using NAudio.CoreAudioApi;

internal static class WindowsVolume
{
    public static bool Control(string uri, JsonObject arguments)
    {
        // Validate before opening an endpoint or changing any system volume.
        float scalar = uri == "com.spotify.superbird.volume.set_volume" ? MediaCommands.ParseVolume(arguments) : 0;
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        var volume = device.AudioEndpointVolume;
        switch (uri)
        {
            case "com.spotify.superbird.volume.volume_up": volume.VolumeStepUp(); break;
            case "com.spotify.superbird.volume.volume_down": volume.VolumeStepDown(); break;
            case "com.spotify.superbird.volume.set_volume": volume.MasterVolumeLevelScalar = scalar; break;
            default: return false;
        }
        Console.WriteLine($"Volume: {volume.MasterVolumeLevelScalar:P0}");
        return true;
    }

    public static JsonObject Read()
    {
        using var enumerator = new MMDeviceEnumerator();
        using var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
        return new JsonObject
        {
            ["volume"] = device.AudioEndpointVolume.MasterVolumeLevelScalar,
            ["volume_steps"] = device.AudioEndpointVolume.StepInformation.StepCount
        };
    }
}
