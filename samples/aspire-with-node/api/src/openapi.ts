/**
 * OpenAPI 3.1 description of the Weather Explorer API.
 *
 * This document powers the interactive Scalar API reference served at `/reference`
 * and is also exposed as raw JSON at `/openapi.json` for external tooling. Keep it
 * in sync with the route handlers in `index.ts`.
 */

const errorSchema = {
  type: "object",
  properties: {
    error: { type: "string", description: "Human-readable failure message." },
  },
  required: ["error"],
};

export const openApiDocument = {
  openapi: "3.1.0",
  info: {
    title: "Aspire Weather Explorer API",
    version: "1.0.0",
    description:
      "Server-side weather and geocoding endpoints for the Aspire Weather Explorer.\n\n" +
      "Current conditions and forecasts come from [Open-Meteo](https://open-meteo.com); " +
      "place lookups use Open-Meteo geocoding and BigDataCloud reverse geocoding. " +
      "All upstreams are keyless.",
  },
  servers: [{ url: "/", description: "This API instance" }],
  tags: [
    { name: "Weather", description: "Current conditions and multi-day forecasts." },
    { name: "Geocoding", description: "Turn place names into coordinates." },
    { name: "System", description: "Operational endpoints." },
  ],
  paths: {
    "/api/weather": {
      get: {
        tags: ["Weather"],
        summary: "Current conditions and 5-day forecast",
        description:
          "Returns current conditions, the next 12 hours, and a 5-day forecast for a " +
          "coordinate, plus a best-effort place name from reverse geocoding.",
        operationId: "getWeather",
        parameters: [
          {
            name: "lat",
            in: "query",
            required: true,
            description: "Latitude in decimal degrees.",
            schema: { type: "number", minimum: -90, maximum: 90, examples: [47.6] },
          },
          {
            name: "lon",
            in: "query",
            required: true,
            description: "Longitude in decimal degrees.",
            schema: { type: "number", minimum: -180, maximum: 180, examples: [-122.3] },
          },
        ],
        responses: {
          "200": {
            description: "Weather forecast for the requested coordinate.",
            content: { "application/json": { schema: { $ref: "#/components/schemas/Weather" } } },
          },
          "400": {
            description: "Missing or out-of-range coordinates.",
            content: { "application/json": { schema: { $ref: "#/components/schemas/Error" } } },
          },
          "502": {
            description: "The upstream weather provider could not be reached.",
            content: { "application/json": { schema: { $ref: "#/components/schemas/Error" } } },
          },
        },
      },
    },
    "/api/geocode": {
      get: {
        tags: ["Geocoding"],
        summary: "Search for places by name",
        description: "Returns up to five coordinate matches for a place-name query.",
        operationId: "geocode",
        parameters: [
          {
            name: "q",
            in: "query",
            required: true,
            description: "Place name to search for (minimum 2 characters).",
            schema: { type: "string", minLength: 2, examples: ["Seattle"] },
          },
        ],
        responses: {
          "200": {
            description: "Matching places (may be empty).",
            content: { "application/json": { schema: { $ref: "#/components/schemas/GeocodeResults" } } },
          },
          "400": {
            description: "Query too short or missing.",
            content: { "application/json": { schema: { $ref: "#/components/schemas/Error" } } },
          },
          "502": {
            description: "The upstream geocoding provider could not be reached.",
            content: { "application/json": { schema: { $ref: "#/components/schemas/Error" } } },
          },
        },
      },
    },
    "/health": {
      get: {
        tags: ["System"],
        summary: "Liveness probe",
        description: "Returns `Healthy` when the service is up. Used by the Aspire health check.",
        operationId: "health",
        responses: {
          "200": {
            description: "The service is healthy.",
            content: { "text/plain": { schema: { type: "string", examples: ["Healthy"] } } },
          },
        },
      },
    },
  },
  components: {
    schemas: {
      Error: errorSchema,
      Location: {
        type: "object",
        description: "Resolved place metadata for the requested coordinate.",
        properties: {
          name: { type: ["string", "null"], description: "City or locality name, if known." },
          region: { type: ["string", "null"], description: "State, province, or region." },
          country: { type: ["string", "null"] },
          latitude: { type: "number" },
          longitude: { type: "number" },
          timezone: { type: ["string", "null"], description: "IANA timezone reported by the provider." },
        },
        required: ["latitude", "longitude"],
      },
      Current: {
        type: "object",
        description: "Current conditions. Temperatures are in Celsius; the client converts as needed.",
        properties: {
          temperatureC: { type: ["number", "null"] },
          apparentC: { type: ["number", "null"], description: "Apparent ('feels like') temperature." },
          humidity: { type: ["number", "null"], description: "Relative humidity, percent." },
          weatherCode: { type: "integer", description: "WMO weather interpretation code." },
          windSpeedKph: { type: ["number", "null"] },
          uvIndex: { type: ["number", "null"] },
          isDay: { type: "boolean", description: "True when the sun is up at the location." },
          time: { type: ["string", "null"], description: "Local observation time (ISO 8601)." },
        },
      },
      Sun: {
        type: "object",
        properties: {
          sunrise: { type: ["string", "null"], description: "Local sunrise time (ISO 8601)." },
          sunset: { type: ["string", "null"], description: "Local sunset time (ISO 8601)." },
        },
      },
      HourlyPoint: {
        type: "object",
        properties: {
          time: { type: "string", description: "Local hour (ISO 8601)." },
          temperatureC: { type: ["number", "null"] },
          weatherCode: { type: "integer", description: "WMO weather interpretation code." },
        },
        required: ["time"],
      },
      DailyPoint: {
        type: "object",
        properties: {
          date: { type: "string", description: "Local date (ISO 8601)." },
          weatherCode: { type: "integer", description: "WMO weather interpretation code." },
          maxC: { type: ["number", "null"], description: "Daily high in Celsius." },
          minC: { type: ["number", "null"], description: "Daily low in Celsius." },
          precipitationProb: { type: ["number", "null"], description: "Max precipitation probability, percent." },
        },
        required: ["date"],
      },
      Weather: {
        type: "object",
        properties: {
          location: { $ref: "#/components/schemas/Location" },
          current: { $ref: "#/components/schemas/Current" },
          sun: { $ref: "#/components/schemas/Sun" },
          hourly: {
            type: "array",
            description: "Up to 12 upcoming hours starting at the current hour.",
            items: { $ref: "#/components/schemas/HourlyPoint" },
          },
          daily: {
            type: "array",
            description: "5-day forecast starting today.",
            items: { $ref: "#/components/schemas/DailyPoint" },
          },
        },
        required: ["location", "current", "sun", "hourly", "daily"],
      },
      GeocodeResult: {
        type: "object",
        properties: {
          name: { type: "string" },
          region: { type: ["string", "null"], description: "State, province, or region." },
          country: { type: ["string", "null"] },
          latitude: { type: "number" },
          longitude: { type: "number" },
        },
        required: ["name", "latitude", "longitude"],
      },
      GeocodeResults: {
        type: "object",
        properties: {
          results: {
            type: "array",
            items: { $ref: "#/components/schemas/GeocodeResult" },
          },
        },
        required: ["results"],
      },
    },
  },
};
