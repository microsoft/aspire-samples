import { useState, useEffect, useRef } from 'react';
import { flushSync } from 'react-dom';
import {
  PlusIcon,
  TrashIcon,
  CheckCircleIcon,
  SparklesIcon,
} from '@heroicons/react/24/solid';
import { ClipboardDocumentListIcon } from '@heroicons/react/24/outline';

const MAX_TITLE_LENGTH = 200;

const prefersReducedMotion = () =>
  typeof window !== 'undefined' &&
  window.matchMedia('(prefers-reduced-motion: reduce)').matches;

// Wrap a state change in a View Transition when the browser supports it and
// the user hasn't asked for reduced motion. flushSync makes React apply the
// update synchronously so the transition can capture before/after frames.
const withViewTransition = (update) => {
  if (!document.startViewTransition || prefersReducedMotion()) {
    update();
    return;
  }
  document.startViewTransition(() => flushSync(update));
};

function App() {
  const [todos, setTodos] = useState([]);
  const [newTodo, setNewTodo] = useState('');
  const [status, setStatus] = useState('');
  const inputRef = useRef(null);
  const submittingRef = useRef(false);
  const pendingRef = useRef(new Set());

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
    if (!title || submittingRef.current) return;

    submittingRef.current = true;
    try {
      const response = await fetch('/api/todos', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title, completed: false }),
      });
      if (!response.ok) return;

      const todo = await response.json();
      const total = todos.length + 1;
      withViewTransition(() => setTodos((current) => [...current, todo]));
      setNewTodo('');
      setStatus(
        `Added "${todo.title}". ${total} ${total === 1 ? 'todo' : 'todos'} total.`
      );
      inputRef.current?.focus();
    } finally {
      submittingRef.current = false;
    }
  };

  const toggleTodo = async (id, completed) => {
    const todo = todos.find((t) => t.id === id);
    if (!todo || pendingRef.current.has(id)) return;

    pendingRef.current.add(id);
    try {
      const response = await fetch(`/api/todos/${id}`, {
        method: 'PUT',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ title: todo.title, completed: !completed }),
      });
      if (!response.ok) return;

      const updated = await response.json();
      const togo =
        todos.filter((t) => !t.completed && t.id !== id).length +
        (updated.completed ? 0 : 1);
      withViewTransition(() =>
        setTodos((current) => current.map((t) => (t.id === id ? updated : t)))
      );
      setStatus(
        `"${updated.title}" marked ${updated.completed ? 'done' : 'not done'}. ${togo} to go.`
      );
    } finally {
      pendingRef.current.delete(id);
    }
  };

  const deleteTodo = async (id) => {
    const todo = todos.find((t) => t.id === id);
    if (pendingRef.current.has(id)) return;

    pendingRef.current.add(id);
    try {
      const response = await fetch(`/api/todos/${id}`, { method: 'DELETE' });
      if (!response.ok) return;

      const total = Math.max(todos.length - 1, 0);
      withViewTransition(() =>
        setTodos((current) => current.filter((t) => t.id !== id))
      );
      setStatus(
        `${todo ? `Deleted "${todo.title}".` : 'Todo deleted.'} ${total} ${
          total === 1 ? 'todo' : 'todos'
        } total.`
      );
    } finally {
      pendingRef.current.delete(id);
    }
  };

  const remaining = todos.filter((t) => !t.completed).length;
  const done = todos.length - remaining;

  return (
    <>
      <a className="skip-link" href="#main">
        Skip to todo list
      </a>

      <div className="page">
        <header className="masthead">
          <span className="brand-blob" aria-hidden="true">
            <ClipboardDocumentListIcon />
          </span>
          <div>
            <h1>Todos</h1>
            <p className="subtitle">
              <SparklesIcon className="subtitle-spark" aria-hidden="true" />
              Create and complete tasks · Vite · React · FastAPI on Aspire
            </p>
          </div>
        </header>

        <main id="main" className="card" aria-labelledby="todo-heading">
          <h2 id="todo-heading" className="visually-hidden">
            Your todos
          </h2>

          <form className="add-form" onSubmit={addTodo}>
            <label className="visually-hidden" htmlFor="new-todo">
              Add a new todo
            </label>
            <input
              id="new-todo"
              ref={inputRef}
              type="text"
              value={newTodo}
              onChange={(e) => setNewTodo(e.target.value)}
              placeholder="Add a new todo…"
              maxLength={MAX_TITLE_LENGTH}
              autoComplete="off"
            />
            <button type="submit" className="btn-add">
              <PlusIcon aria-hidden="true" />
              <span>Add</span>
            </button>
          </form>

          <p className="tally">
            {todos.length === 0 ? (
              'Nothing here yet — add your first todo above.'
            ) : (
              <>
                <strong>{remaining}</strong> to go
                <span className="tally-dot" aria-hidden="true">
                  •
                </span>
                <strong>{done}</strong> done
              </>
            )}
          </p>

          {todos.length === 0 ? (
            <p className="empty-state">
              <span className="empty-blob" aria-hidden="true">
                <SparklesIcon />
              </span>
              You're all caught up — nothing left to do.
            </p>
          ) : (
            <ul className="todo-list">
              {todos.map((todo) => (
                <li
                  key={todo.id}
                  className={`todo ${todo.completed ? 'is-done' : ''}`}
                >
                  <label className="todo-toggle">
                    <input
                      type="checkbox"
                      checked={todo.completed}
                      onChange={() => toggleTodo(todo.id, todo.completed)}
                    />
                    <CheckCircleIcon className="todo-check" aria-hidden="true" />
                    <span className="todo-title">{todo.title}</span>
                  </label>
                  <button
                    type="button"
                    className="btn-delete"
                    onClick={() => deleteTodo(todo.id)}
                    aria-label={`Delete "${todo.title}"`}
                  >
                    <TrashIcon aria-hidden="true" />
                  </button>
                </li>
              ))}
            </ul>
          )}
        </main>

        <p role="status" aria-live="polite" className="visually-hidden">
          {status}
        </p>
      </div>
    </>
  );
}

export default App;
