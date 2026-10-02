using System.Reflection;
using System.Text.Json;

var type = Assembly.Load("YoyoClawCompanion").GetType("YoyoClawCompanion.Services.YoyoQuotaService")!;
var parse = type.GetMethod("Parse", BindingFlags.Static | BindingFlags.NonPublic)!;
var now = DateTimeOffset.Parse("2026-10-02T00:00:00Z");
void Check(string json, double? remaining, double? total, string name)
{
    using var document = JsonDocument.Parse(json);
    var result = ((double? Remaining, double? Total))parse.Invoke(null, [document.RootElement, now])!;
    if (result != (remaining, total)) throw new Exception($"{name}: {result}");
    Console.WriteLine("PASS " + name);
}
Check("""{"quota":{"model_remaining_points":357,"quota_grants":[{"status":"ACTIVE","remaining_points":357,"total_points":370,"window_expires_at":"2026-11-01T00:00:00+08:00"},{"status":"ACTIVE","remaining_points":10,"total_points":10,"window_expires_at":"2026-12-01T00:00:00+08:00"}]}}""", 367,380,"different expiration grants add to 367");
Check("""{"quota_grants":[{"status":"ACTIVE","remaining_points":135,"total_points":135,"window_expires_at":"2026-10-01T00:00:00+08:00"},{"status":"ACTIVE","remaining_points":10,"total_points":10},{"status":"EXHAUSTED","remaining_points":20,"total_points":20}]}""",10,10,"expired and exhausted grants excluded");
Check("""{"model_remaining_points":357,"model_total_points":370}""",357,370,"legacy snapshot fallback");
Check("""{"model_remaining_points":357,"model_total_points":370,"quota_grants":[{"status":"ACTIVE"}]}""",357,370,"incomplete grant fallback");
Check("""{"quota_grants":[{"status":"EXHAUSTED","remaining_points":0,"total_points":100}]}""",0,0,"exhausted balance zero");
Check("""{"quota_grants":[{"status":"ACTIVE","remaining_points":"10","total_points":"10"}]}""",10,10,"numeric strings supported");
var parseLog = type.GetMethod("ParseBalanceLog",BindingFlags.Static|BindingFlags.NonPublic)!;
var logNow = new DateTimeOffset(2026,10,2,11,40,0,TimeSpan.FromHours(8));
var log = "2026-10-02 11:36:37.367 [INFO ] [Billing] cloud subscription balance received {\n\"displayed_remaining_points\":366.9974,\"model_total_points\":380\n}";
object? Log(DateTimeOffset baseline, DateTimeOffset time) => parseLog.Invoke(null,[log,baseline,time]);
if (Log(DateTimeOffset.MinValue,logNow) is not ValueTuple<double?,double?> balance || balance != (366.9974,380)) throw new Exception("Fresh official log balance");
if (Log(logNow,logNow) is not null || Log(DateTimeOffset.MinValue,logNow.AddHours(-1)) is not null) throw new Exception("Old or future log must not override fresh quota");
Console.WriteLine("PASS official log, timestamp precedence, future log excluded");
if (Environment.GetEnvironmentVariable("ISLAND_LIVE_QUOTA_CHECK") == "1")
{
    var instance = Activator.CreateInstance(type,true)!;
    var result = ((double? Remaining,double? Total))type.GetMethod("Read")!.Invoke(instance,null)!;
    Console.WriteLine($"LIVE Remaining={result.Remaining:0.####}, Total={result.Total:0.####}");
    if (result != (366.9974,380)) throw new Exception("Live points do not match current screenshot baseline");
}
