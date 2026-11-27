const HTMLWebpackPlugin = require("html-webpack-plugin");
const { DefinePlugin } = require("webpack");

// Aspire injects these when the AppHost calls WithOtlpExporter(OtlpProtocol.HttpProtobuf).
const otlpEndpoint = process.env.OTEL_EXPORTER_OTLP_ENDPOINT;

/** Parses `key=value,key2=value2` (the OTEL_EXPORTER_OTLP_HEADERS format) into a header object. */
const parseDelimited = (value) =>
  Object.fromEntries(
    (value ?? "")
      .split(",")
      .map((pair) => pair.split("=").map((part) => part.trim()))
      .filter(([key, val]) => key && val)
  );

module.exports = (env, argv) => {
  // Browser telemetry only runs from the dev server, which proxies /otlp to the dashboard.
  const otelEnabled = argv.mode === "development" && Boolean(otlpEndpoint);

  return {
    entry: "./src/index.js",
    devServer: {
      port: env.PORT || 4001,
      allowedHosts: "all",
      proxy: [
        {
          // Same origin as the page, so the fetch instrumentation's `traceparent` header reaches the API.
          context: ["/api"],
          target:
            process.env.services__shipmentsapi__https__0 ||
            process.env.services__shipmentsapi__http__0,
          secure: false,
        },
        // Browser telemetry -> Aspire dashboard OTLP/HTTP. The API key header is added here,
        // server-side, instead of being shipped to the browser.
        ...(otelEnabled
          ? [
              {
                context: ["/otlp"],
                target: otlpEndpoint,
                changeOrigin: true,
                // The dashboard uses the ASP.NET Core development certificate locally.
                secure: false,
                headers: parseDelimited(process.env.OTEL_EXPORTER_OTLP_HEADERS),
                pathRewrite: { "^/otlp": "" },
              },
            ]
          : []),
      ],
    },
    output: {
      path: `${__dirname}/dist`,
      filename: "bundle.js",
    },
    plugins: [
      new HTMLWebpackPlugin({
        template: "./src/index.html",
      }),
      // Only non-secret values are exposed to the browser bundle.
      new DefinePlugin({
        "process.env.OTEL_ENABLED": JSON.stringify(otelEnabled),
        "process.env.OTEL_SERVICE_NAME": JSON.stringify(process.env.OTEL_SERVICE_NAME ?? "react"),
        "process.env.OTEL_RESOURCE_ATTRIBUTES": JSON.stringify(process.env.OTEL_RESOURCE_ATTRIBUTES ?? ""),
      }),
    ],
    module: {
      rules: [
        {
          test: /\.js$/,
          exclude: /node_modules/,
          use: {
            loader: "babel-loader",
            options: {
              presets: [
                "@babel/preset-env",
                ["@babel/preset-react", { runtime: "automatic" }],
              ],
            },
          },
        },
        {
          test: /\.css$/,
          use: [
            "style-loader",
            {
              loader: "css-loader",
              options: {
                modules: {
                  auto: true,
                  namedExport: false,
                  exportLocalsConvention: "as-is",
                },
              },
            },
          ],
        },
        {
          test: /\.woff2?$/,
          type: "asset/resource",
        },
      ],
    },
  };
};