namespace Opstrax.Api.Data;

// Message is safe for the document API; InnerException remains server-side only.
public sealed class DocumentTransactionUncertainException(string message, Exception innerException)
    : Exception(message, innerException);
