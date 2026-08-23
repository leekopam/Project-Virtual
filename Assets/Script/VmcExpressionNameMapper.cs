public static class VmcExpressionNameMapper
{
    public static bool TryMap(string sourceName, out string vmcName)
    {
        switch (sourceName)
        {
            case "あ": vmcName = "A"; return true;
            case "い": vmcName = "I"; return true;
            case "う": vmcName = "U"; return true;
            case "え": vmcName = "E"; return true;
            case "お": vmcName = "O"; return true;
            case "ウィンク": vmcName = "Blink_L"; return true;
            case "ウィンク右": vmcName = "Blink_R"; return true;
            case "笑い": vmcName = "Joy"; return true;
            case "怒り": vmcName = "Angry"; return true;
            case "困る": vmcName = "Sorrow"; return true;
            default:
                vmcName = null;
                return false;
        }
    }
}
