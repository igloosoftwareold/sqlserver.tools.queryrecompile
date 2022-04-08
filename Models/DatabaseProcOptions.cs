namespace sqlserver.tools.queryrecompile.Models
{
    public class DatabaseProcOptions
    {
        public string ConnectionStringsDefaultConnection { get; set; } = string.Empty;
        public string XelSessionName { get; set; } = string.Empty;
        public bool RecompileQueriesNotOnList { get; set; }
        public int QueryCountLimitNotOnList { get; set; }
        public List<DatabaseProcTemplate> DatabaseProcTemplates { get; set; } = new List<DatabaseProcTemplate>();
    }
}
