// Aspire injects these when the AppHost calls WithOtlpExporter(OtlpProtocol.HttpProtobuf).
const otlpEndpoint = process.env["OTEL_EXPORTER_OTLP_ENDPOINT"];

/** Parses `key=value,key2=value2` (the OTEL_EXPORTER_OTLP_HEADERS format) into a header object. */
const parseDelimited = (value) =>
  Object.fromEntries(
    (value ?? "")
      .split(",")
      .map((pair) => pair.split("=").map((part) => part.trim()))
      .filter(([key, val]) => key && val),
  );

module.exports = {
  // Same origin as the page, so the fetch instrumentation's `traceparent` header reaches the API.
  "/api": {
    target:
      process.env["services__shipmentsapi__https__0"] ||
      process.env["services__shipmentsapi__http__0"],
    secure: process.env["NODE_ENV"] !== "development"
  },
  // angular.json can't read environment variables, so the dev server answers this request itself
  // with the non-secret telemetry settings that src/main.ts reads before bootstrapping.
  "/otel-config.json": {
    target: "http://localhost",
    bypass: (req, res) => {
      res.setHeader("Content-Type", "application/json");
      res.setHeader("Cache-Control", "no-store");
      res.end(
        JSON.stringify({
          enabled: Boolean(otlpEndpoint),
          serviceName: process.env["OTEL_SERVICE_NAME"] ?? "angular",
          resourceAttributes: parseDelimited(process.env["OTEL_RESOURCE_ATTRIBUTES"]),
        }),
      );
      return req.url;
    },
  },
  // Browser telemetry -> Aspire dashboard OTLP/HTTP. The API key header is added here,
  // server-side, instead of being shipped to the browser.
  ...(otlpEndpoint && {
    "/otlp": {
      target: otlpEndpoint,
      changeOrigin: true,
      // The dashboard uses the ASP.NET Core development certificate locally.
      secure: false,
      headers: parseDelimited(process.env["OTEL_EXPORTER_OTLP_HEADERS"]),
      pathRewrite: { "^/otlp": "" },
    },
  }),
};
