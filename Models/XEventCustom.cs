namespace sqlserver.tools.queryrecompile.Models
{
    ///<Summary>
    /// XEvent Custom Type
    ///</Summary>
    public class XEventCustom
    {
        /*
        //public int Id { get; set; }
        ///<Summary>
        /// XEvent Event Name
        ///</Summary>
        //public string EventName { get; set; }
        ///<Summary>
        /// XEvent Database Id
        ///</Summary>
        //public int DatabaseId { get; set; }
        */
        ///<Summary>
        /// XEvent Database Name
        ///</Summary>
        public string? DatabaseName { get; set; }
        ///<Summary>
        /// XEvent Object Name
        ///</Summary>
        public string? ObjectName { get; set; }
        ///<Summary>
        /// XEvent Timestamp
        ///</Summary>
        public string? Timestamp { get; set; }
        /*
        ///<Summary>
        /// XEvent Duration Nano
        ///</Summary>
        //public UInt64 DurationNano { get; set; }
        */
        ///<Summary>
        /// XEvent Duration Sec
        ///</Summary>
        public double DurationSec { get; set; }
        ///<Summary>
        /// XEvent Client AppName
        ///</Summary>
        public string? ClientAppName { get; set; }
        ///<Summary>
        /// XEvent Client HostName
        ///</Summary>
        public string? ClientHostName { get; set; }
        ///<Summary>
        /// XEvent Write Format Out
        ///</Summary>
        public override string ToString()
        {
            return string.Format(
                    //$"EventName: {EventName}\t\n" +
                    //$"\tDatabase Id:\t\t{DatabaseId}\n" +
                    $"\tDatabase Name:\t\t{DatabaseName}\n" +
                    $"\tObject Name:\t\t{ObjectName}\n" +
                    $"\tTimestamp:\t\t{Timestamp}\n" +
                    //$"\tStatement:\t\t{xeStatementField}\n" +
                    //$"\tDuration (NaNo):\t{DurationNano}\n" +
                    $"\tDuration (Sec):\t\t{DurationSec}\n" +
                    $"\tClientAppName:\t\t{ClientAppName}\n" +
                    $"\tClientHostName:\t\t{ClientHostName}\n"
                    );
        }
    }
}
