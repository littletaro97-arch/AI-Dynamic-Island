using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using YoyoClawCompanion;
using YoyoClawCompanion.Services;

class Program
{
    // WPF schedules OnStartup when the dispatcher first pumps, even without Run().
    // Do not enter the product's single-instance/IPC/startup-registration path.
    sealed class DiagnosticApp : App
    {
        public DiagnosticApp() => typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        protected override void OnStartup(StartupEventArgs e) { }
    }
    const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static object? Call(object target, string name, params object?[] args) => target.GetType().GetMethod(name, Flags)!.Invoke(target, args);
    static void Set(object target, string name, object? value) => target.GetType().GetField(name, Flags)!.SetValue(target, value);
    static void Property(object target, string name, object value) => target.GetType().GetProperty(name, Flags)!.SetValue(target, value);
    static void Assert(bool value, string name) { if (!value) throw new Exception(name); Console.WriteLine("PASS " + name); }
    static void Pump(int milliseconds)
    {
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start(); Dispatcher.PushFrame(frame);
    }

    [STAThread] static void Main(string[] args)
    {
        if (args.FirstOrDefault() == "--child") { Child(args[1]); return; }
        if (args.FirstOrDefault() == "--benchmark") { Benchmark(args[1], args[2]); return; }
        if (args.FirstOrDefault() == "--quota") { Quota(args[1]); return; }
        if (args.FirstOrDefault() == "--monitor") { Monitor(args[1]); return; }
        if (args.FirstOrDefault() == "--native")
        {
            using var process = Process.GetProcessById(int.Parse(args[1])); process.Refresh();
            File.WriteAllText(args[2], JsonSerializer.Serialize(new { pid = process.Id, workingSetMiB = process.WorkingSet64 / 1048576d,
                privateMiB = process.PrivateMemorySize64 / 1048576d, regions = NativeMemoryProbe.Capture(process) }, new JsonSerializerOptions { WriteIndented = true }));
            return;
        }
        Checks();
    }

    static void Child(string mode)
    {
        if (mode == "leaf") { Thread.Sleep(60000); return; }
        if (mode == "tree")
        {
            // Wait for the parent to assign our private job before creating a child.
            Console.ReadLine();
            using var child = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
                { UseShellExecute = false, CreateNoWindow = true, ArgumentList = { "--child", "leaf" } })!;
            Console.WriteLine(child.Id); Console.Out.Flush();
            Thread.Sleep(60000); return;
        }
        while (Console.ReadLine() is not null) { }
        Console.WriteLine("EOF");
    }

    static void Checks()
    {
        var userDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "YoyoClawCompanion");
        var userSettingsPath = Path.Combine(userDirectory, "settings.json");
        var userPresetsPath = Path.Combine(userDirectory, "presets.json");
        var userSettingsBefore = File.Exists(userSettingsPath) ? File.ReadAllBytes(userSettingsPath) : null;
        var userPresetsBefore = File.Exists(userPresetsPath) ? File.ReadAllBytes(userPresetsPath) : null;
        var directory = Path.Combine(AppContext.BaseDirectory, "fixtures", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        foreach (var mode in new[] { "eof", "tree" })
        {
            using var process = Process.Start(new ProcessStartInfo(Environment.ProcessPath!)
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, ArgumentList = { "--child", mode }
            })!;
            using var scope = OwnedProcessScope.Attach(process);
            int? childId = null;
            if (mode == "tree")
            {
                process.StandardInput.WriteLine("go"); process.StandardInput.Flush();
                childId = int.Parse(process.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult()!);
            }
            OwnedProcessScope.StopAsync(process, scope, TimeSpan.FromMilliseconds(300)).GetAwaiter().GetResult();
            Assert(process.HasExited, mode + " root is reaped");
            if (mode == "eof") Assert(process.ExitCode == 0 && process.StandardOutput.ReadToEnd().Contains("EOF"), "reader exits cooperatively on EOF");
            if (childId is int id)
            {
                var exited = false;
                try { using var child = Process.GetProcessById(id); exited = child.WaitForExit(3000); }
                catch (ArgumentException) { exited = true; }
                Assert(exited, "timeout reaps only owned child tree");
            }
        }

        var root = Path.Combine(directory, "sessions"); Directory.CreateDirectory(root);
        var path = Path.Combine(root, "a.jsonl"); File.WriteAllText(path, "one\n");
        using var index = new RecentSessionIndex(root, "*.jsonl");
        var clock = DateTime.UtcNow;
        Assert(index.Find(4, clock).Single().Name == "a.jsonl" && index.IsWatching, "recent index initializes watcher");
        for (var i = 0; i < 20; i++) index.Find(4, clock.AddSeconds(i));
        Assert(index.FullScanCount == 1, "unchanged polls do not enumerate history");
        File.WriteAllText(Path.Combine(root, "b.jsonl"), "two\n");
        var watch = Stopwatch.StartNew();
        while (index.Find(4, clock.AddSeconds(21)).Length < 2 && watch.ElapsedMilliseconds < 3000) Thread.Sleep(10);
        Assert(index.Find(4, clock.AddSeconds(22)).Length == 2, "new session discovered without periodic scan");
        File.Move(path, Path.Combine(root, "renamed.jsonl"));
        index.Invalidate();
        Assert(index.Find(4, clock.AddSeconds(23)).Any(file => file.Name == "renamed.jsonl"), "rename repaired by full rescan");
        File.Delete(Path.Combine(root, "b.jsonl"));
        Assert(index.Find(4, clock.AddSeconds(24)).Length == 1, "lost delete event repaired by candidate refresh");
        index.Find(4, clock.AddSeconds(90));
        Assert(index.FullScanCount >= 3, "periodic scan covers missed watcher events");

        var log = Path.Combine(directory, "cursor.jsonl");
        File.WriteAllText(log, "{\"type\":\"start\"}\n", new UTF8Encoding(false));
        var cursor = new JsonLineCursor();
        Assert(cursor.Read(log, 1024).Length == 1 && cursor.NewGeneration, "cursor bootstraps log");
        var append = Encoding.UTF8.GetBytes("{\"text\":\"完成\"}\n");
        using (var stream = new FileStream(log, FileMode.Append)) stream.Write(append, 0, 11);
        cursor.Read(log, 1024); // Includes an incomplete UTF-8 character; no parser state is committed.
        using (var stream = new FileStream(log, FileMode.Append)) stream.Write(append, 11, append.Length - 11);
        Assert(cursor.Read(log, 1024).Single().Contains("完成"), "split UTF-8 record survives append boundary");
        Assert(cursor.Read(log, 1024).Length == 0 && cursor.BytesRead == 0, "unchanged cursor reads no payload");
        File.WriteAllText(log, "{\"new\":true}\n");
        Assert(cursor.Read(log, 1024).Single().Contains("new") && cursor.NewGeneration, "truncate resets parser generation");
        File.WriteAllText(log, "{\"rewrite\":\"longer replacement of the same file\"}\n");
        Assert(cursor.Read(log, 1024).Single().Contains("rewrite") && cursor.NewGeneration, "longer in-place rewrite detected by anchor");
        File.Move(log, log + ".old"); File.WriteAllText(log, "{\"rotate\":true}\n");
        Assert(cursor.Read(log, 1024).Single().Contains("rotate") && cursor.NewGeneration, "file replacement detected by identity");
        File.WriteAllText(log, new string('x', 1024 * 1024) + "\n{\"done\":true}\n");
        cursor.Read(log, 1024 * 1024, ["done"]);
        File.AppendAllText(log, "{\"done\":false}\n");
        Assert(cursor.Read(log, 1024 * 1024, ["done"]).Single().Contains("false") && cursor.BytesRead < 100, "append reads only new bytes after large history");

        var buddyPath = Path.Combine(directory, "buddy.jsonl");
        File.WriteAllText(buddyPath, "{\"type\":\"function_call\",\"callId\":\"p\",\"name\":\"Bash\"}\n");
        using var buddy = new WorkBuddyStatusService();
        Assert(((WorkBuddyStatus)Call(buddy, "ReadSession", new FileInfo(buddyPath), true)!).IsBusy, "pending tool established");
        File.AppendAllText(buddyPath, "{\"type\":\"reasoning\"}\n");
        Assert(((WorkBuddyStatus)Call(buddy, "ReadSession", new FileInfo(buddyPath), true)!).IsBusy, "pending tool retained across incremental reads");
        File.AppendAllText(buddyPath, "{\"type\":\"function_call_result\",\"callId\":\"p\"}\n{\"type\":\"message\",\"role\":\"assistant\",\"status\":\"completed\",\"id\":\"b1\",\"providerData\":{\"usage\":{\"outputTokens\":12}},\"content\":[{\"type\":\"output_text\",\"text\":\"完成\"}]}\n");
        var finished = (WorkBuddyStatus)Call(buddy, "ReadSession", new FileInfo(buddyPath), true)!;
        Assert(!finished.IsBusy && finished.Completions?.Count == 1, "incremental result closes pending tool and emits completion");
        File.AppendAllText(buddyPath, "{\"type\":\"message\",\"role\":\"user\",\"content\":[{\"type\":\"input_text\",\"text\":\"新任务\"}]}\n");
        var resumed = (WorkBuddyStatus)Call(buddy, "ReadSession", new FileInfo(buddyPath), true)!;
        Assert(resumed.IsBusy && resumed.Completions?.Single().Id == "b1", "new task retains completion history while busy");

        var codexRoot = Path.Combine(directory, "codex"); Directory.CreateDirectory(codexRoot);
        var aPath = Path.Combine(codexRoot, "a.jsonl"); var bPath = Path.Combine(codexRoot, "b.jsonl");
        var timestamp = DateTimeOffset.UtcNow;
        var start = $"{{\"timestamp\":\"{timestamp:O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\"}}}}\n";
        File.WriteAllText(aPath, start); File.WriteAllText(bPath, start);
        using var codexService = new CodexStatusService();
        using var codexIndex = new RecentSessionIndex(codexRoot, "*.jsonl");
        Set(codexService, "_sessionsRoot", codexRoot); Set(codexService, "_sessionIndex", codexIndex);
        Assert(codexService.ReadAsync(true, false, true).GetAwaiter().GetResult().ActiveSessions == 2, "parallel sessions both detected busy");
        File.AppendAllText(aPath, $"{{\"timestamp\":\"{timestamp.AddSeconds(1):O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_complete\"}}}}\n{{\"timestamp\":\"{timestamp.AddSeconds(1):O}\",\"type\":\"response_item\",\"payload\":{{\"type\":\"message\",\"role\":\"assistant\",\"phase\":\"final_answer\",\"id\":\"a1\",\"content\":[{{\"type\":\"output_text\",\"text\":\"单项完成\"}}]}}}}\n");
        var partialCompletion = codexService.ReadAsync(true, false, true).GetAwaiter().GetResult();
        Assert(partialCompletion.IsBusy && partialCompletion.ActiveSessions == 1 && partialCompletion.Completions?.Single().Id == "a1", "incremental parallel completion keeps aggregate running state");
        File.AppendAllText(aPath, $"{{\"timestamp\":\"{timestamp.AddSeconds(2):O}\",\"type\":\"event_msg\",\"payload\":{{\"type\":\"task_started\"}}}}\n");
        Assert(codexService.ReadAsync(true, false, true).GetAwaiter().GetResult().ActiveSessions == 2, "next task resumes independently with history retained");
        File.WriteAllText(aPath, "{\"type\":\"new_session\"}\n");
        var truncated = codexService.ReadAsync(true, false, true).GetAwaiter().GetResult();
        Assert(truncated.ActiveSessions == 1 && truncated.Completions?.Count == 0, "truncation clears stale lifecycle and completion state");
        var unchangedTimestamp = File.GetLastWriteTimeUtc(bPath);
        File.WriteAllText(bPath, start.Replace("task_started", "turn_aborted"));
        File.SetLastWriteTimeUtc(bPath, unchangedTimestamp);
        codexIndex.Invalidate(); // Fallback must not trust equal size/timestamp.
        Assert(!codexService.ReadAsync(true, false, true).GetAwaiter().GetResult().IsBusy, "fallback detects replacement with preserved size and timestamp");

        var app = new DiagnosticApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        var main = new MainWindow(); Set(main, "_settings", new IslandSettings());
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, typeof(MainWindow).GetMethod("OnLoaded", Flags | BindingFlags.DeclaredOnly)!);
        Set(main, "_refreshing", true); // Showing controls must never start discovery or save fixture settings.
        var status = new CodexStatus(true, false, true, false, null, 73, 94, null, null, null, null, null, 0, null, 0, 0, null, false);
        Call(main, "SetCodexStateText", status);
        var text = (TextBlock)main.FindName("CodexStateText"); var run = text.Inlines.FirstInline;
        Call(main, "SetCodexStateText", status);
        Assert(ReferenceEquals(run, text.Inlines.FirstInline), "unchanged UI preserves inline identity");
        Call(main, "SetCodexStateText", status with { IsBusy = true, FiveHourRemainingPercent = 72 });
        var displayed = string.Concat(text.Inlines.OfType<System.Windows.Documents.Run>().Select(item => item.Text));
        Assert(ReferenceEquals(run, text.Inlines.FirstInline) && displayed.Contains("执行中") && displayed.Contains("72%"), "changed UI updates existing runs correctly");
        var settingsWindow = new SettingsWindow(main);
        Set(settingsWindow, "_loading", true); settingsWindow.Show(); Pump(100);
        var smoothScroll = (ScrollViewer)settingsWindow.FindName("SettingsScroll");
        Call(settingsWindow, "AnimateSettingsScroll", 360d, 180d, new System.Windows.Media.Animation.CubicEase());
        Pump(75);
        Assert(smoothScroll.VerticalOffset > 0 && smoothScroll.VerticalOffset < 360, "settings scroll advances through intermediate offsets");
        Call(settingsWindow, "AnimateSettingsScroll", 60d, 180d, new System.Windows.Media.Animation.CubicEase());
        Pump(220);
        Assert(Math.Abs(smoothScroll.VerticalOffset - 60) < 1, "reverse scroll ends at latest target");
        Assert(!settingsWindow.HasAnimatedProperties, "completed scroll releases window animation clock");
        Pump(300);
        var cards = new[] { "PositionCard", "AppearanceCard", "ComponentCard", "FeatureCard", "NotificationCard", "UpdateCard" };
        Assert(cards.All(name => ((FrameworkElement)settingsWindow.FindName(name)).CacheMode is null), "idle settings releases temporary raster caches");
        smoothScroll.ScrollToVerticalOffset(300); Pump(50);
        Assert(cards.Any(name => ((FrameworkElement)settingsWindow.FindName(name)).CacheMode is BitmapCache), "visible moving settings cards use bounded raster caches");
        settingsWindow.WindowState = WindowState.Minimized; Pump(50);
        Assert(cards.All(name => ((FrameworkElement)settingsWindow.FindName(name)).CacheMode is null), "minimized settings releases all scroll caches");
        settingsWindow.WindowState = WindowState.Normal; Pump(100);
        for (var slot = 1; slot <= 3; slot++)
        {
            ((TextBlock)settingsWindow.FindName($"Preset{slot}NameText")).Text = new string('测', 80);
            ((TextBlock)settingsWindow.FindName($"Preset{slot}NameClone")).Text = new string('测', 80);
        }
        Call(settingsWindow, "SchedulePresetMarquees"); Pump(100);
        var primary = (TranslateTransform)settingsWindow.FindName("Preset1NameTranslate");
        var clone = (TranslateTransform)settingsWindow.FindName("Preset1NameCloneTranslate");
        Assert(!primary.HasAnimatedProperties, "offscreen preset has no animation clock");
        var scroll = (ScrollViewer)settingsWindow.FindName("SettingsScroll");
        var card = (FrameworkElement)settingsWindow.FindName("PresetCard");
        scroll.ScrollToVerticalOffset(scroll.VerticalOffset + card.TransformToAncestor(scroll).Transform(new Point()).Y);
        Pump(200);
        Assert(primary.HasAnimatedProperties && clone.HasAnimatedProperties && primary.X < 0 && Math.Abs(primary.X - clone.X) < .001, "visible preset scrolls with synchronized native clocks");
        settingsWindow.WindowState = WindowState.Minimized; Pump(80);
        Assert(!primary.HasAnimatedProperties && !clone.HasAnimatedProperties, "minimized settings releases marquee clocks");
        settingsWindow.WindowState = WindowState.Normal; Pump(200);
        Assert(primary.HasAnimatedProperties, "restored visible preset resumes animation");
        scroll.ScrollToTop(); Pump(150);
        Assert(!primary.HasAnimatedProperties && ((UIElement)settingsWindow.FindName("Preset1NameClone")).Visibility == Visibility.Collapsed, "scrolling preset out of viewport stops both copies");
        settingsWindow.Close();
        main.Show(); Pump(100);
        Set(main, "_isBalanceSummary", true);
        ((TextBlock)main.FindName("SummaryText")).Text = new string('测', 80);
        ((TextBlock)main.FindName("SummaryTextClone")).Text = new string('测', 80);
        Call(main, "ScheduleSummaryMarquee"); Pump(140);
        var marquee = (TranslateTransform)main.FindName("SummaryPrimaryTranslate");
        var previousOffset = marquee.X;
        Call(main, "ScheduleSummaryMarquee"); Call(main, "ScheduleSummaryMarquee"); Pump(80);
        Assert(previousOffset < 0 && marquee.X < previousOffset, "unchanged and coalesced refresh preserves marquee phase");
        Call(main, "StopSummaryMarquee");
        Assert(!marquee.HasAnimatedProperties, "stopped marquee releases its clock");
        var island = (Border)main.FindName("Island");
        var shadow = (YoyoClawCompanion.Controls.CachedIslandShadow)main.FindName("IslandShadowLayer");
        shadow.SetEnabled(true, false); Pump(100);
        Assert(island.Effect is null && shadow.CacheMode is BitmapCache, "moving island text no longer shares shadow effect");
        var center = island.TranslatePoint(new Point(island.ActualWidth / 2, island.ActualHeight / 2), shadow);
        Assert(shadow.Clip is not null && !shadow.Clip.FillContains(center), "shadow source cannot fill translucent island interior");
        var clip = shadow.Clip;
        Pump(100);
        Assert(ReferenceEquals(clip, shadow.Clip), "unchanged outline preserves cached geometry");
        var scale = (ScaleTransform)island.RenderTransform;
        scale.ScaleX = scale.ScaleY = .93; island.Width = 300; island.CornerRadius = new CornerRadius(8);
        main.UpdateLayout(); Pump(100);
        center = island.TranslatePoint(new Point(island.ActualWidth / 2, island.ActualHeight / 2), shadow);
        Assert(!ReferenceEquals(clip, shadow.Clip) && !shadow.Clip!.FillContains(center), "resizing and grab scale update shadow hole");
        island.VerticalAlignment = VerticalAlignment.Bottom; island.Margin = new Thickness(0, 0, 0, 16);
        main.UpdateLayout(); Pump(100);
        center = island.TranslatePoint(new Point(island.ActualWidth / 2, island.ActualHeight / 2), shadow);
        Assert(!shadow.Clip!.FillContains(center), "upward layout keeps shadow outside island");
        shadow.SetEnabled(false, true); Pump(70); shadow.SetEnabled(true, true); Pump(260);
        Assert(Math.Abs(shadow.Opacity - 1) < .001 && !shadow.HasAnimatedProperties, "rapid shadow reversal keeps latest enabled state");
        main.Close(); app.Shutdown();
        Assert(userSettingsBefore is null ? !File.Exists(userSettingsPath) : File.ReadAllBytes(userSettingsPath).SequenceEqual(userSettingsBefore), "control checks never overwrite user settings");
        Assert(userPresetsBefore is null ? !File.Exists(userPresetsPath) : File.ReadAllBytes(userPresetsPath).SequenceEqual(userPresetsBefore), "control checks never overwrite user presets");
    }

    // Reflection keeps this harness compatible with the captured pre-change assembly.
    static void Benchmark(string scenario, string output)
    {
        var captureNative = scenario.StartsWith("native-");
        if (captureNative) scenario = scenario[7..];
        var app = new DiagnosticApp { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        typeof(App).GetMethod("InitializeComponent", Flags)?.Invoke(app, null);
        typeof(App).GetProperty("IsPreviewMode", BindingFlags.Static | BindingFlags.NonPublic)!.SetValue(null, true);
        var main = new MainWindow();
        main.Loaded -= (RoutedEventHandler)Delegate.CreateDelegate(typeof(RoutedEventHandler), main, typeof(MainWindow).GetMethod("OnLoaded", Flags | BindingFlags.DeclaredOnly)!);
        Set(main, "_refreshing", true);
        var settings = typeof(MainWindow).GetProperty("CurrentSettings", Flags)!.GetValue(main)!;
        foreach (var name in new[] { "Topmost", "ShowTrayIcon", "StartWithWindows", "AutoCheckForUpdates", "EnableFullscreenActiveOnly", "EnableUnchangedAutoHide", "EnableReverseHover", "EnableHoverExpansion" }) Property(settings, name, false);
        Property(settings, "ShowShadow", !scenario.EndsWith("no-shadow"));
        Property(settings, "IslandWidth", 375d); Property(settings, "TextSize", 11.2d);
        Call(main, "ApplySettings", settings, false, false, false);
        main.Left = 20; main.Top = 40; main.ShowInTaskbar = false; main.Show();
        Call(main, "SetPlainSummary", "受控性能测试", null);
        Window? window = null;
        if (scenario is "settings" or "settings-short" or "presets" or "settings-scroll")
        {
            window = (Window)Activator.CreateInstance(typeof(SettingsWindow), Flags, null, new object[] { main }, null)!;
            Set(window, "_loading", true); window.ShowInTaskbar = false; window.Show();
            for (var slot = 1; slot <= 3 && scenario is not ("settings-short" or "settings-scroll"); slot++)
            {
                var label = new string('测', 80);
                ((TextBlock)window.FindName($"Preset{slot}NameText")).Text = label;
                ((TextBlock)window.FindName($"Preset{slot}NameClone")).Text = label;
            }
            if (scenario == "presets")
            {
                Pump(200);
                var scroll = (ScrollViewer)window.FindName("SettingsScroll");
                var card = (FrameworkElement)window.FindName("PresetCard");
                scroll.ScrollToVerticalOffset(scroll.VerticalOffset + card.TransformToAncestor(scroll).Transform(new Point()).Y);
            }
            Call(window, "SchedulePresetMarquees");
        }
        if (scenario.StartsWith("marquee"))
        {
            if (scenario == "marquee-compact-host") main.Height = 80;
            Set(main, "_isBalanceSummary", true);
            ((TextBlock)main.FindName("SummaryText")).Text = new string('测', 80);
            ((TextBlock)main.FindName("SummaryTextClone")).Text = new string('测', 80);
            if (scenario == "marquee-cache")
            {
                ((TextBlock)main.FindName("SummaryText")).CacheMode = new BitmapCache();
                ((TextBlock)main.FindName("SummaryTextClone")).CacheMode = new BitmapCache();
            }
            if (scenario is "marquee-static-shadow" or "marquee-cached-shadow")
            {
                Pump(100);
                var island = (Border)main.FindName("Island"); var root = (Grid)island.Parent;
                var shadow = new Border { Width = island.ActualWidth, Height = island.ActualHeight,
                    HorizontalAlignment = island.HorizontalAlignment, VerticalAlignment = island.VerticalAlignment,
                    Margin = island.Margin, CornerRadius = island.CornerRadius, Background = island.Background,
                    Opacity = island.Opacity, Effect = island.Effect, IsHitTestVisible = false };
                var layer = new Grid { IsHitTestVisible = false }; layer.Children.Add(shadow);
                if (scenario == "marquee-cached-shadow") layer.CacheMode = new BitmapCache();
                var bounds = island.TransformToAncestor(root).TransformBounds(new Rect(island.RenderSize));
                layer.Clip = new CombinedGeometry(GeometryCombineMode.Exclude,
                    new RectangleGeometry(new Rect(root.RenderSize)), new RectangleGeometry(bounds, island.CornerRadius.TopLeft, island.CornerRadius.TopLeft));
                island.Effect = null; root.Children.Insert(0, layer);
            }
            Call(main, "ScheduleSummaryMarquee");
        }
        if (scenario == "expanded")
        {
            main.Height = 360;
            Set(main, "_expanded", true);
            ((FrameworkElement)main.FindName("ExpandedPanel")).Visibility = Visibility.Visible;
            ((FrameworkElement)main.FindName("ExpandedPanel")).Opacity = 1;
            ((FrameworkElement)main.FindName("Island")).Height = 300;
        }
        DispatcherTimer? scrollTimer = null;
        var scrollWatch = Stopwatch.StartNew();
        if (scenario == "settings-scroll")
        {
            var scroll = (ScrollViewer)window!.FindName("SettingsScroll");
            scrollTimer = new DispatcherTimer(DispatcherPriority.Input) { Interval = TimeSpan.FromMilliseconds(16) };
            scrollTimer.Tick += (_, _) => scroll.ScrollToVerticalOffset((Math.Sin(scrollWatch.Elapsed.TotalSeconds * 1.4) + 1) * scroll.ScrollableHeight / 2);
            scrollTimer.Start();
        }
        Pump(3500);
        Assert(main.IsVisible && (window is null || window.IsVisible), "benchmark host stays visible and isolated");
        using var process = Process.GetCurrentProcess(); process.Refresh();
        var cpu = process.TotalProcessorTime; var watch = Stopwatch.StartNew();
        var samples = new List<object>(); var previousCpu = cpu; var previousTime = 0d;
        var frameGaps = new List<double>(); TimeSpan? lastRender = null; var layoutCount = 0;
        EventHandler renderHandler = (_, e) =>
        {
            var time = ((RenderingEventArgs)e).RenderingTime;
            if (lastRender is TimeSpan previous && time > previous) frameGaps.Add((time - previous).TotalMilliseconds);
            lastRender = time;
        };
        EventHandler layoutHandler = (_, _) => layoutCount++;
        CompositionTarget.Rendering += renderHandler;
        if (window is not null) window.LayoutUpdated += layoutHandler;
        for (var i = 0; i < 15; i++)
        {
            Pump(1000); process.Refresh(); var now = watch.Elapsed.TotalSeconds; var currentCpu = process.TotalProcessorTime;
            samples.Add(new { seconds = now, cpu = (currentCpu - previousCpu).TotalSeconds / (now - previousTime) / Environment.ProcessorCount * 100,
                workingSetMiB = process.WorkingSet64 / 1048576d, privateMiB = process.PrivateMemorySize64 / 1048576d,
                heapMiB = GC.GetTotalMemory(false) / 1048576d, gcCommittedMiB = GC.GetGCMemoryInfo().TotalCommittedBytes / 1048576d });
            previousCpu = currentCpu; previousTime = now;
        }
        watch.Stop(); scrollTimer?.Stop();
        CompositionTarget.Rendering -= renderHandler;
        if (window is not null) window.LayoutUpdated -= layoutHandler;
        var cpuAverage = (process.TotalProcessorTime - cpu).TotalSeconds / watch.Elapsed.TotalSeconds / Environment.ProcessorCount * 100;
        var result = new { scenario, configuration = typeof(MainWindow).Assembly.GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration,
            logicalProcessors = Environment.ProcessorCount, renderTier = RenderCapability.Tier >> 16, dpi = VisualTreeHelper.GetDpi(main).PixelsPerInchX,
            elapsedSeconds = watch.Elapsed.TotalSeconds, cpuAverage, samples,
            layoutCount, renderIntervals = frameGaps.Count, renderGapP95Ms = frameGaps.Count == 0 ? 0 : frameGaps.Order().ElementAt((int)((frameGaps.Count - 1) * .95)),
            renderGapsOver25Ms = frameGaps.Count(value => value > 25),
            regions = captureNative ? NativeMemoryProbe.Capture(process) : null,
            presetAnimation = window is null ? null : Enumerable.Range(1, 3).Select(slot => new
            {
                slot, width = ((FrameworkElement)window.FindName($"Preset{slot}NameText")).DesiredSize.Width,
                viewport = ((FrameworkElement)window.FindName($"Preset{slot}NameCanvas")).ActualWidth,
                cloneVisible = ((FrameworkElement)window.FindName($"Preset{slot}NameClone")).Visibility.ToString(),
                animated = ((TranslateTransform)window.FindName($"Preset{slot}NameTranslate")).HasAnimatedProperties,
                offset = ((TranslateTransform)window.FindName($"Preset{slot}NameTranslate")).X,
                visible = ((FrameworkElement)window.FindName($"Preset{slot}NameDisplay")).IsVisible,
                top = ((FrameworkElement)window.FindName($"Preset{slot}NameDisplay")).TransformToAncestor((ScrollViewer)window.FindName("SettingsScroll")).Transform(new Point()).Y
            }).ToArray() };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine($"BENCH {scenario} {result.cpuAverage:F3}%");
        if (Environment.GetEnvironmentVariable("ISLAND_PERF_RENDER_DIR") is { Length: > 0 } renderDirectory)
        {
            Call(main, "StopSummaryMarquee"); main.UpdateLayout(); Pump(50);
            Directory.CreateDirectory(renderDirectory);
            var root = (FrameworkElement)main.Content;
            var bitmap = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * 2), (int)Math.Ceiling(root.ActualHeight * 2), 192, 192, PixelFormats.Pbgra32);
            bitmap.Render(root); var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(renderDirectory, Path.GetFileNameWithoutExtension(output) + ".png")); encoder.Save(file);
        }
        window?.Close(); main.Close(); app.Shutdown();
    }

    static void Quota(string output)
    {
        var errors = 0; string? firstExceptionStack = null;
        EventHandler<FirstChanceExceptionEventArgs> handler = (_, e) =>
        {
            if (e.Exception is System.ComponentModel.Win32Exception)
            { if (Interlocked.Increment(ref errors) == 1) firstExceptionStack = new StackTrace().ToString(); }
        };
        AppDomain.CurrentDomain.FirstChanceException += handler;
        var serviceType = typeof(MainWindow).Assembly.GetType("YoyoClawCompanion.Services.CodexStatusService")!;
        var service = Activator.CreateInstance(serviceType, true)!;
        var trials = new List<object>(); var watch = Stopwatch.StartNew(); using var process = Process.GetCurrentProcess();
        var cpu = process.TotalProcessorTime;
        for (var i = 0; i < 3; i++)
        {
            var trialWatch = Stopwatch.StartNew();
            var task = (Task)Call(service, "ReadLimitsAsync")!; task.GetAwaiter().GetResult();
            var result = task.GetType().GetProperty("Result")!.GetValue(task);
            trials.Add(new { successful = result is not null, seconds = trialWatch.Elapsed.TotalSeconds,
                error = serviceType.GetProperty("LastError", Flags)!.GetValue(service), errors });
        }
        process.Refresh();
        File.WriteAllText(output, JsonSerializer.Serialize(new { seconds = watch.Elapsed.TotalSeconds, cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds, errors, firstExceptionStack, trials }, new JsonSerializerOptions { WriteIndented = true }));
        (service as IDisposable)?.Dispose(); AppDomain.CurrentDomain.FirstChanceException -= handler;
        Console.WriteLine($"QUOTA errors={errors}");
    }

    static void Monitor(string output)
    {
        var assembly = typeof(MainWindow).Assembly;
        var codex = Activator.CreateInstance(assembly.GetType("YoyoClawCompanion.Services.CodexStatusService")!, true)!;
        var buddy = Activator.CreateInstance(assembly.GetType("YoyoClawCompanion.Services.WorkBuddyStatusService")!, true)!;
        var locator = assembly.GetType("YoyoClawCompanion.Services.ApplicationLocator")!;
        var snapshotMethod = locator.GetMethod("CaptureProviderPresence", BindingFlags.Static | BindingFlags.NonPublic);
        void Poll()
        {
            var snapshot = snapshotMethod?.Invoke(null, null);
            object? Running(string name) => snapshot?.GetType().GetProperty(name)!.GetValue(snapshot);
            var method = codex.GetType().GetMethod("ReadAsync", Flags)!;
            var codexTask = (Task)method.Invoke(codex, method.GetParameters().Length == 3 ? new object?[] { true, false, Running("Codex") } : new object?[] { true, false })!;
            var buddyMethod = buddy.GetType().GetMethod("ReadAsync", Flags)!;
            var buddyTask = (Task)buddyMethod.Invoke(buddy, buddyMethod.GetParameters().Length == 1 ? new object?[] { Running("WorkBuddy") } : null)!;
            Task.WhenAll(codexTask, buddyTask).GetAwaiter().GetResult();
        }
        for (var i = 0; i < 5; i++) Poll();
        using var process = Process.GetCurrentProcess(); var cpu = process.TotalProcessorTime;
        var allocated = GC.GetTotalAllocatedBytes(); var durations = new List<double>(); var watch = Stopwatch.StartNew();
        for (var i = 0; i < 50; i++) { var poll = Stopwatch.StartNew(); Poll(); durations.Add(poll.Elapsed.TotalMilliseconds); }
        watch.Stop(); process.Refresh();
        var result = new { polls = 50, seconds = watch.Elapsed.TotalSeconds, cpuSeconds = (process.TotalProcessorTime - cpu).TotalSeconds,
            allocatedBytes = GC.GetTotalAllocatedBytes() - allocated, averagePollMs = durations.Average(), maximumPollMs = durations.Max(),
            workingSetMiB = process.WorkingSet64 / 1048576d, privateMiB = process.PrivateMemorySize64 / 1048576d, durations };
        File.WriteAllText(output, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        (codex as IDisposable)?.Dispose(); (buddy as IDisposable)?.Dispose();
        Console.WriteLine($"MONITOR {result.averagePollMs:F2} ms CPU={result.cpuSeconds:F3}s alloc={result.allocatedBytes / 50} B/poll");
    }
}
