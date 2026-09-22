namespace pious_project_viewer.Diagram;

public static class Heat
{
    public const string Violation = "#9E3A3A";

    public static string Word(int? complexity) => complexity switch
    {
        null => "none",
        <= 5 => "cool",
        <= 10 => "warm",
        <= 20 => "hot",
        _ => "hottest"
    };

    public static string Color(int? complexity) => complexity switch
    {
        null => "#E7E2DA",
        <= 5 => "#D7E3D4",
        <= 10 => "#E8D7B0",
        <= 20 => "#E4C7A8",
        _ => "#C4A48A"
    };
}
