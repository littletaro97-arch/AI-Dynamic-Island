using System.Globalization;
using System.Text.Json;

namespace MagicStatusbarAdapter;

internal record ProviderItem(string Name,string Value,string ShortValue,string Status,DateTimeOffset? CompletedAt,string? Preview=null);
internal record Snapshot(List<ProviderItem> Items,string? Warning);
internal record Notice(string Source,string Text,string State,DateTimeOffset Expires,int Count=1,string? Key=null,DateTimeOffset? Created=null);

internal sealed class StatusModel
{
    private readonly Dictionary<string,DateTimeOffset?> completionBaseline=new();
    private readonly DateTimeOffset started=DateTimeOffset.Now;
    private readonly List<Notice> queue=[];
    private readonly HashSet<string> delivered=[];
    private readonly Queue<string> deliveryOrder=[];
    private Notice? active,paused,attention,codexQuestion;
    private TimeSpan remaining;
    private bool visible=true;
    private DateTimeOffset nextRound=DateTimeOffset.MinValue;
    internal double RoundSeconds {get;set;}=8;
    internal Notice? CurrentNotice=>attention??active;
    internal Snapshot Current {get;private set;}=new([new("AI","等待状态","等待状态","offline",null)],null);
    internal int PendingCount=>queue.Count+(paused==null?0:1)+(OverflowCount>0?1:0);
    internal int PendingMessages=>queue.Sum(n=>n.Count)+(paused?.Count??0)+OverflowCount;
    internal DateTimeOffset? NextTransitionAt=>Blocked?null:active?.Expires??(queue.Count>0||OverflowCount>0?nextRound:null);
    internal int OverflowCount {get;private set;}
    internal void Apply(Snapshot snapshot,DateTimeOffset now)
    {
        Current=snapshot;
        UpdateAttention(now);Advance(now);
    }
    internal void SetCodexQuestion(string? id,string? prompt,DateTimeOffset now)
    {
        codexQuestion=id==null?null:new("Codex","待回答 · "+OneLine(prompt??"等待你回答",2048),"attention",DateTimeOffset.MaxValue,Key:id);
        UpdateAttention(now);
    }
    private void UpdateAttention(DateTimeOffset now)
    {
        var waiting=Current.Warning==null?Current.Items.FirstOrDefault(i=>i.Status=="attention"):null;
        var next=codexQuestion??(waiting==null?null:new Notice(waiting.Name,"等待确认","attention",DateTimeOffset.MaxValue,Key:waiting.Name+"-waiting"));
        var wasBlocked=Blocked;attention=next;
        if(!wasBlocked&&Blocked)Pause(now);
        else if(wasBlocked&&!Blocked)Resume(now);
    }
    private bool Blocked=>!visible||attention!=null;
    internal void SetVisible(bool value,DateTimeOffset now)
    {
        var wasBlocked=Blocked;visible=value;
        if(!wasBlocked&&Blocked)Pause(now);else if(wasBlocked&&!Blocked)Resume(now);
    }
    private void Pause(DateTimeOffset now){if(active==null)return;remaining=active.Expires-now;if(remaining<TimeSpan.Zero)remaining=TimeSpan.Zero;paused=active;active=null;}
    private void Resume(DateTimeOffset now){if(paused!=null){active=paused with{Expires=now+remaining};paused=null;}Advance(now);}
    internal void NewSystemNotice(string application,DateTimeOffset now,string? title=null,string? body=null,string? key=null)
    {
        var text=string.Join(" · ",new[]{OneLine(title??"",256),OneLine(body??"",2048)}.Where(s=>s.Length>0));
        Enqueue(application,text.Length>0?text:"收到新通知","notification",now,key);
    }
    internal void NewCodexCompletion(string key,string text,DateTimeOffset now)=>Enqueue("Codex","任务已完成 · "+text,"complete",now,key);
    private void Enqueue(string source,string text,string state,DateTimeOffset now,string? key)
    {
        if(key!=null){if(!delivered.Add(key))return;deliveryOrder.Enqueue(key);if(deliveryOrder.Count>256)delivered.Remove(deliveryOrder.Dequeue());}
        source=OneLine(source,48);if(source.Length==0)source="应用";text=OneLine(text,2304);
        Advance(now);
        var candidate=new Notice(source,text,state,now.AddSeconds(RoundSeconds),Key:key,Created:now);
        // One round per source across active, paused and queued groups. Arrival never resets its deadline.
        if(active is { } current&&current.Source==source) {active=candidate with{Count=current.Count+1,Expires=current.Expires,Key=current.Key};return;}
        if(paused is { } suspended&&suspended.Source==source){paused=candidate with{Count=suspended.Count+1,Expires=suspended.Expires,Key=suspended.Key};return;}
        var last=queue.FindLastIndex(n=>n.Source==source);
        if(last>=0){candidate=candidate with{Count=queue[last].Count+1,Key=queue[last].Key};queue[last]=candidate;return;}
        if(active==null&&!Blocked&&now>=nextRound){active=candidate;return;}
        if(queue.Count>=12){OverflowCount++;return;}
        queue.Add(candidate);
    }
    internal bool Expire(DateTimeOffset now){var previous=CurrentNotice;Advance(now);return previous!=CurrentNotice;}
    private void Advance(DateTimeOffset now)
    {
        if(Blocked)return;
        if(active!=null&&active.Expires>now)return;
        if(active!=null){active=null;nextRound=now.AddSeconds(2);return;}
        if(now<nextRound)return;
        if(queue.Count>0){active=queue[0] with{Expires=now.AddSeconds(RoundSeconds)};queue.RemoveAt(0);}
        else if(OverflowCount>0){active=new("通知","另有 "+OverflowCount+" 条通知未在顶部逐条展示","summary",now.AddSeconds(RoundSeconds),OverflowCount);OverflowCount=0;}
    }
    internal static string OneLine(string text,int max)
    {
        var normalized=string.Join(" ",text.Split((char[]?)null,StringSplitOptions.RemoveEmptyEntries)).Trim();
        normalized=string.Concat(normalized.Select(c=>char.IsControl(c)?' ':c));
        if(normalized.Length<=max)return normalized;
        var length=max-1;if(length>0&&char.IsHighSurrogate(normalized[length-1]))length--;
        return normalized[..length]+"…";
    }
    internal static Snapshot Parse(string json,DateTimeOffset now)
    {
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;
        var stale=!root.TryGetProperty("updatedAt",out var updated)||!updated.TryGetDateTimeOffset(out var timestamp)||now-timestamp>TimeSpan.FromSeconds(45)||timestamp>now.AddSeconds(10);
        var items=new List<ProviderItem>();
        foreach(var (key,name,quota,label) in new[]{("codex","Codex","fiveHourRemainingPercent","5小时"),("workBuddy","WorkBuddy","credits",""),("yoyo","YOYO","points","")}) {
            if(!root.TryGetProperty(key,out var data)||!Bool(data,"running"))continue;
            var value=data.TryGetProperty(quota,out var q)&&q.ValueKind==JsonValueKind.Number&&q.TryGetDouble(out var n)?(key=="codex"?n.ToString("0",CultureInfo.InvariantCulture)+"%":n.ToString("0.##",CultureInfo.InvariantCulture)):"—";
            var state=stale?"stale":Bool(data,"requiresConfirmation")?"attention":Bool(data,"busy")?"busy":"idle";
            DateTimeOffset? at=data.TryGetProperty("recentResponseAt",out var t)&&t.ValueKind==JsonValueKind.String&&t.TryGetDateTimeOffset(out var completed)?completed:null;
            var longValue=stale?"—":state=="attention"?"等待确认":(label.Length>0?label+" ":"")+value;
            var previewKey=key=="yoyo"?"result":"summary";
            var preview=key!="codex"&&data.TryGetProperty(previewKey,out var p)&&p.ValueKind==JsonValueKind.String?OneLine(p.GetString()??"",2048):null;
            items.Add(new(name,longValue,stale?"—":state=="attention"?"待确认":value,state,at,preview));
        }
        if(root.TryGetProperty("deepSeek",out var deep)&&(Bool(deep,"IsRunning")||Bool(deep,"running"))) {
            var dsState=deep.TryGetProperty("State",out var s)&&s.ValueKind==JsonValueKind.String?s.GetString():"unknown";
            var available=Bool(deep,"Available")||Bool(deep,"available");
            var state=stale?"stale":!available?"unknown":dsState=="decision"?"attention":dsState=="running"?"busy":"idle";
            var value=state switch{"stale"=>"—","unknown"=>"状态未知","attention"=>"等待确认","busy"=>"执行中",_=>"空闲"};
            items.Add(new("DeepSeek Harness",value,value,state,null));
        }
        if(items.Count==0)items.Add(new("AI","未运行","未运行","offline",null));
        return new(items,stale?"状态已过期":null);
    }
    private static bool Bool(JsonElement element,string key)=>element.TryGetProperty(key,out var value)&&value.ValueKind==JsonValueKind.True;
}
