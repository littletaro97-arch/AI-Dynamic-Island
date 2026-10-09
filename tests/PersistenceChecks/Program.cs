using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.Json;
using YoyoClawCompanion.Services;

int count = 0;
void Check(bool value, string name) { if (!value) throw new Exception(name); count++; Console.WriteLine("PASS " + name); }
using var entered = new ManualResetEventSlim();
using var release = new ManualResetEventSlim();
var writes = new ConcurrentQueue<Snapshot>();
int active = 0, maxActive = 0;
var queue = new LatestSnapshotQueue<Snapshot>(value =>
{
    var concurrency = Interlocked.Increment(ref active);
    maxActive = Math.Max(maxActive, concurrency);
    if (value.Sequence == 0) { entered.Set(); if (!release.Wait(10000)) throw new TimeoutException(); }
    writes.Enqueue(value);
    Interlocked.Decrement(ref active);
});
var first = queue.Submit(new(0, "first"));
Check(entered.Wait(5000), "background worker started");
var timer = Stopwatch.StartNew();
Task last = first;
for (int i = 1; i <= 10000; i++) last = queue.Submit(new(i, "snapshot"));
Check(timer.ElapsedMilliseconds < 2000, "slow writer does not block submitter");
Check(ReferenceEquals(first, last), "burst reuses one worker task");
release.Set();
await last.WaitAsync(TimeSpan.FromSeconds(10));
Check(writes.Select(x => x.Sequence).SequenceEqual(new[] { 0, 10000 }), "only latest pending snapshot retained");
Check(maxActive == 1, "writer never overlaps");
await queue.Submit(new(10001, "after idle"));
Check(writes.Last().Sequence == 10001, "worker restarts after idle");
int exceptions = 0;
var recovery = new LatestSnapshotQueue<Snapshot>(_ => { if (Interlocked.Increment(ref exceptions) == 1) throw new IOException("simulated"); });
await recovery.Submit(new(1, "first"));
await recovery.Submit(new(2, "second"));
Check(exceptions == 2, "write failure does not strand worker");
using var stopEntered = new ManualResetEventSlim();
using var stopRelease = new ManualResetEventSlim();
var stoppedWrites = new ConcurrentQueue<int>();
var stopping = new LatestSnapshotQueue<Snapshot>(value => { stopEntered.Set(); stopRelease.Wait(5000); stoppedWrites.Enqueue(value.Sequence); });
var stopTask = stopping.Submit(new(1, "active"));
Check(stopEntered.Wait(5000), "close scenario starts active write");
_ = stopping.Submit(new(2, "pending"));
stopping.Stop();
_ = stopping.Submit(new(3, "after close"));
stopRelease.Set();
await stopTask.WaitAsync(TimeSpan.FromSeconds(10));
Check(stoppedWrites.SequenceEqual(new[] { 1 }), "close drops pending and rejects new submissions");
var concurrentWrites = new ConcurrentQueue<int>();
var concurrent = new LatestSnapshotQueue<Snapshot>(value => concurrentWrites.Enqueue(value.Sequence));
await Task.WhenAll(Enumerable.Range(0, 8).Select(producer => Task.Run(() => { for (int i=0;i<500;i++) concurrent.Submit(new(producer*500+i,"concurrent")); })));
await concurrent.Submit(new(99999,"final"));
Check(concurrentWrites.Last() == 99999, "concurrent producers settle to final snapshot");
using var orderedEntered = new ManualResetEventSlim();
using var orderedRelease = new ManualResetEventSlim();
var orderedWrites = new ConcurrentQueue<int>();
var ordered = new LatestSnapshotQueue<Snapshot>(value => { if(value.Sequence==0) { orderedEntered.Set(); orderedRelease.Wait(5000); } orderedWrites.Enqueue(value.Sequence); },
    (previous,next) => previous.Sequence > next.Sequence ? previous : next);
var orderedTask = ordered.Submit(new(0,"active"));
Check(orderedEntered.Wait(5000), "version ordering scenario starts");
_ = ordered.Submit(new(10,"newer"));
_ = ordered.Submit(new(9,"late older producer"));
orderedRelease.Set();
await orderedTask;
Check(orderedWrites.SequenceEqual(new[]{0,10}), "late older settings cannot displace newer pending version");
var directory = Path.Combine(Path.GetTempPath(), "IslandPersistenceChecks-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
// Leave isolated test files for inspection; never touch application status or user settings.
var payload = new Snapshot(123, new string('x', 4096));
StatusSnapshotStore.WriteTo(directory, payload);
using (var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory,"status.json"))))
    Check(json.RootElement.GetProperty("Sequence").GetInt32() == 123, "snapshot remains valid JSON");
Check(!File.Exists(Path.Combine(directory,"status.json.tmp")), "atomic replace consumes temporary file");
var directTimes = new List<double>();
var backgroundTimes = new List<double>();
var baselinePath = Path.Combine(directory,"baseline.json");
for (int i=0;i<1000;i++)
{
    var watch = Stopwatch.StartNew();
    File.WriteAllText(baselinePath, JsonSerializer.Serialize(payload));
    directTimes.Add(watch.Elapsed.TotalMilliseconds);
}
int actualWrites=0;
var benchmark = new LatestSnapshotQueue<Snapshot>(value => { StatusSnapshotStore.WriteTo(directory,value); Interlocked.Increment(ref actualWrites); });
Task drained = Task.CompletedTask;
for (int i=0;i<1000;i++)
{
    var watch=Stopwatch.StartNew();
    drained=benchmark.Submit(payload);
    backgroundTimes.Add(watch.Elapsed.TotalMilliseconds);
}
await drained;
double P95(List<double> values) => values.Order().ElementAt((int)(values.Count*.95));
Console.WriteLine(JsonSerializer.Serialize(new { iterations=1000, directProducerMilliseconds=directTimes.Sum(), backgroundProducerMilliseconds=backgroundTimes.Sum(), directP95Milliseconds=P95(directTimes),backgroundP95Milliseconds=P95(backgroundTimes),actualWrites,fixture=directory }));
Console.WriteLine($"{count} checks passed. Synthetic persistence workload, not whole-app CPU or frame rate.");
record Snapshot(int Sequence, string Text);
