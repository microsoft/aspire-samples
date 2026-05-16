import { useState, useEffect } from 'react';

const MAX_TITLE_LENGTH = 200;

function App() {
  const [todos, setTodos] = useState([]);
  const [newTodo, setNewTodo] = useState('');

  useEffect(() => {
    fetchTodos();
  }, []);

  const fetchTodos = async () => {
    const response = await fetch('/api/todos');
    const data = await response.json();
    setTodos(data);
  };

  const addTodo = async (e) => {
    e.preventDefault();
    const title = newTodo.trim();
    if (!title) return;

    const response = await fetch('/api/todos', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title, completed: false }),
    });
    if (!response.ok) return;

    const todo = await response.json();
    setTodos([...todos, todo]);
    setNewTodo('');
  };

  const toggleTodo = async (id, completed) => {
    const todo = todos.find((t) => t.id === id);
    if (!todo) return;

    const response = await fetch(`/api/todos/${id}`, {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ title: todo.title, completed: !completed }),
    });
    if (!response.ok) return;

    const updated = await response.json();
    setTodos(todos.map((t) => (t.id === id ? updated : t)));
  };

  const deleteTodo = async (id) => {
    const response = await fetch(`/api/todos/${id}`, { method: 'DELETE' });
    if (!response.ok) return;

    setTodos(todos.filter((t) => t.id !== id));
  };

  return (
    <div className="container">
      <h1>Todo App</h1>
      <p className="subtitle">Vite + React + FastAPI</p>

      <form onSubmit={addTodo}>
        <input
          type="text"
          value={newTodo}
          onChange={(e) => setNewTodo(e.target.value)}
          placeholder="Add a new todo..."
          maxLength={MAX_TITLE_LENGTH}
        />
        <button type="submit">Add</button>
      </form>

      <ul>
        {todos.map((todo) => (
          <li key={todo.id} className={todo.completed ? 'completed' : ''}>
            <input
              type="checkbox"
              checked={todo.completed}
              onChange={() => toggleTodo(todo.id, todo.completed)}
            />
            <span>{todo.title}</span>
            <button onClick={() => deleteTodo(todo.id)}>Delete</button>
          </li>
        ))}
      </ul>
    </div>
  );
}

export default App;
