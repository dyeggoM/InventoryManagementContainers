using System.Threading.Channels;

namespace IMCAPI.Services
{
    public sealed class FileLoggerProvider : ILoggerProvider
    {
        private readonly Channel<LogEntry> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _backgroundTask;
        private readonly IHostEnvironment _env;

        public FileLoggerProvider(IHostEnvironment env)
        {
            _env = env;

            _channel = Channel.CreateUnbounded<LogEntry>(
                new UnboundedChannelOptions
                {
                    SingleReader = true,
                    AllowSynchronousContinuations = false
                });

            _backgroundTask = Task.Run(ProcessQueueAsync);
        }

        public ILogger CreateLogger(string categoryName) =>
            new FileLogger(_channel.Writer, categoryName);

        public void Dispose()
        {
            _channel.Writer.TryComplete();
            _cts.Cancel();

            try { _backgroundTask.Wait(); }
            catch (Exception) { }

            _cts.Dispose();
        }

        private async Task ProcessQueueAsync()
        {
            var reader = _channel.Reader;
            var token = _cts.Token;

            await foreach (var entry in reader.ReadAllAsync(token))
            {
                try
                {
                    var dir = Path.Combine(_env.ContentRootPath, "Logs");
                    Directory.CreateDirectory(dir);

                    var file = Path.Combine(dir, $"{entry.Timestamp:yyyyMMdd}_Logs.txt");

                    await using var sw = File.AppendText(file);
                    await sw.WriteLineAsync(entry.Formatted);
                }
                catch (OperationCanceledException) {  }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex);
                }
            }
        }

        internal record LogEntry(DateTime Timestamp, string Formatted);
    }


    internal sealed class FileLogger : ILogger
    {
        private readonly ChannelWriter<FileLoggerProvider.LogEntry> _writer;
        private readonly string _category;

        public FileLogger(ChannelWriter<FileLoggerProvider.LogEntry> writer, string category)
        {
            _writer = writer;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) => null;
        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;

            var now = DateTime.UtcNow;
            var stack = exception?.ToString() ?? string.Empty;
            var line = $"[{now:yyyy-MM-dd HH:mm:ss+00:00}] [{logLevel}] [{_category}] {formatter(state, exception)} {stack}";

            _writer.TryWrite(new FileLoggerProvider.LogEntry(now, line));
        }
    }
}
