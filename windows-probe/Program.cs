using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Devices.Enumeration;
using Windows.Networking.Sockets;
using Windows.Storage.Streams;

// RFCOMM capture and Windows media bridge.
internal static class Program
{
    private static readonly (string Name, Guid Id)[] Services =
    [
        ("length-prefixed", new("E3CCCCCD-33B7-457D-A03C-AA1C54BF617F")),
        ("escaped-crc", new("7FBE865E-518B-462A-B31B-90ACF6A472FC"))
    ];

    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0 || args is ["--help"])
            {
                Console.WriteLine("CarThingProbe --list\nCarThingProbe --media\nCarThingProbe --volume\nCarThingProbe --self-test\nCarThingProbe --check-radio\nCarThingProbe --listen AA:BB:CC:DD:EE:FF [seconds: 1..3600]\nCarThingProbe --bridge AA:BB:CC:DD:EE:FF [seconds: 1..3600]");
                Console.WriteLine("Pair in Windows first. --listen captures only; --bridge sends current Windows media.");
                Console.WriteLine("--bridge runs until Ctrl+C unless a duration is supplied.");
                return 0;
            }
            if (args is ["--media"])
            {
                var media = await WindowsMedia.CreateAsync();
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(await media.ReadAsync()));
                return 0;
            }
            if (args is ["--self-test"])
            {
                await MediaBridge.SelfTestAsync();
                return 0;
            }
            if (args is ["--volume"])
            {
                Console.WriteLine(WindowsVolume.Read().ToJsonString());
                return 0;
            }
            if (args is ["--list"])
            {
                var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true));
                foreach (var info in devices)
                {
                    using var device = await BluetoothDevice.FromIdAsync(info.Id);
                    if (device != null)
                        Console.WriteLine($"{device.BluetoothAddress:X12}  {device.Name}  {device.ConnectionStatus}");
                }
                return 0;
            }
            bool checkRadio = args is ["--check-radio"];
            bool bridge = args.Length > 0 && args[0] == "--bridge";
            if (!checkRadio && (args.Length is < 2 or > 3 || (args[0] != "--listen" && !bridge)))
                throw new ArgumentException("Use --help for usage.");
            var addressText = checkRadio ? "000000000000" : args[1].Replace(":", "").Replace("-", "");
            if (addressText.Length != 12 || !ulong.TryParse(addressText,
                    System.Globalization.NumberStyles.HexNumber, null, out var address))
                throw new ArgumentException("Expected a 12-digit Bluetooth address.");
            var seconds = checkRadio ? 3 : args.Length == 3 ? int.Parse(args[2]) : bridge ? 0 : 120;
            if ((seconds < 1 && !(bridge && args.Length == 2)) || seconds > 3600)
                throw new ArgumentException("Seconds must be between 1 and 3600.");
            using var target = checkRadio ? null : await BluetoothDevice.FromBluetoothAddressAsync(address);
            if (!checkRadio && (target == null || !target.DeviceInformation.Pairing.IsPaired))
                throw new InvalidOperationException("Target must be paired in Windows Bluetooth settings first.");

            using var stop = seconds == 0 ? new CancellationTokenSource() : new CancellationTokenSource(TimeSpan.FromSeconds(seconds));
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
            var listeners = new List<StreamSocketListener>();
            var providers = new List<RfcommServiceProvider>();
            var tasks = new List<Task>();
            var taskLock = new object();
            var output = Path.GetFullPath(Path.Combine("analysis", "captures", DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fff")));
            Directory.CreateDirectory(output);
            try
            {
                foreach (var service in bridge ? Services.Take(1) : Services)
                {
                    var provider = await RfcommServiceProvider.CreateAsync(RfcommServiceId.FromUuid(service.Id));
                    if (provider == null) throw new InvalidOperationException("RFCOMM service creation failed.");
                    providers.Add(provider);
                    var listener = new StreamSocketListener();
                    listeners.Add(listener);
                    listener.ConnectionReceived += (_, e) =>
                    {
                        lock (taskLock)
                        {
                            if (stop.IsCancellationRequested) e.Socket.Dispose();
                            else tasks.Add(Capture(e.Socket, service.Name, address, output, stop.Token, bridge));
                        }
                    };
                    await listener.BindServiceNameAsync(provider.ServiceId.AsString(),
                        SocketProtectionLevel.BluetoothEncryptionWithAuthentication);
                    using var name = new DataWriter();
                    name.WriteByte(0x25); // SDP text string, uint8 length.
                    name.WriteByte(7);
                    name.WriteString("Spotify");
                    provider.SdpRawAttributes.Add(0x100, name.DetachBuffer());
                    provider.StartAdvertising(listener, true);
                    Console.WriteLine($"Listening: {service.Name} {service.Id}");
                }
                Console.WriteLine($"Target: {target?.Name ?? "radio check only"} ({address:X12}); duration {(seconds == 0 ? "until Ctrl+C" : $"{seconds}s")}; output {output}");
                Console.WriteLine(checkRadio ? "Testing local service advertisement only." : "Select/reconnect this PC from Car Thing. Ctrl+C stops capture.");
                try { await Task.Delay(Timeout.Infinite, stop.Token); }
                catch (OperationCanceledException) { }
            }
            finally
            {
                stop.Cancel();
                foreach (var provider in providers) provider.StopAdvertising();
                foreach (var listener in listeners) listener.Dispose();
                Task[] pending;
                lock (taskLock) pending = tasks.ToArray();
                await Task.WhenAll(pending);
            }
            Console.WriteLine("Stopped. See session logs for protocol progress.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"{ex.GetType().Name}: {ex.Message} (0x{ex.HResult:X8})");
            return 1;
        }
    }

    private static async Task Capture(StreamSocket socket, string transport, ulong expected,
        string output, CancellationToken cancellation, bool bridge)
    {
        using (socket)
        {
            try
            {
                using var peer = await BluetoothDevice.FromHostNameAsync(socket.Information.RemoteHostName);
                if (peer == null || peer.BluetoothAddress != expected)
                {
                    Console.WriteLine("Rejected connection from a different Bluetooth device.");
                    return;
                }
                using var cancelRegistration = cancellation.Register(socket.Dispose);
                var stem = Path.Combine(output, $"{transport}-{Guid.NewGuid():N}");
                if (bridge)
                {
                    await MediaBridge.RunAsync(socket, stem, cancellation);
                    return;
                }
                await using var raw = File.Create(stem + ".bin");
                await using var log = new StreamWriter(stem + ".log");
                using var reader = new DataReader(socket.InputStream) { InputStreamOptions = InputStreamOptions.Partial };
                Console.WriteLine($"Connected: {peer.Name}, {transport}");
                while (!cancellation.IsCancellationRequested)
                {
                    uint count = await reader.LoadAsync(4096);
                    if (count == 0) break;
                    byte[] bytes = new byte[count];
                    reader.ReadBytes(bytes);
                    await raw.WriteAsync(bytes);
                    await raw.FlushAsync();
                    var line = $"{DateTimeOffset.UtcNow:O} {Convert.ToHexString(bytes)}";
                    await log.WriteLineAsync(line);
                    await log.FlushAsync();
                    Console.WriteLine($"RX {transport}: {count} bytes");
                }
            }
            catch (Exception ex)
            {
                if (!cancellation.IsCancellationRequested)
                    Console.Error.WriteLine($"Capture {transport}: {ex.Message}");
            }
        }
    }
}
