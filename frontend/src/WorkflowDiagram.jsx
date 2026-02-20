import { useMemo } from 'react';
import { ReactFlow, Background, Controls, Handle, Position } from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import { workflowToGraph } from './workflowToGraph';

const TYPE_CONFIG = {
  database_reader:        { label: 'DB Reader',       color: '#1565c0', bg: '#e3f2fd' },
  green_screen_connector: { label: 'Green Screen',    color: '#2e7d32', bg: '#e8f5e9' },
  calculate:              { label: 'Calculate',       color: '#e65100', bg: '#fff3e0' },
  decision:               { label: 'Decision',        color: '#6a1b9a', bg: '#f3e5f5' },
  pdf_generator:          { label: 'PDF Generator',   color: '#b71c1c', bg: '#fce4ec' },
  email_sender:           { label: 'Email Sender',    color: '#006064', bg: '#e0f7fa' },
};

function WorkflowStepNode({ data }) {
  const cfg = TYPE_CONFIG[data.componentType] ?? {
    label: data.componentType ?? 'Step',
    color: '#455a64',
    bg: '#f5f5f5',
  };
  return (
    <div className="wf-node" style={{ borderColor: cfg.color, backgroundColor: cfg.bg }}>
      <Handle type="target" position={Position.Top} />
      <div className="wf-node-type" style={{ color: cfg.color }}>{cfg.label}</div>
      <div className="wf-node-name">{data.label}</div>
      {data.onFailure && <div className="wf-node-meta">on fail: {data.onFailure}</div>}
      <Handle type="source" position={Position.Bottom} />
    </div>
  );
}

function ForeachStepNode({ data }) {
  return (
    <div className="wf-foreach">
      <Handle type="target" position={Position.Top} />
      <div className="wf-foreach-header">
        <span className="wf-foreach-icon">⟳</span>
        <span className="wf-foreach-badge">For Each</span>
        <span className="wf-foreach-name">{data.label}</span>
      </div>
      {data.foreach && (
        <div className="wf-foreach-meta">
          rows: <code>{data.foreach.rows_key}</code>
          {data.foreach.row_prefix && <>, prefix: <code>{data.foreach.row_prefix}</code></>}
        </div>
      )}
      <Handle type="source" position={Position.Bottom} />
    </div>
  );
}

function StartEndNode({ data }) {
  const isStart = data.label === 'Start';
  return (
    <div className="wf-startend">
      {!isStart && <Handle type="target" position={Position.Top} />}
      <span className="wf-startend-label">{data.label}</span>
      {data.params?.length > 0 && (
        <span className="wf-startend-params">params: {data.params.join(', ')}</span>
      )}
      {isStart && <Handle type="source" position={Position.Bottom} />}
    </div>
  );
}

const nodeTypes = {
  workflowStep: WorkflowStepNode,
  foreachStep: ForeachStepNode,
  startEnd: StartEndNode,
};

export function WorkflowDiagram({ json }) {
  const { nodes, edges } = useMemo(() => {
    if (!json) return { nodes: [], edges: [] };
    return workflowToGraph(json);
  }, [json]);

  if (!json) {
    return (
      <div className="wf-diagram-container wf-diagram-empty">
        Select a workflow to view its diagram.
      </div>
    );
  }

  return (
    <div className="wf-diagram-container">
      <ReactFlow
        nodes={nodes}
        edges={edges}
        nodeTypes={nodeTypes}
        nodesDraggable={false}
        nodesConnectable={false}
        elementsSelectable={false}
        fitView
        fitViewOptions={{ padding: 0.15 }}
      >
        <Background color="#cfe0f0" gap={20} size={1} />
        <Controls showInteractive={false} />
      </ReactFlow>
    </div>
  );
}
