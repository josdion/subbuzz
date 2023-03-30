using System;

#if JELLYFIN
using Microsoft.Extensions.Logging;
#else
using MediaBrowser.Model.Logging;
#endif

namespace subbuzz.Helpers
{
    public class Logger
    {
#if JELLYFIN

        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger _logger;
        
        public Logger(ILoggerFactory loggerFactory, string name)
        {
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger(name);
        }

        public Logger GetLogger(string name)
        {
            return new Logger(_loggerFactory, name);
        }

        public Logger GetLogger<T>()
        {
            return new Logger(_loggerFactory, typeof(T).FullName);
        }

        public void LogInformation(string message, params object[] paramList) => _logger?.LogInformation(message, paramList);
        public void LogDebug(string message, params object[] paramList) => _logger?.LogDebug(message, paramList);
        public void LogError(Exception exception, string message, params object[] args) => _logger?.LogError(exception, message, args);

#else

        private readonly ILogger _logger;
        private readonly string _name;

        public Logger(ILogger logger, string name)
        {
            _logger = logger;
            _name = name;
        }

        public Logger GetLogger(string name)
        {
            return new Logger(_logger, name);
        }

        public Logger GetLogger<T>()
        {
            return new Logger(_logger, typeof(T).FullName);
        }

        public void LogInformation(string message, params object[] paramList) => _logger?.Info($"{_name}: {message}", paramList);
        public void LogDebug(string message, params object[] paramList) => _logger?.Debug($"{_name}: {message}", paramList);
        public void LogError(Exception exception, string message, params object[] args) => _logger?.ErrorException($"{_name}: {message}", exception, args);

#endif
    }
}
