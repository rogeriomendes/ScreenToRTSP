using System.Collections.Generic;

namespace ScreenToRTSP.I18n
{
    public static class en_US
    {
        public static Dictionary<string, string> Strings = new Dictionary<string, string>
        {
            {"AppTitle", "Screen To RTSP"},
            {"AppSubtitle", "Screen Transmission Server"},
            {"TabVideo", "Video"},
            {"TabNetwork", "Network & Security"},
            {"TabAutomation", "Automation"},
            {"TabConfig", "Settings"},
            {"MonitorLabel", "Capture Screen:"},
            {"AllMonitors", "All Monitors"},
            {"MonitorPrefix", "Monitor"},
            {"EncoderLabel", "Encoder:"},
            {"EncoderCPU", "Processor (CPU)"},
            {"EncoderNVENC", "NVIDIA (NVENC)"},
            {"EncoderQSV", "Intel (QSV)"},
            {"QualityLabel", "Quality:"},
            {"QualityLow", "Low (Fast)"},
            {"QualityMedium", "Medium (Balanced)"},
            {"QualityHigh", "High (Heavy)"},
            {"FpsLabel", "Frames per Sec (FPS):"},
            {"TipDualStream", "Creates a secondary low-resolution channel. Ideal for DVR/NVR software to display multiple cameras at once without lagging (Mosaic mode)."},
            {"DualStreamLabel", "Stream Lightweight Extra Channel (Sub-stream for Mosaic)"},
            {"MouseCursorLabel", "Capture Mouse Cursor on image"},
            {"StreamNameLabel", "Channel Name:"},
            {"PortLabel", "RTSP Port:"},
            {"AuthLabel", "Require Username and Password to view (Recommended)"},
            {"UserLabel", "Username:"},
            {"PasswordLabel", "Password:"},
            {"PassPlaceholder", "(Leave blank to keep unchanged)"},
            {"TipSchedule", "Transmits automatically ONLY within this time frame. Outside of it, the server rests waiting for the right time."},
            {"ScheduleLabel", "Turn on automatically only during this time:"},
            {"ScheduleStartLabel", "Start Time:"},
            {"ScheduleEndLabel", "End Time:"},
            {"StartWindowsLabel", "Start minimized automatically with Windows"},
            {"WebServerLabel", "Enable Remote Control Web Panel"},
            {"WebPortLabel", "Web Panel Port:"},
            {"LanguageLabel", "Interface Language:"},
            {"ThemeLabel", "Application Theme:"},
            {"ThemeSystem", "System"},
            {"ThemeDark", "Dark"},
            {"ThemeLight", "Light"},
            {"LanguageSystem", "System Language"},
            {"StatusPrefix", "Status: "},
            {"StatusStopped", "Waiting"},
            {"StatusRunning", "Transmitting"},
            {"StatusWaitingLinks", "Camera links will appear here."},
            {"BtnStart", "Start Transmission"},
            {"BtnStop", "Stop Transmission"},
            {"BtnSave", "Save"},
            {"BtnCopy", "Copy Link"},
            {"BtnCopyLan", "Main"},
            {"BtnCopyLocal", "Light"},
            {"BtnOpenWeb", "Open Web Panel"},
            {"MsgSaveSuccess", "Settings saved. The server was restarted successfully!"},
            {"MsgSaving", "Saving..."},
            {"MsgSaved", "Settings saved!"},
            {"MsgSuccessTitle", "Success"},
            {"TrayConfig", "Open Configuration"},
            {"TrayExit", "Exit"},
            {"TabAbout", "About"}, {"TabPreview", "Preview"},
            {"AboutDesc", "Lightweight and automated server to capture and stream the computer screen via RTSP protocol to NVRs."},
            {"AboutVersion", "Version: "},
            {"UpdateAvailable", "A new update is available!"},
            {"BtnUpdateBinaries", "Install Updates"},
            {"MsgStopBeforeUpdate", "The current transmission will be paused to update the essential components and restarted afterwards. Do you want to continue?"},
            {"NewVersion", "New: "},
            {"MsgUpdateSuccess", "Essential components successfully updated!"},
            {"BtnCheckUpdates", "Check for Updates"},
            {"MsgNoUpdate", "You already have the latest version of the essential components."},
            {"MsgCheckUpdateError", "Error checking for updates. Check your connection or try again later."}
        };
    }
}




