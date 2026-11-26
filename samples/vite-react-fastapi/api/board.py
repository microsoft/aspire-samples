"""In-memory task board with domain spans, metrics, and structured logs.

Data lives in process memory and is lost when the API restarts.
"""

import logging
import random
import threading
from datetime import date, datetime, timedelta, timezone

from opentelemetry.trace import Status as SpanStatus, StatusCode

from models import MAX_TASKS, BoardSummary, DemoResult, Priority, Status, Task, TaskCreate, TaskUpdate
from telemetry import board_metrics, tracer

logger = logging.getLogger("contoso.taskboard")


class TaskNotFound(Exception):
    def __init__(self, task_id: int):
        super().__init__(f"Task {task_id} was not found.")


class BoardConflict(Exception):
    """The request is valid but conflicts with the board's current state."""


def _now() -> datetime:
    return datetime.now(timezone.utc).replace(microsecond=0)


def _is_overdue(task: dict) -> bool:
    return task["status"] != Status.done and task["due_date"] is not None and task["due_date"] < date.today()


def _to_model(task: dict) -> Task:
    return Task(**task, overdue=_is_overdue(task))


def _span_attributes(task: dict) -> dict:
    return {
        "taskboard.task.id": task["id"],
        "taskboard.task.priority": task["priority"].value,
        "taskboard.task.status": task["status"].value,
        "taskboard.task.assignee": task["assignee"] or "",
    }


class TaskBoard:
    def __init__(self) -> None:
        self._tasks: dict[int, dict] = {}
        self._next_id = 1
        self._lock = threading.Lock()

    # --- Queries --------------------------------------------------------------

    def list(self, status, priority, assignee, skip: int, limit: int) -> list[Task]:
        with self._lock:
            tasks = [
                t
                for t in self._tasks.values()
                if (status is None or t["status"] == status)
                and (priority is None or t["priority"] == priority)
                and (assignee is None or (t["assignee"] or "").lower() == assignee.lower())
            ]
            return [_to_model(t) for t in tasks[skip : skip + limit]]

    def get(self, task_id: int) -> Task:
        with self._lock:
            return _to_model(self._require(task_id))

    def summary(self) -> BoardSummary:
        with self._lock:
            tasks = list(self._tasks.values())
        open_tasks = [t for t in tasks if t["status"] != Status.done]
        return BoardSummary(
            total=len(tasks),
            by_status={s: sum(1 for t in tasks if t["status"] == s) for s in Status},
            overdue=sum(1 for t in open_tasks if _is_overdue(t)),
            urgent_open=sum(1 for t in open_tasks if t["priority"] == Priority.urgent),
        )

    # --- Commands -------------------------------------------------------------

    def create(self, request: TaskCreate, created_at: datetime | None = None) -> Task:
        with tracer.start_as_current_span("taskboard.create_task") as span, self._lock:
            span.set_attribute("taskboard.board.size", len(self._tasks))
            if len(self._tasks) >= MAX_TASKS:
                span.set_status(SpanStatus(StatusCode.ERROR, "board full"))
                logger.warning("Board is full; rejected task %r", request.title, extra={"taskboard.board.limit": MAX_TASKS})
                raise BoardConflict(f"The board already holds the maximum of {MAX_TASKS} tasks.")

            now = _now()
            task = {
                "id": self._next_id,
                **request.model_dump(),
                "created_at": created_at or now,
                "updated_at": now,
                "completed_at": None,
            }
            if task["status"] == Status.done:
                task["completed_at"] = now
            self._tasks[task["id"]] = task
            self._next_id += 1

            span.set_attributes(_span_attributes(task))
            board_metrics.created.add(1, {"taskboard.task.priority": task["priority"].value})
            board_metrics.tasks.add(1, {"taskboard.task.status": task["status"].value})
            logger.info(
                "Created %s-priority task %d %r for %s",
                task["priority"].value, task["id"], task["title"], task["assignee"] or "nobody",
                extra={"taskboard.task.id": task["id"], "taskboard.task.priority": task["priority"].value,
                       "taskboard.task.assignee": task["assignee"] or ""},
            )
            return _to_model(task)

    def update(self, task_id: int, changes: TaskUpdate) -> Task:
        with tracer.start_as_current_span("taskboard.update_task") as span, self._lock:
            span.set_attribute("taskboard.task.id", task_id)
            task = self._require(task_id)
            fields = changes.model_dump(exclude_unset=True, exclude_none=True)
            new_status = fields.pop("status", None)

            if new_status is not None and new_status != task["status"]:
                self._transition(task, new_status)
            for key, value in fields.items():
                task[key] = value
            task["updated_at"] = _now()
            span.set_attributes(_span_attributes(task))
            span.set_attribute("taskboard.update.fields", sorted([*fields, *(["status"] if new_status else [])]))
            return _to_model(task)

    def delete(self, task_id: int) -> None:
        with tracer.start_as_current_span("taskboard.delete_task") as span, self._lock:
            span.set_attribute("taskboard.task.id", task_id)
            task = self._require(task_id)
            del self._tasks[task_id]
            span.set_attributes(_span_attributes(task))
            board_metrics.tasks.add(-1, {"taskboard.task.status": task["status"].value})
            logger.info("Deleted task %d %r", task_id, task["title"], extra={"taskboard.task.id": task_id})

    def reset(self) -> None:
        with self._lock:
            for task in self._tasks.values():
                board_metrics.tasks.add(-1, {"taskboard.task.status": task["status"].value})
            self._tasks.clear()
            self._next_id = 1

    # --- Internals ------------------------------------------------------------

    def _require(self, task_id: int) -> dict:
        task = self._tasks.get(task_id)
        if task is None:
            raise TaskNotFound(task_id)
        return task

    def _transition(self, task: dict, target: Status) -> None:
        """Move a task between columns. Caller holds the lock."""
        source = task["status"]
        with tracer.start_as_current_span("taskboard.transition") as span:
            span.set_attributes({
                **_span_attributes(task),
                "taskboard.transition.from": source.value,
                "taskboard.transition.to": target.value,
            })
            if source == Status.blocked and target == Status.done:
                span.add_event("transition.rejected", {"reason": "blocked tasks must be unblocked first"})
                span.set_status(SpanStatus(StatusCode.ERROR, "invalid transition"))
                logger.warning(
                    "Rejected move of blocked task %d %r straight to done", task["id"], task["title"],
                    extra={"taskboard.task.id": task["id"], "taskboard.transition.from": source.value,
                           "taskboard.transition.to": target.value},
                )
                raise BoardConflict(f"Task {task['id']} is blocked; move it to in_progress before marking it done.")

            if _is_overdue(task):
                span.add_event("task.overdue", {"taskboard.task.due_date": task["due_date"].isoformat()})

            task["status"] = target
            attrs = {"taskboard.transition.from": source.value, "taskboard.transition.to": target.value}
            board_metrics.transitions.add(1, attrs)
            board_metrics.tasks.add(-1, {"taskboard.task.status": source.value})
            board_metrics.tasks.add(1, {"taskboard.task.status": target.value})
            span.add_event("task.status_changed", attrs)

            log_extra = {"taskboard.task.id": task["id"], "taskboard.task.priority": task["priority"].value,
                         "taskboard.task.assignee": task["assignee"] or "", **attrs}
            if target == Status.done:
                task["completed_at"] = _now()
                lead_time_hours = (task["completed_at"] - task["created_at"]).total_seconds() / 3600
                board_metrics.lead_time.record(lead_time_hours, {"taskboard.task.priority": task["priority"].value})
                span.set_attribute("taskboard.task.lead_time_hours", round(lead_time_hours, 1))
                logger.info(
                    "Completed task %d %r in %.1f h", task["id"], task["title"], lead_time_hours,
                    extra={**log_extra, "taskboard.task.lead_time_hours": round(lead_time_hours, 1)},
                )
            else:
                task["completed_at"] = None
                logger.info("Moved task %d %r from %s to %s", task["id"], task["title"], source.value, target.value,
                            extra=log_extra)


board = TaskBoard()


# --- Demo data (dashboard commands) ---------------------------------------------

TEAM = ["Priya Patel", "Marcus Chen", "Sofia Alvarez", "Jamal Wright", "Elena Novak"]

# (title, assignee, priority, status, due in days, created days ago)
SAMPLE_TASKS = [
    ("Rotate TLS certificates on the edge gateway", "Priya Patel", Priority.urgent, Status.in_progress, 1, 3),
    ("Fix flaky checkout integration test", "Marcus Chen", Priority.high, Status.in_progress, 2, 4),
    ("Add retry policy to payment webhooks", "Sofia Alvarez", Priority.high, Status.todo, 4, 2),
    ("Update on-call runbook for Redis failover", "Jamal Wright", Priority.medium, Status.todo, 6, 5),
    ("Migrate billing reports to the new Postgres cluster", "Elena Novak", Priority.high, Status.blocked, -1, 9),
    ("Review Q3 access audit findings", "Priya Patel", Priority.medium, Status.todo, -2, 8),
    ("Upgrade Node 20 services to Node 24", "Marcus Chen", Priority.medium, Status.todo, 10, 6),
    ("Draft postmortem for INC-2041 search outage", "Sofia Alvarez", Priority.high, Status.done, 0, 3),
    ("Instrument the import worker with OpenTelemetry", "Jamal Wright", Priority.medium, Status.in_progress, 5, 4),
    ("Tune autoscaling for the search API", "Elena Novak", Priority.low, Status.todo, 14, 1),
    ("Archive stale feature flags", None, Priority.low, Status.done, 3, 7),
    ("Load-test the new pricing endpoint", "Marcus Chen", Priority.urgent, Status.todo, 0, 1),
]

INCOMING = [
    ("Investigate 502s from the CDN origin", Priority.urgent),
    ("Patch OpenSSL on build agents", Priority.high),
    ("Rotate the shared staging API key", Priority.high),
    ("Add dashboards for queue backlog", Priority.medium),
    ("Clean up unused S3 buckets", Priority.low),
    ("Document the release checklist", Priority.medium),
    ("Reduce cold-start time of the auth service", Priority.medium),
    ("Triage customer report of duplicate invoices", Priority.urgent),
    ("Bump base images to Azure Linux 3", Priority.low),
    ("Review pull requests for the mobile API", Priority.medium),
]


def seed_sample_tasks() -> DemoResult:
    """Replace the board with a realistic platform-team sprint."""
    with tracer.start_as_current_span("taskboard.seed") as span:
        board.reset()
        today = date.today()
        for title, assignee, priority, status, due_in, created_ago in SAMPLE_TASKS:
            board.create(
                TaskCreate(title=title, assignee=assignee, priority=priority, status=status,
                           due_date=today + timedelta(days=due_in)),
                created_at=_now() - timedelta(days=created_ago, hours=random.randint(0, 20)),
            )
        span.set_attribute("taskboard.seed.count", len(SAMPLE_TASKS))
        logger.info("Seeded the board with %d sample tasks", len(SAMPLE_TASKS),
                    extra={"taskboard.seed.count": len(SAMPLE_TASKS)})
        return DemoResult(created=len(SAMPLE_TASKS), total=len(SAMPLE_TASKS))


def simulate_traffic(rounds: int) -> DemoResult:
    """Simulate a burst of team activity: triage, pick up, block, finish, and tidy up tasks."""
    result = DemoResult(total=0)
    rng = random.Random()
    tried_blocked_done = False

    def move(task_ids: list[int], target: Status) -> None:
        if not task_ids:
            return
        task_id = task_ids.pop(rng.randrange(len(task_ids)))
        try:
            board.update(task_id, TaskUpdate(status=target))
            result.moved += 1
            result.completed += int(target == Status.done)
        except BoardConflict:
            result.rejected += 1
        except TaskNotFound:
            pass

    with tracer.start_as_current_span("taskboard.simulate") as span:
        span.set_attribute("taskboard.simulate.rounds", rounds)
        for _ in range(rounds):
            by_status = {s: [t.id for t in board.list(s, None, None, 0, MAX_TASKS)] for s in Status}

            open_titles = {t.title for t in board.list(None, None, None, 0, MAX_TASKS) if t.status != Status.done}
            title, priority = rng.choice([item for item in INCOMING if item[0] not in open_titles] or INCOMING)
            try:
                # Arrivals are backdated as if triaged from the intake queue earlier in the sprint,
                # so completed tasks report realistic lead times.
                board.create(
                    TaskCreate(title=title, assignee=rng.choice(TEAM), priority=priority,
                               due_date=date.today() + timedelta(days=rng.randint(1, 10))),
                    created_at=_now() - timedelta(hours=rng.randint(2, 96)),
                )
                result.created += 1
            except BoardConflict:
                result.rejected += 1

            move(by_status[Status.in_progress], Status.done)
            move(by_status[Status.todo], Status.in_progress)
            if rng.random() < 0.35:
                move(by_status[Status.in_progress], Status.blocked)
            if not tried_blocked_done:
                # Rejected with 409: blocked work must be unblocked before it can be done.
                blocked = [t.id for t in board.list(Status.blocked, None, None, 0, MAX_TASKS)]
                tried_blocked_done = bool(blocked)
                move(blocked, Status.done)
            if rng.random() < 0.5:
                move(by_status[Status.blocked], Status.in_progress)

            # Keep the board tidy: archive the oldest finished work once it grows.
            done = by_status[Status.done]
            if len(done) > 6:
                board.delete(done[0])
                result.deleted += 1

        result.total = board.summary().total
        span.set_attributes({"taskboard.simulate.created": result.created, "taskboard.simulate.completed": result.completed})
        logger.info("Simulated %d rounds of team activity: %d created, %d moved, %d rejected",
                    rounds, result.created, result.moved, result.rejected,
                    extra={"taskboard.simulate.rounds": rounds, "taskboard.simulate.rejected": result.rejected})
        return result
