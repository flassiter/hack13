import { useMemo, useState } from 'react';

const statusClass = {
  Pending: 'pending',
  Running: 'running',
  Success: 'ok',
  Failure: 'bad',
  Skipped: 'skip'
};

const workflowSteps = [
  'lookup_escrow_data',
  'calculate_shortage',
  'determine_notice_type',
  'generate_pdf',
  'send_email'
];

export function App() {
  const apiBaseUrl = import.meta.env.VITE_API_BASE_URL || '';
  const [loanNumber, setLoanNumber] = useState('1000001');
  const [customerEmail, setCustomerEmail] = useState('demo@example.com');
  const [loading, setLoading] = useState(false);
  const [result, setResult] = useState(null);
  const [liveSteps, setLiveSteps] = useState([]);
  const [error, setError] = useState('');

  const pdfPath = result?.finalDataDictionary?.pdf_file_path;
  const pdfDownloadUrl = pdfPath
    ? `${apiBaseUrl}/api/files/pdf?path=${encodeURIComponent(pdfPath)}`
    : '';
  const failedStep = (result?.steps || []).find((s) => s.status === 'Failure');

  const emailStatus = useMemo(() => {
    const status = result?.finalDataDictionary?.email_status;
    return status === 'sent' ? 'Email sent' : status === 'failed' ? 'Email failed' : 'Email not attempted';
  }, [result]);

  const onSubmit = async (event) => {
    event.preventDefault();
    setLoading(true);
    setError('');
    setResult(null);
    setLiveSteps(workflowSteps.map((stepName) => ({ stepName, status: 'Pending', message: '' })));

    try {
      const params = new URLSearchParams({
        loan_number: loanNumber,
        customer_email: customerEmail
      });

      await new Promise((resolve, reject) => {
        const eventSource = new EventSource(
          `${apiBaseUrl}/api/workflows/escrow_statement_generation/execute-stream?${params.toString()}`
        );

        eventSource.addEventListener('progress', (event) => {
          const update = JSON.parse(event.data);
          setLiveSteps((current) => current.map((step) => {
            if (step.stepName !== update.stepName) return step;

            return {
              ...step,
              status: update.state === 'Running' || update.state === 'Retrying'
                ? 'Running'
                : update.state,
              message: update.message || ''
            };
          }));
        });

        eventSource.addEventListener('summary', (event) => {
          const summary = JSON.parse(event.data);
          setResult(summary);
          setLiveSteps((summary.steps || []).map((step) => ({
            stepName: step.stepName,
            status: step.status,
            message: step.error?.errorMessage || ''
          })));
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
    ? (result.steps.map((step) => ({
      stepName: step.stepName,
      status: step.status,
      message: step.error?.errorMessage || ''
    })))
    : liveSteps;

  return (
    <main className="page">
      <section className="panel">
        <h1>Escrow Statement Demo</h1>
        <p>Run the workflow, observe step outcomes, then verify PDF/email results.</p>

        <form onSubmit={onSubmit} className="form">
          <label>
            Loan Number
            <input value={loanNumber} onChange={(e) => setLoanNumber(e.target.value)} required />
          </label>
          <label>
            Customer Email
            <input value={customerEmail} onChange={(e) => setCustomerEmail(e.target.value)} required />
          </label>
          <button type="submit" disabled={loading}>{loading ? 'Running...' : 'Generate Escrow Statement'}</button>
        </form>

        {error ? <div className="alert">{error}</div> : null}
      </section>

      <section className="panel">
        <h2>Step Progress</h2>
        <ul className="steps">
          {(visibleSteps || []).map((step) => (
            <li key={step.stepName}>
              <span>{step.stepName}</span>
              <span className={statusClass[step.status] || ''}>
                {step.status}{step.message ? ` - ${step.message}` : ''}
              </span>
            </li>
          ))}
        </ul>
      </section>

      <section className="panel">
        <h2>Result</h2>
        <p><strong>Workflow Status:</strong> {result?.finalStatus || 'n/a'}</p>
        <p><strong>{emailStatus}</strong></p>
        <p>
          <strong>PDF:</strong>{' '}
          {pdfPath ? (
            <a href={pdfDownloadUrl} target="_blank" rel="noreferrer">
              Download {result?.finalDataDictionary?.pdf_file_name || 'statement.pdf'}
            </a>
          ) : (
            'not generated'
          )}
        </p>
        {failedStep ? (
          <div className="alert">
            <strong>Failed Step:</strong> {failedStep.stepName}<br />
            <strong>Error:</strong> {failedStep.error?.errorCode} - {failedStep.error?.errorMessage}
          </div>
        ) : null}
      </section>
    </main>
  );
}
