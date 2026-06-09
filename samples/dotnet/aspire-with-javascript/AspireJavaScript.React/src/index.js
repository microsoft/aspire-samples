import React from "react";
import { createRoot } from "react-dom/client";
import "@fontsource-variable/space-grotesk";
import "./index.css";
import App from "./components/App";

const container = document.getElementById("root");
const root = createRoot(container);
root.render(
  <React.StrictMode>
    <App />
  </React.StrictMode>
);
