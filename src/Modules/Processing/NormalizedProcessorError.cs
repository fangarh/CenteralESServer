namespace CenteralES.Processing;

public enum NormalizedProcessorError
{
    InvalidInput,
    ProcessorTimeout,
    ProcessorUnreachable,
    ProcessorHttpError,
    ProcessorBadResponse,
    ProcessorContractError,
    ProcessorOverloaded,
    TemporaryStorageFull,
    InternalError
}
