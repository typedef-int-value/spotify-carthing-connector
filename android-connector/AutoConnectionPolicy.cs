namespace CarThing.Connector;

public sealed record PairedCarThing(string Address, string Name)
{
    public override string ToString() => $"{Name} ({Address})";
}

public static class AutoConnectionPolicy
{
    public static bool IsCarThing(string? name) =>
        name?.StartsWith("Car Thing", StringComparison.OrdinalIgnoreCase) == true;

    public static PairedCarThing? Select(IEnumerable<PairedCarThing> devices, string? savedAddress)
    {
        var matches = devices.Where(d => IsCarThing(d.Name)).ToList();
        return matches.FirstOrDefault(d => string.Equals(d.Address, savedAddress, StringComparison.OrdinalIgnoreCase))
            ?? (matches.Count == 1 ? matches[0] : null);
    }
}
