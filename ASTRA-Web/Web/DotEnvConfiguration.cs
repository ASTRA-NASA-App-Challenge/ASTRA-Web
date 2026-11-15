using Microsoft.Extensions.Configuration;

public static class DotEnvConfiguration
{
    public static void Add(ConfigurationManager configuration, string root, string[] args)
    {
        var path = Path.Combine(root, ".env");
        if (!File.Exists(path)) return;
        var values = new Dictionary<string, string?>();
        foreach (var raw in File.ReadLines(path))
        {
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            var separator = line.IndexOf('=');
            if (separator < 1) throw new InvalidOperationException("Invalid .env assignment.");
            var value = line[(separator + 1)..].Trim();
            if (value.Length >= 2 && (value[0] == '\"' && value[^1] == '\"' || value[0] == '\'' && value[^1] == '\'')) value = value[1..^1];
            values[line[..separator].Trim().Replace("__", ":")] = value;
        }
        // Existing settings (including user secrets) override the local file.
        var existing = configuration.AsEnumerable().Where(x => x.Value is not null).ToArray();
        configuration.AddInMemoryCollection(values);
        configuration.AddInMemoryCollection(existing);
        configuration.AddEnvironmentVariables();
        configuration.AddCommandLine(args);
    }
}
