from fastapi import FastAPI, HTTPException, Query, status
from pydantic import BaseModel, Field, field_validator
from typing import List

app = FastAPI(title="Todo API")

MAX_TODOS = 100
MAX_TITLE_LENGTH = 200

# In-memory storage
todos: List[dict] = []
next_id = 1


class TodoCreate(BaseModel):
    title: str = Field(..., min_length=1, max_length=MAX_TITLE_LENGTH)
    completed: bool = False

    @field_validator("title")
    @classmethod
    def validate_title(cls, value: str) -> str:
        title = value.strip()
        if not title:
            raise ValueError("Title must not be empty")
        return title


class Todo(TodoCreate):
    id: int


@app.get("/")
def read_root():
    return {"message": "Todo API", "endpoints": ["/todos", "/health"]}


@app.get("/health")
def health_check():
    return {"status": "healthy"}


@app.get("/todos", response_model=List[Todo])
def get_todos(
    skip: int = Query(default=0, ge=0),
    limit: int = Query(default=MAX_TODOS, ge=1, le=MAX_TODOS),
):
    return todos[skip : skip + limit]


@app.post("/todos", response_model=Todo)
def create_todo(todo: TodoCreate):
    global next_id
    if len(todos) >= MAX_TODOS:
        raise HTTPException(
            status_code=status.HTTP_409_CONFLICT,
            detail=f"Todo limit of {MAX_TODOS} reached",
        )

    new_todo = {"id": next_id, "title": todo.title, "completed": todo.completed}
    todos.append(new_todo)
    next_id += 1
    return new_todo


@app.put("/todos/{todo_id}", response_model=Todo)
def update_todo(todo_id: int, todo: TodoCreate):
    for item in todos:
        if item["id"] == todo_id:
            item["title"] = todo.title
            item["completed"] = todo.completed
            return item
    raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Todo not found")


@app.delete("/todos/{todo_id}", status_code=status.HTTP_204_NO_CONTENT)
def delete_todo(todo_id: int):
    global todos
    if not any(t["id"] == todo_id for t in todos):
        raise HTTPException(status_code=status.HTTP_404_NOT_FOUND, detail="Todo not found")

    todos = [t for t in todos if t["id"] != todo_id]
