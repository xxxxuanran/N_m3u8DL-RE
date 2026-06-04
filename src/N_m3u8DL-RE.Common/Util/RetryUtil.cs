using System.Net;
using N_m3u8DL_RE.Common.Log;
using Spectre.Console;

namespace N_m3u8DL_RE.Common.Util;

public sealed class WebRequestRetryException(string message, IReadOnlyList<Exception> attempts, int maxRetries)
    : Exception(message, attempts.LastOrDefault())
{
    public IReadOnlyList<Exception> Attempts { get; } = attempts;
    public int MaxRetries { get; } = maxRetries;
}

public static class RetryUtil
{
    public static async Task<T?> WebRequestRetryAsync<T>(
        Func<Task<T>> funcAsync,
        int maxRetries = 10,
        int retryDelayMilliseconds = 1500,
        int retryDelayIncrementMilliseconds = 0,
        CancellationToken cancellationToken = default)
    {
        var retryCount = 0;
        var result = default(T);
        var exceptions = new List<Exception>();

        while (retryCount < maxRetries)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                result = await funcAsync();
                break;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransientWebRequestException(ex))
            {
                exceptions.Add(ex);
                retryCount++;
                Logger.WarnMarkUp($"[grey]{ex.Message.EscapeMarkup()} ({retryCount}/{maxRetries})[/]");
                await Task.Delay(retryDelayMilliseconds + (retryDelayIncrementMilliseconds * (retryCount - 1)), cancellationToken);
            }
        }

        if (retryCount == maxRetries)
        {
            throw new WebRequestRetryException($"Failed to execute action after {maxRetries} retries.", exceptions, maxRetries);
        }

        return result;
    }

    private static bool IsTransientWebRequestException(Exception ex)
    {
        return ex is WebException or IOException or HttpRequestException or OperationCanceledException;
    }
}
