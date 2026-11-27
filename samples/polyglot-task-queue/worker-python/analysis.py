"""Dataset analysis: the Python worker's job.

pandas profiles a CSV dataset (for example monthly sales by region): totals and averages for the
numeric columns, the leading group for the last numeric column (the measure) and its trend from
the first period to the last.
"""

from io import StringIO

import pandas as pd

PERIOD_COLUMNS = {"month", "period", "date", "quarter", "week", "year"}


class DatasetError(ValueError):
    """The document can't be analyzed: it isn't CSV, has no rows, or has no numeric columns."""


def compact(value: float) -> str:
    """1284500 -> '1.28M', 18420 -> '18.4K'."""
    for divisor, suffix in ((1_000_000_000, "B"), (1_000_000, "M"), (1_000, "K")):
        if abs(value) >= divisor:
            return f"{value / divisor:.3g}{suffix}"
    return f"{value:,.0f}" if float(value).is_integer() else f"{value:,.2f}"


def load(csv_text: str) -> pd.DataFrame:
    try:
        frame = pd.read_csv(StringIO(csv_text), skipinitialspace=True)
    except (pd.errors.ParserError, pd.errors.EmptyDataError) as exc:
        raise DatasetError(f"The document is not a readable CSV dataset: {exc}") from exc
    if frame.empty:
        raise DatasetError("The dataset has a header but no rows.")
    return frame


def analyze(frame: pd.DataFrame) -> dict:
    """Returns the result envelope every worker sends back: headline, facts and details."""
    numeric = frame.select_dtypes(include="number")
    if numeric.empty:
        columns = ", ".join(map(str, frame.columns))
        raise DatasetError(f"The dataset has no numeric columns to analyze (found: {columns}).")

    measure = str(numeric.columns[-1])
    labels = [str(column) for column in frame.columns if column not in numeric.columns]
    period = next((column for column in labels if column.lower() in PERIOD_COLUMNS), None)
    group = next((column for column in labels if column != period), None)
    total = float(numeric[measure].sum())

    details: dict = {
        "rows": len(frame),
        "columns": [str(column) for column in frame.columns],
        "measure": measure,
        "numeric": {
            str(column): {
                "sum": round(float(numeric[column].sum()), 2),
                "mean": round(float(numeric[column].mean()), 2),
                "min": round(float(numeric[column].min()), 2),
                "max": round(float(numeric[column].max()), 2),
            }
            for column in numeric.columns
        },
    }
    headline = f"{measure.capitalize()} totals {compact(total)}"
    facts = [
        {"label": "Rows", "value": f"{len(frame):,}"},
        {"label": f"Total {measure}", "value": compact(total)},
    ]

    if group:
        by_group = frame.groupby(group)[measure].sum().sort_values(ascending=False)
        leader, leader_total = str(by_group.index[0]), float(by_group.iloc[0])
        share = leader_total / total if total else 0
        details["groups"] = [{group: str(key), measure: round(float(value), 2)} for key, value in by_group.items()]
        headline += f" across {len(by_group)} {group}s; {leader} leads with {share:.0%}"
        facts.append({"label": f"Top {group}", "value": f"{leader} ({share:.0%})"})

    if period:
        by_period = frame.groupby(period, sort=True)[measure].sum()
        if len(by_period) > 1 and by_period.iloc[0]:
            change = float(by_period.iloc[-1] / by_period.iloc[0] - 1)
            direction = "up" if change >= 0 else "down"
            details["trend"] = {"from": str(by_period.index[0]), "to": str(by_period.index[-1]), "change": round(change, 4)}
            headline += f", {direction} {abs(change):.0%} from {by_period.index[0]} to {by_period.index[-1]}"
            facts.append({"label": "Trend", "value": f"{change:+.1%}"})

    return {"headline": headline, "facts": facts, "details": details}
