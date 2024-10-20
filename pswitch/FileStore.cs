using System.Text.Json;

namespace pswitch;


class FileStore
{
    static readonly JsonSerializerOptions options = new() { WriteIndented = true };

    public static T Get<T>(string path)
    {
        if (!File.Exists(path))
        {   // First time, create the file
            Write(path, (T)Activator.CreateInstance(typeof(T))!);
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<T>(json) ?? throw new FormatException($"Failed to deserialize '{path}'");
    }

    public static T Set<T>(string path, Action<T> setAction)
    {
        var data = Get<T>(path);
        setAction(data);
        Write(path, data);
        return data;
    }

    static void Write<T>(string path, T data)
    {
        string json = JsonSerializer.Serialize(data, options);
        File.WriteAllText(path, json);
    }
}
