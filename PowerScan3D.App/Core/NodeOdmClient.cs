using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace PowerScan3D.App.Core;

public class NodeOdmTaskStatusObj
{
    public int code { get; set; }
}

public class NodeOdmTaskInfo
{
    public string uuid { get; set; } = string.Empty;
    public string name { get; set; } = string.Empty;
    public NodeOdmTaskStatusObj status { get; set; } = new NodeOdmTaskStatusObj();
    public double progress { get; set; }
    public string error { get; set; } = string.Empty;
}

public class NodeOdmClient
{
    private readonly HttpClient _client;
    private readonly string _endpoint;

    public NodeOdmClient(string endpoint = "http://localhost:3000")
    {
        _endpoint = endpoint.TrimEnd('/');
        _client = new HttpClient { Timeout = TimeSpan.FromHours(4) }; // El procesamiento puede tomar mucho tiempo
    }

    public async Task<bool> PingAsync()
    {
        try
        {
            var res = await _client.GetAsync($"{_endpoint}/info");
            return res.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<string> CreateTaskAsync(string name, List<string> photoPaths, string preset)
    {
        using var content = new MultipartFormDataContent();
        
        object options;
        if (preset == "fast")
        {
            options = new { name = name, dsm = true, dtm = true, fast_orthophoto = true, orthophoto_resolution = 10.0, dsm_resolution = 10.0, dtm_resolution = 10.0 };
        }
        else if (preset == "highres")
        {
            options = new { name = name, dsm = true, dtm = true, orthophoto_resolution = 2.0, dsm_resolution = 2.0, dtm_resolution = 2.0, feature_quality = "ultra" };
        }
        else // corridor / default
        {
            options = new { name = name, dsm = true, dtm = true, orthophoto_resolution = 5.0, dsm_resolution = 5.0, dtm_resolution = 5.0, feature_quality = "medium" };
        }

        content.Add(new StringContent(JsonSerializer.Serialize(options)), "options");

        // Photos
        foreach (var path in photoPaths)
        {
            var fileContent = new StreamContent(File.OpenRead(path));
            content.Add(fileContent, "images", Path.GetFileName(path));
        }

        var response = await _client.PostAsync($"{_endpoint}/task/new", content);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty("uuid").GetString() ?? throw new Exception("UUID not returned");
    }

    public async Task<NodeOdmTaskInfo> GetTaskInfoAsync(string uuid)
    {
        var response = await _client.GetAsync($"{_endpoint}/task/{uuid}/info");
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<NodeOdmTaskInfo>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new NodeOdmTaskInfo();
    }

    public async Task DownloadOrthophotoAsync(string uuid, string outputPath)
    {
        var response = await _client.GetAsync($"{_endpoint}/task/{uuid}/download/odm_orthophoto/odm_orthophoto.tif");
        response.EnsureSuccessStatusCode();
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }

    public async Task DownloadDsmAsync(string uuid, string outputPath)
    {
        var response = await _client.GetAsync($"{_endpoint}/task/{uuid}/download/odm_dem/dsm.tif");
        response.EnsureSuccessStatusCode();
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }

    public async Task DownloadDtmAsync(string uuid, string outputPath)
    {
        var response = await _client.GetAsync($"{_endpoint}/task/{uuid}/download/odm_dem/dtm.tif");
        response.EnsureSuccessStatusCode();
        using var fs = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await response.Content.CopyToAsync(fs);
    }
}
