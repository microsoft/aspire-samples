import { useState, useEffect } from 'react';

interface Todo {
  id: number;
  title: string;
  completed: boolean;
  createdAt: string;
}

function App() {
  const [todos, setTodos] = useState<Todo[]>([]);
  const [newTodo, setNewTodo] = useState('');
  const [loading, setLoading] = useState(false);

  const fetchTodos = async () => {
    try {
      const response = await fetch('/api/todos');
      const data = await response.json();
      setTodos(data);
    } catch (error) {
      console.error('Failed to fetch todos:', error);
    }
  };

  useEffect(() => {
    fetchTodos();
  }, []);

  const addTodo = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTodo.trim()) return;

    setLoading(true);
    try {
      await fetch('/api/todos', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: newTodo }),
      });
      setNewTodo('');
      await fetchTodos();
    } catch (error) {
      console.error('Failed to add todo:', error);
    } finally {
      setLoading(false);
    }
  };

  const toggleTodo = async (todo: Todo) => {
    setLoading(true);
    try {
      await fetch(`/api/todos/${todo.id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ completed: !todo.completed }),
      });
      await fetchTodos();
    } catch (error) {
      console.error('Failed to update todo:', error);
    } finally {
      setLoading(false);
    }
  };

  const deleteTodo = async (id: number) => {
    setLoading(true);
    try {
      await fetch(`/api/todos/${id}`, {
        method: 'DELETE',
      });
      await fetchTodos();
    } catch (error) {
      console.error('Failed to delete todo:', error);
    } finally {
      setLoading(false);
    }
  };

  return (
    <div className="container">
      <h1>Todo List</h1>
      <p className="subtitle">ASP.NET Core Minimal API + PostgreSQL + Vite</p>

      <form onSubmit={addTodo} className="add-todo-form">
        <input
          type="text"
          value={newTodo}
          onChange={(e) => setNewTodo(e.target.value)}
          placeholder="What needs to be done?"
          disabled={loading}
        />
        <button type="submit" disabled={loading || !newTodo.trim()}>
          Add
        </button>
      </form>

      <div className="todos-list">
        {todos.length === 0 ? (
          <p className="empty-state">No todos yet. Add one above!</p>
        ) : (
          todos.map((todo) => (
            <div key={todo.id} className={`todo-item ${todo.completed ? 'completed' : ''}`}>
              <input
                type="checkbox"
                checked={todo.completed}
                onChange={() => toggleTodo(todo)}
                disabled={loading}
              />
              <span className="todo-title">{todo.title}</span>
              <button
                onClick={() => deleteTodo(todo.id)}
                className="delete-btn"
                disabled={loading}
              >
                Delete
              </button>
            </div>
          ))
        )}
      </div>

      {todos.length > 0 && (
        <div className="stats">
          {todos.filter(t => !t.completed).length} of {todos.length} remaining
        </div>
      )}
    </div>
  );
}

export default App;
