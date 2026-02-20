import { useEffect, useMemo, useState } from 'react';
import { WorkflowCatalog } from './WorkflowCatalog';

const statusClass = {
  Pending: 'pending',
  Running: 'running',
  Success: 'ok',
  Failure: 'bad',
  Skipped: 'skip'
};

const formatLabel = (str) =>
  str.replace(/_/g, ' ').replace(/\b\w/g, (c) => c.toUpperCase());

export function App() {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || '';
  const [page, setPage] = useState('runner');
  const [paramValues, setParamValues] = useState({});
  const [workflows, setWorkflows] = useState([]);
  const [selectedWorkflowId, setSelectedWorkflowId] = useState('escrow_statement_generation');
  const [workflowsLoading, setWorkflowsLoading] = useState(true);
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [liveSteps, setLiveSteps] = useState([]);
  const [error, setError] = useState('');

  useEffect(() => {
    let isActive = true;

    const loadWorkflows = async () => {
      setWorkflowsLoading(true);
      try {
        const response = await fetch(`${apiBaseUrl}/api/workflows`);
        if (!response.ok) {
          throw new Error(`Unable to load workflows (${response.status})`);
        }

        const payload = await response.json();
        if (!Array.isArray(payload)) {
          throw new Error('Unexpected workflow list response.');
        }

        if (!isActive) return;

        const metadata = payload.map((item) => {
          if (typeof item === 'string') {
            return {
              id: item,
              workflowId: item,
              workflowVersion: '',
              description: '',
              lastModified: '',
              initialParameters: [],
              stepCount: 0,
              stepNames: [],
              componentTypes: [],
              pdfTemplates: [],
              hasEmailSender: false,
              parseError: false
            };
          }
          return {
            id: item.id,
            workflowId: item.workflowId || item.id,
            workflowVersion: item.workflowVersion || '',
            description: item.description || '',
            lastModified: item.lastModified || '',
            initialParameters: Array.isArray(item.initialParameters) ? item.initialParameters : [],
            stepCount: typeof item.stepCount === 'number' ? item.stepCount : 0,
            stepNames: Array.isArray(item.stepNames) ? item.stepNames : [],
            componentTypes: Array.isArray(item.componentTypes) ? item.componentTypes : [],
            pdfTemplates: Array.isArray(item.pdfTemplates) ? item.pdfTemplates : [],
            hasEmailSender: Boolean(item.hasEmailSender),
            parseError: Boolean(item.parseError),
            parseErrorMessage: item.parseErrorMessage || ''
          };
        });

        setWorkflows(metadata);
        const validIds = metadata.filter((w) => !w.parseError).map((w) => w.id);
        setSelectedWorkflowId((current) => {
          if (metadata.length === 0) return '';
          if (validIds.includes('escrow_statement_generation')) return 'escrow_statement_generation';
          if (validIds.includes(current)) return current;
          if (validIds.length > 0) return validIds[0];
          return metadata[0].id;
        });

        if (metadata.length === 0) {
          setError('No workflows found in configs/workflows.');
        }
      } catch (err) {
        if (isActive) {
          setError(err.message || 'Unable to load workflows.');
        }
      } finally {
        if (isActive) {
          setWorkflowsLoading(false);
        }
      }
    };

    loadWorkflows();
    return () => { isActive = false; };
  }, [apiBaseUrl]);

  const selectedWorkflow = useMemo(
    () => workflows.find((w) => w.id === selectedWorkflowId) || null,
    [workflows, selectedWorkflowId]
  );

  // Reset param inputs and clear prior results whenever the selected workflow changes
  useEffect(() => {
    if (!selectedWorkflow) return;
    setParamValues(
      Object.fromEntries((selectedWorkflow.initialParameters || []).map((p) => [p, '']))
    );
    setResult(null);
    setLiveSteps([]);
    setError('');
  }, [selectedWorkflow?.id]); // eslint-disable-line react-hooks/exhaustive-deps

  const pdfPath = result?.finalDataDictionary?.pdf_file_path;
  const pdfDownloadUrl = pdfPath
    ? `${apiBaseUrl}/api/files/pdf?path=${encodeURIComponent(pdfPath)}`
    : '';
  const failedStep = (result?.steps || []).find((s) => s.status === 'Failure');

  const hasPdfComponent = selectedWorkflow?.componentTypes?.includes('pdf_generator') ?? false;
  const hasEmailSender = selectedWorkflow?.hasEmailSender ?? false;

  const emailStatus = useMemo(() => {
    const status = result?.finalDataDictionary?.email_status;
    return status === 'sent' ? 'Email sent' : status === 'failed' ? 'Email failed' : 'Email not attempted';
  }, [result]);

  const selectedWorkflowLastModified = selectedWorkflow?.lastModified
    ? new Date(selectedWorkflow.lastModified).toLocaleString()
    : 'n/a';

  const onSubmit = async (event) => {
    event.preventDefault();
    setLoading(true);
    setError('');
    setResult(null);
    setLiveSteps([]);

    try {
      if (!selectedWorkflowId) {
        throw new Error('Select a workflow before running.');
      }
      if (selectedWorkflow?.parseError) {
        throw new Error('Selected workflow has configuration errors and cannot be executed.');
      }

      const params = new URLSearchParams(paramValues);

      await new Promise((resolve, reject) => {
        const eventSource = new EventSource(
          `${apiBaseUrl}/api/workflows/${encodeURIComponent(selectedWorkflowId)}/execute-stream?${params.toString()}`
        );

        eventSource.addEventListener('progress', (event) => {
          const update = JSON.parse(event.data);
          setLiveSteps((current) => {
            const nextStatus =
              update.state === 'Running' || update.state === 'Retrying' ? 'Running' : update.state;
            const existingIndex = current.findIndex((s) => s.stepName === update.stepName);
            if (existingIndex < 0) {
              return [...current, { stepName: update.stepName, status: nextStatus, message: update.message || '' }];
            }
            return current.map((s) =>
              s.stepName !== update.stepName ? s : { ...s, status: nextStatus, message: update.message || '' }
            );
          });
        });

        eventSource.addEventListener('summary', (event) => {
          const summary = JSON.parse(event.data);
          setResult(summary);
          setLiveSteps(
            (summary.steps || []).map((s) => ({
              stepName: s.stepName,
              status: s.status,
              message: s.error?.errorMessage || ''
            }))
          );
          eventSource.close();
          resolve();
        });

        eventSource.addEventListener('workflow_error', (event) => {
          const body = JSON.parse(event.data || '{}');
          eventSource.close();
          reject(new Error(body.message || 'Workflow execution failed.'));
        });

        eventSource.onerror = () => {
          eventSource.close();
          reject(new Error('Workflow stream disconnected unexpectedly.'));
        };
      });
    } catch (err) {
      setError(err.message || 'Unknown request error');
    } finally {
      setLoading(false);
    }
  };

  const visibleSteps = result?.steps?.length
    ? result.steps.map((s) => ({ stepName: s.stepName, status: s.status, message: s.error?.errorMessage || '' }))
    : liveSteps;

  if (page === 'catalog') {
    return (
      <>
        <nav className="top-nav">
          <button className="btn-nav" onClick={() => setPage('runner')}>
            &larr; Workflow Runner
          </button>
        </nav>
        <WorkflowCatalog
          workflows={workflows}
          workflowsLoading={workflowsLoading}
          selectedWorkflowId={selectedWorkflowId}
          onSelectWorkflow={setSelectedWorkflowId}
          formatLabel={formatLabel}
        />
      </>
    );
  }

  return (
    <>
    <nav className="top-nav">
      <button className="btn-nav" onClick={() => setPage('catalog')}>
        Workflow Catalog
      </button>
    </nav>
    <main className="page">
      <section className="panel">
        <h1>Hack13 Demo</h1>
        <p>Select a workflow, fill in the required parameters, and observe step outcomes in real time.</p>

        <form onSubmit={onSubmit} className="form">
          <label>
            Workflow
            <select
              value={selectedWorkflowId}
              onChange={(e) => setSelectedWorkflowId(e.target.value)}
              required
              disabled={loading || workflowsLoading || workflows.length === 0}
            >
              {workflows.map((w) => (
                <option key={w.id} value={w.id}>
                  {w.id}{w.parseError ? ' (invalid)' : ''}
                </option>
              ))}
            </select>
          </label>

          {selectedWorkflow && (
            <div className="workflow-meta">
              <p><strong>Description:</strong> {selectedWorkflow.description || 'n/a'}</p>
              <p><strong>Version:</strong> {selectedWorkflow.workflowVersion || 'n/a'}</p>
              <p><strong>Last Modified:</strong> {selectedWorkflowLastModified}</p>
              {selectedWorkflow.componentTypes.length > 0 && (
                <p>
                  <strong>Components:</strong>{' '}
                  {selectedWorkflow.componentTypes.map(formatLabel).join(' · ')}
                </p>
              )}
              {selectedWorkflow.pdfTemplates.length > 0 && (
                <p>
                  <strong>PDF Template:</strong>{' '}
                  {selectedWorkflow.pdfTemplates.map(formatLabel).join(', ')}
                </p>
              )}
              {selectedWorkflow.parseError && (
                <p className="workflow-meta-error">
                  <strong>Config Error:</strong>{' '}
                  {selectedWorkflow.parseErrorMessage || 'Unable to parse workflow file.'}
                </p>
              )}
            </div>
          )}

          {selectedWorkflow && !selectedWorkflow.parseError && (
            selectedWorkflow.initialParameters.length === 0 ? (
              <p className="workflow-meta">This workflow requires no input parameters.</p>
            ) : (
              selectedWorkflow.initialParameters.map((param) => (
                <label key={param}>
                  {formatLabel(param)}
                  <input
                    value={paramValues[param] ?? ''}
                    onChange={(e) => setParamValues((v) => ({ ...v, [param]: e.target.value }))}
                    required
                  />
                </label>
              ))
            )
          )}

          <button
            type="submit"
            disabled={loading || workflowsLoading || workflows.length === 0 || selectedWorkflow?.parseError}
          >
            {loading ? 'Running...' : 'Run Workflow'}
          </button>
        </form>

        {error && <div className="alert">{error}</div>}
      </section>

      <section className="panel">
        <h2>Step Progress</h2>
        <ul className="steps">
          {visibleSteps.map((step) => (
            <li key={step.stepName}>
              <span>{step.stepName}</span>
              <span className={statusClass[step.status] || ''}>
                {step.status}{step.message ? ` — ${step.message}` : ''}
              </span>
            </li>
          ))}
        </ul>
      </section>

      <section className="panel">
        <h2>Result</h2>
        <p><strong>Workflow Status:</strong> {result?.finalStatus || 'n/a'}</p>
        {hasEmailSender && (
          <p><strong>{emailStatus}</strong></p>
        )}
        {(hasPdfComponent || pdfPath) && (
          <p>
            <strong>PDF:</strong>{' '}
            {pdfPath ? (
              <a href={pdfDownloadUrl} target="_blank" rel="noreferrer">
                Download {result?.finalDataDictionary?.pdf_file_name || 'document.pdf'}
              </a>
            ) : (
              'not generated'
            )}
          </p>
        )}
        {failedStep && (
          <div className="alert">
            <strong>Failed Step:</strong> {failedStep.stepName}<br />
            <strong>Error:</strong> {failedStep.error?.errorCode} — {failedStep.error?.errorMessage}
          </div>
        )}
      </section>
    </main>
    </>
  );
}
