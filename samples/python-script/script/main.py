"""Simple Python script demonstrating Aspire Python support."""

import time
import os
from datetime import datetime

# Get service name from Aspire
service_name = os.getenv("OTEL_SERVICE_NAME", "script")

print(f"✓ {service_name} started at {datetime.now()}")
print()

# Simple message processor
messages = [
    "Processing data batch 1...",
    "Processing data batch 2...",
    "Processing data batch 3...",
    "Analyzing results...",
    "Generating report...",
]

for i, message in enumerate(messages, 1):
    print(f"[{datetime.now().strftime('%H:%M:%S')}] {message}")
    time.sleep(2)

print()
print(f"✓ {service_name} completed at {datetime.now()}")
