using System.Text.Json.Nodes;

namespace CarThing.Core;

/// <summary>Platform media adapter. JSON fields follow the Car Thing protocol specification.</summary>
public interface IMediaSource
{
    Task<JsonObject> ReadAsync();
    Task<JsonObject?> ReadVolumeAsync();
    JsonObject? GetImage(string id);
    Task<bool> ControlAsync(string uri, JsonObject arguments);
}
