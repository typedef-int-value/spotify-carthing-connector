using Android.Bluetooth;
using Android.Content;
using Android.Provider;

namespace CarThing.Connector;

public sealed class MainPage : ContentPage
{
    private readonly Label status = new() { FontSize = 18, TextColor = Colors.White };
    private readonly Label access = new() { TextColor = Color.FromArgb("#B9C9D5") };
    private readonly Picker devices = new() { Title = "Paired Car Thing", TextColor = Colors.White, TitleColor = Colors.LightGray };
    private readonly Picker mode = new() { Title = "Display mode", TextColor = Colors.White };
    private readonly Button connect = new() { Text = "Connect" };
    private readonly Button disconnect = new() { Text = "Disconnect" };
    private readonly Button refresh = new() { Text = "Refresh paired devices" };
    private readonly Switch autoStart = new();
    private bool updatingAutoStart;
    private readonly IDispatcherTimer timer;
    private bool busy;
    private static Context Context => Android.App.Application.Context;

    public MainPage()
    {
        Title = "Carthing Connector";
        BackgroundColor = Color.FromArgb("#111B23");
        mode.Items.Add("Spotify");
        mode.Items.Add("External media");
        mode.SelectedIndex = Preferences.Default.Get("SpotifyMode", true) ? 0 : 1;
        autoStart.IsToggled = Preferences.Default.Get("AutoStart", false);
        autoStart.Toggled += async (_, e) => await SetAutoStartAsync(e.Value);
        var bluetooth = new Button { Text = "Open Bluetooth settings" };
        bluetooth.Clicked += (_, _) => OpenSettings(Settings.ActionBluetoothSettings);
        var mediaAccess = new Button { Text = "Allow media access" };
        mediaAccess.Clicked += (_, _) => OpenSettings(Settings.ActionNotificationListenerSettings);
        refresh.Clicked += async (_, _) => await RefreshDevicesAsync();
        connect.Clicked += async (_, _) => await ConnectAsync();
        disconnect.Clicked += (_, _) => Context.StopService(new Intent(Context, typeof(ConnectorService)));
        Content = new ScrollView
        {
            Content = new VerticalStackLayout
            {
                Padding = new Thickness(24, 32), Spacing = 16,
                Children =
                {
                    new Label { Text = "Carthing Connector", FontSize = 28, FontAttributes = FontAttributes.Bold, TextColor = Colors.White },
                    status,
                    new Label { Text = "Pair your Car Thing in Android Bluetooth settings, then allow media access to share the playing app's track and controls.", TextColor = Colors.LightGray },
                    access, bluetooth, mediaAccess, refresh, devices,
                    new Label { Text = "Start after reboot", TextColor = Colors.White }, autoStart,
                    new Label { Text = "Automatically select a paired device named Car Thing… and keep waiting or reconnecting. Applies on Connect and after reboot. With multiple devices, select one first.", TextColor = Colors.LightGray },
                    new Label { Text = "Display mode", TextColor = Colors.LightGray }, mode, connect, disconnect,
                    new Label { Text = "You can close this screen while connected. Disconnect stops the current session, including automatic retries. Start after reboot remains enabled until you turn it off.", TextColor = Colors.LightGray }
                }
            }
        };
        timer = Dispatcher.CreateTimer();
        timer.Interval = TimeSpan.FromSeconds(1);
        timer.Tick += (_, _) => UpdateStatus();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        UpdateStatus();
        timer.Start();
    }

    protected override void OnDisappearing()
    {
        timer.Stop();
        base.OnDisappearing();
    }

    private static void OpenSettings(string action) => Context.StartActivity(new Intent(action).AddFlags(ActivityFlags.NewTask));

    private void UpdateStatus()
    {
        status.Text = ConnectorService.Status;
        access.Text = MediaAccessService.HasAccess(Context) ? "Media access allowed" : "Media access required. Android may ask you to allow restricted settings for a sideloaded app.";
        bool editable = !busy && !ConnectorService.IsRunning;
        connect.IsEnabled = refresh.IsEnabled = devices.IsEnabled = mode.IsEnabled = autoStart.IsEnabled = editable;
        disconnect.IsEnabled = ConnectorService.IsRunning;
    }

    private async Task RefreshDevicesAsync()
    {
        if (busy || ConnectorService.IsRunning) return;
        busy = true;
        UpdateStatus();
        try
        {
            if (await Permissions.RequestAsync<BluetoothConnectPermission>() != PermissionStatus.Granted)
                throw new InvalidOperationException("Bluetooth permission is required to list paired devices.");
            var adapter = ((BluetoothManager?)Context.GetSystemService(Context.BluetoothService))?.Adapter;
            if (adapter?.IsEnabled != true) throw new InvalidOperationException("Turn on Bluetooth in Android settings first.");
            var choices = adapter.BondedDevices?.Where(d => AutoConnectionPolicy.IsCarThing(d.Name))
                .Select(d => new PairedCarThing(d.Address!, d.Name!)).OrderBy(d => d.Name).ToList() ?? [];
            devices.ItemsSource = choices;
            string saved = Preferences.Default.Get("DeviceAddress", "");
            devices.SelectedItem = AutoConnectionPolicy.Select(choices, saved);
            if (choices.Count == 0) await DisplayAlertAsync("No paired devices", "Pair your Car Thing in Bluetooth settings, then refresh.", "OK");
        }
        catch (Exception ex) { await DisplayAlertAsync("Bluetooth", ex.Message, "OK"); }
        finally { busy = false; UpdateStatus(); }
    }

    private async Task ConnectAsync()
    {
        if (busy || ConnectorService.IsRunning) return;
        busy = true;
        UpdateStatus();
        try
        {
            var selected = devices.SelectedItem as PairedCarThing;
            bool automatic = Preferences.Default.Get("AutoStart", false);
            if (selected == null && !automatic) throw new InvalidOperationException("Refresh and select a paired Car Thing first.");
            if (!MediaAccessService.HasAccess(Context)) throw new InvalidOperationException("Allow media access before connecting.");
            if (await Permissions.RequestAsync<BluetoothConnectPermission>() != PermissionStatus.Granted)
                throw new InvalidOperationException("Bluetooth permission is required.");
            // Denial does not prevent foreground operation; Android still shows the active service.
            await Permissions.RequestAsync<NotificationPermission>();
            if (selected != null) Preferences.Default.Set("DeviceAddress", selected.Address);
            Preferences.Default.Set("SpotifyMode", mode.SelectedIndex == 0);
            var intent = new Intent(Context, typeof(ConnectorService));
            intent.PutExtra("address", selected?.Address);
            intent.PutExtra("automatic", automatic);
            intent.PutExtra("spotify", mode.SelectedIndex == 0);
            Context.StartForegroundService(intent);
        }
        catch (Exception ex) { await DisplayAlertAsync("Cannot connect", ex.Message, "OK"); }
        finally { busy = false; UpdateStatus(); }
    }

    private async Task SetAutoStartAsync(bool enabled)
    {
        if (updatingAutoStart) return;
        busy = true;
        UpdateStatus();
        try
        {
            if (enabled)
            {
                if (!MediaAccessService.HasAccess(Context)) throw new InvalidOperationException("Allow media access before enabling auto start.");
                if (await Permissions.RequestAsync<BluetoothConnectPermission>() != PermissionStatus.Granted)
                    throw new InvalidOperationException("Bluetooth permission is required for auto start.");
                await Permissions.RequestAsync<NotificationPermission>();
            }
            Preferences.Default.Set("AutoStart", enabled);
            Preferences.Default.Set("SpotifyMode", mode.SelectedIndex == 0);
            if (devices.SelectedItem is PairedCarThing selected) Preferences.Default.Set("DeviceAddress", selected.Address);
        }
        catch (Exception ex)
        {
            updatingAutoStart = true;
            autoStart.IsToggled = Preferences.Default.Get("AutoStart", false);
            updatingAutoStart = false;
            await DisplayAlertAsync("Auto start", ex.Message, "OK");
        }
        finally { busy = false; UpdateStatus(); }
    }
}
