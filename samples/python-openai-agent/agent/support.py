"""Order-support domain: demo data, OpenAI tool definitions, and tool execution."""

import json
import logging
from datetime import date, timedelta
from time import perf_counter
from typing import Any

from opentelemetry import metrics, trace
from opentelemetry.trace import Span, Status, StatusCode

from telemetry import INSTRUMENTATION_NAME, capture_message_content

logger = logging.getLogger("support_agent.tools")
tracer = trace.get_tracer(INSTRUMENTATION_NAME)
meter = metrics.get_meter(INSTRUMENTATION_NAME)

# Domain metrics. Tags stay bounded (tool name, outcome, reason) - never order or session ids.
conversations_started = meter.create_counter(
    "support_agent.conversations.started",
    unit="{conversation}",
    description="Support conversations started.",
)
active_sessions = meter.create_up_down_counter(
    "support_agent.sessions.active",
    unit="{session}",
    description="Conversations currently held in memory.",
)
turns_per_conversation = meter.create_histogram(
    "support_agent.conversation.turns",
    unit="{turn}",
    description="User turns per conversation, recorded when a conversation ends.",
    explicit_bucket_boundaries_advisory=[1, 2, 3, 5, 8, 13, 21],
)
tool_invocations = meter.create_counter(
    "support_agent.tool.invocations",
    unit="{invocation}",
    description="Tool (function) calls executed on behalf of the model.",
)
tool_duration = meter.create_histogram(
    "support_agent.tool.duration",
    unit="s",
    description="Duration of tool (function) executions.",
    explicit_bucket_boundaries_advisory=[0.0005, 0.001, 0.0025, 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1],
)

SYSTEM_PROMPT = (
    "You are Ember, the customer-support assistant for Contoso Outdoor, an online outdoor "
    "gear store. Help customers with order status and returns. Always call "
    "get_order_status before answering questions about a specific order, and "
    "lookup_return_policy before answering return or refund questions. Never invent order "
    "details, dates, or policies. Keep answers short, friendly, and specific."
)

# In-memory demo data. Dates are relative to startup so the scenarios stay realistic.
_today = date.today()
ORDERS: dict[str, dict[str, Any]] = {
    "1001": {
        "status": "shipped",
        "item": "Trailblazer trail running shoes",
        "category": "footwear",
        "ordered_on": (_today - timedelta(days=4)).isoformat(),
        "carrier": "Contoso Express",
        "tracking_number": "CE123456789",
        "estimated_delivery": (_today + timedelta(days=2)).isoformat(),
    },
    "1002": {
        "status": "delivered",
        "item": "Summit Shell rain jacket",
        "category": "apparel",
        "ordered_on": (_today - timedelta(days=16)).isoformat(),
        "delivered_on": (_today - timedelta(days=12)).isoformat(),
    },
    "1003": {
        "status": "processing",
        "item": "Ridgeline 2-person tent",
        "category": "camping",
        "ordered_on": (_today - timedelta(days=1)).isoformat(),
        "estimated_ship_date": (_today + timedelta(days=2)).isoformat(),
    },
    "1004": {
        "status": "cancelled",
        "item": "Aurora GPS watch",
        "category": "electronics",
        "ordered_on": (_today - timedelta(days=9)).isoformat(),
        "cancelled_on": (_today - timedelta(days=8)).isoformat(),
        "refund": "Refunded in full to the original payment method.",
    },
}

RETURN_POLICIES: dict[str, dict[str, Any]] = {
    "apparel": {
        "return_window_days": 30,
        "condition": "Unworn and unwashed, with the original tags attached.",
        "return_shipping": "Free with the prepaid label from the order page.",
        "refund": "Original payment method within 5-7 business days.",
    },
    "footwear": {
        "return_window_days": 30,
        "condition": "Worn indoors only, in the original box.",
        "return_shipping": "Free with the prepaid label from the order page.",
        "refund": "Original payment method within 5-7 business days.",
    },
    "camping": {
        "return_window_days": 45,
        "condition": "Unused and complete, including poles, stakes, and stuff sacks.",
        "return_shipping": "Free with the prepaid label from the order page.",
        "refund": "Original payment method within 5-7 business days.",
    },
    "electronics": {
        "return_window_days": 15,
        "condition": "Unopened, or defective. Opened non-defective items have a 15% restocking fee.",
        "return_shipping": "Customer pays return shipping unless the item is defective.",
        "refund": "Original payment method within 5-7 business days of inspection.",
    },
}

# OpenAI function-calling schemas; also recorded on the invoke_agent span as gen_ai.tool.definitions.
TOOLS: list[dict[str, Any]] = [
    {
        "type": "function",
        "function": {
            "name": "get_order_status",
            "description": "Look up the status, item, and shipping details of a Contoso Outdoor order.",
            "parameters": {
                "type": "object",
                "properties": {
                    "order_id": {"type": "string", "description": "The order number, for example 1001."},
                },
                "required": ["order_id"],
                "additionalProperties": False,
            },
            "strict": True,
        },
    },
    {
        "type": "function",
        "function": {
            "name": "lookup_return_policy",
            "description": "Get the return window, conditions, and refund terms for a product category.",
            "parameters": {
                "type": "object",
                "properties": {
                    "category": {
                        "type": "string",
                        "enum": sorted(RETURN_POLICIES),
                        "description": "Product category, as returned by get_order_status.",
                    },
                },
                "required": ["category"],
                "additionalProperties": False,
            },
            "strict": True,
        },
    },
]
_DESCRIPTIONS = {t["function"]["name"]: t["function"]["description"] for t in TOOLS}


def _get_order_status(span: Span, order_id: str) -> tuple[dict[str, Any], str]:
    order_id = str(order_id).strip().lstrip("#")
    span.set_attribute("order.id", order_id)
    order = ORDERS.get(order_id)
    if order is None:
        span.add_event("order.not_found", {"order.id": order_id})
        logger.warning("Order %s not found", order_id, extra={"order.id": order_id})
        return {"error": "order_not_found", "order_id": order_id}, "not_found"
    span.set_attribute("order.status", order["status"])
    result = {"order_id": order_id, **order}
    if order["status"] == "delivered":
        result["days_since_delivery"] = (_today - date.fromisoformat(order["delivered_on"])).days
    return result, "success"


def _lookup_return_policy(span: Span, category: str) -> tuple[dict[str, Any], str]:
    category = str(category).strip().lower()
    span.set_attribute("product.category", category)
    policy = RETURN_POLICIES.get(category)
    if policy is None:
        return {"error": "unknown_category", "categories": sorted(RETURN_POLICIES)}, "not_found"
    return {"category": category, **policy}, "success"


_HANDLERS = {"get_order_status": _get_order_status, "lookup_return_policy": _lookup_return_policy}


def execute_tool(name: str, call_id: str, arguments: str) -> str:
    """Run one model-requested tool inside a GenAI `execute_tool` span and return its JSON result."""
    handler = _HANDLERS.get(name)
    # The model chooses the name, so keep metric tags to known tools.
    tool_tag = name if handler else "unknown"
    with tracer.start_as_current_span(f"execute_tool {name}") as span:
        span.set_attributes({
            "gen_ai.operation.name": "execute_tool",
            "gen_ai.tool.name": name,
            "gen_ai.tool.call.id": call_id,
            "gen_ai.tool.type": "function",
            "gen_ai.tool.description": _DESCRIPTIONS.get(name, ""),
        })
        if capture_message_content():
            span.set_attribute("gen_ai.tool.call.arguments", arguments)

        start = perf_counter()
        try:
            if handler is None:
                raise ValueError(f"Unknown tool '{name}'")
            result, outcome = handler(span, **json.loads(arguments or "{}"))
        except (ValueError, TypeError) as ex:  # bad JSON, unknown tool, or unexpected arguments
            span.record_exception(ex)
            span.set_status(Status(StatusCode.ERROR, type(ex).__name__))
            span.set_attribute("error.type", type(ex).__name__)
            result, outcome = {"error": "invalid_tool_call", "detail": str(ex)}, "error"
        elapsed = perf_counter() - start

        payload = json.dumps(result)
        span.set_attribute("support.tool.outcome", outcome)
        if capture_message_content():
            span.set_attribute("gen_ai.tool.call.result", payload)

        tool_invocations.add(1, {"gen_ai.tool.name": tool_tag, "support.tool.outcome": outcome})
        tool_duration.record(elapsed, {"gen_ai.tool.name": tool_tag})
        logger.info(
            "Tool %s completed: %s", name, outcome,
            extra={
                "gen_ai.tool.name": name,
                "gen_ai.tool.call.id": call_id,
                "support.tool.outcome": outcome,
                "support.tool.duration_ms": round(elapsed * 1000, 3),
            },
        )
        return payload
