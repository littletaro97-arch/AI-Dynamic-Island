using System.Runtime.InteropServices;

namespace YoyoClawCompanion.Services;

// Windows ships this SQLite API. Open existing databases READONLY; no schema or journal changes.
internal sealed class ReadOnlySqlite : IDisposable
{
    private nint db;
    internal ReadOnlySqlite(string path)
    {
        var error=sqlite3_open_v2(path,out db,1|0x40,0);
        if(error!=0){Dispose();throw new InvalidOperationException("SQLite readonly open failed: "+error);}
        sqlite3_busy_timeout(db,100);
    }
    internal List<string?[]> Query(string sql,long? parameter=null)
    {
        var error=sqlite3_prepare_v2(db,sql,-1,out var statement,0);
        if(error!=0)throw new InvalidOperationException("SQLite readonly query incompatible: "+error);
        try {
            if(parameter.HasValue)sqlite3_bind_int64(statement,1,parameter.Value);
            var rows=new List<string?[]>();int code;
            while((code=sqlite3_step(statement))==100){var row=new string?[sqlite3_column_count(statement)];for(var i=0;i<row.Length;i++)row[i]=Marshal.PtrToStringUTF8(sqlite3_column_text(statement,i));rows.Add(row);}
            if(code!=101)throw new InvalidOperationException("SQLite readonly query unavailable: "+code);
            return rows;
        }finally{sqlite3_finalize(statement);}
    }
    public void Dispose(){if(db!=0){sqlite3_close(db);db=0;}}
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_open_v2([MarshalAs(UnmanagedType.LPUTF8Str)]string path,out nint db,int flags,nint vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_close(nint db);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_busy_timeout(nint db,int ms);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_prepare_v2(nint db,[MarshalAs(UnmanagedType.LPUTF8Str)]string sql,int bytes,out nint statement,nint tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_step(nint statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_finalize(nint statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_column_count(nint statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern nint sqlite3_column_text(nint statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)] private static extern int sqlite3_bind_int64(nint statement,int index,long value);
}
