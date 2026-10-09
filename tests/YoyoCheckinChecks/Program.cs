using YoyoClawCompanion.Services;

var count = 0;
void Check(bool condition, string name)
{
    if (!condition) throw new Exception(name);
    Console.WriteLine("PASS " + name);
    count++;
}
var now = new DateTimeOffset(2026, 10, 8, 18, 32, 0, TimeSpan.FromHours(8));
var schedule = new YoyoCheckinSchedule();
Check(schedule.TryStart(now) && schedule.Attempts == 1, "initial run");
Check(!schedule.TryStart(now.AddMinutes(10)), "single in-flight run");
schedule.Complete(now, true);
Check(!schedule.TryStart(now.AddHours(4)), "success suppresses same-day refreshes");
Check(schedule.TryStart(now.AddDays(1)) && schedule.Attempts == 1, "next day starts despite yesterday success");
schedule.Complete(now.AddDays(1), false);
Check(!schedule.TryStart(now.AddDays(1).AddMinutes(4)), "first failure backoff");
Check(schedule.TryStart(now.AddDays(1).AddMinutes(5)) && schedule.Attempts == 2, "first retry boundary");
schedule.Complete(now.AddDays(1).AddMinutes(5), false);
Check(!schedule.TryStart(now.AddDays(1).AddMinutes(34)), "second failure backoff");
Check(schedule.TryStart(now.AddDays(1).AddMinutes(35)) && schedule.Attempts == 3, "second retry boundary");
schedule.Complete(now.AddDays(1).AddMinutes(35), false);
Check(!schedule.TryStart(now.AddDays(1).AddHours(4)), "daily attempts bounded at three");
Check(schedule.TryStart(now.AddDays(4)) && schedule.Attempts == 1, "resume after several days");
schedule.Complete(now.AddDays(4), true);
Check(!schedule.TryStart(now.AddDays(4).AddHours(1)), "retry success stops further calls");
var crossing = new YoyoCheckinSchedule();
var late = new DateTimeOffset(2026, 10, 8, 23, 59, 0, TimeSpan.FromHours(8));
Check(crossing.TryStart(late), "run before midnight");
Check(!crossing.TryStart(late.AddMinutes(2)), "midnight does not overlap ongoing request");
crossing.Complete(late.AddMinutes(3), true);
Check(crossing.TryStart(late.AddMinutes(4)) && crossing.Attempts == 1, "old-day completion does not consume next day");
crossing.Complete(late.AddMinutes(4), true);
Check(!crossing.TryStart(late.AddMinutes(5)), "new-day success retained");
Console.WriteLine($"{count} checks passed; no network, account or application build performed.");
