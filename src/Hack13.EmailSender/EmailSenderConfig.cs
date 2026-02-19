namespace Hack13.EmailSender;

public sealed class EmailSenderConfig
{
    public string From { get; set; } = string.Empty;
    public List<string> To { get; set; } = [];
    public List<string> Cc { get; set; } = [];
    public List<string> Bcc { get; set; } = [];
    public string Subject { get; set; } = string.Empty;
    public string? Body { get; set; }
    public string? BodyTemplate { get; set; }
    public List<string> Attachments { get; set; } = [];
    public string? ReplyTo { get; set; }
}
