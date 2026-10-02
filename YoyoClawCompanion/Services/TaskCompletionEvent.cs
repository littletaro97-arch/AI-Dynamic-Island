namespace YoyoClawCompanion.Services;

internal sealed record TaskCompletionEvent(string Id, string Response, DateTimeOffset CompletedAt);
