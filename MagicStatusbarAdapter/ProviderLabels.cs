namespace MagicStatusbarAdapter;

internal static class ProviderLabels
{
    internal static string Short(string name)=>name switch{"WorkBuddy"=>"WB","DeepSeek Harness"=>"DSH",_=>name};
    internal static string? ProcessName(string name)=>name switch{"Codex"=>"ChatGPT","WorkBuddy"=>"WorkBuddy","YOYO"=>"HnMagicClawUI","DeepSeek Harness"=>"DeepSeek Harness",_=>null};
}
