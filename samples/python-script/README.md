# Python Script Sample

**Simple Python script demonstrating Aspire's Python integration with automatic virtual environment management.**

This sample shows how Aspire 13's Python support automatically manages Python applications, including virtual environment creation and dependency installation.

## Quick Start

### Prerequisites

- [Aspire CLI](https://aspire.dev/get-started/install-cli/)
- [Python 3.8+](https://www.python.org/)

### Commands

```bash
aspire run      # Run locally
```

## Overview

The application consists of:

- **Aspire AppHost** - Orchestrates the Python script
- **Python Script** - Simple console application that processes messages

## Key Code

The AppHost configuration shows the minimal setup for Python scripts:

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddPythonApp("script", "./script", "main.py");

builder.Build().Run();
```

## What Aspire's Python Integration Does

When you run this sample, Aspire's Python integration automatically:

1. **Creates a Virtual Environment**:
   - Aspire detects your Python application and creates a `.venv` directory
   - This isolates the Python environment from your system Python installation

2. **Runs Your Script**:
   - Aspire executes your Python script using the virtual environment's Python interpreter
   - Example: `.venv/bin/python main.py` (or `.venv/Scripts/python.exe` on Windows)
   - **No dependencies needed**: This sample has no external dependencies, just pure Python!

3. **Provides Environment Variables**:
   - `OTEL_SERVICE_NAME`: Service name for observability
   - Other Aspire-specific variables for integration

4. **Displays Output**:
   - All console output appears in the Aspire Dashboard
   - Logs are aggregated with other services

**Note**: This sample intentionally has no `requirements.txt` or `pyproject.toml` to demonstrate running simple Python scripts with just a virtual environment and no external dependencies.

## Key Features

- **Minimal Configuration**: Just point to your Python script - Aspire handles the rest
- **Virtual Environment Management**: Automatic `.venv` creation and activation
- **No Web Server**: Demonstrates running Python console applications
- **Environment Integration**: Aspire provides standard environment variables
- **Logging**: Script output appears in the Aspire Dashboard console view

## How It Works

1. **Initialization**: Aspire creates a virtual environment in `./script/.venv`
2. **Execution**: The script runs using the virtual environment's Python (no dependencies to install!)
3. **Lifecycle**: The script runs once and completes
4. **Dashboard Integration**: All output is visible in the Aspire Dashboard

## VS Code Integration

This sample includes VS Code configuration for Python development:

- **`.vscode/settings.json`**: Configures the Python interpreter to use the Aspire-created virtual environment
- After running `aspire run`, open the sample in VS Code for full IntelliSense support
- The virtual environment at `script/.venv` will be automatically detected

## Running Without Aspire

The Python script is just a normal Python application. You can also run it directly:

```bash
cd script
python main.py
```

However, Aspire adds:
- Automatic virtual environment management
- Integrated logging and observability
- Environment variable injection
- Unified dashboard view
