namespace LocalTransfer;

internal sealed record TransferRecord(string FileName, long Size, DateTime ReceivedAt, string FullPath);

internal sealed record OutgoingFile(string Id, string FileName, long Size, string FullPath);

internal sealed record OutgoingText(string Text, long Version);
