param(
    [string]$Provider = "workbuddy"
)

$ErrorActionPreference = "Stop"
$eventName = "Local\YoyoClawCompanion.ProviderStatusChanged"

try {
    $signal = [System.Threading.EventWaitHandle]::OpenExisting($eventName)
    try {
        [void]$signal.Set()
    }
    finally {
        $signal.Dispose()
    }
    exit 0
}
catch [System.Threading.WaitHandleCannotBeOpenedException] {
    # Hooks must not start the island or fail a provider task. The normal polling
    # path remains the fallback whenever the island is not running.
    exit 0
}
catch {
    # Provider hooks are advisory. Any notifier failure must leave WorkBuddy's
    # own task result untouched and let the fallback poll recover naturally.
    exit 0
}
