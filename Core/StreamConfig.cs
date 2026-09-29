namespace ScreenToRTSP.Core
{
    public class StreamConfig
    {
        public string AppDataPath { get; set; }
        public string Port { get; set; }
        public bool UseAuth { get; set; }
        public string Username { get; set; }
        public string Password { get; set; }
        public string StreamName { get; set; }
        
        public bool DualStream { get; set; }
        public int QualityIndex { get; set; }
        public string Fps { get; set; }
        public int EncoderIndex { get; set; }
        
        public bool ShowCursor { get; set; }
        public int MonitorIndex { get; set; }
        public System.Drawing.Rectangle MonitorBounds { get; set; }
    }
}
