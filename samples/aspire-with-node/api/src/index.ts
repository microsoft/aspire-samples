/**
 * Import the OpenTelemetry instrumentation setup first, before any other modules.
 * This ensures all subsequent imports are automatically instrumented for
 * distributed tracing, metrics, and logging in the Aspire dashboard.
 */
import "./instrumentation.ts";
import express from "express";
import { existsSync } from "fs";
import { join } from "path";
import { apiReference } from "@scalar/express-api-reference";
import { openApiDocument } from "./openapi.ts";

const app = express();
const port = process.env.PORT || 5000;

// Base URL for the Open-Meteo forecast API. Injected by the Aspire AppHost, which
// models Open-Meteo as an external service; falls back to the public endpoint.
const OPEN_METEO_URL = (process.env.OPEN_METEO_URL || "https://api.open-meteo.com").replace(/\/+$/, "");
// Keyless reverse geocoding used to turn map coordinates into a friendly place name.
const REVERSE_GEOCODE_URL = "https://api.bigdatacloud.net/data/reverse-geocode-client";
// Keyless forward geocoding (place name -> coordinates) for the search box.
const GEOCODING_URL = "https://geocoding-api.open-meteo.com/v1/search";

interface OpenMeteoCurrent {
  time?: string;
  temperature_2m?: number;
  apparent_temperature?: number;
  relative_humidity_2m?: number;
  weather_code?: number;
  wind_speed_10m?: number;
  is_day?: number;
  uv_index?: number;
}

interface OpenMeteoHourly {
  time?: string[];
  temperature_2m?: (number | null)[];
  weather_code?: number[];
}

interface OpenMeteoDaily {
  time?: string[];
  weather_code?: number[];
  temperature_2m_max?: number[];
  temperature_2m_min?: number[];
  precipitation_probability_max?: (number | null)[];
  sunrise?: string[];
  sunset?: string[];
}

interface OpenMeteoForecast {
  timezone?: string;
  current?: OpenMeteoCurrent;
  hourly?: OpenMeteoHourly;
  daily?: OpenMeteoDaily;
}

interface PlaceName {
  name: string | null;
  region: string | null;
  country: string | null;
}

/** Best-effort reverse geocode; returns null if the lookup fails or times out. */
async function reverseGeocode(lat: number, lon: number): Promise<PlaceName | null> {
  try {
    const url = new URL(REVERSE_GEOCODE_URL);
    url.searchParams.set("latitude", lat.toString());
    url.searchParams.set("longitude", lon.toString());
    url.searchParams.set("localityLanguage", "en");
    const response = await fetch(url, { signal: AbortSignal.timeout(2500) });
    if (!response.ok) return null;
    const data = (await response.json()) as {
      city?: string;
      locality?: string;
      principalSubdivision?: string;
      countryName?: string;
    };
    return {
      name: data.city || data.locality || data.principalSubdivision || null,
      region: data.principalSubdivision || null,
      country: data.countryName || null,
    };
  } catch {
    return null;
  }
}

/**
 * Returns the real current conditions and 5-day forecast for a coordinate.
 * Coordinates come from the map's current position on the client.
 */
app.get("/api/weather", async (req, res) => {
  const lat = Number(req.query.lat);
  const lon = Number(req.query.lon);
  if (!Number.isFinite(lat) || !Number.isFinite(lon) || lat < -90 || lat > 90 || lon < -180 || lon > 180) {
    res.status(400).json({ error: "Query params 'lat' (-90..90) and 'lon' (-180..180) are required." });
    return;
  }

  try {
    const url = new URL("/v1/forecast", OPEN_METEO_URL);
    url.searchParams.set("latitude", lat.toString());
    url.searchParams.set("longitude", lon.toString());
    url.searchParams.set("current", "temperature_2m,apparent_temperature,relative_humidity_2m,weather_code,wind_speed_10m,is_day,uv_index");
    url.searchParams.set("hourly", "temperature_2m,weather_code");
    url.searchParams.set("daily", "weather_code,temperature_2m_max,temperature_2m_min,precipitation_probability_max,sunrise,sunset");
    url.searchParams.set("timezone", "auto");
    url.searchParams.set("forecast_days", "5");

    const [forecastResponse, place] = await Promise.all([
      fetch(url, { signal: AbortSignal.timeout(8000) }),
      reverseGeocode(lat, lon),
    ]);
    if (!forecastResponse.ok) {
      throw new Error(`Open-Meteo responded ${forecastResponse.status}`);
    }

    const data = (await forecastResponse.json()) as OpenMeteoForecast;
    const current = data.current ?? {};
    const daily = data.daily ?? {};
    const days = (daily.time ?? []).slice(0, 5).map((date, i) => ({
      date,
      weatherCode: daily.weather_code?.[i] ?? 0,
      maxC: daily.temperature_2m_max?.[i] ?? null,
      minC: daily.temperature_2m_min?.[i] ?? null,
      precipitationProb: daily.precipitation_probability_max?.[i] ?? null,
    }));

    // Upcoming hours starting from the current hour (times are already local to the place).
    const hourly = data.hourly ?? {};
    const hourlyTimes = hourly.time ?? [];
    const nowHour = (current.time ?? "").slice(0, 13);
    let startIdx = hourlyTimes.findIndex((t) => t.slice(0, 13) >= nowHour);
    if (startIdx < 0) startIdx = 0;
    const hours = hourlyTimes.slice(startIdx, startIdx + 12).map((time, i) => ({
      time,
      temperatureC: hourly.temperature_2m?.[startIdx + i] ?? null,
      weatherCode: hourly.weather_code?.[startIdx + i] ?? 0,
    }));

    res.json({
      location: {
        name: place?.name ?? null,
        region: place?.region ?? null,
        country: place?.country ?? null,
        latitude: lat,
        longitude: lon,
        timezone: data.timezone ?? null,
      },
      current: {
        temperatureC: current.temperature_2m ?? null,
        apparentC: current.apparent_temperature ?? null,
        humidity: current.relative_humidity_2m ?? null,
        weatherCode: current.weather_code ?? 0,
        windSpeedKph: current.wind_speed_10m ?? null,
        uvIndex: current.uv_index ?? null,
        isDay: current.is_day === 1,
        time: current.time ?? null,
      },
      sun: {
        sunrise: daily.sunrise?.[0] ?? null,
        sunset: daily.sunset?.[0] ?? null,
      },
      hourly: hours,
      daily: days,
    });
  } catch (err) {
    console.error("Failed to load weather forecast:", err);
    res.status(502).json({ error: "Failed to load weather data for this location." });
  }
});

/** Searches for places by name and returns up to five coordinate matches. */
app.get("/api/geocode", async (req, res) => {
  const query = typeof req.query.q === "string" ? req.query.q.trim() : "";
  if (query.length < 2) {
    res.status(400).json({ error: "Query param 'q' (min 2 characters) is required." });
    return;
  }

  try {
    const url = new URL(GEOCODING_URL);
    url.searchParams.set("name", query);
    url.searchParams.set("count", "5");
    url.searchParams.set("language", "en");
    url.searchParams.set("format", "json");

    const response = await fetch(url, { signal: AbortSignal.timeout(6000) });
    if (!response.ok) {
      throw new Error(`Geocoding responded ${response.status}`);
    }

    const data = (await response.json()) as {
      results?: Array<{ name: string; admin1?: string; country?: string; latitude: number; longitude: number }>;
    };
    const results = (data.results ?? []).map((result) => ({
      name: result.name,
      region: result.admin1 ?? null,
      country: result.country ?? null,
      latitude: result.latitude,
      longitude: result.longitude,
    }));

    res.json({ results });
  } catch (err) {
    console.error("Failed to geocode place:", err);
    res.status(502).json({ error: "Failed to search for that place." });
  }
});

app.get("/health", (_req, res) => {
  res.send("Healthy");
});

// Expose the raw OpenAPI 3.1 document for external tooling and clients.
app.get("/openapi.json", (_req, res) => {
  res.json(openApiDocument);
});

// Render an interactive Scalar API reference from the OpenAPI document at /reference.
app.use(
  "/reference",
  apiReference({
    content: openApiDocument,
    theme: "deepSpace",
    layout: "modern",
    _integration: "express",
    metaData: { title: "Aspire Weather Explorer API" },
  }),
);

// Serve static files from the "static" directory if it exists (used in publish/deploy mode
// when the frontend's build output is bundled into this container via publishWithContainerFiles)
const staticDir = join(import.meta.dirname, "..", "static");
if (existsSync(staticDir)) {
  app.use(express.static(staticDir));
} else {
  // Dev mode (no bundled SPA): point the API root at the Scalar reference instead of
  // Express's default "Cannot GET /" response.
  app.get("/", (_req, res) => {
    res.redirect("/reference");
  });
}

app.listen(port, () => {
  console.log(`API server listening on port ${port}`);
});
