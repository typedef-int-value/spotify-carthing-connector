using System.Diagnostics;
using System.Text.Json;
using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

internal sealed class ConnectorTray : ApplicationContext
{
    private static readonly string DataDirectory = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "carthing-connector");
    private readonly Control dispatcher = new();
    private readonly NotifyIcon icon;
    private readonly ContextMenuStrip menu = new();
    private readonly ToolStripMenuItem statusItem = new("시작 중") { Enabled = false };
    private readonly ToolStripMenuItem deviceItem = new("장치 선택…");
    private readonly ToolStripMenuItem restartItem = new("연결 다시 시작");
    private readonly ToolStripMenuItem exitItem = new("종료");
    private readonly TextWriter originalOutput = Console.Out;
    private readonly TextWriter originalError = Console.Error;
    private readonly StreamWriter log;
    private readonly CancellationTokenSource lifetime = new();
    private Form? deviceDialog;
    private CancellationTokenSource? stop;
    private Task<int>? bridge;
    private string? address;
    private bool busy;
    private bool closing;
    private bool initialized;

    public ConnectorTray(bool initialize = true)
    {
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
        log = new StreamWriter(Path.Combine(DataDirectory, "logs", $"app-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log")) { AutoFlush = true };
        var writer = TextWriter.Synchronized(log);
        Console.SetOut(writer);
        Console.SetError(writer);
        if (Program.SpotifyUiExperiment) Console.WriteLine("EXPERIMENT: Spotify UI; Windows media source; empty home shelf.");
        _ = dispatcher.Handle;
        icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "carthing-connector · 시작 중",
            ContextMenuStrip = menu,
            Visible = true
        };
        menu.Items.Add(new ToolStripMenuItem("carthing-connector") { Enabled = false });
        if (Program.SpotifyUiExperiment)
            menu.Items.Add(new ToolStripMenuItem("시험 모드 · Spotify 화면") { Enabled = false });
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(deviceItem);
        menu.Items.Add(restartItem);
        menu.Items.Add("로그 폴더 열기", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(DataDirectory, "logs")) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        deviceItem.Click += async (_, _) => await SelectDeviceAsync();
        restartItem.Click += async (_, _) => await RestartAsync();
        exitItem.Click += async (_, _) => await CloseAsync();
        icon.DoubleClick += async (_, _) => await SelectDeviceAsync();
        if (initialize) Application.Idle += Initialize;
    }

    private async void Initialize(object? sender, EventArgs e)
    {
        if (initialized) return;
        initialized = true;
        Application.Idle -= Initialize;
        try
        {
            string settings = Path.Combine(DataDirectory, "settings.json");
            if (File.Exists(settings))
            {
                var saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settings));
                if (saved != null && saved.DeviceAddress.Length == 12 &&
                    ulong.TryParse(saved.DeviceAddress, System.Globalization.NumberStyles.HexNumber, null, out _))
                    address = saved.DeviceAddress;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Settings: {ex.Message}"); }
        if (address == null) await SelectDeviceAsync();
        else await RestartAsync();
    }

    private void SetStatus(string text)
    {
        if (closing || dispatcher.IsDisposed) return;
        if (dispatcher.InvokeRequired)
        {
            try { dispatcher.BeginInvoke(() => SetStatus(text)); }
            catch (InvalidOperationException) { }
            return;
        }
        statusItem.Text = text;
        string tooltip = "carthing-connector · " + text;
        icon.Text = tooltip[..Math.Min(tooltip.Length, 63)];
        Console.WriteLine($"{DateTimeOffset.Now:O} Status: {text}");
    }

    private void SetBusy(bool value)
    {
        busy = value;
        deviceItem.Enabled = restartItem.Enabled = !value && !closing;
        exitItem.Enabled = !closing;
    }

    private async Task SelectDeviceAsync()
    {
        if (busy || closing) return;
        SetBusy(true);
        CancellationToken cancellation = lifetime.Token;
        try
        {
            var choices = await Task.Run(async () =>
            {
                var result = new List<DeviceChoice>();
                var devices = await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                    ["System.Devices.Aep.DeviceAddress"], DeviceInformationKind.AssociationEndpoint).AsTask(cancellation);
                foreach (var info in devices)
                {
                    string? deviceAddress = (info.Properties.GetValueOrDefault("System.Devices.Aep.DeviceAddress") as string)?.Replace(":", "").Replace("-", "");
                    if (deviceAddress?.Length == 12 && ulong.TryParse(deviceAddress,
                        System.Globalization.NumberStyles.HexNumber, null, out _))
                        result.Add(new DeviceChoice(deviceAddress.ToUpperInvariant(), info.Name));
                }
                return result.OrderByDescending(d => d.Name.Contains("Car Thing", StringComparison.OrdinalIgnoreCase)).ThenBy(d => d.Name).ToList();
            }).WaitAsync(cancellation);
            if (closing) return;
            Console.WriteLine($"Paired devices: {choices.Count}");
            using var dialog = new Form
            {
                Text = "carthing-connector · 장치 선택", ClientSize = new Size(470, 150),
                StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog,
                MaximizeBox = false, MinimizeBox = false, TopMost = true
            };
            dialog.Controls.Add(new Label { Text = "Windows에서 페어링한 Car Thing을 선택하세요.", AutoSize = true, Location = new Point(16, 18) });
            var devicesBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Location = new Point(16, 49), Width = 438 };
            foreach (var choice in choices) devicesBox.Items.Add(choice);
            if (choices.Count > 0) devicesBox.SelectedIndex = Math.Max(0, choices.FindIndex(d => d.Address == address));
            var ok = new Button { Text = "연결 대기", DialogResult = DialogResult.OK, Location = new Point(252, 99), Width = 100, Enabled = choices.Count > 0 };
            var cancel = new Button { Text = "취소", DialogResult = DialogResult.Cancel, Location = new Point(362, 99), Width = 90 };
            dialog.Controls.AddRange([devicesBox, ok, cancel]);
            dialog.AcceptButton = ok;
            dialog.CancelButton = cancel;
            deviceDialog = dialog;
            DeviceChoice? selectedChoice = null;
            var selectedResult = new TaskCompletionSource<DialogResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // A modeless window keeps the tray's Exit command accessible.
            ok.Click += (_, _) => { selectedChoice = devicesBox.SelectedItem as DeviceChoice; dialog.DialogResult = DialogResult.OK; dialog.Close(); };
            cancel.Click += (_, _) => { dialog.DialogResult = DialogResult.Cancel; dialog.Close(); };
            dialog.FormClosed += (_, _) => selectedResult.TrySetResult(dialog.DialogResult);
            dialog.Show();
            dialog.Activate();
            var result = await selectedResult.Task;
            deviceDialog = null;
            if (closing) return;
            if (result == DialogResult.OK && selectedChoice is DeviceChoice selected)
            {
                string path = Path.Combine(DataDirectory, "settings.json");
                File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(new Settings(selected.Address)));
                File.Move(path + ".tmp", path, true);
                address = selected.Address;
                await StopBridgeAsync();
                if (!closing) StartBridge();
            }
            else if (bridge == null) SetStatus("장치 선택 필요");
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        catch (Exception ex) { if (!closing) ShowError(ex); }
        finally { if (!closing) SetBusy(false); }
    }

    private async Task RestartAsync()
    {
        if (busy || closing) return;
        if (address == null) { await SelectDeviceAsync(); return; }
        SetBusy(true);
        try
        {
            await StopBridgeAsync();
            if (!closing) StartBridge();
        }
        catch (Exception ex) { if (!closing) ShowError(ex); }
        finally { if (!closing) SetBusy(false); }
    }

    private void StartBridge()
    {
        stop = new CancellationTokenSource();
        CancellationToken token = stop.Token;
        string target = address!;
        SetStatus("연결 준비 중");
        bridge = Task.Run(() => Program.RunAsync(["--bridge", target], token,
            Path.Combine(DataDirectory, "logs", "captures"), SetStatus));
    }

    private async Task StopBridgeAsync()
    {
        var currentStop = stop;
        var currentBridge = bridge;
        await WaitForStopAsync(currentStop, currentBridge, TimeSpan.FromSeconds(3));
        if (ReferenceEquals(stop, currentStop))
        {
            currentStop?.Dispose();
            stop = null;
            bridge = null;
        }
    }

    internal static async Task WaitForStopAsync(CancellationTokenSource? cancellation, Task? running, TimeSpan timeout)
    {
        // Include cancellation callbacks in the deadline; native socket shutdown can stall.
        Task cancel = cancellation?.CancelAsync() ?? Task.CompletedTask;
        await Task.WhenAll(cancel, running ?? Task.CompletedTask).WaitAsync(timeout);
    }

    private async Task CloseAsync()
    {
        if (closing) return;
        closing = true;
        SetBusy(true);
        lifetime.Cancel();
        deviceDialog?.Close();
        try { await StopBridgeAsync(); }
        catch (TimeoutException) { Console.Error.WriteLine("Shutdown: connection cleanup exceeded 3 seconds; exiting."); }
        catch (Exception ex) { Console.Error.WriteLine(ex); }
        finally { icon.Visible = false; ExitThread(); }
    }

    private void ShowError(Exception ex)
    {
        Console.Error.WriteLine(ex);
        SetStatus("오류 · " + ex.Message);
        icon.ShowBalloonTip(5000, "carthing-connector", ex.Message, ToolTipIcon.Error);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Application.Idle -= Initialize;
            lifetime.Dispose();
            icon.Visible = false;
            icon.Dispose();
            menu.Dispose();
            dispatcher.Dispose();
            Console.SetOut(originalOutput);
            Console.SetError(originalError);
            log.Dispose();
        }
        base.Dispose(disposing);
    }

    private sealed record Settings(string DeviceAddress);

    internal static void ShutdownSelfTest()
    {
        foreach (bool stalled in new[] { false, true })
        {
            using var tray = new ConnectorTray(initialize: false);
            tray.SetBusy(true);
            tray.deviceDialog = new Form();
            var dialog = tray.deviceDialog;
            tray.stop = new CancellationTokenSource();
            tray.bridge = stalled ? new TaskCompletionSource<int>().Task : Task.FromResult(1);
            if (!tray.exitItem.Enabled) throw new Exception("Exit disabled while busy.");
            var elapsed = Stopwatch.StartNew();
            tray.dispatcher.BeginInvoke(() => tray.exitItem.PerformClick());
            Application.Run(tray);
            if (!tray.closing || !dialog.IsDisposed || elapsed.Elapsed > TimeSpan.FromSeconds(5))
                throw new Exception("Tray shutdown did not close the dialog within its deadline.");
            tray.originalOutput.WriteLine($"PASS: tray exit while busy, stalled={stalled}, elapsed={elapsed.ElapsedMilliseconds}ms");
        }
        using var cancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = cancellation.Token.Register(() => release.Task.GetAwaiter().GetResult());
        try
        {
            bool timedOut = false;
            try { WaitForStopAsync(cancellation, Task.CompletedTask, TimeSpan.FromMilliseconds(100)).GetAwaiter().GetResult(); }
            catch (TimeoutException) { timedOut = true; }
            if (!timedOut) throw new Exception("Blocking cancellation callback bypassed shutdown deadline.");
        }
        finally { release.TrySetResult(); }
        Console.WriteLine("PASS: blocking cancellation callback deadline.");
    }
    private sealed record DeviceChoice(string Address, string Name)
    {
        public override string ToString() => $"{Name} ({Address})";
    }
}
