namespace DumpPoc.Agent;

public class AgentOptions
{
    public string Secret      { get; set; } = string.Empty;
    public string DumpsDir    { get; set; } = string.Empty;
    public string ProcDumpPath{ get; set; } = string.Empty;
}
