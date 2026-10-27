using Android.App;
using Android.Bluetooth;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using CarThing.Core;

namespace CarThing.Connector;

[Service(Name = "io.github.carthingconnector.android.ConnectorService", Exported = false,
    ForegroundServiceType = ForegroundService.TypeConnectedDevice)]
public sealed class ConnectorService : Service
{
    private const string ChannelId = "carthing_connection";
    private const string DisconnectAction = "io.github.carthingconnector.android.DISCONNECT";
    private const int NotificationId = 1;
    private CancellationTokenSource? stop;
    private static int running;
    private static string status = "Disconnected";
    public static bool IsRunning => Volatile.Read(ref running) != 0;
    public static string Status => Volatile.Read(ref status);

    public override IBinder? OnBind(Intent? intent) => null;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == DisconnectAction)
        {
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        // Do not recreate a connection after Android or the user has stopped it.
        bool automatic = intent?.GetBooleanExtra("automatic", false) == true;
        string? address = intent?.GetStringExtra("address");
        if (!automatic && !BluetoothAdapter.CheckBluetoothAddress(address))
        {
            Volatile.Write(ref status, "No valid paired device selected");
            StopSelf();
            return StartCommandResult.NotSticky;
        }
        if (Interlocked.CompareExchange(ref running, 1, 0) != 0) return StartCommandResult.NotSticky;
        stop = new CancellationTokenSource();
        try
        {
            var manager = (NotificationManager)GetSystemService(NotificationService)!;
            using var channel = new NotificationChannel(ChannelId, "Car Thing connection", NotificationImportance.Low);
            channel.SetSound(null, null);
            channel.EnableVibration(false);
            manager.CreateNotificationChannel(channel);
            var notification = BuildNotification("Waiting for Car Thing");
            if (OperatingSystem.IsAndroidVersionAtLeast(29))
                StartForeground(NotificationId, notification, ForegroundService.TypeConnectedDevice);
            else StartForeground(NotificationId, notification);
            Volatile.Write(ref status, "Waiting for Car Thing");
            var cancellation = stop;
            bool spotify = intent!.GetBooleanExtra("spotify", true);
            _ = Task.Run(() => RunConnectionAsync(address, spotify, automatic, cancellation));
        }
        catch (Exception ex)
        {
            Volatile.Write(ref status, "Cannot start: " + ex.Message);
            Interlocked.Exchange(ref running, 0);
            stop.Dispose();
            stop = null;
            StopSelf();
        }
        return StartCommandResult.NotSticky;
    }

    private Notification BuildNotification(string text)
    {
        var open = PendingIntent.GetActivity(this, 0, new Intent(this, typeof(MainActivity)),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var disconnect = PendingIntent.GetService(this, 1,
            new Intent(this, typeof(ConnectorService)).SetAction(DisconnectAction),
            PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("Carthing Connector")!
            .SetContentText(text)!
            .SetSmallIcon(Resource.Drawable.ic_connector)!
            .SetContentIntent(open)!
            .SetOngoing(true)!
            .SetOnlyAlertOnce(true)!
            .AddAction(new Notification.Action.Builder(null, "Disconnect", disconnect).Build())!
            .Build();
    }

    private void SetConnectionStatus(string text)
    {
        if (Status == text) return;
        Volatile.Write(ref status, text);
        ((NotificationManager)GetSystemService(NotificationService)!).Notify(NotificationId, BuildNotification(text));
    }

    private async Task RunConnectionAsync(string? address, bool spotify, bool automatic, CancellationTokenSource cancellation)
    {
        string outcome = "Disconnected";
        CancellationToken token = cancellation.Token;
        try
        {
            var adapter = ((BluetoothManager)GetSystemService(BluetoothService)!).Adapter
                ?? throw new InvalidOperationException("Bluetooth is unavailable.");
            do
            {
                token.ThrowIfCancellationRequested();
                if (automatic)
                {
                    if (!adapter.IsEnabled) SetConnectionStatus("Waiting for Bluetooth to be enabled");
                    else if (!MediaAccessService.HasAccess(this)) SetConnectionStatus("Waiting for media access");
                    else
                    {
                        var devices = adapter.BondedDevices?.Select(d => new PairedCarThing(d.Address!, d.Name ?? "")).ToList() ?? [];
                        var selected = AutoConnectionPolicy.Select(devices, Preferences.Default.Get("DeviceAddress", ""));
                        if (selected == null)
                            SetConnectionStatus(devices.Count(d => AutoConnectionPolicy.IsCarThing(d.Name)) > 1
                                ? "Multiple Car Things: select one in the app" : "Waiting for a paired Car Thing");
                        else
                        {
                            address = selected.Address;
                            Preferences.Default.Set("DeviceAddress", address);
                            try
                            {
                                SetConnectionStatus("Waiting for " + selected.Name);
                                await ConnectOnceAsync(adapter, address, spotify, true, token);
                                SetConnectionStatus("Car Thing disconnected; waiting to reconnect");
                            }
                            catch (Exception ex) when (!token.IsCancellationRequested &&
                                ex is Java.IO.IOException or IOException or InvalidOperationException)
                            {
                                Android.Util.Log.Warn("CarThing", "Retry: " + ex.Message);
                                SetConnectionStatus("Connection lost; retrying");
                            }
                        }
                    }
                    await Task.Delay(TimeSpan.FromSeconds(10), token);
                }
                else
                {
                    if (!adapter.IsEnabled) throw new InvalidOperationException("Bluetooth is off.");
                    if (!MediaAccessService.HasAccess(this)) throw new InvalidOperationException("Media access is required.");
                    await ConnectOnceAsync(adapter, address!, spotify, false, token);
                }
            } while (automatic);
        }
        catch (Exception) when (token.IsCancellationRequested) { }
        catch (EndOfStreamException) { outcome = "Car Thing disconnected"; }
        catch (Exception ex)
        {
            outcome = "Connection ended: " + ex.Message;
            Android.Util.Log.Warn("CarThing", ex.ToString());
        }
        finally
        {
            Volatile.Write(ref status, outcome);
            // Keep the service marked busy until cleanup and notification removal finish.
            MainThread.BeginInvokeOnMainThread(() =>
            {
                StopForeground(StopForegroundFlags.Remove);
                StopSelf();
                if (ReferenceEquals(stop, cancellation)) stop = null;
                cancellation.Dispose();
                Interlocked.Exchange(ref running, 0);
            });
        }
    }

    private async Task ConnectOnceAsync(BluetoothAdapter adapter, string address, bool spotify,
        bool automatic, CancellationToken token)
    {
        using var target = adapter.GetRemoteDevice(address);
        if (target?.BondState != Bond.Bonded) throw new InvalidOperationException("Pair Car Thing in Bluetooth settings first.");
        using var uuid = Java.Util.UUID.FromString("E3CCCCCD-33B7-457D-A03C-AA1C54BF617F")!;
        using var listener = adapter.ListenUsingRfcommWithServiceRecord("Carthing Connector", uuid)!;
        using var cancelListener = token.Register(() => CloseQuietly(listener));
        var wait = System.Diagnostics.Stopwatch.StartNew();
        BluetoothSocket? socket = null;
        while (socket == null)
        {
            token.ThrowIfCancellationRequested();
            // Automatic mode blocks in Accept without polling; cancellation closes the socket.
            int remaining = (int)(TimeSpan.FromMinutes(2) - wait.Elapsed).TotalMilliseconds;
            if (!automatic && remaining <= 0) throw new TimeoutException("Select this phone on Car Thing and try again.");
            var incoming = automatic ? listener.Accept() : listener.Accept(remaining);
            if (incoming?.RemoteDevice?.Address == address) socket = incoming;
            else { incoming?.Close(); incoming?.Dispose(); }
        }
        using (socket)
        using (token.Register(() => CloseQuietly(socket)))
        using (var media = new AndroidMediaSource(this))
        {
            listener.Close();
            token.ThrowIfCancellationRequested();
            SetConnectionStatus("Connected to " + (target.Name ?? "Car Thing"));
            string directory = Path.Combine(FilesDir!.AbsolutePath, "captures");
            Directory.CreateDirectory(directory);
            string stem = Path.Combine(directory, $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}");
            await MediaBridge.RunAsync(socket.InputStream!, socket.OutputStream!, media, stem,
                spotify, () => CloseQuietly(socket), token);
        }
    }

    private static void CloseQuietly(BluetoothSocket socket)
    {
        try { socket.Close(); } catch (Java.IO.IOException) { }
        catch (ObjectDisposedException) { }
    }

    private static void CloseQuietly(BluetoothServerSocket socket)
    {
        try { socket.Close(); } catch (Java.IO.IOException) { }
        catch (ObjectDisposedException) { }
    }

    public override void OnDestroy()
    {
        stop?.Cancel();
        StopForeground(StopForegroundFlags.Remove);
        base.OnDestroy();
    }
}
