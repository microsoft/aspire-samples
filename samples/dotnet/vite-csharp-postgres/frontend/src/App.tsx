import { useState, useEffect, useRef } from 'react';
import { flushSync } from 'react-dom';

interface Todo {
  id: number;
  title: string;
  completed: boolean;
  createdAt: string;
}

type Filter = 'all' | 'active' | 'completed';

const FILTERS: { id: Filter; label: string }[] = [
  { id: 'all', label: 'All' },
  { id: 'active', label: 'Active' },
  { id: 'completed', label: 'Done' },
];

/** Run a state update inside a View Transition when supported and motion is allowed. */
function withTransition(update: () => void) {
  const reduceMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
  if (!reduceMotion && 'startViewTransition' in document) {
    (document as Document).startViewTransition(() => flushSync(update));
  } else {
    update();
  }
}

function App() {
  const [todos, setTodos] = useState<Todo[]>([]);
  const [newTodo, setNewTodo] = useState('');
  const [filter, setFilter] = useState<Filter>('all');
  const [loading, setLoading] = useState(false);
  const [ready, setReady] = useState(false);
  const inputRef = useRef<HTMLInputElement>(null);

  const fetchTodos = async (animate = false) => {
    try {
      const response = await fetch('/api/todos');
      const data: Todo[] = await response.json();
      if (animate) {
        withTransition(() => setTodos(data));
      } else {
        setTodos(data);
      }
    } catch (error) {
      console.error('Failed to fetch todos:', error);
    } finally {
      setReady(true);
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
      await fetchTodos(true);
      inputRef.current?.focus();
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
      await fetchTodos(true);
    } catch (error) {
      console.error('Failed to update todo:', error);
    } finally {
      setLoading(false);
    }
  };

  const deleteTodo = async (id: number) => {
    setLoading(true);
    try {
      await fetch(`/api/todos/${id}`, { method: 'DELETE' });
      await fetchTodos(true);
    } catch (error) {
      console.error('Failed to delete todo:', error);
    } finally {
      setLoading(false);
    }
  };

  const clearCompleted = async () => {
    const done = todos.filter((t) => t.completed);
    if (done.length === 0) return;
    setLoading(true);
    try {
      await Promise.all(done.map((t) => fetch(`/api/todos/${t.id}`, { method: 'DELETE' })));
      await fetchTodos(true);
    } catch (error) {
      console.error('Failed to clear completed todos:', error);
    } finally {
      setLoading(false);
    }
  };

  const remaining = todos.filter((t) => !t.completed).length;
  const completedCount = todos.length - remaining;
  const visible = todos.filter((t) =>
    filter === 'all' ? true : filter === 'active' ? !t.completed : t.completed
  );

  const emptyMessage =
    todos.length === 0
      ? 'Nothing here yet. Add your first task above.'
      : filter === 'active'
        ? 'No active tasks — you’re all caught up.'
        : 'No completed tasks yet.';

  return (
    <main className="mx-auto flex min-h-svh w-full max-w-xl flex-col justify-center px-5 py-12">
      <section
        className="rounded-[var(--radius-lg)] border bg-card p-6 shadow-sm sm:p-8"
        style={{ boxShadow: '0 1px 2px oklch(0 0 0 / 0.05), 0 12px 32px -16px oklch(0 0 0 / 0.25)' }}
        aria-busy={loading}
      >
        <header className="mb-6 flex items-start gap-3">
          <span
            aria-hidden="true"
            className="grid size-10 flex-none place-content-center rounded-[var(--radius-md)] bg-primary text-primary-foreground"
          >
            <svg width="20" height="20" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" strokeLinejoin="round">
              <path d="m5 12 4.5 4.5L19 7" />
            </svg>
          </span>
          <div>
            <p className="text-xs font-semibold uppercase tracking-[0.14em] text-muted-foreground">
              PostgreSQL · EF Core · Vite
            </p>
            <h1 className="text-2xl font-semibold tracking-tight text-foreground">Tasks</h1>
          </div>
        </header>

        <form onSubmit={addTodo} className="flex flex-col gap-2.5 sm:flex-row">
          <label htmlFor="new-todo" className="sr-only">
            Add a new task
          </label>
          <input
            id="new-todo"
            ref={inputRef}
            className="field"
            type="text"
            value={newTodo}
            onChange={(e) => setNewTodo(e.target.value)}
            placeholder="What needs to be done?"
            disabled={loading}
            autoComplete="off"
            enterKeyHint="done"
          />
          <button type="submit" className="btn btn-primary sm:w-auto" disabled={loading || !newTodo.trim()}>
            <svg width="16" height="16" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2.5" strokeLinecap="round" aria-hidden="true">
              <path d="M12 5v14M5 12h14" />
            </svg>
            Add task
          </button>
        </form>

        <div className="mt-6 mb-3 flex items-center justify-between gap-3">
          <div className="seg" role="group" aria-label="Filter tasks">
            {FILTERS.map((f) => (
              <button
                key={f.id}
                type="button"
                className="seg-btn"
                aria-pressed={filter === f.id}
                onClick={() => withTransition(() => setFilter(f.id))}
              >
                {f.label}
              </button>
            ))}
          </div>
          <p className="text-sm font-medium text-muted-foreground" aria-live="polite">
            {ready ? `${remaining} ${remaining === 1 ? 'task' : 'tasks'} left` : '\u00a0'}
          </p>
        </div>

        {visible.length === 0 ? (
          <p className="rounded-[var(--radius-md)] border border-dashed px-4 py-10 text-center text-sm text-muted-foreground">
            {emptyMessage}
          </p>
        ) : (
          <ul role="list" className="flex flex-col gap-2">
            {visible.map((todo) => (
              <li
                key={todo.id}
                className="row-enter flex items-center gap-3 rounded-[var(--radius-md)] border bg-card px-3.5 py-3 transition-colors hover:bg-accent"
              >
                <input
                  id={`todo-${todo.id}`}
                  className="check"
                  type="checkbox"
                  checked={todo.completed}
                  onChange={() => toggleTodo(todo)}
                  disabled={loading}
                  aria-label={`Mark “${todo.title}” as ${todo.completed ? 'not done' : 'done'}`}
                />
                <label
                  htmlFor={`todo-${todo.id}`}
                  className={`flex-1 cursor-pointer text-[0.97rem] leading-snug ${
                    todo.completed ? 'text-muted-foreground line-through' : 'text-foreground'
                  }`}
                >
                  {todo.title}
                </label>
                <button
                  type="button"
                  onClick={() => deleteTodo(todo.id)}
                  className="btn btn-ghost px-2.5 py-2"
                  disabled={loading}
                  aria-label={`Delete “${todo.title}”`}
                >
                  <svg width="17" height="17" viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" aria-hidden="true">
                    <path d="M3 6h18M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2m2 0v14a1 1 0 0 1-1 1H6a1 1 0 0 1-1-1V6" />
                    <path d="M10 11v6M14 11v6" />
                  </svg>
                </button>
              </li>
            ))}
          </ul>
        )}

        {todos.length > 0 && (
          <footer className="mt-6 flex items-center justify-between border-t pt-4 text-sm text-muted-foreground">
            <span>
              {completedCount} of {todos.length} done
            </span>
            <button
              type="button"
              className="btn btn-ghost px-2.5 py-1.5 text-sm hover:text-destructive disabled:opacity-40"
              onClick={clearCompleted}
              disabled={loading || completedCount === 0}
            >
              Clear completed
            </button>
          </footer>
        )}
      </section>

      <p className="mt-6 text-center text-xs text-muted-foreground">
        Built with Aspire · ASP.NET Core Minimal API + PostgreSQL
      </p>
    </main>
  );
}

export default App;
