import { useEffect, useState, useCallback } from 'react';
import { WorkflowDiagram } from './WorkflowDiagram';

export function WorkflowEditor({ apiBaseUrl }) {
  const [workflows, setWorkflows] = useState([]);
  const [selectedId, setSelectedId] = useState('');
  const [json, setJson] = useState('');
  const [originalJson, setOriginalJson] = useState('');
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [status, setStatus] = useState('');
  const [parseError, setParseError] = useState('');
  const [activeTab, setActiveTab] = useState('diagram');
  const [explanation, setExplanation] = useState('');
  const [explaining, setExplaining] = useState(false);
  const [explainError, setExplainError] = useState('');
  const [assistRequest, setAssistRequest] = useState('');
  const [suggestion, setSuggestion] = useState('');
  const [suggesting, setSuggesting] = useState(false);
  const [suggestError, setSuggestError] = useState('');

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
    setExplanation('');
    setExplainError('');
    setSuggestion('');
    setSuggestError('');
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

  const fetchExplanation = useCallback(async () => {
    if (!selectedId) return;
    setExplaining(true);
    setExplainError('');
    setExplanation('');
    try {
      const res = await fetch(`${apiBaseUrl}/api/workflows/${encodeURIComponent(selectedId)}/explain`);
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.detail || body.message || `Request failed (${res.status})`);
      }
      const data = await res.json();
      setExplanation(data.explanation ?? '');
    } catch (err) {
      setExplainError(err.message);
    } finally {
      setExplaining(false);
    }
  }, [apiBaseUrl, selectedId]);

  // Auto-fetch when switching to Explain tab if not yet loaded
  useEffect(() => {
    if (activeTab === 'explain' && !explanation && !explaining && !explainError && selectedId) {
      fetchExplanation();
    }
  }, [activeTab, explanation, explaining, explainError, selectedId, fetchExplanation]);

  const fetchSuggestion = useCallback(async () => {
    if (!selectedId || !assistRequest.trim()) return;
    setSuggesting(true);
    setSuggestError('');
    setSuggestion('');
    try {
      const res = await fetch(
        `${apiBaseUrl}/api/workflows/${encodeURIComponent(selectedId)}/assist`,
        {
          method: 'POST',
          headers: { 'Content-Type': 'application/json' },
          body: JSON.stringify({ request: assistRequest }),
        }
      );
      if (!res.ok) {
        const body = await res.json().catch(() => ({}));
        throw new Error(body.detail || body.message || `Request failed (${res.status})`);
      }
      const data = await res.json();
      setSuggestion(data.suggestion ?? '');
    } catch (err) {
      setSuggestError(err.message);
    } finally {
      setSuggesting(false);
    }
  }, [apiBaseUrl, selectedId, assistRequest]);

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
          <div className="editor-toolbar-right">
            <div className="editor-tabs">
              <button
                type="button"
                className={`btn-tab${activeTab === 'explain' ? ' active' : ''}`}
                onClick={() => setActiveTab('explain')}
              >
                Explain
              </button>
              <button
                type="button"
                className={`btn-tab${activeTab === 'assistant' ? ' active' : ''}`}
                onClick={() => setActiveTab('assistant')}
              >
                Assistant
              </button>
              <button
                type="button"
                className={`btn-tab${activeTab === 'diagram' ? ' active' : ''}`}
                onClick={() => setActiveTab('diagram')}
              >
                Diagram
              </button>
              <button
                type="button"
                className={`btn-tab${activeTab === 'definition' ? ' active' : ''}`}
                onClick={() => setActiveTab('definition')}
              >
                Definition
              </button>
            </div>
            {activeTab === 'explain' && explanation && (
              <button
                type="button"
                onClick={fetchExplanation}
                disabled={explaining}
                className="btn-secondary"
              >
                {explaining ? 'Generating...' : 'Regenerate'}
              </button>
            )}
            {activeTab === 'definition' && (
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
            )}
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

        {activeTab === 'explain' && (
          <ExplainPanel
            explanation={explanation}
            explaining={explaining}
            explainError={explainError}
            onRetry={fetchExplanation}
          />
        )}
        {activeTab === 'assistant' && (
          <AssistantPanel
            assistRequest={assistRequest}
            onRequestChange={setAssistRequest}
            suggestion={suggestion}
            suggesting={suggesting}
            suggestError={suggestError}
            onGenerate={fetchSuggestion}
          />
        )}
        {activeTab === 'diagram' && (
          <WorkflowDiagram json={parseError ? '' : json} />
        )}
        {activeTab === 'definition' && (
          <textarea
            className="json-editor"
            value={json}
            onChange={(e) => handleJsonChange(e.target.value)}
            spellCheck={false}
            placeholder={loading ? 'Loading...' : 'Select a workflow to edit'}
          />
        )}
      </section>
    </main>
  );
}

function ExplainPanel({ explanation, explaining, explainError, onRetry }) {
  if (explaining) {
    return (
      <div className="explain-panel explain-loading">
        <div className="explain-spinner" />
        <p>Generating explanation via AI&hellip;</p>
      </div>
    );
  }
  if (explainError) {
    return (
      <div className="explain-panel">
        <div className="alert">
          <strong>Error:</strong> {explainError}
        </div>
        <button type="button" onClick={onRetry} className="btn-secondary" style={{ marginTop: '0.75rem' }}>
          Try again
        </button>
      </div>
    );
  }
  if (!explanation) {
    return (
      <div className="explain-panel explain-empty">
        <p>No explanation yet.</p>
      </div>
    );
  }
  return (
    <div className="explain-panel">
      <pre className="explain-text">{explanation}</pre>
    </div>
  );
}

function AssistantPanel({ assistRequest, onRequestChange, suggestion, suggesting, suggestError, onGenerate }) {
  return (
    <div className="assist-panel">
      <div className="assist-input-area">
        <textarea
          className="assist-textarea"
          value={assistRequest}
          onChange={(e) => onRequestChange(e.target.value)}
          placeholder="Describe the change you want to make to this workflow and its components. For example: &quot;Add a retry policy to the database step&quot; or &quot;Change the settlement discount to 25% for loans over 2 years old.&quot;"
          spellCheck={false}
          disabled={suggesting}
        />
        <div className="assist-actions">
          <button
            type="button"
            onClick={onGenerate}
            disabled={suggesting || !assistRequest.trim()}
          >
            {suggesting ? 'Generating...' : 'Generate Suggestion'}
          </button>
        </div>
      </div>

      {suggesting && (
        <div className="explain-loading" style={{ minHeight: '120px' }}>
          <div className="explain-spinner" />
          <p>Generating suggestion via AI&hellip;</p>
        </div>
      )}

      {suggestError && !suggesting && (
        <div className="alert" style={{ marginTop: '0.75rem' }}>
          <strong>Error:</strong> {suggestError}
        </div>
      )}

      {suggestion && !suggesting && (
        <div className="assist-result">
          <pre className="suggest-text">{suggestion}</pre>
        </div>
      )}
    </div>
  );
}
