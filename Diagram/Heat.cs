namespace PiousProjectViewer.Diagram;

public static class Heat
{
    public const string Violation = "#E15B5B";

    public static string Word(int? complexity) => complexity switch
    {
        null => "none",
        <= 4 => "very good",
        <= 7 => "good",
        <= 10 => "med",
        <= 20 => "bad",
        _ => "very bad"
    };

    public static string Color(int? complexity) => complexity switch
    {
        null => "#2E3C44",
        <= 4 => "#2F6B4F",
        <= 7 => "#3E7A58",
        <= 10 => "#8A7040",
        <= 20 => "#A15A3A",
        _ => "#8C3E4E"
    };

    public const string Legend = "Very good is 1–4. Good is 5–7. Med is 8–10. Bad is 11–20. Very bad is 21 and above.";
}
