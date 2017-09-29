var myApp = {};
var pushNotifications = Windows.Networking.PushNotifications;

var inBackground = true;
Windows.UI.WebUI.WebUIApplication.addEventListener("enteredbackground", function () { inBackground = true });
Windows.UI.WebUI.WebUIApplication.addEventListener("leavingbackground", function () { inBackground = false });

var coldstartCollected = false;
var coldstartNotification = null;

var collectColdstartNotification = function () {
    if (coldstartCollected) {
        return;
    }
    coldstartCollected = true;

    // Retrieve the coldstart notification that started the application
    var activationContext = cordova.require("cordova/platform").activationContext;
    if (activationContext.kind === Windows.ApplicationModel.Activation.ActivationKind.toastNotification) {
        var argsObj = parseLaunchArgs(activationContext.argument);
        if ("cdvttl" in argsObj || "cdvmsg" in argsObj) {
            var title = argsObj.cdvttl || "";
            var message = argsObj.cdvmsg || "";
            delete argsObj.cdvttl;
            delete argsObj.cdvmsg;
            var additionalData = argsObj;
            additionalData.coldstart = true;
            coldstartNotification = {
                title: title,
                message: message,
                additionalData: additionalData
            }
        }
    }
};

var createNotificationJSON = function (e) {
    var result = { message: '' };       //Added to identify callback as notification type in the API in case where notification has no message
    var notificationPayload;

    result.additionalData = {};

    switch (e.notificationType) {
        case pushNotifications.PushNotificationType.toast:
        case pushNotifications.PushNotificationType.tile:
            if (e.notificationType === pushNotifications.PushNotificationType.toast) {
                notificationPayload = e.toastNotification.content;
            }
            else {
                notificationPayload = e.tileNotification.content;
            }
            var texts = notificationPayload.getElementsByTagName("text");
            if (texts.length > 1) {
                result.title = texts[0].innerText;
                result.message = texts[1].innerText;
            }
            else if(texts.length === 1) {
                result.message = texts[0].innerText;
            }
            var images = notificationPayload.getElementsByTagName("image");
            if (images.length > 0) {
                result.image = images[0].getAttribute("src");
            }
            var soundFile = notificationPayload.getElementsByTagName("audio");
            if (soundFile.length > 0) {
                result.sound = soundFile[0].getAttribute("src");
            }
            var toasts = notificationPayload.getElementsByTagName("toast");
            if (toasts.length > 0) { // should be at most 1
                var argsObj = parseLaunchArgs(toasts[0].getAttribute("launch"));
                Object.keys(argsObj).forEach(function (key) {
                    if (key === "cdvttl") {
                        result.title = argsObj[key]; // Prefer the title in launch args
                    } else if (key === "cdvmsg") {
                        result.message = argsObj[key]; // Prefer the message in launch args
                    } else {
                        result.additionalData[key] = argsObj[key];
                    }
                });
            }
            break;

        case pushNotifications.PushNotificationType.badge:
            notificationPayload = e.badgeNotification.content;
            result.count = notificationPayload.getElementsByTagName("badge")[0].getAttribute("value");
            break;

        case pushNotifications.PushNotificationType.raw:
            result.message = e.rawNotification.content;
            break;
    }

    result.additionalData.foreground = !inBackground;
    result.additionalData.pushNotificationReceivedEventArgs = e;
    return result;
}

var parseLaunchArgs = function (argsString) {
    try {
        return JSON.parse(argsString);
    } catch (e) {
        return {};
    }
}

module.exports = {
    init: function (onSuccess, onFail, args) {
        var options = (args[0] || {}).windows || {};

        var onNotificationReceived = function (e) {
            var result = createNotificationJSON(e);
            if (options.silentForeground && result.additionalData.foreground) {
                e.cancel = true;
            }
            onSuccess(result, { keepCallback: true });
        }

        try {
            pushNotifications.PushNotificationChannelManager.createPushNotificationChannelForApplicationAsync().done(
                function (channel) {
                    var result = {};
                    result.registrationId = channel.uri;
                    myApp.channel = channel;
                    channel.addEventListener("pushnotificationreceived", onNotificationReceived);
                    myApp.notificationEvent = onNotificationReceived;
                    onSuccess(result, { keepCallback: true });

                    // Flush coldstart notification
                    collectColdstartNotification();
                    if (coldstartNotification) {
                        onSuccess(coldstartNotification, { keepCallback: true });
                        coldstartNotification = null;
                    }
                }, function (error) {
                    onFail(error);
                });
        } catch (ex) {
            onFail(ex);
        }
    },
    hasColdStartNotification: function (onSuccess, onFail, args) {
        collectColdstartNotification();
        onSuccess(coldstartNotification != null);
    },
    unregister: function (onSuccess, onFail, args) {
        try {
            myApp.channel.removeEventListener("pushnotificationreceived", myApp.notificationEvent);
            myApp.channel.close();
            onSuccess();
        } catch(ex) {
            onFail(ex);
        }
    }
};
require("cordova/exec/proxy").add("PushNotification", module.exports);


