using Epic.OnlineServices.Logging;
using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace EpicTransport {
    public static class Logger {

        // 13RoH: EOS messages that are expected and harmless. They are shown as normal
        // (white) logs instead of red errors, so real errors stand out.
        //  - DeviceId credentials already exist: normal on every start after the first.
        //  - RTC room not requested: printed for every voice lobby with this SDK; voice works.
        //  - Overlay subclass: the overlay is disabled (EOSSDKComponent Flags) and unused.
        private static readonly string[] HarmlessMessages = {
            "DeviceId access credentials already exist",
            "associated RTC room that was not requested",
            "Failed to subclass window",
        };

        private static bool IsHarmless(string text) {
            if (string.IsNullOrEmpty(text)) return false;
            foreach (string harmless in HarmlessMessages) {
                if (text.Contains(harmless)) return true;
            }
            return false;
        }

        public static void EpicDebugLog(LogMessage message) {
            if (message.Level != LogLevel.Fatal && IsHarmless(message.Message)) {
                Debug.Log($"Epic Manager (harmless): Category - {message.Category} Message - {message.Message}");
                return;
            }

            switch (message.Level) {
                case LogLevel.Info:
                    Debug.Log($"Epic Manager: Category - {message.Category} Message - {message.Message}");
                    break;
                case LogLevel.Error:
                    Debug.LogError($"Epic Manager: Category - {message.Category} Message - {message.Message}");
                    break;
                case LogLevel.Warning:
                    Debug.LogWarning($"Epic Manager: Category - {message.Category} Message - {message.Message}");
                    break;
                case LogLevel.Fatal:
                    Debug.LogException(new Exception($"Epic Manager: Category - {message.Category} Message - {message.Message}"));
                    break;
                default:
                    Debug.Log($"Epic Manager: Unknown log processing. Category - {message.Category} Message - {message.Message}");
                    break;
            }
        }
    }
}