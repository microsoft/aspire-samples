import React from "react";
import { createRoot } from "react-dom/client";
import "@fontsource-variable/space-grotesk";
import "./index.css";
import App from "./components/App";

function render() {
  createRoot(document.getElementById("root")).render(
    <React.StrictMode>
      <App />
    </React.StrictMode>
  );
}

// Start browser telemetry before the first render so the first fetches are traced.
// DefinePlugin sets OTEL_ENABLED to false in production builds, so the SDK is never bundled there.
if (process.env.OTEL_ENABLED) {
  import("./telemetry")
    .then(({ initializeTelemetry }) => initializeTelemetry())
    .catch((err) => console.warn("[telemetry] disabled:", err))
    .finally(render);
} else {
  render();
}