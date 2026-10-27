using Android.App;
using Android.Content;
using Android.Service.Notification;

namespace CarThing.Connector;

// Binding grants media-session access. Notification contents are not read or stored.
[Service(Name = "io.github.carthingconnector.android.MediaAccessService",
    Label = "Carthing Connector media access", Permission = "android.permission.BIND_NOTIFICATION_LISTENER_SERVICE",
    Exported = true)]
[IntentFilter(["android.service.notification.NotificationListenerService"])]
public sealed class MediaAccessService : NotificationListenerService
{
    public static ComponentName Component(Context context) => new(context, Java.Lang.Class.FromType(typeof(MediaAccessService)));

    public static bool HasAccess(Context context)
    {
        var enabled = Android.Provider.Settings.Secure.GetString(context.ContentResolver, "enabled_notification_listeners");
        return enabled?.Split(':').Any(value => ComponentName.UnflattenFromString(value)?.Equals(Component(context)) == true) == true;
    }
}
