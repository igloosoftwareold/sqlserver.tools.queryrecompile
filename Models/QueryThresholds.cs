using System.Collections.Generic;
namespace sqlserver.tools.queryrecompile.Models
{
    public class DatabaseProcOptions
    {
        public List<DatabaseProcTemplate> DatabaseProcTemplates { get; set; } = new List<DatabaseProcTemplate>();
    }
    public class DatabaseProcTemplate
    {
        public string DatabaseName { get; set; }
        public string ObjectName { get; set; }
        public string SchemaName { get; set; }
        /*
        DateTime LastRecompileDate { get; set; }
        DateTime TimeBetweenRecompiles { get; set; }
        int OverrideRecompileIfCountEqualsThreshold { get; set; }
        int SendErrorMessageCountThreshhold { get; set; }
        bool NotifyOnRecompile { get; set; }
        */
    }
}
