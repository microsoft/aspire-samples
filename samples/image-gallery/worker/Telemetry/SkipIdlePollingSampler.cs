using OpenTelemetry.Trace;

namespace Worker.Telemetry;

// The worker polls the queue every few seconds. Drop those root-level Azure SDK spans so the dashboard only
// shows traces for real work; spans that belong to a thumbnail request still follow their parent's decision.
public sealed class SkipIdlePollingSampler : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.ParentContext.TraceId == default && samplingParameters.Name.StartsWith("QueueClient.", StringComparison.Ordinal)
            ? new SamplingResult(SamplingDecision.Drop)
            : _inner.ShouldSample(samplingParameters);
}
