namespace unoframework.RequestHandlers;

/// <summary>Thrown when the client sent an invalid request; the host answers it with 400.</summary>
public sealed class BadRequestException(string message, Exception? innerException = null)
    : Exception(message, innerException);
