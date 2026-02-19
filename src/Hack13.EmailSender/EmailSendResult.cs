namespace Hack13.EmailSender;

public sealed class EmailSendResult
{
    public static EmailSendResult Sent(string messageId) =>
        new() { IsSuccess = true, MessageId = messageId };

    public static EmailSendResult Failed(string errorCode, string errorMessage, bool isRetryable = false) =>
        new() { IsSuccess = false, ErrorCode = errorCode, ErrorMessage = errorMessage, IsRetryable = isRetryable };

    public bool IsSuccess { get; init; }
    public string? MessageId { get; init; }
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public bool IsRetryable { get; init; }
}
