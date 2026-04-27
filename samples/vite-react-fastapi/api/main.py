from fastapi import FastAPI
from pydantic import BaseModel
from typing import List

app = FastAPI(title="Todo API")

# In-memory storage
todos: List[dict] = []
next_id = 1


class TodoCreate(BaseModel):
    title: str
    completed: bool = False


class Todo(TodoCreate):
    id: int


@app.get("/")
def read_root():
    return {"message": "Todo API", "endpoints": ["/todos", "/health"]}


@app.get("/health")
def health_check():
    return {"status": "healthy"}


@app.get("/todos", response_model=List[Todo])
def get_todos():
    return todos


@app.post("/todos", response_model=Todo)
def create_todo(todo: TodoCreate):
    global next_id
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
    return {"error": "Todo not found"}


@app.delete("/todos/{todo_id}")
def delete_todo(todo_id: int):
    global todos
    todos = [t for t in todos if t["id"] != todo_id]
    return {"message": "Deleted"}
