# OpsTrax AI Autonomy Architecture

## Operating Loop
Observe -> Understand -> Prioritize -> Recommend -> Ask Approval or Auto-act Safely -> Execute Through Services -> Track Outcome -> Learn

## AI Is Not a Chatbot
The AI layer is an event-driven intelligence engine that consumes operational signals, generates recommendations, and requests actions through governed business services.

## Required Agents
- Fleet Operations Agent
- Dispatch Agent
- Maintenance Agent
- Safety Agent
- Compliance Agent
- Customer Success Agent
- Finance Agent
- Sales Agent
- Platform Admin Agent
- Executive Briefing Agent
- IoT Automation Agent

## Guardrails
- No direct writes to business tables.
- Respect tenant_id, RBAC, and feature flags.
- Financial, contract, customer external, tenant suspension, and physical IoT actions require approval at launch.
- Every AI input, output, recommendation, approval, execution, and outcome must be logged.

## Autonomy Levels
- L0 Assistant only
- L1 Insight engine
- L2 Recommendation engine
- L3 Approval-based action
- L4 Controlled automation
- L5 Full autonomy, disabled for launch

## Launch Target
- P0: L1/L2
- P1: L3
- Later: L4 only for low-risk internal automation

