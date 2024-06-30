namespace ExtendedFileHandler.EventArgs
{
    public class LogMessageArgs
    {
        public string Message { get; }

        public LogMessageArgs(string message)
        {
            Message = message;
        }
    }
}
