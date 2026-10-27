using CarThing.Connector;

internal static class VolumeControllerTests
{
    public static void Run()
    {
        // Replay the captured vendor adjustment: five requests, 54 -> 39,
        // exposed as 6 -> 4 on a public range of 0..15.
        int fine = 54;
        for (int i = 0; i < 5; i++) fine -= 3;
        Check((int)Math.Ceiling(fine / 10.0) == 4, "captured fine-step baseline");

        int actual = 6, max = 15;
        long now = 0;
        var writes = new List<int>();
        var volume = new VolumeController(() => actual, () => max, writes.Add, () => now);
        for (int i = 0; i < 5; i++) volume.Change(-1);
        Check(writes.SequenceEqual(new[] { 5, 4, 3, 2, 1 }), "five steps with stale reads");
        Check(volume.Read() == null, "suppress stale feedback");
        actual = 1;
        Check(volume.Read()?.Value == 1, "acknowledged target");
        for (int i = 0; i < 5; i++) volume.Change(1);
        Check(writes.Last() == 6, "five upward steps");
        volume.Change(-1);
        Check(writes.Last() == 5, "direction reversal");
        now = 500;
        Check(volume.Read()?.Value == 1, "platform rejection wins after timeout");
        actual = 9;
        volume.Change(1);
        Check(writes.Last() == 10, "external volume change");
        volume.SetNormalized(1);
        volume.Change(1);
        Check(writes.Last() == 15, "upper bound");
        volume.SetNormalized(0);
        volume.Change(-1);
        Check(writes.Last() == 0, "lower bound");
        max = 7;
        actual = 3;
        volume.Change(1);
        Check(writes.Last() == 4, "range change discards pending target");
        Console.WriteLine("PASS: Android volume units, burst input, feedback, timeout, bounds, external changes.");
    }

    private static void Check(bool result, string name)
    {
        if (!result) throw new Exception("Volume: " + name);
    }
}
