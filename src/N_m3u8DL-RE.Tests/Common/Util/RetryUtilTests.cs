using N_m3u8DL_RE.Common.Util;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Common.Util;

public class RetryUtilTests
{
    [Fact]
    public async Task WebRequestRetryAsync_NonUserCancellation_Retries()
    {
        var attempts = 0;

        var result = await RetryUtil.WebRequestRetryAsync(async () =>
        {
            attempts++;
            await Task.Yield();
            if (attempts == 1)
                throw new TaskCanceledException("request timed out");

            return true;
        }, maxRetries: 2, retryDelayMilliseconds: 1);

        result.ShouldBeTrue();
        attempts.ShouldBe(2);
    }

    [Fact]
    public async Task WebRequestRetryAsync_UserCancellation_DoesNotRetry()
    {
        var attempts = 0;
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Should.ThrowAsync<OperationCanceledException>(() => RetryUtil.WebRequestRetryAsync(async () =>
        {
            attempts++;
            await Task.Yield();
            return true;
        }, maxRetries: 2, retryDelayMilliseconds: 1, cancellationToken: cts.Token));

        attempts.ShouldBe(0);
    }
}
