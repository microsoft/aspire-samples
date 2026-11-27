using OpenTelemetry.Trace;

namespace Notes.Api;

// The AppHost polls /health, whose Redis health check sends PING outside any traced request.
// Drop those root-level spans so the dashboard only shows traces for note operations;
// Redis commands inside a request still follow their parent's sampling decision.
public sealed class SkipHealthCheckPingSampler : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.ParentContext.TraceId == default && samplingParameters.Name == "PING"
            ? new SamplingResult(SamplingDecision.Drop)
            : _inner.ShouldSample(samplingParameters);
}
