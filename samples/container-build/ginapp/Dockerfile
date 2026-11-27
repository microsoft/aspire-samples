# Production image (aspire publish / aspire deploy): multi-stage build into a minimal, non-root runtime image.
ARG GO_VERSION=1.27.4

# Stage 1: build a static Go binary
FROM golang:${GO_VERSION} AS build
WORKDIR /src

# Download modules first so this layer is cached until go.mod or go.sum change
COPY go.mod go.sum ./
RUN go mod download

COPY . .
RUN CGO_ENABLED=0 GOOS=linux go build -trimpath -ldflags="-s -w" -o /out/ginapp .

# Stage 2: run it on Azure Linux distroless (includes CA certificates for the OTLP exporters)
FROM mcr.microsoft.com/azurelinux/distroless/minimal:3.0
WORKDIR /app
COPY --from=build /out/ginapp .
USER 65532:65532
CMD ["./ginapp"]
