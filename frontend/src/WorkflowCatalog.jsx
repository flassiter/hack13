import { useMemo } from 'react';

export function WorkflowCatalog({
  workflows,
  workflowsLoading,
  selectedWorkflowId,
  onSelectWorkflow,
  formatLabel,
}) {
  const selectedWorkflow = useMemo(
    () => workflows.find((w) => w.id === selectedWorkflowId) || null,
    [workflows, selectedWorkflowId]
  );

  const selectedWorkflowLastModified = selectedWorkflow?.lastModified
    ? new Date(selectedWorkflow.lastModified).toLocaleString()
    : 'n/a';

  return (
    <main className="page">
      <section className="panel">
        <h1>Workflow Catalog</h1>
        <p>Browse available workflows. This view is read-only and does not modify configs.</p>
      </section>

      <section className="panel workflow-catalog">
        <div className="workflow-grid">
          <div className="workflow-list">
            <h2>Workflows</h2>
            {workflowsLoading && <p>Loading workflows...</p>}
            {!workflowsLoading && workflows.length === 0 && (
              <p>No workflows found.</p>
            )}
            {!workflowsLoading && workflows.length > 0 && (
              <ul className="workflow-items">
                {workflows.map((workflow) => {
                  const isActive = workflow.id === selectedWorkflowId;
                  return (
                    <li key={workflow.id}>
                      <button
                        type="button"
                        className={`workflow-item${isActive ? ' active' : ''}`}
                        onClick={() => onSelectWorkflow(workflow.id)}
                      >
                        <span className="workflow-item-title">
                          {workflow.id}
                          {workflow.parseError ? ' (invalid)' : ''}
                        </span>
                        {workflow.description && (
                          <span className="workflow-item-desc">{workflow.description}</span>
                        )}
                        <span className="workflow-item-meta">
                          steps: {workflow.stepCount || 0}
                        </span>
                      </button>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>

          <div className="workflow-detail">
            <h2>Details</h2>
            {!selectedWorkflow && <p>Select a workflow to see details.</p>}
            {selectedWorkflow && (
              <div className="workflow-meta">
                <p><strong>Description:</strong> {selectedWorkflow.description || 'n/a'}</p>
                <p><strong>Version:</strong> {selectedWorkflow.workflowVersion || 'n/a'}</p>
                <p><strong>Last Modified:</strong> {selectedWorkflowLastModified}</p>
                <p>
                  <strong>Initial Parameters:</strong>{' '}
                  {selectedWorkflow.initialParameters?.length
                    ? selectedWorkflow.initialParameters.map(formatLabel).join(', ')
                    : 'none'}
                </p>
                <p>
                  <strong>Steps:</strong>{' '}
                  {selectedWorkflow.stepNames?.length
                    ? selectedWorkflow.stepNames.join(' · ')
                    : 'n/a'}
                </p>
                {selectedWorkflow.componentTypes?.length > 0 && (
                  <p>
                    <strong>Components:</strong>{' '}
                    {selectedWorkflow.componentTypes.map(formatLabel).join(' · ')}
                  </p>
                )}
                {selectedWorkflow.pdfTemplates?.length > 0 && (
                  <p>
                    <strong>PDF Templates:</strong>{' '}
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

            {selectedWorkflow && (
              <details className="workflow-json">
                <summary>Raw metadata (JSON)</summary>
                <pre>{JSON.stringify(selectedWorkflow, null, 2)}</pre>
              </details>
            )}
          </div>
        </div>
      </section>
    </main>
  );
}
