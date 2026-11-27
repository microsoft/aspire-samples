using OpenTelemetry.Trace;

namespace Api.Services;

// The AppHost polls /health, whose EF Core health check runs "SELECT 1" outside any traced request.
// Drop those root-level PostgreSQL spans so the dashboard only shows traces for real work;
// database spans inside a request still follow their parent's decision.
public sealed class SkipHealthProbeQueriesSampler : Sampler
{
    private readonly Sampler _inner = new ParentBasedSampler(new AlwaysOnSampler());

    public override SamplingResult ShouldSample(in SamplingParameters samplingParameters) =>
        samplingParameters.ParentContext.TraceId == default && samplingParameters.Name == "postgresql"
            ? new SamplingResult(SamplingDecision.Drop)
            : _inner.ShouldSample(samplingParameters);
}