internal static class AdminRetentionPolicyCatalog
{
    public static AdminRetentionPolicyResponse Create()
    {
        return new AdminRetentionPolicyResponse(
            ActiveCleanupEnabled: false,
            DryRunAvailable: false,
            Boundary: "MVP exposes retention policy as read-only visibility only. Automatic cleanup, dry-run cleanup, manual deletion, and retention editing are not enabled.",
            Rules:
            [
                new AdminRetentionRuleResponse(
                    "temporary-input-active",
                    "Temporary PDF input is retained while a job is queued or processing.",
                    "Until the active attempt reaches a terminal state.",
                    "Worker lifecycle",
                    AutomaticCleanupEnabled: false,
                    "No standalone cleanup job scans active inputs."),
                new AdminRetentionRuleResponse(
                    "temporary-input-completed",
                    "Temporary PDF input is deleted after successful processing completion.",
                    "Immediate worker-managed cleanup after completed state is persisted.",
                    "Worker completion",
                    AutomaticCleanupEnabled: true,
                    "This is lifecycle cleanup, not a background retention sweep."),
                new AdminRetentionRuleResponse(
                    "temporary-input-failed-blocked",
                    "Temporary PDF input is retained for failed or blocked jobs so manual retry can reuse it.",
                    "Indefinite in current MVP until a future audited retention cleanup is implemented.",
                    "Future audited cleanup",
                    AutomaticCleanupEnabled: false,
                    "Manual retry safety takes priority over reclaiming storage in the current MVP."),
                new AdminRetentionRuleResponse(
                    "result-json-payload",
                    "Result JSON payloads are retained as the cache and diagnostic source.",
                    "Indefinite in current MVP.",
                    "Future retention policy",
                    AutomaticCleanupEnabled: false,
                    "Controlled debug download is available to admins, but payload cleanup is not active."),
                new AdminRetentionRuleResponse(
                    "admin-audit-events",
                    "Admin audit events are append-only operational records.",
                    "Indefinite in current MVP.",
                    "Future compliance retention policy",
                    AutomaticCleanupEnabled: false,
                    "Audit cleanup is intentionally not implemented before explicit retention requirements."),
                new AdminRetentionRuleResponse(
                    "orphan-temporary-input",
                    "Orphan temporary files are not actively swept by the current MVP.",
                    "Planned future grace window: 24 hours before cleanup candidate status.",
                    "Future dry-run cleanup",
                    AutomaticCleanupEnabled: false,
                    "First implementation should be dry-run visibility with audit before deletion is enabled.")
            ]);
    }
}
