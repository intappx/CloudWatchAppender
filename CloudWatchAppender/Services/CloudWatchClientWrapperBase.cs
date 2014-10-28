using System;
using System.Collections.Concurrent;
using System.Configuration;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Amazon;
using Amazon.CloudWatch;
using Amazon.CloudWatchLogs;
using Amazon.Runtime;
using Amazon.Util;
using CloudWatchAppender.Appenders;
using log4net.Util;

namespace CloudWatchAppender.Services
{
    public abstract class CloudWatchClientWrapperBase<T> where T : AmazonServiceClient
    {
        protected string _endPoint;
        protected string _accessKey;
        protected string _secret;
        protected static ConcurrentDictionary<int, Task> _tasks = new ConcurrentDictionary<int, Task>();

        protected T Client { get; private set; }

        protected CloudWatchClientWrapperBase(string endPoint, string accessKey, string secret)
        {
            _endPoint = endPoint;
            _accessKey = accessKey;
            _secret = secret;

            SetupClient();
        }

        private void SetupClient()
        {
            if (Client != null)
                return;

            RegionEndpoint regionEndpoint;
            ClientConfig cloudWatchConfig;
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                cloudWatchConfig = new AmazonCloudWatchLogsConfig();
            else
                cloudWatchConfig = new AmazonCloudWatchConfig();


            if (string.IsNullOrEmpty(_endPoint) && ConfigurationManager.AppSettings["AWSServiceEndpoint"] != null)
                _endPoint = ConfigurationManager.AppSettings["AWSServiceEndpoint"];

            if (string.IsNullOrEmpty(_accessKey) && ConfigurationManager.AppSettings["AWSAccessKey"] != null)
                _accessKey = ConfigurationManager.AppSettings["AWSAccessKey"];

            if (string.IsNullOrEmpty(_secret) && ConfigurationManager.AppSettings["AWSSecretKey"] != null)
                _secret = ConfigurationManager.AppSettings["AWSSecretKey"];

            //_client = AWSClientFactory.CreateAmazonCloudWatchClient(_accessKey, _secret);

            if (!string.IsNullOrEmpty(_endPoint))
            {
                if (_endPoint.StartsWith("http"))
                {
                    cloudWatchConfig.ServiceURL = _endPoint;
                }
                else
                {
                    cloudWatchConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(_endPoint);
                }
            }

            if (string.IsNullOrEmpty(_accessKey))
                try
                {
                    if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AWSProfileName"]) || ProfileManager.GetAWSCredentials("default") != null)
                    {
                        if (!string.IsNullOrEmpty(ConfigurationManager.AppSettings["AWSRegion"]))
                            Client = AWSClientFactoryWrapper<T>.CreateServiceClient();
                        else if (cloudWatchConfig.RegionEndpoint != null)
                            Client = AWSClientFactoryWrapper<T>.CreateServiceClient(cloudWatchConfig);
                    }
                }
                catch (AmazonServiceException e)
                {

                }
                catch (Exception e)
                {

                }


            if (Client == null && !string.IsNullOrEmpty(_accessKey))
                if (cloudWatchConfig != null)
                    Client = AWSClientFactoryWrapper<T>.CreateServiceClient(_accessKey, _secret, cloudWatchConfig);
                else
                    Client = AWSClientFactoryWrapper<T>.CreateServiceClient(_accessKey, _secret);

            if (Client == null)
                throw new CloudWatchAppenderException("Couldn't create Amazon client.");

        }

        public ConcurrentDictionary<int, Task> Tasks
        {
            get { return _tasks; }
        }

        public static bool HasPendingRequests
        {
            get { return _tasks.Values.Any(t => !t.IsCompleted); }
        }

        public static void WaitForPendingRequests(TimeSpan timeout)
        {
            var startedTime = DateTime.UtcNow;
            var timeConsumed = TimeSpan.Zero;
            while (HasPendingRequests && timeConsumed < timeout)
            {
                Task.WaitAll(_tasks.Values.ToArray(), timeout - timeConsumed);
                timeConsumed = DateTime.UtcNow - startedTime;
            }
        }

        public static void WaitForPendingRequests()
        {
            while (HasPendingRequests)
                Task.WaitAll(_tasks.Values.ToArray());
        }

        protected ClientConfig AmazonCloudWatchConfig(out RegionEndpoint regionEndpoint)
        {
            ClientConfig cloudWatchConfig = null;
            if (this is CloudWatchClientWrapper)
                cloudWatchConfig = new AmazonCloudWatchConfig();
            else
                cloudWatchConfig = new AmazonCloudWatchLogsConfig();

            regionEndpoint = null;

            if (string.IsNullOrEmpty(_endPoint) && ConfigurationManager.AppSettings["AWSServiceEndpoint"] != null)
                _endPoint = ConfigurationManager.AppSettings["AWSServiceEndpoint"];

            if (string.IsNullOrEmpty(_accessKey) && ConfigurationManager.AppSettings["AWSAccessKey"] != null)
                _accessKey = ConfigurationManager.AppSettings["AWSAccessKey"];

            if (string.IsNullOrEmpty(_secret) && ConfigurationManager.AppSettings["AWSSecretKey"] != null)
                _secret = ConfigurationManager.AppSettings["AWSSecretKey"];

            //_client = AWSClientFactory.CreateAmazonCloudWatchClient(_accessKey, _secret);

            if (!string.IsNullOrEmpty(_endPoint))
            {
                if (_endPoint.StartsWith("http"))
                {
                    cloudWatchConfig.ServiceURL = _endPoint;
                }
                else
                {
                    regionEndpoint = cloudWatchConfig.RegionEndpoint = RegionEndpoint.GetBySystemName(_endPoint);
                }
            }



            return cloudWatchConfig;
        }

        protected void QueueRequest(Func<AmazonWebServiceResponse> func)
        {
            var tokenSource = new CancellationTokenSource();
            CancellationToken ct = tokenSource.Token;

            try
            {
                Task superTask = null;
                superTask =
                    new Task(() =>
                             {
                                 var nestedTask =
                                     Task.Factory.StartNew(() =>
                                                           {
                                                               try
                                                               {
                                                                   var tmpCulture = Thread.CurrentThread.CurrentCulture;
                                                                   Thread.CurrentThread.CurrentCulture = new CultureInfo(
                                                                       "en-GB", false);

                                                                   LogLog.Debug(GetType(), "Sending");
                                                                   var response = func();
                                                                   LogLog.Debug(GetType(),
                                                                       "RequestID: " + response.ResponseMetadata.RequestId);

                                                                   Thread.CurrentThread.CurrentCulture = tmpCulture;
                                                               }
                                                               catch (Exception e)
                                                               {
                                                                   LogLog.Debug(GetType(), e.ToString());
                                                               }
                                                           }, ct);

                                 try
                                 {
                                     if (!nestedTask.Wait(30000))
                                     {
                                         tokenSource.Cancel();
                                         LogLog.Error(GetType(),
                                             "CloudWatchAppender timed out while submitting to CloudWatch. Exception (if any): {0}",
                                             nestedTask.Exception);
                                     }
                                 }
                                 catch (Exception e)
                                 {
                                     LogLog.Error(GetType(),
                                         "CloudWatchAppender encountered an error while submitting to cloudwatch. {0}", e);
                                 }

                                 superTask.ContinueWith(t =>
                                                        {
                                                            Task task2;
                                                            _tasks.TryRemove(superTask.Id, out task2);
                                                            LogLog.Debug(GetType(), "Cloudwatch complete");
                                                            if (superTask.Exception != null)
                                                                LogLog.Error(GetType(),
                                                                    string.Format(
                                                                        "CloudWatchAppender encountered an error while submitting to CloudWatch. {0}",
                                                                        superTask.Exception));
                                                        });
                             });

                _tasks.TryAdd(superTask.Id, superTask);
                superTask.Start();
            }
            catch (Exception e)
            {
                LogLog.Error(GetType(),
                    string.Format(
                        "CloudWatchAppender encountered an error while submitting to cloudwatch. {0}", e));
            }
        }
    }

    static class AWSClientFactoryWrapper<T>
    {
        public static T CreateServiceClient(ClientConfig cloudWatchConfig)
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient((AmazonCloudWatchLogsConfig)cloudWatchConfig);
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient((AmazonCloudWatchConfig)cloudWatchConfig);

            throw new NotSupportedException();
        }

        public static T CreateServiceClient(RegionEndpoint regionEndpoint)
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient(regionEndpoint);
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient(regionEndpoint);
            throw new NotSupportedException();
        }

        public static T CreateServiceClient(string accessKey, string secret, RegionEndpoint regionEndpoint)
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient(accessKey, secret, regionEndpoint);
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient(accessKey, secret, regionEndpoint);
            throw new NotSupportedException();
        }

        public static T CreateServiceClient(string accessKey, string secret)
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient(accessKey, secret);
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient(accessKey, secret);
            throw new NotSupportedException();
        }

        public static T CreateServiceClient(string accessKey, string secret, ClientConfig clientConfig)
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient(accessKey, secret, (AmazonCloudWatchLogsConfig)clientConfig);
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient(accessKey, secret, (AmazonCloudWatchConfig)clientConfig);
            throw new NotSupportedException();
        }

        public static T CreateServiceClient()
        {
            if (typeof(T) == typeof(AmazonCloudWatchLogsClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchLogsClient();
            if (typeof(T) == typeof(AmazonCloudWatchClient))
                return (T)AWSClientFactory.CreateAmazonCloudWatchClient();
            throw new NotSupportedException();
        }
    }
}