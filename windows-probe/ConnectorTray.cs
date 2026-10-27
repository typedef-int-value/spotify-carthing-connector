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
    private readonly ToolStripMenuItem modeItem = new("화면 모드");
    private readonly ToolStripMenuItem externalModeItem = new("외부 미디어");
    private readonly ToolStripMenuItem spotifyModeItem = new("Spotify");
    private readonly bool? requestedSpotifyMode;
    private readonly string settingsPath;
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

    public ConnectorTray(bool initialize = true, bool? spotifyMode = null, string? settingsPath = null)
    {
        requestedSpotifyMode = spotifyMode;
        this.settingsPath = settingsPath ?? Path.Combine(DataDirectory, "settings.json");
        Directory.CreateDirectory(DataDirectory);
        Directory.CreateDirectory(Path.Combine(DataDirectory, "logs"));
        log = new StreamWriter(Path.Combine(DataDirectory, "logs", $"app-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}.log")) { AutoFlush = true };
        var writer = TextWriter.Synchronized(log);
        Console.SetOut(writer);
        Console.SetError(writer);
        _ = dispatcher.Handle;
        icon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "carthing-connector · 시작 중",
            ContextMenuStrip = menu,
            Visible = true
        };
        menu.Items.Add(new ToolStripMenuItem("carthing-connector") { Enabled = false });
        menu.Items.Add(statusItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(deviceItem);
        menu.Items.Add(restartItem);
        modeItem.DropDownItems.AddRange([externalModeItem, spotifyModeItem]);
        menu.Items.Add(modeItem);
        UpdateModeMenu();
        menu.Items.Add("로그 폴더 열기", null, (_, _) =>
        {
            try { Process.Start(new ProcessStartInfo(Path.Combine(DataDirectory, "logs")) { UseShellExecute = true }); }
            catch (Exception ex) { ShowError(ex); }
        });
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(exitItem);
        deviceItem.Click += async (_, _) => await SelectDeviceAsync();
        restartItem.Click += async (_, _) => await RestartAsync();
        externalModeItem.Click += async (_, _) => await ChangeModeAsync(false);
        spotifyModeItem.Click += async (_, _) => await ChangeModeAsync(true);
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
            if (File.Exists(settingsPath))
            {
                var saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(settingsPath));
                Program.SpotifyUiExperiment = saved?.SpotifyMode ?? false;
                if (saved != null && saved.DeviceAddress?.Length == 12 &&
                    ulong.TryParse(saved.DeviceAddress, System.Globalization.NumberStyles.HexNumber, null, out _))
                    address = saved.DeviceAddress;
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"Settings: {ex.Message}"); }
        if (requestedSpotifyMode.HasValue) Program.SpotifyUiExperiment = requestedSpotifyMode.Value;
        UpdateModeMenu();
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
        deviceItem.Enabled = restartItem.Enabled = modeItem.Enabled = !value && !closing;
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
                SaveSettings(selected.Address, Program.SpotifyUiExperiment);
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
        Console.WriteLine($"Screen mode: {(Program.SpotifyUiExperiment ? "Spotify" : "External media")}");
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

    private void SaveSettings(string? deviceAddress, bool spotifyMode)
    {
        File.WriteAllText(settingsPath + ".tmp", JsonSerializer.Serialize(new Settings(deviceAddress, spotifyMode)));
        File.Move(settingsPath + ".tmp", settingsPath, true);
    }

    private void UpdateModeMenu()
    {
        spotifyModeItem.Checked = Program.SpotifyUiExperiment;
        externalModeItem.Checked = !Program.SpotifyUiExperiment;
    }

    private async Task ChangeModeAsync(bool spotifyMode)
    {
        if (busy || closing || spotifyMode == Program.SpotifyUiExperiment) return;
        SetBusy(true);
        try
        {
            // Do not change the mode while an old connection is still running.
            await StopBridgeAsync();
            if (closing) return;
            SaveSettings(address, spotifyMode);
            Program.SpotifyUiExperiment = spotifyMode;
            UpdateModeMenu();
            if (address != null) StartBridge();
            else SetStatus("장치 선택 필요");
        }
        catch (Exception ex) { if (!closing) ShowError(ex); }
        finally { if (!closing) SetBusy(false); }
    }

    private sealed record Settings(string? DeviceAddress, bool SpotifyMode = false);

    internal static void ShutdownSelfTest()
    {
        string temporarySettings = Path.Combine(Path.GetTempPath(), $"carthing-settings-{Guid.NewGuid():N}.json");
        bool originalMode = Program.SpotifyUiExperiment;
        try
        {
            var legacy = JsonSerializer.Deserialize<Settings>("{\"DeviceAddress\":\"30E3D604D71F\"}");
            if (legacy == null || legacy.SpotifyMode) throw new Exception("Legacy settings mode changed.");
            Program.SpotifyUiExperiment = false;
            using var tray = new ConnectorTray(initialize: false, settingsPath: temporarySettings);
            foreach (bool mode in new[] { true, false })
            {
                tray.stop = new CancellationTokenSource();
                tray.bridge = Task.FromResult(0);
                bool cancelledBeforeChange = false;
                using var modeRegistration = tray.stop.Token.Register(() =>
                    cancelledBeforeChange = Program.SpotifyUiExperiment != mode);
                Task change = tray.ChangeModeAsync(mode);
                var deadline = Stopwatch.StartNew();
                while (!change.IsCompleted && deadline.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Application.DoEvents();
                    Thread.Sleep(1);
                }
                if (!change.IsCompleted) throw new Exception("Mode switch timed out.");
                change.GetAwaiter().GetResult();
                var saved = JsonSerializer.Deserialize<Settings>(File.ReadAllText(temporarySettings));
                if (!cancelledBeforeChange || saved?.SpotifyMode != mode ||
                    tray.spotifyModeItem.Checked != mode || tray.externalModeItem.Checked == mode)
                    throw new Exception("Mode switch or persistence failed.");
            }
            tray.originalOutput.WriteLine("PASS: both tray modes, connection cancellation, settings round trip and legacy settings.");
        }
        finally
        {
            Program.SpotifyUiExperiment = originalMode;
            File.Delete(temporarySettings);
            File.Delete(temporarySettings + ".tmp");
        }
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
