/*
 * Copyright 2012 Google Inc.
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 */

package com.google.android.gcm;

import static com.google.android.gcm.GCMConstants.DEFAULT_INTENT_SERVICE_CLASS_NAME;

import java.util.Arrays;

import android.app.Activity;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.util.Log;

import com.adobe.phonegap.push.RegistrationIntentService;
import com.adobe.phonegap.push.GCMIntentService;

/**
 * {@link BroadcastReceiver} that receives GCM messages and delivers them to an application-specific {@link GCMIntentService}
 * subclass.
 * <p>
 * By default, the {@link GCMIntentService} class belongs to the application main package and is named
 * {@link GCMConstants#DEFAULT_INTENT_SERVICE_CLASS_NAME}. To use a new class, the {@link #getGCMIntentServiceClassName(Context)} must
 * be overridden.
 */
public class GCMBroadcastReceiver extends BroadcastReceiver {

    private static final String TAG = "GCMBroadcastReceiver";
    private static boolean mReceiverSet = false;

    @Override
    public final void onReceive(Context context, Intent intent) {
        Log.v(TAG, "onReceive: " + intent.getAction());
        if (Intent.ACTION_BOOT_COMPLETED.equals(intent.getAction())
                || "android.intent.action.QUICKBOOT_POWERON".equals(intent.getAction())) {
            String[] senderIds = GCMRegistrar.getSenderIds(context);
            Log.v(TAG, "senderIds: " + Arrays.toString(senderIds));
            if (senderIds != null && senderIds.length > 0) {
                GCMRegistrar.checkDevice(context);
                GCMRegistrar.checkManifest(context);
                com.google.android.gcm.GCMRegistrar.register(context, senderIds);
            }
        } else {
            // do a one-time check if app is using a custom GCMBroadcastReceiver
            if (!mReceiverSet) {
                mReceiverSet = true;
                GCMRegistrar.setRetryReceiverClassName(getClass().getName());
            }
            String className = getGCMIntentServiceClassName(context);
            Log.v(TAG, "GCM IntentService class: " + className);
            // Delegates to the application-specific intent service.
            RegistrationIntentService.runIntentInService(context, intent, className);
        }
        setResult(Activity.RESULT_OK, null, null);
    }

    /**
     * Gets the class name of the intent service that will handle GCM messages.
     */
    protected String getGCMIntentServiceClassName(Context context) {
        return getDefaultIntentServiceClassName(context);
    }

    /**
     * Gets the default class name of the intent service that will handle GCM messages.
     */
    static final String getDefaultIntentServiceClassName(Context context) {
        return context.getPackageName() + DEFAULT_INTENT_SERVICE_CLASS_NAME;
    }
}
