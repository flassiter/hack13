import { useEffect, useState, useCallback } from 'react';

export function WorkflowEditor({ apiBaseUrl }) {
  const [workflows, setWorkflows] = useState([]);
  const [selectedId, setSelectedId] = useState('');
  const [json, setJson] = useState('');
  const [originalJson, setOriginalJson] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState('');
  const [parseError, setParseError] = useState('');

  useEffect(() => {
    (async () => {
      setLoading(true);
      try {
        const res = await fetch(`${apiBaseUrl}/api/workflows`);
        if (!res.ok) throw new Error(`Failed to load workflows (${res.status})`);
        const data = await res.json();
        const list = (Array.isArray(data) ? data : []).map((w) =>
          typeof w === 'string' ? { id: w } : { id: w.id || w.workflowId }
        );
        setWorkflows(list);
        if (list.length > 0 && !selectedId) {
          setSelectedId(list[0].id);
        }
      } catch (err) {
        setStatus(err.message);
      } finally {
        setLoading(false);
      }
    })();
  }, [apiBaseUrl]);

  const loadDefinition = useCallback(async (id) => {
    if (!id) return;
    setStatus('');
    setParseError('');
    try {
      const res = await fetch(`${apiBaseUrl}/api/workflows/${encodeURIComponent(id)}/definition`);
      if (!res.ok) throw new Error(`Failed to load definition (${res.status})`);
      const raw = await res.text();
      const formatted = JSON.stringify(JSON.parse(raw), null, 2);
      setJson(formatted);
      setOriginalJson(formatted);
    } catch (err) {
      setJson('');
      setOriginalJson('');
      setStatus(err.message);
    }
  }, [apiBaseUrl]);

  useEffect(() => {
    if (selectedId) loadDefinition(selectedId);
  }, [selectedId, loadDefinition]);

  const handleJsonChange = (value) => {
    setJson(value);
    setParseError('');
    try {
      JSON.parse(value);
    } catch (e) {
      setParseError(e.message);
    }
  };

  const handleSave = async () => {
    if (parseError) return;
    setSaving(true);
    setStatus('');
    try {
      const res = await fetch(
        `${apiBaseUrl}/api/workflows/${encodeURIComponent(selectedId)}/definition`,
        {
          method: 'PUT',
          headers: { 'Content-Type': 'application/json' },
          body: json,
        }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.message || `Save failed (${res.status})`);
      }
      setOriginalJson(json);
      setStatus('Saved successfully.');
    } catch (err) {
      setStatus(err.message);
    } finally {
      setSaving(false);
    }
  };

  const handleFormat = () => {
    if (parseError) return;
    try {
      setJson(JSON.stringify(JSON.parse(json), null, 2));
    } catch { /* ignore */ }
  };

  const isDirty = json !== originalJson;

  return (
    <main className="page">
      <section className="panel">
        <h1>Workflow Editor</h1>
        <p>View and edit workflow definitions.</p>

        <div className="form">
          <label>
            Workflow
            <select
              value={selectedId}
              onChange={(e) => setSelectedId(e.target.value)}
              disabled={loading || workflows.length === 0}
            >
              {workflows.map((w) => (
                <option key={w.id} value={w.id}>{w.id}</option>
              ))}
            </select>
          </label>
        </div>
      </section>

      <section className="panel editor-panel">
        <div className="editor-toolbar">
          <h2>Definition</h2>
          <div className="editor-actions">
            <button
              type="button"
              onClick={handleFormat}
              disabled={!!parseError || !json}
              className="btn-secondary"
            >
              Format
            </button>
            <button
              type="button"
              onClick={handleSave}
              disabled={saving || !!parseError || !isDirty || !json}
            >
              {saving ? 'Saving...' : 'Save'}
            </button>
          </div>
        </div>

        {parseError && (
          <div className="alert">
            <strong>JSON Error:</strong> {parseError}
          </div>
        )}
        {status && !parseError && (
          <div className={status.includes('success') || status.includes('Saved') ? 'status-ok' : 'alert'}>
            {status}
          </div>
        )}

        <textarea
          className="json-editor"
          value={json}
          onChange={(e) => handleJsonChange(e.target.value)}
          spellCheck={false}
          placeholder={loading ? 'Loading...' : 'Select a workflow to edit'}
        />
      </section>
    </main>
  );
}
