namespace ResolveAI.Api.Services;

public class AIIncidentAnalysisException : Exception
{
    public AIIncidentAnalysisException(
        string message,
        bool isConfigurationError = false,
        Exception? innerException = null)
        : base(message, innerException)
    {
        IsConfigurationError = isConfigurationError;
    }

    public bool IsConfigurationError { get; }
}
