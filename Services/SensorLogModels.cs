namespace BLE_APP.Services;

public enum LogStopReason
{
    UserDisconnect,
    ProgrammaticDisconnect,
    UnexpectedDisconnect,
    Error,
    Cleanup,
    Shutdown
}

public sealed record LogSaveResult(
    string? FileName,
    string? Location,
    bool Saved,
    string? ErrorMessage);
