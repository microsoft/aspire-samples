using OpenTelemetry.Trace;

namespace ImageGallery.FrontEnd.Claims;

// PhotoResultsWorker polls the results queue every second. Drop those root-level Azure SDK spans so the dashboard
// only shows traces for real work; spans that belong to an upload still follow their parent's sampling decision.
public sealed class SkipIdlePollingSampler : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.ParentContext.TraceId == default && samplingParameters.Name.StartsWith("QueueClient.", StringComparison.Ordinal)
            ? new SamplingResult(SamplingDecision.Drop)
            : _inner.ShouldSample(samplingParameters);
}
