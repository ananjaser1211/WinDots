namespace WinDots.Core.Media;

/// <summary>Outcome of a player command. Commands are requests; a session may reject or not support them.</summary>
public readonly record struct CommandResult(CommandStatus Status, string? Message = null)
{
    public static CommandResult Succeeded { get; } = new(CommandStatus.Succeeded);

    public static CommandResult Rejected(string? message = null) => new(CommandStatus.Rejected, message);

    public static CommandResult Unsupported(string capability) => new(CommandStatus.Unsupported, $"{capability} is not supported by this player.");

    public static CommandResult Faulted(Exception exception) => new(CommandStatus.Faulted, exception.Message);

    public bool IsSuccess => Status == CommandStatus.Succeeded;
}
