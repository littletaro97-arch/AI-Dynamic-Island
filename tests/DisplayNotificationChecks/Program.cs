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

    private static void CheckResetReminders()
    {
        var reset = new DateTimeOffset(2026,10,4,13,27,48,TimeSpan.FromHours(8));
        var lead = TimeSpan.FromMinutes(40);
        var status = new CodexStatus(true,true,true,false,null,47,50,reset,null,null,null,null,0,null,1,1,null,false,[]);
        var tracker = new CodexResetReminderTracker();
        Assert(tracker.Take(status,reset.AddMinutes(-41),lead) is null,"reset preview waits for configured forty minute threshold");
        Assert(tracker.Take(status,reset.AddMinutes(-40),lead)?.Kind == "5 小时额度","reset preview first fires at forty minutes");
        Assert(tracker.Take(status with {FiveHourResetsAt=reset.AddSeconds(1)},reset.AddMinutes(-26),lead) is null,"one second reset correction does not alert again at twenty six minutes");
        Assert(tracker.Take(status with {FiveHourResetsAt=reset.AddSeconds(-1)},reset.AddMinutes(-25),lead) is null,"alternating reset timestamps do not alert again at twenty five minutes");
        Assert(tracker.Take(status with {FiveHourRemainingPercent=12},reset.AddMinutes(-24),lead) is null,"quota usage does not retrigger reset forecast");
        Assert(tracker.Take(status with {FiveHourResetsAt=reset.ToUniversalTime()},reset.AddMinutes(-23),lead) is null,"same reset with a different timezone representation remains deduplicated");
        Assert(tracker.Take(status with {FiveHourResetsAt=reset.AddMinutes(1)},reset.AddSeconds(10),lead) is null,"correction just after original boundary still belongs to notified cycle");
        Assert(tracker.Take(status with {FiveHourResetsAt=reset.AddMinutes(14)},reset.AddMinutes(-25),lead) is null,"rescheduling a still pending reset does not repeat forecast");
        Assert(tracker.Take(status,reset.AddMinutes(-20),TimeSpan.FromMinutes(60)) is null,"changing lead time does not replay notified cycle");
        var next = reset.AddHours(5);
        Assert(tracker.Take(status with {FiveHourResetsAt=next},next.AddMinutes(-40),lead) is not null,"true next cycle can notify normally");
        var both = new CodexResetReminderTracker();
        var combined = status with {WeeklyResetsAt=reset.AddMinutes(2)};
        Assert(both.Take(combined,reset.AddMinutes(-38),lead)?.Kind == "5 小时额度","earlier five hour reset gets first forecast");
        Assert(both.Take(combined,reset.AddMinutes(-37),lead)?.Kind == "周额度","weekly reset has its own notification record");
        Assert(both.Take(combined,reset.AddMinutes(-36),lead) is null,"both quota windows remain deduplicated");
        Assert(new CodexResetReminderTracker().Take(status with {FiveHourResetsAt=null},reset.AddMinutes(-40),lead) is null,"missing reset time cannot create forecast");
        Assert(new CodexResetReminderTracker().Take(status,reset,lead) is null,"past reset cannot create forecast");
        var directory=Path.Combine(Path.GetTempPath(),"IslandResetChecks-"+Guid.NewGuid().ToString("N"));
        var path=Path.Combine(directory,"state.json");
        Directory.CreateDirectory(directory);
        try
        {
            both.Save(path);
            var loaded=CodexResetReminderTracker.Load(path);
            Assert(loaded.FiveHourResetAt==reset && loaded.WeeklyResetAt==reset.AddMinutes(2),"reset state persists both quota windows independently");
            Assert(loaded.Take(combined with {FiveHourResetsAt=reset.AddSeconds(1)},reset.AddMinutes(-25),lead) is null,"process restart does not replay already notified quota cycles");
            var weeklyNext=reset.AddDays(7);
            Assert(loaded.Take(combined with {FiveHourResetsAt=null,WeeklyResetsAt=weeklyNext},weeklyNext.AddMinutes(-40),lead)?.Kind=="周额度","persisted record allows real next weekly cycle");
            File.WriteAllText(path,"broken json");
            Assert(CodexResetReminderTracker.Load(path).Take(status,reset.AddMinutes(-40),lead) is not null,"corrupt local record cannot disable future reminders");
        }
        finally {Directory.Delete(directory,true);}
    }

    private static void CheckNotificationBatches()
    {
        var now = DateTimeOffset.UtcNow;
        var messages = Enumerable.Range(1, 8).Select(i => new SystemToast(i.ToString(), "QQ", "消息 " + i, "正文 " + i, now.AddSeconds(i), "QQ")).ToArray();
        foreach (var lines in Enumerable.Range(1, 6))
        {
            var queue = new SystemNotificationQueue();
            foreach (var message in messages) queue.Enqueue(message);
            var seen = new List<SystemToast>();
            while (queue.Count > 0)
            {
                var batch = queue.Take(SystemNotificationQueue.BatchSize(lines));
                Assert(batch.Length <= Math.Max(1, lines / 2), "batch respects line budget " + lines);
                seen.AddRange(batch);
            }
            Assert(seen.SequenceEqual(messages), "all notifications delivered once in arrival order " + lines);
        }
        Assert(SystemNotificationQueue.BatchSize(6) == 3 && SystemNotificationQueue.BatchSize(5) == 2, "six lines show three and five lines show two");
        var pending = new SystemNotificationQueue();
        foreach (var message in messages) pending.Enqueue(message);
        var first = pending.Take(3); pending.Prepend(first);
        Assert(pending.Take(8).SequenceEqual(messages), "decision interruption restores entire batch ahead of newer messages");
        pending.Enqueue(messages[0]); pending.Enqueue(messages[0] with { Body = "最新正文" });
        Assert(pending.Count == 1 && pending.Take(1)[0].Body == "最新正文", "updated pending ID replaces rather than duplicating message");
        var vpn = new SystemToast("vpn", "OPPO 互联", "手机FlClash | 魔戒.net", "流量", now, "com.oplus.devicespace");
        var rule = NotificationTitleRule.From(vpn);
        Assert(rule.Matches(vpn with { Id = "vpn-new", Body = "新的流量", CreatedAt = now.AddMinutes(1) }), "persistent VPN title filter survives new IDs times and changing body");
        Assert(!rule.Matches(vpn with { Title = "手机微信" }) && !rule.Matches(vpn with { AppUserModelId = "another.app" }), "filter preserves other phone messages and same title from another source");
        pending.Enqueue(vpn); pending.Enqueue(messages[0]); pending.Remove(rule.Matches);
        Assert(pending.Take(2).SequenceEqual([messages[0]]), "filter removes only matching queued messages");
        var settings = new IslandSettings { IgnoredNotificationTitles = [rule] };
        var restored = JsonSerializer.Deserialize<IslandSettings>(JsonSerializer.Serialize(settings))!;
        Assert(restored.IgnoredNotificationTitles.Single().Matches(vpn), "exact title filters survive settings roundtrip");
        Assert(SettingsPresetStore.Apply(new SettingsPresetSlot(), settings).IgnoredNotificationTitles.Single() == rule, "applying visual preset preserves current notification filters");
        for (var i = 0; i < 600; i++) pending.Enqueue(messages[0] with { Id = "bounded" + i });
        Assert(pending.Count == SystemNotificationQueue.Limit && pending.DroppedCount == 88, "exceptional flood is memory bounded with overflow counter");
        pending.Clear(); Assert(pending.Count == 0 && pending.DroppedCount == 0, "manual dismissal clears both pending items and overflow count");
    }

    private static void CheckNotificationBatchUi(MainWindow main)
    {
        void Set(string field, object? value) => typeof(MainWindow).GetField(field, Flags)!.SetValue(main, value);
        void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame();
            var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        var settings = main.CurrentSettings;
        settings.EnableSystemNotifications = true; settings.MaxResponseLines = 6; settings.SystemNotificationDisplaySeconds = 10;
        Call(main, "ApplyTypography"); // Keep row layout synchronized, as the product's ApplySettings does.
        Set("_refreshing", true); // Controller-only test: no real providers or files are read.
        var panel = (StackPanel)main.FindName("NotificationBatchPanel");
        var pending = (SystemNotificationQueue)typeof(MainWindow).GetField("_pendingSystemToasts", Flags)!.GetValue(main)!;
        var active = (List<SystemToast>)typeof(MainWindow).GetField("_activeSystemToastBatch", Flags)!.GetValue(main)!;
        var messages = Enumerable.Range(1, 8).Select(i => new SystemToast("batch" + i, "来源" + i, "标题" + i, "正文\n第二段", DateTimeOffset.UtcNow, "source" + i)).ToArray();
        Call(main, "ClearSystemToastBatch");
        foreach (var message in messages) Call(main, "ReceiveSystemToast", message);
        Assert(active.Count == 0 && pending.Count == 8, "same snapshot coalesces before first presentation");
        Pump(30);
        Assert(active.SequenceEqual(messages.Take(3)) && panel.Children.Count == 3 && pending.Count == 5, "six line controller presents three and retains remaining five");
        var lineHeight = ((TextBlock)main.FindName("RecentResultText")).LineHeight;
        Assert(panel.Children.OfType<Grid>().All(row => row.Height == lineHeight * 2), "every notification row consumes exactly two lines");
        Assert(panel.Children.OfType<Grid>().Select(row => (SystemToast)((Button)row.Children[0]).Tag).SequenceEqual(messages.Take(3)), "each row launch target retains its own source identity");
        var originalRow = panel.Children[0]; Call(main, "UpdateRecentNotice");
        Assert(ReferenceEquals(originalRow, panel.Children[0]), "unchanged status refresh reuses notification views");
        if (Environment.GetEnvironmentVariable("ISLAND_LAYOUT_EVIDENCE_DIR") is { Length: > 0 } evidence)
        {
            Pump(400); main.UpdateLayout();
            var border = (Border)main.FindName("RecentBorder");
            var width = (int)Math.Ceiling(border.ActualWidth); var height = (int)Math.Ceiling(border.ActualHeight);
            Console.WriteLine($"BATCH LAYOUT {width}x{height}, required={lineHeight * 6}, panel={panel.ActualWidth}x{panel.ActualHeight}");
            Assert(width > 0 && height >= lineHeight * 6, "six line batch has real visible layout space");
            Directory.CreateDirectory(evidence);
            var bitmap = new RenderTargetBitmap(width * 2, height * 2, 192, 192, PixelFormats.Pbgra32); bitmap.Render(border);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using var file = File.Create(Path.Combine(evidence, "notification-three-rows.png")); encoder.Save(file);
        }
        settings.MaxResponseLines = 5; Call(main, "FillSystemToastBatch"); Call(main, "UpdateRecentNotice");
        Assert(active.Count == 2 && panel.Children.Count == 2 && pending.Count == 6, "reducing budget to five returns third message to front of queue");
        Call(main, "EndCompletionNotice");
        Assert(active.SequenceEqual(messages.Skip(2).Take(2)) && pending.Count == 4, "timeout advances to next whole batch in order");
        Call(main, "PauseSystemToastForDecision"); Set("_activeConfirmationNotice", "等待回答"); Call(main, "UpdateRecentNotice");
        Assert(active.Count == 0 && pending.Count == 6 && panel.Visibility == Visibility.Collapsed, "decision pauses every displayed notification");
        Set("_activeConfirmationNotice", null); Call(main, "TryShowNextSystemToast");
        Assert(active.SequenceEqual(messages.Skip(2).Take(2)), "same interrupted batch resumes first");
        Call(main, "CollapseHandle_Click", main, new RoutedEventArgs(Button.ClickEvent)); Pump(30);
        Assert(active.Count == 0 && pending.Count == 0, "manual collapse cancels active batch and entire pending round");
        var vpn = messages[0] with { AppUserModelId = "com.oplus.devicespace", Title = "手机FlClash | 魔戒.net" };
        settings.IgnoredNotificationTitles = [NotificationTitleRule.From(vpn)];
        Call(main, "ReceiveSystemToast", vpn); Call(main, "ReceiveSystemToast", vpn with { Id = "new-vpn-id", Body = "流量变化" });
        Assert(pending.Count == 0 && active.Count == 0, "known persistent VPN never starts a batch or queues again");
        settings.IgnoredNotificationTitles = []; settings.MaxResponseLines = 6; settings.SystemNotificationDisplaySeconds = .24;
        Call(main, "ReceiveSystemToast", messages[0]); Pump(30); Pump(90);
        Call(main, "ReceiveSystemToast", messages[1]); Pump(20);
        Assert(active.Count == 1 && pending.Count == 1, "later arrival waits for next batch and its own full interval");
        Pump(150);
        Assert(active.SequenceEqual([messages[1]]) && pending.Count == 0, "later arrival does not extend original batch deadline");
        Call(main, "CollapseHandle_Click", main, new RoutedEventArgs(Button.ClickEvent));
        settings.EnableSystemNotifications = false; settings.MaxResponseLines = 3; settings.SystemNotificationDisplaySeconds = 10;
        Set("_refreshing", false); Set("_manualCollapseUntilPointerExit", false);
        Call(main, "CompleteCollapseImmediately");
    }

    private static void CheckHoverNotificationHold(MainWindow main)
    {
        void Set(string field, object? value) => typeof(MainWindow).GetField(field, Flags)!.SetValue(main, value);
        void Pump(int milliseconds)
        {
            var frame = new DispatcherFrame(); var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; }; timer.Start(); Dispatcher.PushFrame(frame);
        }
        var mouse = new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0);
        var timer = (DispatcherTimer)typeof(MainWindow).GetField("_completionTimer", Flags)!.GetValue(main)!;
        var active = (List<SystemToast>)typeof(MainWindow).GetField("_activeSystemToastBatch", Flags)!.GetValue(main)!;
        var pending = (SystemNotificationQueue)typeof(MainWindow).GetField("_pendingSystemToasts", Flags)!.GetValue(main)!;
        var settings = main.CurrentSettings;
        Set("_refreshing", true); Set("_expanded", true);
        settings.EnableSystemNotifications = true; settings.SystemNotificationDisplaySeconds = .3; settings.MaxResponseLines = 2;
        var first = new SystemToast("hover-first", "QQ", "第一条", "阅读内容", DateTimeOffset.UtcNow);
        var second = first with { Id = "hover-next", Title = "第二条" };
        Call(main, "ReceiveSystemToast", first); Call(main, "ReceiveSystemToast", second); Pump(40);
        Call(main, "Island_MouseEnter", main, mouse);
        Assert(!timer.IsEnabled && active.Single().Id == first.Id, "hover stops existing display timer");
        Pump(450);
        Assert(active.Single().Id == first.Id && pending.Count == 1, "hover preserves current content beyond configured expiry and prevents batch advance");
        Call(main, "NotificationHold_Tick");
        Assert(active.Single().Id == first.Id, "queued expiry callback cannot clear hovered notification");
        Call(main, "Island_MouseLeave", main, mouse);
        Assert(timer.IsEnabled && timer.Interval.TotalMilliseconds < 290, "leave resumes remaining time instead of restarting full interval");
        Pump(40); Call(main, "Island_MouseEnter", main, mouse);
        var remaining = (TimeSpan)typeof(MainWindow).GetField("_notificationHoldRemaining", Flags)!.GetValue(main)!;
        Pump(380);
        Assert(active.Single().Id == first.Id && !timer.IsEnabled, "repeated hover continues to protect same notification");
        Call(main, "Island_MouseLeave", main, mouse);
        Assert(timer.Interval <= remaining + TimeSpan.FromMilliseconds(1), "repeated hover does not accumulate extra display time");
        Pump(320);
        Assert(active.Single().Id == second.Id, "next batch advances normally after pointer leaves");
        Call(main, "Island_MouseEnter", main, mouse);
        Call(main, "CollapseHandle_Click", main, new RoutedEventArgs(Button.ClickEvent));
        Call(main, "Island_MouseLeave", main, mouse); Pump(40);
        Assert(active.Count == 0 && pending.Count == 0 && !timer.IsEnabled, "manual collapse cancels paused batch without a later timer restart");
        Set("_expanded", true); Call(main, "Island_MouseEnter", main, mouse);
        foreach (var field in new[] { "_activeCompletionNotice", "_activeSystemNotice" })
        {
            Set(field, "阅读中的提醒");
            Call(main, "BeginNotificationCountdown", TimeSpan.FromMilliseconds(120));
            Pump(180);
            Assert(!timer.IsEnabled && (string?)typeof(MainWindow).GetField(field, Flags)!.GetValue(main) == "阅读中的提醒", "new reminder already under pointer starts paused " + field);
            Call(main, "CancelNotificationHold"); Set(field, null);
        }
        Call(main, "Island_MouseLeave", main, mouse);
        Set("_expanded", true); Call(main, "Island_MouseEnter", main, mouse);
        Call(main, "BeginNotificationCountdown", TimeSpan.FromMilliseconds(120));
        Call(main, "CompleteCollapseImmediately");
        Assert(timer.IsEnabled, "forced collapse resumes countdown even before pointer leaves old bounds");
        Call(main, "CancelNotificationHold"); Call(main, "Island_MouseLeave", main, mouse);
        settings.EnableSystemNotifications = false; settings.SystemNotificationDisplaySeconds = 10; settings.MaxResponseLines = 3;
        Set("_refreshing", false); Set("_manualCollapseUntilPointerExit", false); Call(main, "CompleteCollapseImmediately");
    }

    [STAThread] static void Main()
    {
        Assert(WorkBuddyCreditsService.DescribeError("connect:UnauthorizedAccessException").Contains("连接权限"), "WorkBuddy pipe permission rejection is distinguished from schema failure");
        Assert(WorkBuddyCreditsService.DescribeError("discovery:UnauthorizedAccessException").Contains("文件权限"), "WorkBuddy discovery permission rejection is distinct");
        Assert(WorkBuddyCreditsService.DescribeError(null) == "积分不可用", "unknown WorkBuddy failure retains generic label");
        Assert(WorkBuddyCreditsService.DescribeError("fetch-rejected-403") == "积分不可用", "unverified API errors are not labelled as local permission errors");
        CheckNotificationBatches();
        CheckResetReminders();
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
        Assert(NotificationAppLauncher.GetWakeKey("QQ", @"D:\Apps\QQ.exe", 1) == 0x58, "QQ hotkey requires registered executable and running instance");
        Assert(NotificationAppLauncher.GetWakeKey("Weixin", @"D:\Apps\QQ.exe", 1) is null
            && NotificationAppLauncher.GetWakeKey("QQ", @"D:\Apps\other.exe", 1) is null
            && NotificationAppLauncher.GetWakeKey("QQ", @"D:\Apps\QQ.exe", 0) is null, "hotkey never applied to unrelated or absent apps");
        var inputCalls = 0;
        Assert(NotificationAppLauncher.SendHotkey(0x58, _ => 0, (count, inputs, size) => {
            inputCalls++;
            Assert(size == (IntPtr.Size == 8 ? 40 : 28) && count == 6 && inputs.All(i => i.Type == 1), "SendInput layout and one complete keyboard batch");
            Assert(inputs.Select(i => i.Data.Keyboard.Key).SequenceEqual(new ushort[] { 0x11, 0x12, 0x58, 0x58, 0x12, 0x11 })
                && inputs.Select(i => i.Data.Keyboard.Flags).SequenceEqual(new uint[] { 0, 0, 0, 2, 2, 2 }), "Ctrl Alt X keys released in reverse order");
            return count;
        }) && inputCalls == 1, "successful hotkey sent once");
        foreach (var held in new[] { 0x10, 0x11, 0x12, 0x5B, 0x5C, 0x58 })
            Assert(!NotificationAppLauncher.SendHotkey(0x58, k => k == held ? unchecked((short)0x8000) : (short)0,
                (_, _, _) => throw new Exception("input while physical key held")), "physical held key prevents injection " + held);
        inputCalls = 0;
        Assert(!NotificationAppLauncher.SendHotkey(0x58, _ => 0, (_, _, _) => { inputCalls++; return 0; }) && inputCalls == 1, "blocked injection never retries");
        inputCalls = 0;
        Assert(!NotificationAppLauncher.SendHotkey(0x58, _ => 0, (count, inputs, _) => {
            if (++inputCalls == 1) return 2;
            Assert(count == 2 && inputs.All(i => i.Data.Keyboard.Flags == 2)
                && inputs.Select(i => i.Data.Keyboard.Key).SequenceEqual(new ushort[] { 0x12, 0x11 }), "partial injection releases only injected modifiers");
            return count;
        }) && inputCalls == 2, "partial injection reports failure after cleanup");
        Assert(NotificationAppLauncher.GetWakeKey(@"E:\Apps\Weixin.exe", @"E:\Apps\Weixin.exe", 1) == 0x57
            && NotificationAppLauncher.GetWakeKey("WeChat", @"E:\Apps\WeChat.exe", 1) == 0x57, "Weixin path identity and legacy WeChat resolve Ctrl Alt W");
        Assert(NotificationAppLauncher.GetWakeKey("QQ", @"E:\Apps\Weixin.exe", 1) is null
            && NotificationAppLauncher.GetWakeKey("other", @"E:\Apps\Weixin.exe", 1) is null
            && NotificationAppLauncher.GetWakeKey("WeChat", @"E:\Apps\WeChat.exe", 0) is null, "Weixin hotkey requires matching identity and verified running executable");
        Assert(NotificationAppLauncher.GetWakeKey("Tencent.Weixin.Desktop", @"E:\Apps\Weixin.exe", 1, true) == 0x57
            && NotificationAppLauncher.GetWakeKey("Tencent.Weixin.Desktop", @"E:\Apps\Weixin.exe", 1) is null
            && NotificationAppLauncher.GetWakeKey("Tencent.Weixin.Desktop", @"E:\Apps\other.exe", 1, true) is null,
            "WeChat aliases require exact registered target and verified process path");
        var auxiliary = new NotificationAppLauncher.AppWindow(new IntPtr(1), 1, false, true, false, false, 160, 100, true, "Auxiliary", 1);
        Assert(NotificationAppLauncher.ShouldUseHotkey(0x57, auxiliary, false), "visible WeChat auxiliary window cannot bypass native shortcut");
        Assert(!NotificationAppLauncher.ShouldUseHotkey(0x58, auxiliary, false)
            && NotificationAppLauncher.ShouldUseHotkey(0x58, auxiliary with { Visible = false }, false)
            && !NotificationAppLauncher.ShouldUseHotkey(0x58, auxiliary with { Visible = false }, true),
            "QQ visible and minimized windows retain existing activation while tray uses shortcut");
        Assert(NotificationAppLauncher.SendHotkey(0x57, _ => 0, (count, inputs, _) => {
            Assert(count == 6 && inputs.Select(i => i.Data.Keyboard.Key).SequenceEqual(new ushort[] { 0x11, 0x12, 0x57, 0x57, 0x12, 0x11 })
                && inputs.Select(i => i.Data.Keyboard.Flags).SequenceEqual(new uint[] { 0, 0, 0, 2, 2, 2 }), "Ctrl Alt W balanced input batch"); return count;
        }), "Weixin uses shared shortcut sender");
        Assert(!NotificationAppLauncher.SendHotkey(0x57, k => k == 0x57 ? unchecked((short)0x8000) : (short)0,
            (_, _, _) => throw new Exception("held W injected")), "held physical W prevents injection");
        var old = new SystemToast("old", "Mail", "历史消息", "正文", DateTimeOffset.UtcNow.AddMinutes(-2));
        Assert(tracker.Accept([old]).Count == 0, "enabling notifications does not replay Action Center history");
        var fresh = old with { Id = "new", Title = "新消息", CreatedAt = DateTimeOffset.UtcNow.AddSeconds(1) };
        Assert(tracker.Accept([old, fresh]).Single().Id == "new", "new notification delivered");
        Assert(tracker.Accept([old, fresh]).Count == 0, "unchanged notification deduplicates");
        Assert(tracker.Accept([old, fresh with { CreatedAt = fresh.CreatedAt.AddSeconds(1) }]).Count == 0, "timestamp correction alone never creates another notification");
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
        if (Environment.GetEnvironmentVariable("ISLAND_WEIXIN_HOTKEY_SMOKE") == "1")
        {
            var identity = @"E:\D-diskExpansionCabin\Weixin\Weixin.exe";
            int[] Pids() => System.Diagnostics.Process.GetProcessesByName("Weixin").Select(p => { using (p) return p.Id; }).Order().ToArray();
            var before = Pids();
            Assert(before.Length > 0 && NotificationAppLauncher.TryResolveRegisteredTarget(identity, out _), "live Weixin smoke requires registered running app");
            Assert(NotificationAppLauncher.OpenAsync(identity).GetAwaiter().GetResult() == NotificationOpenResult.Opened, "production Weixin activation reaches foreground");
            System.Threading.Thread.Sleep(700);
            Assert(!Pids().Except(before).Any(), "Weixin activation creates no new process");
        }
        if (Environment.GetEnvironmentVariable("ISLAND_QQ_HOTKEY_SMOKE") == "1")
        {
            int[] QqPids() => System.Diagnostics.Process.GetProcessesByName("QQ").Select(p => { using (p) return p.Id; }).Order().ToArray();
            var before = QqPids();
            Assert(before.Length > 0 && NotificationAppLauncher.TryResolveRegisteredTarget("QQ", out _), "live QQ smoke requires registered running QQ");
            var activation = NotificationAppLauncher.OpenAsync("QQ").GetAwaiter().GetResult();
            Assert(activation == NotificationOpenResult.Opened, "production QQ activation reaches QQ foreground");
            System.Threading.Thread.Sleep(700);
            Assert(!QqPids().Except(before).Any(), "QQ activation creates no new process");
        }
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
        var noticeText = (TextBlock)main.FindName("RecentResultText");
        typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.SetValue(main, fresh);
        Call(main, "UpdateRecentNotice");
        var notificationPanel = (StackPanel)main.FindName("NotificationBatchPanel");
        var notificationRow = (Grid)notificationPanel.Children[0];
        var notificationOpen = (Button)notificationRow.Children[0];
        Assert(notificationPanel.Visibility == Visibility.Visible && Equals(notificationOpen.Tag, fresh) && noticeText.Text == fresh.Text, "system notification content provides own app launch hit target");
        notificationOpen.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert(noticeText.Text.Contains("无法打开来源应用") && typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.GetValue(main) is not null, "launch failure leaves toast visible with feedback");
        typeof(MainWindow).GetField("_activeConfirmationNotice", Flags)!.SetValue(main, "Codex 等待回答");
        Call(main, "UpdateRecentNotice");
        Assert(notificationPanel.Visibility == Visibility.Collapsed && noticeText.Text == "Codex 等待回答", "priority confirmation never opens the interrupted toast app");
        typeof(MainWindow).GetField("_activeConfirmationNotice", Flags)!.SetValue(main, null);
        typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.SetValue(main, null);
        Call(main, "UpdateRecentNotice");
        Assert(notificationPanel.Visibility == Visibility.Collapsed, "ordinary result has no notification click overlay");
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
        CheckNotificationBatchUi(main);
        CheckHoverNotificationHold(main);
        var savedHover = main.CurrentSettings.EnableHoverExpansion;
        var savedReverse = main.CurrentSettings.EnableReverseHover;
        main.CurrentSettings.EnableReverseHover = false; main.CurrentSettings.EnableHoverExpansion = true;
        var hoverTimer = (DispatcherTimer)typeof(MainWindow).GetField("_enterTimer", Flags)!.GetValue(main)!;
        Call(main, "Island_MouseEnter", main, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0));
        Assert(hoverTimer.IsEnabled, "enabled hover in normal collapsed mode arms expansion timer");
        hoverTimer.Stop(); main.CurrentSettings.EnableHoverExpansion = false;
        Call(main, "Island_MouseEnter", main, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0));
        Assert(!hoverTimer.IsEnabled, "explicitly disabled hover does not expand");
        Call(main, "Island_MouseLeave", main, new System.Windows.Input.MouseEventArgs(System.Windows.Input.Mouse.PrimaryDevice, 0));
        main.CurrentSettings.EnableHoverExpansion = savedHover; main.CurrentSettings.EnableReverseHover = savedReverse;
        var creditService = (WorkBuddyCreditsService)typeof(MainWindow).GetField("_workBuddyCreditsService", Flags)!.GetValue(main)!;
        typeof(WorkBuddyCreditsService).GetProperty("LastError", Flags)!.SetValue(creditService, "connect:UnauthorizedAccessException");
        var metric = typeof(MainWindow).GetMethod("WorkBuddyMetric", Flags)!;
        Assert(((string)metric.Invoke(main, [new WorkBuddyCredits(false, null, null)])!).Contains("连接权限"), "summary displays confirmed WorkBuddy permission cause");
        Assert((string)metric.Invoke(main, [new WorkBuddyCredits(true, 123, 200)])! == "123 积分", "valid WorkBuddy credits override stale diagnostic label");
        creditService.ResetCache();
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
        var passTimer = (DispatcherTimer)typeof(MainWindow).GetField("_passThroughTimer", Flags)!.GetValue(main)!;
        Call(main, "ApplyReverseHoverEntry", true);
        Assert(!ReverseHidden() && passTimer.IsEnabled, "holding Alt before entry keeps collapsed island visible with on demand key check");
        var reverseBounds = (Rect)typeof(MainWindow).GetField("_reverseHoverBoundsPixels", Flags)!.GetValue(main)!;
        var inside = new Point(reverseBounds.Left + reverseBounds.Width / 2, reverseBounds.Top + reverseBounds.Height / 2);
        Call(main, "UpdateReverseHoverPassThrough", true, inside);
        Assert(!ReverseHidden(), "Alt hold does not trigger hiding on repeated checks");
        Call(main, "UpdateReverseHoverPassThrough", false, inside);
        Assert(ReverseHidden(), "releasing Alt without moving pointer resumes hiding");
        Call(main, "UpdateReverseHoverPassThrough", true, inside);
        Assert(!ReverseHidden() && island.Visibility == Visibility.Visible, "pressing Alt after island hid restores it");
        Call(main, "UpdateReverseHoverPassThrough", true, new Point(reverseBounds.Right + 100, reverseBounds.Bottom + 100));
        Assert(!passTimer.IsEnabled && !ReverseHidden(), "leaving Alt bypass area stops key polling");
        SetMain("_expanded", true); Call(main, "ApplyReverseHoverEntry", false);
        Assert(!ReverseHidden() && !passTimer.IsEnabled, "expanded island ignores reverse hide with or without Alt");
        Call(main, "CompleteCollapseImmediately");
        main.CurrentSettings.EnableHoverExpansion = true;
        Assert((bool)typeof(MainWindow).GetMethod("CanPointerExpand", Flags)!.Invoke(main, [true])!
            && !(bool)typeof(MainWindow).GetMethod("CanPointerExpand", Flags)!.Invoke(main, [false])!, "both enabled only allow pointer expansion while Alt is held");
        Call(main, "ApplyReverseHoverEntry", true);
        Assert(hoverTimer.IsEnabled, "Alt entry arms configured hover delay with both modes enabled");
        hoverTimer.Stop();
        Call(main, "TryExpandReverseAltClick", true, true, true);
        Assert((bool)typeof(MainWindow).GetField("_expanded", Flags)!.GetValue(main)!, "Alt click opens collapsed island immediately");
        Call(main, "ApplyReverseHoverEntry", false);
        Assert(!ReverseHidden(), "releasing Alt after expansion cannot hide expanded island");
        Call(main, "CompleteCollapseImmediately");
        SetMain("_manualCollapseUntilPointerExit", true);
        Call(main, "TryExpandReverseAltClick", true, true, true);
        Assert((bool)typeof(MainWindow).GetField("_expanded", Flags)!.GetValue(main)!, "explicit Alt click can reopen after manual collapse without requiring pointer exit");
        Call(main, "CompleteCollapseImmediately");
        main.CurrentSettings.EnableHoverExpansion = false;
        Call(main, "TryExpandReverseAltClick", true, true, true);
        Assert(!(bool)typeof(MainWindow).GetField("_expanded", Flags)!.GetValue(main)!, "reverse only setting remains visible with Alt but does not enable disabled expansion");
        Call(main, "HideIslandForReverseHover");
        Assert(ReverseHidden(), "settled collapsed island retains reverse hover pass through");
        Call(main, "ExpandIsland", true);
        Assert(!ReverseHidden() && island.Visibility == Visibility.Visible, "forced expansion cancels in flight reverse fade");
        // Pump past the obsolete fade; it must not hide the expanded notification.
        var reverseFrame = new DispatcherFrame(); var reverseTick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(380) };
        reverseTick.Tick += (_, _) => { reverseTick.Stop(); reverseFrame.Continue = false; }; reverseTick.Start(); Dispatcher.PushFrame(reverseFrame);
        Assert(island.Visibility == Visibility.Visible && !ReverseHidden(), "obsolete reverse fade cannot hide later expansion");
        // The off-screen fixture can receive MouseLeave and auto-collapse while
        // the dispatcher pumps. Establish the expanded precondition explicitly.
        ((DispatcherTimer)typeof(MainWindow).GetField("_leaveTimer", Flags)!.GetValue(main)!).Stop();
        Call(main, "CompleteCollapseImmediately"); SetMain("_expanded", true);
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
        window.Close();
        foreach (var name in new[] { "_holdTimer", "_enterTimer", "_leaveTimer" })
            ((DispatcherTimer)typeof(MainWindow).GetField(name, Flags)!.GetValue(main)!).Start();
        ((SystemNotificationQueue)typeof(MainWindow).GetField("_pendingSystemToasts", Flags)!.GetValue(main)!).Enqueue(fresh);
        typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.SetValue(main, fresh);
        Call(main, "DiscardSystemToasts");
        Assert(((SystemNotificationQueue)typeof(MainWindow).GetField("_pendingSystemToasts", Flags)!.GetValue(main)!).Count == 0
            && typeof(MainWindow).GetField("_activeWindowsToast", Flags)!.GetValue(main) is null, "shared reset dismisses entire system notification round");
        main.Close();
        Assert(new[] { "_holdTimer", "_enterTimer", "_leaveTimer", "_refreshTimer", "_completionTimer", "_passThroughTimer", "_fullscreenTimer", "_zOrderTimer", "_readyClockTimer" }
            .All(name => !((DispatcherTimer)typeof(MainWindow).GetField(name, Flags)!.GetValue(main)!).IsEnabled), "closing stops both interaction and polling timers");
        var closedRefresh = (Task)typeof(MainWindow).GetMethod("RefreshStatusAsync", Flags)!.Invoke(main, null)!;
        Assert(closedRefresh.IsCompletedSuccessfully && !((DispatcherTimer)typeof(MainWindow).GetField("_refreshTimer", Flags)!.GetValue(main)!).IsEnabled, "closed window rejects delayed status refresh without restarting timers");
        app.Shutdown();
    }
}
