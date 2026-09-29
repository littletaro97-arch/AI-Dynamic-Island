namespace YoyoClawCompanion.Services;

internal sealed record ProviderInstallationState(string Key, string DisplayName, bool IsInstalled, string? ExecutablePath);
