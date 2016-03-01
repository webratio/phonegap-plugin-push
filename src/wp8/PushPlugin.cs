using Microsoft.Phone.Notification;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows;
using System.Windows.Controls;

namespace WPCordovaClassLib.Cordova.Commands
{
    public class PushPlugin : BaseCommand
    {
        public const string FOREGROUND_ADDITIONAL_DATA = "foreground";
        public const string COLDSTART_ADDITIONAL_DATA = "coldstart";

        public const string TITLE_PARAMETER_KEY = "cdvttl";
        public const string MESSAGE_PARAMETER_KEY = "cdvmsg";

        private const string ALREADY_INITIALIZED_ERROR = "Already initialized";
        private const string INVALID_REGISTRATION_ERROR = "Unable to open a channel with the specified name. The most probable cause is that you have already registered a channel with a different name. Call unregister(old-channel-name) or uninstall and redeploy your application.";

        private string callbackId = null;
        private volatile HttpNotificationChannel currentChannel = null;
        private volatile Uri lastChannelUri = null;
        private bool coldstartCollected = false;
        private NotificationResult coldstartNotification = null;

        public void init(string argsString)
        {
            Options options;
            try
            {
                options = JsonConvert.DeserializeObject<Options>(JsonConvert.DeserializeObject<string[]>(argsString)[0]);
            }
            catch (Exception)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.JSON_EXCEPTION));
                return;
            }

            // Prevent double initialization.
            if (this.currentChannel != null)
            {
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.INVALID_ACTION, ALREADY_INITIALIZED_ERROR));
            }

            // Create or retrieve the notification channel.
            var channel = HttpNotificationChannel.Find(options.WP8.ChannelName);
            if (channel == null)
            {
                channel = new HttpNotificationChannel(options.WP8.ChannelName);
                SubscribeChannelEvents(channel);

                try
                {
                    channel.Open();
                }
                catch (InvalidOperationException)
                {
                    UnsubscribeChannelEvents(channel);
                    this.DispatchCommandResult(new PluginResult(PluginResult.Status.ERROR, INVALID_REGISTRATION_ERROR));
                    return;
                }

                channel.BindToShellToast();
                channel.BindToShellTile();
            }
            else
            {
                SubscribeChannelEvents(channel);
            }
            this.currentChannel = channel;
            this.lastChannelUri = null;
            this.callbackId = CurrentCommandCallbackId;

            // First attempt at notifying the URL (most of the times it won't notify anything)
            NotifyRegitrationIfNeeded();
        }

        public void unregister(string argsString)
        {
            var channel = Interlocked.Exchange(ref this.currentChannel, null);
            if (channel != null)
            {
                UnsubscribeChannelEvents(channel);
                channel.UnbindToShellTile();
                channel.UnbindToShellToast();
                channel.Close();
                channel.Dispose();
                this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, "Channel " + channel.ChannelName + " closed!"));
            }
        }

        private void SubscribeChannelEvents(HttpNotificationChannel channel)
        {
            channel.ChannelUriUpdated += CurrentChannel_ChannelUriUpdated;
            channel.ErrorOccurred += CurrentChannel_ErrorOccurred;
            channel.ShellToastNotificationReceived += CurrentChannel_ShellToastNotificationReceived;
            channel.HttpNotificationReceived += CurrentChannel_HttpNotificationReceived;
        }

        private void UnsubscribeChannelEvents(HttpNotificationChannel channel)
        {
            channel.ChannelUriUpdated -= CurrentChannel_ChannelUriUpdated;
            channel.ErrorOccurred -= CurrentChannel_ErrorOccurred;
            channel.ShellToastNotificationReceived -= CurrentChannel_ShellToastNotificationReceived;
            channel.HttpNotificationReceived -= CurrentChannel_HttpNotificationReceived;
        }

        private void CurrentChannel_ChannelUriUpdated(object sender, NotificationChannelUriEventArgs e)
        {
            NotifyRegitrationIfNeeded();
        }

        private void CurrentChannel_ErrorOccurred(object sender, NotificationChannelErrorEventArgs e)
        {
            DispatchNonFinalResult(PluginResult.Status.ERROR, e.Message);
        }

        private void CurrentChannel_ShellToastNotificationReceived(object sender, NotificationEventArgs e)
        {
            var nodeValues = e.Collection;

            var title = nodeValues.ContainsKey("wp:Text1") ? nodeValues["wp:Text1"] : string.Empty;
            var message = nodeValues.ContainsKey("wp:Text2") ? nodeValues["wp:Text2"] : string.Empty;

            var notification = new NotificationResult { Title = title, Message = message };
            if (nodeValues.ContainsKey("wp:Param"))
            {
                var queryDict = ParseQueryString(nodeValues["wp:Param"]);
                foreach (var entry in queryDict)
                {
                    if (entry.Key == TITLE_PARAMETER_KEY)
                    {
                        notification.Title = entry.Value; // prefer the title found in parameters
                    }
                    else if (entry.Key == MESSAGE_PARAMETER_KEY)
                    {
                        notification.Message = entry.Value; // prefer the message found in parameters
                    }
                    else
                    {
                        notification.AdditionalData.Add(entry.Key, entry.Value);
                    }
                }
                notification.AdditionalData.Add(FOREGROUND_ADDITIONAL_DATA, true);
            }

            NotifyNotification(notification);
        }

        private void CurrentChannel_HttpNotificationReceived(object sender, HttpNotificationEventArgs e)
        {
            var notification = new NotificationResult { Title = string.Empty, Message = string.Empty };
            using (var reader = new StreamReader(e.Notification.Body))
            {
                notification.AdditionalData.Add("body", reader.ReadToEnd());
            }

            NotifyNotification(notification);
        }

        public void hasColdStartNotification(string argsString)
        {
            bool notificationPresent;
            lock (this)
            {
                CollectColdstartNotification();
                notificationPresent = (this.coldstartNotification != null);
            }
            this.DispatchCommandResult(new PluginResult(PluginResult.Status.OK, notificationPresent ? "true" : "false"));
        }

        private void NotifyRegitrationIfNeeded()
        {
            Uri currentChannelUri;
            lock (this)
            {
                currentChannelUri = this.currentChannel.ChannelUri;
                if (currentChannelUri == this.lastChannelUri)
                {
                    return;
                }
                this.lastChannelUri = currentChannelUri;
            }

            // If we have a URI, notify the client and take the change to flush any coldstart notification.
            if (currentChannelUri != null)
            {
                var result = new RegisterResult { Uri = currentChannelUri.ToString() };
                DispatchNonFinalResult(PluginResult.Status.OK, JsonConvert.SerializeObject(result));

                NotifyColdstartNotificationIfNeeded();
            }
        }

        private void NotifyColdstartNotificationIfNeeded()
        {
            NotificationResult notification;
            lock (this)
            {
                CollectColdstartNotification();
                if (this.coldstartNotification == null)
                {
                    return;
                }
                notification = this.coldstartNotification;
                this.coldstartNotification = null;
            }

            NotifyNotification(notification);
        }

        private void CollectColdstartNotification()
        {
            lock (this)
            {
                if (this.coldstartCollected)
                {
                    return;
                }
                this.coldstartCollected = true;
            }

            // Retrieve the coldstart notification that started the application.
            var query = RetrieveQueryString();
            if (query.ContainsKey(TITLE_PARAMETER_KEY) || query.ContainsKey(MESSAGE_PARAMETER_KEY))
            {
                var title = query.ContainsKey(TITLE_PARAMETER_KEY) ? query[TITLE_PARAMETER_KEY] : string.Empty;
                var message = query.ContainsKey(MESSAGE_PARAMETER_KEY) ? query[MESSAGE_PARAMETER_KEY] : string.Empty;

                var notification = new NotificationResult { Title = title, Message = message };
                foreach (string key in query.Keys)
                {
                    if (key != TITLE_PARAMETER_KEY && key != MESSAGE_PARAMETER_KEY)
                    {
                        notification.AdditionalData.Add(key, query[key]);
                    }
                }
                notification.AdditionalData.Add(COLDSTART_ADDITIONAL_DATA, true);
                this.coldstartNotification = notification;
            }
            else
            {
                this.coldstartNotification = null;
            }
        }

        private void NotifyNotification(NotificationResult notification)
        {
            DispatchNonFinalResult(PluginResult.Status.OK, JsonConvert.SerializeObject(notification));
        }

        private IDictionary<string, string> RetrieveQueryString()
        {
            var waitHandle = new AutoResetEvent(false);
            IDictionary<string, string> query = null;
            Deployment.Current.Dispatcher.BeginInvoke(() =>
            {
                var mainPage = (Page)((Frame)Application.Current.RootVisual).Content;
                query = mainPage.NavigationContext.QueryString;
                waitHandle.Set();
            });
            waitHandle.WaitOne();
            if (query == null)
            {
                throw new Exception("Unable to obtain main page query string");
            }
            return query;
        }

        private void DispatchNonFinalResult(PluginResult.Status status, object message = null)
        {
            string callbackId = CurrentCommandCallbackId ?? this.callbackId;
            if (callbackId == null)
            {
                throw new InvalidOperationException("No callback id is available");
            }
            PluginResult result = message != null ? new PluginResult(status, message) : new PluginResult(status);
            result.KeepCallback = true;
            this.DispatchCommandResult(result, callbackId);
        }

        private IDictionary<string, string> ParseQueryString(string query)
        {
            if (query.StartsWith("?"))
            {
                query = query.Substring(1);
            }
            var dict = new Dictionary<string, string>();
            foreach (var parameter in query.Split('&'))
            {
                if (parameter.Length > 0)
                {
                    var parts = parameter.Split('=');
                    var key = Uri.UnescapeDataString(parts[0]);
                    var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;
                    dict.Add(key, value);
                }
            }
            return dict;
        }

        [DataContract]
        public class Options
        {
            [DataMember(Name = "wp8", IsRequired = true)]
            public WP8Options WP8 { get; set; }
        }

        [DataContract]
        public class WP8Options
        {
            [DataMember(Name = "channelName", IsRequired = true)]
            public string ChannelName { get; set; }
        }

        [DataContract]
        public class RegisterResult
        {
            [DataMember(Name = "registrationId", IsRequired = true)]
            public string Uri { get; set; }
        }

        [DataContract]
        public class NotificationResult
        {
            public NotificationResult()
            {
                this.AdditionalData = new Dictionary<string, object>();
            }

            [DataMember(Name = "message", IsRequired = true)]
            public string Message { get; set; }

            [DataMember(Name = "title", IsRequired = true)]
            public string Title { get; set; }

            [DataMember(Name = "additionalData", IsRequired = true)]
            public IDictionary<string, object> AdditionalData { get; set; }

        }

    }
}