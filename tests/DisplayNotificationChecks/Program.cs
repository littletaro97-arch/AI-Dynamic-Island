using System.Reflection;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoyoClawCompanion;
using YoyoClawCompanion.Services;

internal static class Program
{
    private sealed class TestApp : App
    {
        public TestApp() => typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        protected override void OnStartup(StartupEventArgs e) { }
    }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    [System.Runtime.InteropServices.DllImport("user32.dll")]
    static extern bool IsWindowVisible(IntPtr window);
    static void Assert(bool ok, string name) { if (!ok) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Call(object target, string name, params object[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    static string Row(object payload) => JsonSerializer.Serialize(new { timestamp = DateTimeOffset.UtcNow, type = "response_item", payload });
    static string Ask(string id, bool async = true) => Row(new { type = "function_call", name = async ? "request_user_input_async" : "request_user_input", call_id = id,
        arguments = JsonSerializer.Serialize(new { questions = new[] { new { title = "是否采用这个方案？" } } }) });
    static string Reply(string id) => Row(new { type = "message", role = "user", content = new[] { new { type = "input_text", text = "<send_user_message_question_reply>\n" + JsonSerializer.Serialize(new[] { new { questionItemId = JsonSerializer.Serialize(new object[] { "request_user_input_async", id, 0 }), answer = "是" } }) + "\n</send_user_message_question_reply>" } } });

    [STAThread] static void Main()
    {
        var horizontal = new DisplayInfo("main", "display1", true, new Rect(0, 0, 2560, 1600));
        var portrait = new DisplayInfo("secondary", "display2", false, new Rect(-1200, -500, 1200, 1920));
        Assert(portrait.IsPortrait && !horizontal.IsPortrait, "Windows bounds identify portrait and landscape");
        foreach (var screen in new[] { horizontal, portrait, portrait with { Bounds = new Rect(2560, -800, 1920, 1200) } })
        foreach (var preset in new[] { "topLeft", "topCenter", "topRight", "bottomLeft", "bottomCenter", "bottomRight" })
        foreach (var size in new[] { new Size(224, 48), new Size(448, 96) })
        {
            var point = DisplayPlacement.Target(screen.Bounds, size, new() { Preset = preset });
            Assert(screen.Bounds.Contains(new Rect(point, size)), "six anchors remain visible including negative coordinates and DPI " + preset);
            Assert(Math.Abs(point.Y - (preset.StartsWith("bottom") ? screen.Bounds.Bottom - size.Height - 8 : screen.Bounds.Top + 8)) < .01, "anchor vertical edge " + preset);
        }
        Assert(DisplayPlacement.Resolve([horizontal], portrait.Id) == horizontal, "disconnected secondary resolves to primary");
        Assert(DisplayPlacement.Resolve([horizontal, portrait], portrait.Id) == portrait, "reconnected preferred secondary restored");
        var settings = new IslandSettings { PrimaryDisplayPosition = new() { Preset = "bottomRight" }, PreferredDisplayId = portrait.Id,
            SecondaryDisplayPositions = new() { [portrait.Id] = new() { Preset = "topLeft" } } };
        var restored = JsonSerializer.Deserialize<IslandSettings>(JsonSerializer.Serialize(settings))!;
        Assert(restored.PrimaryDisplayPosition!.Preset == "bottomRight" && restored.SecondaryDisplayPositions[portrait.Id].Preset == "topLeft" && restored.PreferredDisplayId == portrait.Id, "independent display positions roundtrip");
        var custom = DisplayPlacement.Target(portrait.Bounds, new Size(448, 96), new() { Preset = "custom", OffsetX = double.NaN, OffsetY = 99999 });
        Assert(portrait.Bounds.Contains(new Rect(custom, new Size(448, 96))), "corrupt or rotated custom offsets remain on screen");

        var reader = new CodexDecisionReader();
        reader.Apply([Ask("a")], "session");
        Assert(reader.Current?.Id == "session|a" && reader.Current.Prompt.Contains("方案"), "async question exposes decision");
        reader.Apply([Row(new { type = "function_call_output", call_id = "a", output = "{\"accepted\":true}" })], "session");
        Assert(reader.Current is not null, "delivery acknowledgement does not pretend user answered");
        reader.Apply([Reply("other")], "session"); Assert(reader.Current is not null, "unrelated answer preserves pending question");
        reader.Apply([Reply("a")], "session"); Assert(reader.Current is null, "actual async answer clears decision");
        reader.Apply([Ask("b", false), Row(new { type = "function_call_output", call_id = "b", output = "{\"answers\":{}}" })], "session");
        Assert(reader.Current is null, "blocking question result clears decision");
        reader.Apply([Row(new { type = "function_call", name = "exec", call_id = "quoted", arguments = "request_user_input_async is mentioned in code" })], "session");
        Assert(reader.Current is null, "quoted tool name cannot create a decision");
        var temp = Path.Combine(Path.GetTempPath(), "IslandDecisionChecks-" + Guid.NewGuid().ToString("N")); Directory.CreateDirectory(temp);
        try
        {
            var file = Path.Combine(temp, "session.jsonl");
            using var service = new CodexStatusService();
            CodexDecision? Read()
            {
                typeof(CodexStatusService).GetField("_candidateFiles", Flags)!.SetValue(service, new[] { new FileInfo(file) });
                return (CodexDecision?)typeof(CodexStatusService).GetMethod("ReadDecisionState", Flags)!.Invoke(service, null);
            }
            File.WriteAllLines(file, [Ask("synchronous", false), Row(new { type = "function_call_output", call_id = "synchronous", output = "{\"answers\":{}}" })]);
            Assert(Read() is null, "first bounded scan sees blocking question result");
            File.AppendAllLines(file, [Ask("incremental")]); Assert(Read() is not null, "incremental scan detects new question");
            File.AppendAllLines(file, [Reply("incremental")]); Assert(Read() is null, "incremental scan consumes corresponding answer");
        }
        finally { File.Delete(Path.Combine(temp, "session.jsonl")); Directory.Delete(temp); }

        var tracker = new SystemNotificationTracker(); tracker.Reset();
        Assert(NotificationAppLauncher.IsValidAppId("OpenAI.Codex_2p2nqsd0c76g0!App"), "packaged notification app identity accepted");
        Assert(NotificationAppLauncher.IsValidAppId("com.squirrel.WorkBuddy.WorkBuddy"), "desktop notification app identity accepted");
        Assert(NotificationAppLauncher.IsValidAppId(@"E:\Apps\Weixin\Weixin.exe"), "registered path style desktop identity can be resolved");
        foreach (var invalid in new[] { "", "https://example.com", "..\\app.exe", "id\nother", new string('x', 129), @"C:\Apps\..\app.exe", @"C:\Apps\app.exe:evil", @"C:\Apps\app.exe --login" })
            Assert(!NotificationAppLauncher.IsValidAppId(invalid) && !NotificationAppLauncher.TryOpen(invalid), "invalid app identity cannot become shell target");
        NotificationAppLauncher.AppWindow Candidate(int handle, int width, int height, long started = 10,
            bool visible = false, bool owned = false, bool tool = false, string kind = "Chrome_WidgetWin_1", bool title = true, bool exact = false)
            => new(new IntPtr(handle), handle, exact, visible, owned, tool, width, height, title, kind, started);
        var trayMain = Candidate(1, 1549, 925);
        var newerLogin = Candidate(2, 800, 600, 20);
        Assert(NotificationAppLauncher.SelectWindow([newerLogin, trayMain]) == trayMain, "hidden existing session beats newer login process");
        Assert(NotificationAppLauncher.SelectWindow([Candidate(3, 720, 640), trayMain]) == trayMain, "main window beats smaller auxiliary window in same session");
        Assert(NotificationAppLauncher.SelectWindow([Candidate(4, 800, 600, kind: "Electron_NotifyIconHostWindow"), Candidate(5, 32, 36), Candidate(6, 800, 600, tool: true), Candidate(7, 800, 600, owned: true), Candidate(8, 800, 600, title: false)]) is null, "tray host helper owned and tiny windows never activated");
        var visibleSession = Candidate(9, 800, 600, 20, visible: true);
        Assert(NotificationAppLauncher.SelectWindow([trayMain, visibleSession]) == visibleSession, "visible session preferred to hidden session");
        var exactSession = Candidate(10, 800, 600, 20, exact: true);
        Assert(NotificationAppLauncher.SelectWindow([trayMain, exactSession]) == exactSession, "window app identity preferred to executable fallback");
        Assert(NotificationAppLauncher.SelectWindow([trayMain with { Enabled = false }, newerLogin with { Hung = true }]) is null, "disabled or hung windows never activated");
        var old = new SystemToast("old", "Mail", "历史消息", "正文", DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert(tracker.Accept([old]).Count == 0, "enabling notifications does not replay Action Center history");
        var fresh = old with { Id = "new", Title = "新消息", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
        Assert(tracker.Accept([old, fresh]).Single().Id == "new", "new notification delivered");
        Assert(tracker.Accept([old, fresh]).Count == 0, "unchanged notification deduplicates");
        Assert(tracker.Accept([old, fresh with { Body = "更新内容" }]).Count == 1, "updated notification content delivered");
        Assert(tracker.Accept([old]).Count == 0, "dismissal never emits another notice");
        tracker.Reset(); Assert(tracker.Accept([fresh]).Count == 0, "re-enable creates a fresh baseline");

        var app = new TestApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        typeof(App).GetMethod("InitializeComponent", Flags)?.Invoke(app, null);
        var hiddenApp = new Window { Title = "Hidden framework fixture", Width = 800, Height = 600 };
        var hiddenHandle = new System.Windows.Interop.WindowInteropHelper(hiddenApp).EnsureHandle();
        Assert(!IsWindowVisible(hiddenHandle), "hidden framework fixture starts hidden");
        Assert(NotificationAppLauncher.ActivateExistingWindow(hiddenHandle) == NotificationOpenResult.RunningButUnavailable
            && !IsWindowVisible(hiddenHandle), "activation never forcibly exposes hidden framework window");
        hiddenApp.Close();
        if (Environment.GetEnvironmentVariable("ISLAND_NATIVE_NOTIFICATION_CHECK") == "1")
        {
            foreach (var identity in new[] { "QQ", @"E:\D-diskExpansionCabin\Weixin\Weixin.exe" })
                Assert(NotificationAppLauncher.TryResolveRegisteredTarget(identity, out var target) && File.Exists(target), "real registered desktop identity resolves without launching " + identity);
            Assert(!NotificationAppLauncher.TryResolveRegisteredTarget(@"C:\NotRegistered\arbitrary.exe", out _), "arbitrary absolute exe is rejected by registration check");
            if (Environment.GetEnvironmentVariable("ISLAND_ACTIVATION_SMOKE") == "1")
            {
                foreach (var (identity, processName) in new[] { ("QQ", "QQ"), (@"E:\D-diskExpansionCabin\Weixin\Weixin.exe", "Weixin") })
                {
                    int[] Pids() => System.Diagnostics.Process.GetProcessesByName(processName).Select(p => { using (p) return p.Id; }).Order().ToArray();
                    var before = Pids();
                    Assert(before.Length > 0, "live activation requires existing app " + processName);
                    var activation = NotificationAppLauncher.OpenAsync(identity).GetAwaiter().GetResult();
                    Assert(activation != NotificationOpenResult.Failed, "registered running app activation handled " + processName);
                    System.Threading.Thread.Sleep(750);
                    Assert(!Pids().Except(before).Any(), "activation creates no additional process " + processName);
                    Console.WriteLine("ACTIVATION " + processName + " " + activation);
                }
            }
            var launch = NotificationAppLauncher.TryOpenAsync("AI.Dynamic.Island.Nonexistent.Test." + Guid.NewGuid().ToString("N"));
            var launchFrame = new DispatcherFrame(); var launchDeadline = DateTime.UtcNow.AddSeconds(15);
            var launchTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(30) };
            launchTick.Tick += (_, _) => { if (launch.IsCompleted || DateTime.UtcNow >= launchDeadline) { launchTick.Stop(); launchFrame.Continue = false; } };
            launchTick.Start(); Dispatcher.PushFrame(launchFrame);
            Assert(launch.IsCompleted && !launch.GetAwaiter().GetResult(), "unregistered app fails async STA Shell resolution without an error popup");
            using var listener = new SystemNotificationService();
            var count = 0; listener.Received += (_, _) => count++;
            var ready = listener.StartAsync(false);
            var frame = new DispatcherFrame(); var deadline = DateTime.UtcNow.AddSeconds(10);
            var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            tick.Tick += (_, _) => { if (ready.IsCompleted || DateTime.UtcNow >= deadline) { tick.Stop(); frame.Continue = false; } };
            tick.Start(); Dispatcher.PushFrame(frame); ready.GetAwaiter().GetResult();
            Assert(listener.Status.StartsWith("已连接 Windows 通知"), "production listener connects without requesting permissions");
            Assert(listener.LastSnapshotCount > 0 && count == 0, "production listener extracts real notifications without history replay");
            Console.WriteLine("NATIVE baseline count " + listener.LastSnapshotCount);
        }
        var main = new MainWindow();
        typeof(MainWindow).GetField("_settings", Flags)!.SetValue(main, new IslandSettings());
        var notificationButton = (Button)main.FindName("NotificationOpenButton");
        var noticeText = (TextBlock)main.FindName("RecentResultText");
        typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.SetValue(main, fresh);
        Call(main, "UpdateRecentNotice");
        Assert(notificationButton.Visibility == Visibility.Visible && noticeText.Text == fresh.Text, "system notification content provides app launch hit target");
        notificationButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(noticeText.Text.Contains("无法打开来源应用") && typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.GetValue(main) is not null, "launch failure leaves toast visible with feedback");
        typeof(MainWindow).GetField("_activeConfirmationNotice", Flags)!.SetValue(main, "Codex 等待回答");
        Call(main, "UpdateRecentNotice");
        Assert(notificationButton.Visibility == Visibility.Collapsed && noticeText.Text == "Codex 等待回答", "priority confirmation never opens the interrupted toast app");
        typeof(MainWindow).GetField("_activeConfirmationNotice", Flags)!.SetValue(main, null);
        typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.SetValue(main, null);
        Call(main, "UpdateRecentNotice");
        Assert(notificationButton.Visibility == Visibility.Collapsed, "ordinary result has no notification click overlay");
        void SetMain(string field, object? value) => typeof(MainWindow).GetField(field, Flags)!.SetValue(main, value);
        bool ReverseHidden() => (bool)typeof(MainWindow).GetField("_reverseHoverHidden", Flags)!.GetValue(main)!;
        main.CurrentSettings.EnableReverseHover = true;
        main.CurrentSettings.EnableHoverExpansion = false;
        // Connect the real visual tree off screen, without starting live providers,
        // saved-setting writes or update checks from the application's Loaded hook.
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main,
            typeof(MainWindow).GetMethod("OnLoaded", Flags | BindingFlags.DeclaredOnly)!);
        main.ShowActivated = false; main.ShowInTaskbar = false; main.Topmost = false;
        main.Left = -10000; main.Top = -10000; main.Show();
        var island = (Border)main.FindName("Island");
        var mainRoot = (FrameworkElement)main.Content;
        mainRoot.Measure(new Size(550, 500)); mainRoot.Arrange(new Rect(0, 0, 550, 500)); mainRoot.UpdateLayout();
        foreach (var provider in new[] { "Codex", "WorkBuddy", "YOYO Claw" })
        {
            SetMain("_expanded", true); SetMain("_notificationHoldActive", false);
            SetMain("_activeCompletionNotice", provider + " 完成了任务");
            Call(main, "HideIslandForReverseHover");
            Assert(!ReverseHidden(), "expired expanded completion never reverse hides " + provider);
            Call(main, "CollapseHandle_Click", main, new RoutedEventArgs(Button.ClickEvent));
            Call(main, "HideIslandForReverseHover");
            Assert(!ReverseHidden(), "manual collapse animation never hides whole island " + provider);
            Call(main, "CompleteCollapseImmediately"); Call(main, "HideIslandForReverseHover");
            Assert(!ReverseHidden(), "manual collapse stays visible until pointer exits " + provider);
            Call(main, "Island_MouseLeave", main, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0));
        }
        Call(main, "HideIslandForReverseHover");
        Assert(ReverseHidden(), "settled collapsed island retains reverse hover pass through");
        Call(main, "ExpandIsland", true);
        Assert(!ReverseHidden() && island.Visibility == Visibility.Visible, "forced expansion cancels in flight reverse fade");
        // Pump past the obsolete fade; it must not hide the expanded notification.
        var reverseFrame = new DispatcherFrame(); var reverseTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        reverseTick.Tick += (_, _) => { reverseTick.Stop(); reverseFrame.Continue = false; }; reverseTick.Start(); Dispatcher.PushFrame(reverseFrame);
        Assert(island.Visibility == Visibility.Visible && !ReverseHidden(), "obsolete reverse fade cannot hide later expansion");
        SetMain("_notificationHoldActive", false);
        main.ApplySettings(main.CurrentSettings, persist: false);
        Assert((bool)typeof(MainWindow).GetField("_expanded", Flags)!.GetValue(main)!, "reapplying reverse settings preserves an expanded notice");
        Call(main, "CompleteCollapseImmediately"); main.CurrentSettings.EnableReverseHover = false;
        var window = (SettingsWindow)Activator.CreateInstance(typeof(SettingsWindow), Flags, null, [main], null)!;
        var root = (FrameworkElement)window.Content;
        foreach (var width in new[] { 780d, 1200d })
        {
            root.Measure(new Size(width, 850)); root.Arrange(new Rect(0, 0, width, 850)); root.UpdateLayout();
            var path = (TextBlock)window.FindName("DeepSeekInstallStatus");
            var row = (Grid)path.Parent;
            var label = row.Children.OfType<TextBlock>().First(t => t != path);
            var bounds = label.TransformToAncestor(row).TransformBounds(new Rect(label.RenderSize));
            Assert(label.TextTrimming == TextTrimming.CharacterEllipsis && bounds.Right <= path.TranslatePoint(new Point(), row).X, "long app name never overlaps unchanged path column " + width);
        }
        var role = (TextBlock)window.FindName("DisplayRoleText");
        Assert(role.Text.StartsWith("主屏幕"), "settings initially shows primary even when a secondary is connected");
        var displays = NativeWindow.GetDisplays();
        Console.WriteLine("DISPLAYS " + string.Join(", ", displays.Select(d => $"{d.Label} {d.Bounds.Width}x{d.Bounds.Height}")));
        var secondary = displays.FirstOrDefault(d => !d.IsPrimary);
        if (secondary is not null)
        {
            typeof(SettingsWindow).GetField("_previewDisplayId", Flags)!.SetValue(window, secondary.Id);
            Call(window, "RefreshDisplayPreview", false);
            Assert(role.Text.StartsWith("副屏幕") && ((FrameworkElement)window.FindName("PositionPreviewSurface")).Height > 400 == secondary.IsPortrait, "secondary preview uses Windows orientation");
            typeof(SettingsWindow).GetField("_previewDisplayId", Flags)!.SetValue(window, DisplayPlacement.PrimaryId);
            Call(window, "RefreshDisplayPreview", true);
            typeof(SettingsWindow).GetField("_previewDisplayId", Flags)!.SetValue(window, secondary.Id);
            Call(window, "RefreshDisplayPreview", true);
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
            Assert(role.Text.StartsWith("副屏幕") && ((FrameworkElement)window.FindName("PositionPreviewSurface")).Opacity == 1, "rapid screen reversal finishes at latest selection");
        }
        var capture = Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(capture))
        {
            Directory.CreateDirectory(capture);
            root.Measure(new Size(1000, 850)); root.Arrange(new Rect(0, 0, 1000, 850)); root.UpdateLayout();
            var image = new RenderTargetBitmap(1000, 850, 96, 96, PixelFormats.Pbgra32); image.Render(root);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(image));
            using var file = File.Create(Path.Combine(capture, "display-settings.png")); encoder.Save(file);
        }
        window.Close(); main.Close(); app.Shutdown();
    }
}
