namespace Web.Identity;

public static class AppRoles
{
    public const string Crew = "Crew";
    public const string Medic = "Medic";
    public const string GroundControl = "GroundControl";
    public static readonly string[] All = [Crew, Medic, GroundControl];
}
