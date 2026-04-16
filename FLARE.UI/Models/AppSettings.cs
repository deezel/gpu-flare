namespace FLARE.UI.Models;

public class AppSettings
{
    public string OutputPath { get; set; } = "";
    public int MaxDays { get; set; } = 365;
    public int MaxEvents { get; set; } = 5000;
    public bool SortDescending { get; set; } = true;
}
