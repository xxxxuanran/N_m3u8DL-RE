using N_m3u8DL_RE.Util.LiveRecord.SubTask;
using Shouldly;

namespace N_m3u8DL_RE.Tests.Util.LiveRecord.SubTask;

public class LiveGapFillBatchTrackerTests
{
    [Fact]
    public void MarkFilled_AllSegmentsFilled_ReturnsBatchCompletionOnce()
    {
        var tracker = new LiveGapFillBatchTracker();
        var batchId = tracker.AddBatch(413296553, 413296579, 27);

        for (var i = 0; i < 26; i++)
        {
            tracker.MarkFilled(batchId).ShouldBeNull();
        }

        var completion = tracker.MarkFilled(batchId);

        completion.ShouldNotBeNull();
        completion.Value.Start.ShouldBe(413296553);
        completion.Value.End.ShouldBe(413296579);
        completion.Value.Count.ShouldBe(27);
        tracker.MarkFilled(batchId).ShouldBeNull();
    }

    [Fact]
    public void MarkLost_AnySegmentLost_SuppressesBatchCompletion()
    {
        var tracker = new LiveGapFillBatchTracker();
        var batchId = tracker.AddBatch(100, 102, 3);

        tracker.MarkFilled(batchId).ShouldBeNull();
        tracker.MarkLost(batchId);

        tracker.MarkFilled(batchId).ShouldBeNull();
        tracker.MarkFilled(batchId).ShouldBeNull();
    }
}
