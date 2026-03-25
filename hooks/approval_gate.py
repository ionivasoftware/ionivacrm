"""
Approval Gate — Auto Approve Mode
Tüm onaylar otomatik geçer.
"""
import os
from datetime import datetime

WORKSPACE = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
LOG_FILE = os.path.join(WORKSPACE, "logs", "approvals.log")

def log_approval(gate_name: str, approved: bool, notes: str = ""):
    os.makedirs(os.path.dirname(LOG_FILE), exist_ok=True)
    with open(LOG_FILE, "a") as f:
        status = "AUTO-APPROVED"
        f.write(f"[{datetime.now().isoformat()}] {gate_name}: {status}\n")

def approval_gate(gate_name: str, summary: str, details: str = "", timeout: int = 300) -> bool:
    """Auto-approves everything."""
    log_approval(gate_name, True)
    return True

def sprint_plan_approval(sprint_plan: str) -> bool:
    return True

def db_schema_approval(schema: str) -> bool:
    return True

def deployment_approval(deployment_info: str) -> bool:
    return True

# Aliases for backward compatibility
gate_sprint_plan = sprint_plan_approval
gate_db_schema = db_schema_approval
gate_sprint_complete = lambda *args, **kwargs: True
gate_deployment = deployment_approval
