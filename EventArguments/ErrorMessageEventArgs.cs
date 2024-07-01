namespace ExtendedFileHandler.EventArguments
{
    public class ErrorMessageEventArgs(string message, string stackTrace)
    {
        public string Message { get; } = message;
        public string StackTrace { get; } = stackTrace;
    }
}
