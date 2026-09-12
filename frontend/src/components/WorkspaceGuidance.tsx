import { useId, useState } from "react";
import { CircleHelp, ChevronDown, ArrowRight } from "lucide-react";
import "./workspace-guidance.css";

/** Pages supply workflow guidance; this component never infers readiness or permissions. */
export function WorkspaceGuidance({ nextStep, steps }: { nextStep: string; steps: string[] }) {
  const id = useId();
  const [expanded, setExpanded] = useState(false);
  return <section className="workspace-guidance" aria-label="Workflow guidance">
    <div className="workspace-guidance-bar">
      <p><ArrowRight aria-hidden="true" /><span>{nextStep}</span></p>
      <button type="button" aria-expanded={expanded} aria-controls={id} title="Show the steps and explain what each action does" onClick={() => setExpanded(value => !value)}>
        <CircleHelp aria-hidden="true" /><span>How this works</span><ChevronDown aria-hidden="true" className={expanded ? "is-expanded" : ""} />
      </button>
    </div>
    {expanded && <ol id={id} className="workspace-guidance-steps">{steps.map((step, index) => <li key={index}><span aria-hidden="true">{index + 1}</span><p>{step}</p></li>)}</ol>}
  </section>;
}
