namespace CarThing.Connector;

// Calls are serialized by the bridge. Keep relative input in the same integer
// units advertised to Car Thing, independently of vendor adjustment increments.
internal sealed class VolumeController(Func<int> read, Func<int> maximum,
    Action<int> write, Func<long> milliseconds)
{
    private int? pending;
    private long deadline;
    private int pendingMaximum;

    private int Observe(int max)
    {
        int actual = Math.Clamp(read(), 0, max);
        if (pending == actual || milliseconds() >= deadline || pendingMaximum != max)
            pending = null;
        return actual;
    }

    public (int Value, int Maximum)? Read()
    {
        int max = maximum();
        int actual = Observe(max);
        // Do not contradict an in-flight request with an older platform value.
        // After the bounded settling interval, the platform is authoritative.
        return pending.HasValue ? null : (actual, max);
    }

    public void Change(int delta) => Set(null, delta);
    public void SetNormalized(double value) => Set(value, 0);

    private void Set(double? normalized, int delta)
    {
        int max = maximum();
        int actual = Observe(max);
        int target = normalized.HasValue
            ? (int)Math.Round(normalized.Value * max)
            : (pending ?? actual) + delta;
        target = Math.Clamp(target, 0, max);
        write(target);
        pending = target;
        pendingMaximum = max;
        deadline = milliseconds() + 500;
    }
}
