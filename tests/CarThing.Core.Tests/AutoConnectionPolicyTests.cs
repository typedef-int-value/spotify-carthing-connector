using CarThing.Connector;

internal static class AutoConnectionPolicyTests
{
    public static void Run()
    {
        var first = new PairedCarThing("00:11:22:33:44:55", "Car Thing Q61S");
        var second = new PairedCarThing("AA:BB:CC:DD:EE:FF", "car thing Second");
        var other = new PairedCarThing("11:22:33:44:55:66", "Speaker Car Thing");
        if (AutoConnectionPolicy.Select([], null) != null ||
            AutoConnectionPolicy.Select([other], other.Address) != null ||
            AutoConnectionPolicy.Select([first, other], null) != first ||
            AutoConnectionPolicy.Select([first, second], null) != null ||
            AutoConnectionPolicy.Select([first, second], "aa:bb:cc:dd:ee:ff") != second ||
            AutoConnectionPolicy.Select([first, second], other.Address) != null ||
            AutoConnectionPolicy.Select([first], second.Address) != first)
            throw new Exception("Automatic paired-device selection regression.");
        Console.WriteLine("PASS: Car Thing prefix, unique candidate, saved-device preference, ambiguity and stale selection.");
    }
}
