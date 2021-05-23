using System;
using MediaBrowser.Model.Logging;

namespace subbuzz.Extensions
{
    public static class ILoggerExtensions
    {
        public static void LogInformation(this ILogger logger, string message, params object[] paramList)
        {
            logger.Info(message, paramList);
        }

        public static void LogDebug(this ILogger logger, string message, params object[] paramList)
        {
            logger.Debug(message, paramList);
        }

        public static void LogError(this ILogger logger, Exception exception, string message, params object[] args)
        {
            logger.ErrorException(message, exception, args);
        }
    }
}
