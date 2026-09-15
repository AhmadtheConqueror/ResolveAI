namespace ResolveAI.Api.Services;

public sealed class IncidentWorkflowDecision
{
    private IncidentWorkflowDecision(
        bool isAllowed,
        bool isForbidden,
        string message)
    {
        IsAllowed = isAllowed;
        IsForbidden = isForbidden;
        Message = message;
    }

    public bool IsAllowed { get; }

    public bool IsForbidden { get; }

    public string Message { get; }

    public static IncidentWorkflowDecision Allow()
    {
        return new IncidentWorkflowDecision(true, false, string.Empty);
    }

    public static IncidentWorkflowDecision Reject(string message)
    {
        return new IncidentWorkflowDecision(false, false, message);
    }

    public static IncidentWorkflowDecision Forbid(string message)
    {
        return new IncidentWorkflowDecision(false, true, message);
    }
}
