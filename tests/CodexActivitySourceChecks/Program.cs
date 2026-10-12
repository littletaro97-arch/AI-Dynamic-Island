using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using YoyoClawCompanion;
class Program
{
    const BindingFlags Flags=BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic;
    static int checks;
    static void Check(bool value,string name){if(!value)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_exec(IntPtr db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,IntPtr callback,IntPtr arg,IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] static extern int sqlite3_close(IntPtr db);
    // Only write generated fixtures. The production reader opens these READONLY.
    static void Seed(string path,string sql){if(sqlite3_open_v2(path,out var db,6,IntPtr.Zero)!=0)throw new Exception("fixture open");try{if(sqlite3_exec(db,sql,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=0)throw new Exception("fixture SQL");}finally{sqlite3_close(db);}}
    static void Main()
    {
        var root=Path.Combine(Path.GetTempPath(),"CodexActivitySourceChecks-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"sessions"));
        var previous=Environment.GetEnvironmentVariable("CODEX_HOME");Environment.SetEnvironmentVariable("CODEX_HOME",root);
        IDisposable? service=null;
        try{
            var now=DateTimeOffset.UtcNow;var log=Path.Combine(root,"sessions","long-paginated.jsonl");
            var start=JsonSerializer.Serialize(new{timestamp=now.AddMinutes(-18),type="event_msg",payload=new{type="task_started"}});
            // A long turn grows beyond the 1 MiB lifecycle tail while its file mtime remains old.
            var body=JsonSerializer.Serialize(new{timestamp=now,type="response_item",payload=new{type="custom_tool_call_output",output=new string('x',1300000)}});
            File.WriteAllText(log,start+"\n"+body+"\n");File.SetLastWriteTimeUtc(log,now.AddHours(-2).UtcDateTime);
            Seed(Path.Combine(root,"state_5.sqlite"),$"CREATE TABLE threads(id TEXT,history_mode TEXT,archived INTEGER,thread_source TEXT,updated_at INTEGER);INSERT INTO threads VALUES('user','paginated',0,'user',{now.ToUnixTimeSeconds()});INSERT INTO threads VALUES('guardian','paginated',0,'guardian_review',{now.ToUnixTimeSeconds()});");
            var history=Path.Combine(root,"thread_history_1.sqlite");
            Seed(history,"CREATE TABLE thread_turns(thread_id TEXT,turn_id TEXT,status TEXT);CREATE TABLE thread_items(thread_id TEXT,turn_id TEXT,item_id TEXT,created_at_ms INTEGER,item_type TEXT,item_json TEXT);INSERT INTO thread_turns VALUES('user','turn','inProgress');INSERT INTO thread_turns VALUES('guardian','approval','inProgress');");
            var type=typeof(App).Assembly.GetType("YoyoClawCompanion.Services.CodexStatusService")!;
            service=(IDisposable)Activator.CreateInstance(type,true)!;
            var legacy=type.GetMethod("ReadLifecycle",Flags)!.Invoke(service,new object[]{new FileInfo(log),now})!;
            Check(legacy.GetType().GetProperty("State")!.GetValue(legacy)==null,"legacy cold tail reproduces missing busy marker");
            object Read(bool running)=>(type.GetMethod("ReadStatus",Flags)!.Invoke(service,new object?[]{true,false,running}))!;
            bool Busy(object status)=>(bool)status.GetType().GetProperty("IsBusy")!.GetValue(status)!;
            Check(Busy(Read(true)),"shared paginated source keeps long turn busy despite missing JSONL marker");
            Check(!Busy(Read(false)),"closing Codex cannot retain cached paginated busy flag");
            Seed(history,"UPDATE thread_turns SET status='completed' WHERE thread_id='user';");
            Check(!Busy(Read(true)),"completed user turn returns idle even while guardian review remains active");
            Seed(history,"UPDATE thread_turns SET status='inProgress' WHERE thread_id='user';");
            Check(Busy(Read(true)),"later user execution resumes busy");
            Seed(history,"UPDATE thread_turns SET status='interrupted' WHERE thread_id='user';");
            Check(!Busy(Read(true)),"interrupted turn clears busy");
            Console.WriteLine($"{checks} activity source checks passed; all database writes confined to generated fixture directory.");
        }finally{service?.Dispose();Environment.SetEnvironmentVariable("CODEX_HOME",previous);Directory.Delete(root,true);}
    }
}
