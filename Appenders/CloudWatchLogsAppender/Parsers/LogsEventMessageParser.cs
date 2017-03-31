using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using AWSAppender.CloudWatchLogs.Model;
using AWSAppender.Core.Services;

namespace AWSAppender.CloudWatchLogs.Parsers
{
    public class LogsEventMessageParser : EventMessageParserBase<LogDatum>, ILogsEventMessageParser
    {
        //private Dictionary<string, Dimension> _dimensions;
        private LogDatum _currentDatum;
        private static string _assemblyName;

        public string DefaultGroupName { get; set; }
        public string DefaultMessage { get; set; }
        public DateTime? DefaultTimestamp { get; set; }
        public string DefaultStreamName { get; set; }
        public new bool ConfigOverrides { get { return base.ConfigOverrides; } set { base.ConfigOverrides = value; } }

        public LogsEventMessageParser()
            : base(true)
        { }
        public LogsEventMessageParser(bool useOverrides)
            : base(useOverrides)
        {
            if (Assembly.GetEntryAssembly() != null)
                _assemblyName = Assembly.GetEntryAssembly().GetName().Name;
        }
        protected override void ApplyDefaults()
        {
            if (string.IsNullOrEmpty(_currentDatum.StreamName))
                _currentDatum.StreamName = DefaultStreamName ?? _assemblyName ?? "unspecified";


            if (string.IsNullOrEmpty(_currentDatum.GroupName))
                _currentDatum.GroupName = DefaultGroupName ?? "unspecified";

            if (string.IsNullOrEmpty(_currentDatum.Message))
                _currentDatum.Message = DefaultMessage ?? "";

            if (!_currentDatum.Timestamp.HasValue)
                _currentDatum.Timestamp = DefaultTimestamp;
        }


        protected override bool IsSupportedName(string t0)
        {
            return SupportedNames.Any(x => x.Equals(t0, StringComparison.InvariantCultureIgnoreCase));
        }

        protected override bool IsSupportedValueField(string t0)
        {
            return false;
        }

        

        protected override bool FillName(AppenderValue value)
        {
            switch (value.Name.ToLowerInvariant())
            {
                case "message":
                case "__cav_rest":
                    _currentDatum.Message = DefaultsOverridePattern ? DefaultMessage ?? value.sValue : value.sValue;
                    break;

                case "streamname":
                    if (!string.IsNullOrEmpty(_currentDatum.StreamName))
                        return false;

                    _currentDatum.StreamName = DefaultsOverridePattern ? DefaultStreamName ?? value.sValue : value.sValue;
                    break;

                case "groupname":
                    if (!string.IsNullOrEmpty(_currentDatum.GroupName))
                        return false;

                    _currentDatum.GroupName = DefaultsOverridePattern ? DefaultGroupName ?? value.sValue : value.sValue;
                    break;

                case "timestamp":
                    if (_currentDatum.Timestamp.HasValue)
                        return false;

                    _currentDatum.Timestamp = DefaultsOverridePattern ? DefaultTimestamp ?? value.Time.Value.DateTime : value.Time.Value.DateTime;
                    break;
            }

            return true;
        }

        protected override void NewDatum()
        {
            _currentDatum = new LogDatum();
        }




        protected override bool ShouldLocalParse(string t0)
        {
            return t0.Equals("message",StringComparison.OrdinalIgnoreCase);
        }


        protected override void LocalParse(ref List<Match>.Enumerator tokens)
        {
            tokens.MoveNext();
            AddValue(new AppenderValue{Name = "message",sValue = tokens.Current.Value.Trim(" \"".ToCharArray())});
        }

        protected override void Init()
        {
            base.Init();
            //_dimensions = new Dictionary<string, Dimension>();
            _currentDatum = null;
        }

        protected override IEnumerable<LogDatum> GetParsedData()
        {
            if (_currentDatum.GroupName.Contains("instance"))
            {

            }
            return new[] { _currentDatum };
        }

        public static readonly HashSet<string> SupportedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                                                    {
                                                        "Message",
                                                        "GroupName",
                                                        "StreamName",
                                                        "Timestamp",
                                                        "Message"
                                                    };

    }

}