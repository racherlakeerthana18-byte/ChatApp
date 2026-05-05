namespace ChatApp.Application.Exceptions;

public sealed class ChatDomainException : Exception
{
    public ChatDomainException(string message, string errorCode = "chat_error")
        : base(message)
    {
        ErrorCode = errorCode;
    }

    public string ErrorCode { get; }
}
