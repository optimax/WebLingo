namespace WebLingo.Runtime;

public interface IWsDisposable { void Dispose(); }
public enum WsOperationState { Created, Pending, Completed, Failed, Cancelled }
public readonly record struct WsWorkerId(int Value);
public sealed record WsExecutionContext(WsWorkerId OwnerWorkerId);
