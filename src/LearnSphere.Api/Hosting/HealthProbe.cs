namespace LearnSphere.Api.Hosting;

internal static class HealthProbe
{
    public static async Task<int> RunAsync()
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(4) };
            var response = await client.GetAsync("http://localhost:8080/health");
            return response.IsSuccessStatusCode ? 0 : 1;
        }
        catch (HttpRequestException) { return 1; }
        catch (TaskCanceledException) { return 1; }
    }
}
