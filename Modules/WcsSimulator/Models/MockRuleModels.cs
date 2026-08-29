namespace GRCS.Dashboard.Modules.WcsSimulator.Models;

public class MockMatcher
{
    public string Key { get; set; } = "";
    public string Op { get; set; } = "equals";
    public string Expected { get; set; } = "";
    public string Source { get; set; } = "query";
}

public class MockRuleDto
{
    public string Id { get; set; } = "";
    /// <summary>名称（用于列表/记录检索显示；可空）。</summary>
    public string Name { get; set; } = "";
    public string Method { get; set; } = "POST";
    public string PathPattern { get; set; } = "";
    public List<MockMatcher> Matchers { get; set; } = [];
    public int ResponseCode { get; set; } = 200;
    public string ResponseBody { get; set; } = "{}";
    public bool Enabled { get; set; } = true;
    public int Priority { get; set; } = 0;
    public string Description { get; set; } = "";
    public bool AlsoRecord { get; set; } = true;
    public bool BoardSync { get; set; } = false;
    public bool RequiresApproval { get; set; } = false;
    public string ApprovalVariable { get; set; } = "success";
    public string ApprovalTrueValue { get; set; } = "true";
    public string ApprovalFalseValue { get; set; } = "false";
}
