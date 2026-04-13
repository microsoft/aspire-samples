import { useState, useEffect } from 'react';

interface Task {
  id: string;
  type: string;
  data: string;
  status: string;
  createdAt: string;
  completedAt?: string;
  result?: any;
  worker?: string;
}

function App() {
  const [tasks, setTasks] = useState<Task[]>([]);
  const [taskType, setTaskType] = useState<string>('analyze');
  const [taskData, setTaskData] = useState<string>('');
  const [loading, setLoading] = useState(false);

  const fetchTasks = async () => {
    try {
      const response = await fetch('/tasks');
      const data = await response.json();
      setTasks(data);
    } catch (error) {
      console.error('Failed to fetch tasks:', error);
    }
  };

  useEffect(() => {
    fetchTasks();
    const interval = setInterval(fetchTasks, 2000); // Poll every 2 seconds
    return () => clearInterval(interval);
  }, []);

  const submitTask = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!taskData.trim()) return;

    setLoading(true);
    try {
      await fetch('/tasks', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          type: taskType,
          data: taskData
        }),
      });
      setTaskData('');
      await fetchTasks();
    } catch (error) {
      console.error('Failed to submit task:', error);
    } finally {
      setLoading(false);
    }
  };

  const clearTasks = async () => {
    if (!confirm('Clear all tasks?')) return;

    setLoading(true);
    try {
      await fetch('/tasks', { method: 'DELETE' });
      await fetchTasks();
    } catch (error) {
      console.error('Failed to clear tasks:', error);
    } finally {
      setLoading(false);
    }
  };

  const getStatusBadge = (status: string) => {
    const classes = {
      queued: 'status-badge status-queued',
      completed: 'status-badge status-completed',
      failed: 'status-badge status-failed'
    };
    return classes[status as keyof typeof classes] || 'status-badge';
  };

  return (
    <div className="container">
      <h1>Polyglot Task Queue</h1>
      <p className="subtitle">Distributed task processing with Python, C#, and Node.js</p>

      <div className="submit-section">
        <h2>Submit Task</h2>
        <form onSubmit={submitTask}>
          <div className="form-row">
            <div className="form-group">
              <label>Task Type</label>
              <select
                value={taskType}
                onChange={(e) => setTaskType(e.target.value)}
                disabled={loading}
              >
                <option value="analyze">Data Analysis (Python)</option>
                <option value="report">Report Generation (C#)</option>
              </select>
            </div>
          </div>

          <div className="form-group">
            <label>Task Data (CSV or JSON)</label>
            <textarea
              value={taskData}
              onChange={(e) => setTaskData(e.target.value)}
              placeholder="Enter data to process..."
              rows={4}
              disabled={loading}
            />
          </div>

          <div className="button-row">
            <button type="submit" disabled={loading || !taskData.trim()}>
              Submit Task
            </button>
            <button
              type="button"
              onClick={clearTasks}
              disabled={loading || tasks.length === 0}
              className="btn-secondary"
            >
              Clear All
            </button>
          </div>
        </form>
      </div>

      <div className="tasks-section">
        <div className="section-header">
          <h2>Tasks</h2>
          <span className="task-count">
            {tasks.filter(t => t.status === 'queued').length} queued,{' '}
            {tasks.filter(t => t.status === 'completed').length} completed
          </span>
        </div>

        {tasks.length === 0 ? (
          <p className="empty-state">No tasks yet. Submit one above!</p>
        ) : (
          <div className="tasks-list">
            {tasks.map((task) => (
              <div key={task.id} className="task-card">
                <div className="task-header">
                  <div>
                    <span className="task-id">{task.id}</span>
                    <span className={getStatusBadge(task.status)}>
                      {task.status}
                    </span>
                    {task.worker && (
                      <span className="worker-badge">{task.worker}</span>
                    )}
                  </div>
                  <div className="task-type">{task.type}</div>
                </div>

                <div className="task-body">
                  <div className="task-data">
                    <strong>Data:</strong>
                    <pre>{task.data}</pre>
                  </div>

                  {task.result && (
                    <div className="task-result">
                      <strong>Result:</strong>
                      <pre>{JSON.stringify(task.result, null, 2)}</pre>
                    </div>
                  )}
                </div>

                <div className="task-footer">
                  <span>Created: {new Date(task.createdAt).toLocaleString()}</span>
                  {task.completedAt && (
                    <span>Completed: {new Date(task.completedAt).toLocaleString()}</span>
                  )}
                </div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  );
}

export default App;
