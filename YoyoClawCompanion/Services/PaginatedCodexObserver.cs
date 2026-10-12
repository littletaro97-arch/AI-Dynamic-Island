using System.IO;
using System.Text.Json;
using YoyoClawCompanion.Services;

namespace YoyoClawCompanion.Services;

internal sealed record CodexSignal(CodexDecision? Pending,List<(string Key,string Text)> Completions);
internal sealed record HistoryItem(string Thread,string Turn,string Id,long CreatedAt,string Type,string Json);
internal sealed class PaginatedCodexObserver
{
    private readonly string home;
    private readonly DateTimeOffset started=DateTimeOffset.UtcNow;
    private readonly HashSet<string> seenCompletions=[];
    private readonly Queue<string> completionOrder=[];
    internal string Status {get;private set;}="未读取";
    internal int QuestionCount {get;private set;}
    internal int ActiveTurns {get;private set;}
    internal PaginatedCodexObserver(string directory)=>home=directory;
    internal CodexSignal Read()
    {
        var empty=new CodexSignal(null,[]);
        var statePath=Path.Combine(home,"state_5.sqlite");var historyPath=Path.Combine(home,"thread_history_1.sqlite");
        if(!File.Exists(statePath)||!File.Exists(historyPath)){Status="没有分页数据库 · 使用 JSONL";return empty;}
        try {
            var cutoff=DateTimeOffset.UtcNow.AddHours(-2);
            using var state=new ReadOnlySqlite(statePath);using var history=new ReadOnlySqlite(historyPath);
            var eligible=state.Query("SELECT id FROM threads WHERE history_mode='paginated' AND archived=0 AND (thread_source='user' OR thread_source IS NULL) AND updated_at>=? ORDER BY updated_at DESC LIMIT 32",cutoff.ToUnixTimeSeconds()).Select(r=>r[0]!).ToHashSet();
            var turns=history.Query("SELECT thread_id,turn_id FROM thread_turns WHERE status='inProgress'").Where(r=>eligible.Contains(r[0]!)).Select(r=>r[0]+"|"+r[1]).ToHashSet();ActiveTurns=turns.Count;
            var raw=history.Query("SELECT thread_id,turn_id,item_id,created_at_ms,item_type,item_json FROM thread_items WHERE created_at_ms>=? AND item_type IN ('agentMessage','userMessage') ORDER BY created_at_ms DESC LIMIT 512",cutoff.ToUnixTimeMilliseconds());
            var rows=raw.Where(r=>eligible.Contains(r[0]!)).Select(r=>new HistoryItem(r[0]!,r[1]!,r[2]!,long.Parse(r[3]!),r[4]!,r[5]!)).OrderBy(r=>r.CreatedAt).ToArray();
            var pending=ReadQuestions(rows,turns);QuestionCount=pending.Count;
            var completions=new List<(string,string)>();
            foreach(var row in rows.Where(r=>r.Type=="agentMessage"&&r.CreatedAt>=started.ToUnixTimeMilliseconds())) {
                using var document=JsonDocument.Parse(row.Json);var root=document.RootElement;
                if(HasQuestions(root)||Text(root,"phase")!="final_answer")continue;
                var text=Text(root,"text");var key=row.Thread+"|"+row.Id;
                if(text.Length==0||!seenCompletions.Add(key))continue;
                completionOrder.Enqueue(key);if(completionOrder.Count>256)seenCompletions.Remove(completionOrder.Dequeue());
                completions.Add((key,text.Length>2048?text[..2048]+"…":text));
            }
            Status="分页数据库只读 · 提问/答复/终结已关联";
            return new(pending.OrderBy(q=>q.CreatedAt).FirstOrDefault(),completions);
        }catch(Exception ex)when(ex is InvalidOperationException or JsonException or IOException){Status="分页读取不可用 · "+ex.GetType().Name;return empty;}
    }
    internal static List<CodexDecision> ReadQuestions(IEnumerable<HistoryItem> rows,HashSet<string> activeTurns)
    {
        var readers=new Dictionary<string,CodexDecisionReader>();
        foreach(var row in rows.OrderBy(r=>r.CreatedAt)) {
            try {
                using var document=JsonDocument.Parse(row.Json);var root=document.RootElement;
                if(!readers.TryGetValue(row.Thread,out var reader))readers[row.Thread]=reader=new();
                if(row.Type=="agentMessage"&&HasQuestions(root)&&activeTurns.Contains(row.Thread+"|"+row.Turn)) {
                    // Question cards use phase=final_answer too. They are requests, not completions.
                    var questions=root.GetProperty("questions");
                    var line=JsonSerializer.Serialize(new{timestamp=DateTimeOffset.FromUnixTimeMilliseconds(row.CreatedAt),type="response_item",payload=new{type="function_call",name="request_user_input_async",call_id=row.Id,arguments=JsonSerializer.Serialize(new{questions})}});
                    reader.Apply([line],row.Thread);
                }else if(row.Type=="userMessage"&&root.TryGetProperty("content",out var content)) {
                    var line=JsonSerializer.Serialize(new{type="response_item",payload=new{type="message",role="user",content}});
                    reader.Apply([line],row.Thread);
                }
            }catch(JsonException){}
        }
        return readers.Values.Select(r=>r.Current).Where(q=>q!=null).Cast<CodexDecision>().ToList();
    }
    private static bool HasQuestions(JsonElement root)=>root.TryGetProperty("questions",out var q)&&q.ValueKind==JsonValueKind.Array&&q.GetArrayLength()>0;
    private static string Text(JsonElement root,string property)=>root.TryGetProperty(property,out var p)&&p.ValueKind==JsonValueKind.String?p.GetString()??"":"";
}
