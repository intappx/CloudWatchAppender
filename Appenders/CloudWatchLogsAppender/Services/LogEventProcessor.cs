using System;
using System.Collections.Generic;
using AWSAppender.CloudWatchLogs.Model;
using AWSAppender.CloudWatchLogs.Parsers;
using AWSAppender.Core.Services;
using log4net.Core;

namespace AWSAppender.CloudWatchLogs.Services
{
    public class LogEventProcessor : EventProcessorBase, IEventProcessor<LogDatum>
    {
        private string _parsedStreamName;
        private string _parsedGroupName;
        private string _parsedMessage;
        private DateTime? _dateTimeOffset;
        private readonly string _groupName;
        private readonly string _streamName;
        private readonly string _timestamp;
        private readonly string _message;

        public LogEventProcessor(string groupName, string streamName, string timestamp, string message)
        {
            _groupName = groupName;
            _streamName = streamName;
            _timestamp = timestamp;
            _message = message;
        }


        public IEnumerable<LogDatum> ProcessEvent(LoggingEvent loggingEvent, string renderedString)
        {
            renderedString = PreProcess(loggingEvent, renderedString);

            var eventMessageParser = EventMessageParser as ILogsEventMessageParser;

            eventMessageParser.DefaultStreamName = _parsedStreamName;
            eventMessageParser.DefaultGroupName = _parsedGroupName;
            eventMessageParser.DefaultMessage = _parsedMessage;
            eventMessageParser.DefaultTimestamp = _dateTimeOffset ?? loggingEvent.TimeStamp;

            return eventMessageParser.Parse(renderedString);
        }

        public IEventMessageParser<LogDatum> EventMessageParser { get; set; }

        protected override void ParseProperties(PatternParser patternParser)
        {
            _parsedStreamName = string.IsNullOrEmpty(_streamName)
                ? null
                : patternParser.Parse(_streamName);

            _parsedGroupName = string.IsNullOrEmpty(_groupName)
                ? null
                : patternParser.Parse(_groupName);

            _parsedMessage = string.IsNullOrEmpty(_message)
                ? null
                : patternParser.Parse(_message);

            _dateTimeOffset = string.IsNullOrEmpty(_timestamp)
                ? null
                : (DateTime?)DateTime.Parse(patternParser.Parse(_timestamp));
        }

    }
}