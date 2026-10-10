using System.Text.Json;
using YoyoClawCompanion.Services;
int count=0;
void Check(bool value,string name){if(!value)throw new Exception(name);count++;Console.WriteLine("PASS "+name);}
string Record(object payload,string type="response_item")=>JsonSerializer.Serialize(new{timestamp=DateTimeOffset.UtcNow,type,payload});
string Ask(string id)=>Record(new{type="function_call",name="request_user_input_async",call_id=id,arguments=JsonSerializer.Serialize(new{questions=new[]{new{title="Test question"}}})});
string Output(string id,object output)=>Record(new{type="function_call_output",call_id=id,output=JsonSerializer.Serialize(output)});
string Event(string type)=>Record(new{type},"event_msg");
var reader=new CodexDecisionReader();
reader.Apply([Ask("a"),Output("a",new{accepted=true})],"session");
Check(reader.Current is not null,"delivery acknowledgement remains pending");
reader.Apply([Event("task_complete")],"session");
Check(reader.Current is null && !reader.PendingCallIds.Any(),"normal completion clears unanswered question");
reader.Apply([Ask("b"),Event("turn_aborted")],"session");
Check(reader.Current is null,"explicit abort still clears question");
reader.Apply([Ask("c"),Output("c",new{answers=new{q="yes"}})],"session");
Check(reader.Current is null,"actual answer clears question");
reader.Apply([Ask("d"),Output("d",new{error="cancelled"})],"session");
Check(reader.Current is null,"tool error clears question");
reader.Apply([Ask("e"),Event("task_started")],"session");
Check(reader.Current is not null,"nonterminal event does not invent cancellation");
reader.Apply([Record(new{type="message",role="assistant",content=new[]{new{type="output_text",text="task_complete"}}})],"session");
Check(reader.Current is not null,"text mentioning completion cannot clear real question");
reader.Apply([Ask("f"),Event("task_complete"),Ask("g")],"session");
Check(reader.Current?.Id=="session|g","later turn can ask normally");
var other=new CodexDecisionReader();other.Apply([Ask("other")],"other-session");
reader.Apply([Event("task_complete")],"session");
Check(other.Current is not null,"one session ending does not clear parallel session question");
var fixture=Path.Combine(Path.GetTempPath(),"IslandDecisionChecks-"+Guid.NewGuid().ToString("N")+".jsonl");
File.WriteAllText(fixture,Ask("incremental")+"\n"+Output("incremental",new{accepted=true})+"\n");
var cursor=new JsonLineCursor();var incremental=new CodexDecisionReader();
incremental.Apply(cursor.Read(fixture,262144,incremental.ReadMarkers),"incremental-session");
Check(incremental.Current is not null,"initial cursor captures pending question");
File.AppendAllText(fixture,Event("task_complete")+"\n");
incremental.Apply(cursor.Read(fixture,262144,incremental.ReadMarkers),"incremental-session");
Check(incremental.Current is null,"incremental marker admits terminal event");
var replay=Environment.GetEnvironmentVariable("ISLAND_DECISION_REPLAY");
if(!string.IsNullOrEmpty(replay))
{
    var fixedReader=new CodexDecisionReader();var original=new CodexDecisionReader();
    using var stream = new FileStream(replay,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
    using var source = new StreamReader(stream);
    while(source.ReadLine() is { } line)
    {
        using var doc=JsonDocument.Parse(line);
        if(!doc.RootElement.TryGetProperty("timestamp",out var stamp)||!DateTimeOffset.TryParse(stamp.GetString(),out var at)||at>DateTimeOffset.Parse("2026-10-10T10:54:00Z"))continue;
        fixedReader.Apply([line],"replay");
        var terminal=doc.RootElement.GetProperty("type").GetString()=="event_msg" && doc.RootElement.GetProperty("payload").TryGetProperty("type",out var eventType) && eventType.GetString()=="task_complete";
        if(!terminal)original.Apply([line],"replay");
    }
    Check(original.PendingCallIds.Any()&&!fixedReader.PendingCallIds.Any(),"real pre-18:54 trace reproduces old stale question and clears after fix");
}
Console.WriteLine($"{count} checks passed; no account mutation or application launch.");
