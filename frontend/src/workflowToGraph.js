const NODE_W = 260;
const NODE_H = 80;
const SUB_W = 220;
const SUB_H = 70;
const GAP = 40;
const FOREACH_HEADER_H = 58;
const FOREACH_PAD_X = 20;
const FOREACH_PAD_BOT = 20;

/**
 * Converts a workflow JSON definition into React Flow nodes and edges.
 * Supports linear steps and one level of foreach nesting.
 */
export function workflowToGraph(workflowJson) {
  let parsed;
  try {
    parsed = typeof workflowJson === 'string' ? JSON.parse(workflowJson) : workflowJson;
  } catch {
    return { nodes: [], edges: [] };
  }

  const steps = Array.isArray(parsed?.steps) ? parsed.steps : [];
  const nodes = [];
  const edges = [];
  let y = 0;
  const x = 0;

  const link = (src, tgt) =>
    edges.push({ id: `e-${src}-${tgt}`, source: src, target: tgt, type: 'smoothstep' });

  // START
  nodes.push({
    id: '__start__',
    type: 'startEnd',
    data: { label: 'Start', params: parsed.initial_parameters ?? [] },
    position: { x, y },
    style: { width: NODE_W },
  });
  y += NODE_H + GAP;

  let prev = '__start__';

  for (const step of steps) {
    const id = step.step_name;

    if (step.component_type === 'foreach') {
      const subs = Array.isArray(step.sub_steps) ? step.sub_steps : [];
      const n = subs.length;
      const innerH =
        n > 0
          ? FOREACH_HEADER_H + n * SUB_H + (n - 1) * GAP + FOREACH_PAD_BOT
          : FOREACH_HEADER_H + FOREACH_PAD_BOT;

      // Parent node must be pushed before its children
      nodes.push({
        id,
        type: 'foreachStep',
        data: { label: step.step_name, foreach: step.foreach, onFailure: step.on_failure },
        position: { x, y },
        style: { width: NODE_W, height: innerH },
      });

      let subY = FOREACH_HEADER_H;
      let prevSub = null;
      for (const sub of subs) {
        const subId = `${id}__${sub.step_name}`;
        nodes.push({
          id: subId,
          type: 'workflowStep',
          data: {
            label: sub.step_name,
            componentType: sub.component_type,
            onFailure: sub.on_failure,
          },
          position: { x: FOREACH_PAD_X, y: subY },
          parentId: id,
          extent: 'parent',
          style: { width: SUB_W },
        });
        if (prevSub) link(prevSub, subId);
        prevSub = subId;
        subY += SUB_H + GAP;
      }

      y += innerH + GAP;
    } else {
      nodes.push({
        id,
        type: 'workflowStep',
        data: {
          label: step.step_name,
          componentType: step.component_type,
          onFailure: step.on_failure,
        },
        position: { x, y },
        style: { width: NODE_W },
      });
      y += NODE_H + GAP;
    }

    link(prev, id);
    prev = id;
  }

  // END
  nodes.push({
    id: '__end__',
    type: 'startEnd',
    data: { label: 'End' },
    position: { x, y },
    style: { width: NODE_W },
  });
  link(prev, '__end__');

  return { nodes, edges };
}
