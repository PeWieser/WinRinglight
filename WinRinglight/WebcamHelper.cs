using Microsoft.Win32;

namespace WinRinglight
{
    public static class WebcamHelper
    {
        public static bool IsWebcamInUse()
        {
            try
            {
                string basePath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\CapabilityAccessManager\ConsentStore\webcam";

                // Allow RegistryKey to be null
                using (RegistryKey? consentKey = Registry.CurrentUser.OpenSubKey(basePath))
                {
                    if (consentKey == null) return false;

                    foreach (string subKeyName in consentKey.GetSubKeyNames())
                    {
                        if (subKeyName == "NonPackaged") continue;
                        using (RegistryKey? appKey = consentKey.OpenSubKey(subKeyName))
                        {
                            if (IsKeyActive(appKey)) return true;
                        }
                    }

                    using (RegistryKey? nonPackagedKey = consentKey.OpenSubKey("NonPackaged"))
                    {
                        if (nonPackagedKey != null)
                        {
                            foreach (string subKeyName in nonPackagedKey.GetSubKeyNames())
                            {
                                using (RegistryKey? appKey = nonPackagedKey.OpenSubKey(subKeyName))
                                {
                                    if (IsKeyActive(appKey)) return true;
                                }
                            }
                        }
                    }
                }
            }
            catch
            {
                // Ignore registry access errors silently
            }

            return false;
        }

        // Allow parameter to be null
        private static bool IsKeyActive(RegistryKey? key)
        {
            if (key == null) return false;

            // Values can be null
            object? stopTimeObj = key.GetValue("LastUsedTimeStop");
            object? startTimeObj = key.GetValue("LastUsedTimeStart");

            if (stopTimeObj is long stopTime && startTimeObj is long startTime)
            {
                if (stopTime == 0 && startTime > 0)
                    return true;
            }
            return false;
        }
    }
}