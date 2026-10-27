using Android.App;
using Android.Content;
using Android.Content.PM;

namespace CarThing.Connector;

[BroadcastReceiver(Name = "io.github.carthingconnector.android.BootReceiver", Enabled = true, Exported = true)]
[IntentFilter([Intent.ActionBootCompleted])]
public sealed class BootReceiver : BroadcastReceiver
{
    public override void OnReceive(Context? context, Intent? intent)
    {
        if (context == null || intent?.Action != Intent.ActionBootCompleted ||
            !Preferences.Default.Get("AutoStart", false)) return;
        if (OperatingSystem.IsAndroidVersionAtLeast(31) &&
            context.CheckSelfPermission(Android.Manifest.Permission.BluetoothConnect) != Permission.Granted)
        {
            Android.Util.Log.Warn("CarThing", "Auto start skipped: Bluetooth permission is missing.");
            return;
        }
        try
        {
            var service = new Intent(context, typeof(ConnectorService));
            service.PutExtra("automatic", true);
            service.PutExtra("spotify", Preferences.Default.Get("SpotifyMode", true));
            context.StartForegroundService(service);
        }
        catch (Exception ex) { Android.Util.Log.Warn("CarThing", "Auto start: " + ex.Message); }
    }
}
